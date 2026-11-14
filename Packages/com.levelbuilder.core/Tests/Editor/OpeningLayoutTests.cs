using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

public class OpeningLayoutTests
{
    static WallOpening Door(float center, float width = 0.9f, float height = 2.1f) =>
        new WallOpening { type = OpeningType.Door, center = center, width = width, height = height };

    static BuildingData Wall6(out FloorData f, out WallSegment seg)
    {
        var b = new BuildingData();
        f = b.AddFloor();
        seg = b.AddSegment(f, new Vector2(0, 0), new Vector2(6, 0))[0];
        return b;
    }

    static OpeningRect Single(FloorData f, string segId) => OpeningLayout.Compute(f)[segId][0];

    [Test] public void CenteredDoor_IsValid()
    {
        var b = Wall6(out var f, out var seg);
        b.AddOpening(f, seg.id, Door(3f));
        var r = Single(f, seg.id);
        Assert.IsTrue(r.valid);
        Assert.AreEqual(OpeningIssue.None, r.issue);
        Assert.AreEqual(2.55f, r.u0, 1e-4f);
        Assert.AreEqual(3.45f, r.u1, 1e-4f);
        Assert.AreEqual(0f, r.v0, 1e-4f);
        Assert.AreEqual(2.1f, r.v1, 1e-4f);
    }

    [Test] public void ShorteningWall_MakesDoorInvalid_LengtheningRestores()
    {
        var b = Wall6(out var f, out var seg);
        b.AddOpening(f, seg.id, Door(3f));
        b.MoveNode(f, seg.endNodeId, new Vector2(3.2f, 0));
        var r = Single(f, seg.id);
        Assert.IsFalse(r.valid);
        Assert.IsTrue(r.issue == OpeningIssue.IntoJoint || r.issue == OpeningIssue.OutsideWall, r.issue.ToString());
        b.MoveNode(f, seg.endNodeId, new Vector2(6f, 0));
        Assert.IsTrue(Single(f, seg.id).valid);
    }

    static WallSegment SouthWall(FloorData f)
    {
        foreach (var s in f.segments)
        {
            var a = f.GetNode(s.startNodeId).position; var c = f.GetNode(s.endNodeId).position;
            if (Mathf.Abs(a.y) < 1e-4f && Mathf.Abs(c.y) < 1e-4f && Mathf.Abs(a.x - c.x) > 5f) return s;
        }
        return null;
    }

    [Test] public void DoorNextToCorner_IntoJoint()
    {
        var b = new BuildingData();
        var f = b.AddFloor();
        b.AddSegment(f, new Vector2(0, 0), new Vector2(6, 0));
        b.AddSegment(f, new Vector2(6, 0), new Vector2(6, 4));
        b.AddSegment(f, new Vector2(6, 4), new Vector2(0, 4));
        b.AddSegment(f, new Vector2(0, 4), new Vector2(0, 0));
        var south = SouthWall(f);
        Assert.NotNull(south);
        bool fromWest = f.GetNode(south.startNodeId).position.x < 3f;
        float C(float fromCorner) => fromWest ? fromCorner : 6f - fromCorner;
        var near = b.AddOpening(f, south.id, Door(C(0.5f)));
        var far = b.AddOpening(f, south.id, Door(C(0.65f)));
        var rects = OpeningLayout.Compute(f)[south.id];
        Assert.AreEqual(OpeningIssue.IntoJoint, rects.Find(r => r.opening == near).issue);
        Assert.IsTrue(rects.Find(r => r.opening == far).valid);
    }

    [Test] public void DoorNextToTJunction_UsesJointCut()
    {
        var b = new BuildingData();
        var f = b.AddFloor();
        b.AddSegment(f, new Vector2(0, 0), new Vector2(6, 0));
        b.AddSegment(f, new Vector2(3, 2), new Vector2(3, 0));
        var east = f.segments.Find(s =>
        {
            var a = f.GetNode(s.startNodeId).position; var c = f.GetNode(s.endNodeId).position;
            return Mathf.Abs(a.y) < 1e-4f && Mathf.Abs(c.y) < 1e-4f && Mathf.Min(a.x, c.x) > 2.9f;
        });
        Assert.NotNull(east);
        bool fromWest = f.GetNode(east.startNodeId).position.x < 4f; // start node at the T
        float C(float fromT) => fromWest ? fromT : 3f - fromT;
        var near = b.AddOpening(f, east.id, Door(C(0.5f)));
        var far = b.AddOpening(f, east.id, Door(C(0.7f)));
        var rects = OpeningLayout.Compute(f)[east.id];
        Assert.AreEqual(OpeningIssue.IntoJoint, rects.Find(r => r.opening == near).issue);
        Assert.IsTrue(rects.Find(r => r.opening == far).valid);
    }

    [Test] public void TooTall_WhenTopWithinMarginOfWallTop()
    {
        var b = Wall6(out var f, out var seg);
        var w = b.AddOpening(f, seg.id, new WallOpening { type = OpeningType.Window, center = 3f, width = 1.2f, height = 1.98f, sill = 1f });
        Assert.AreEqual(OpeningIssue.TooTall, Single(f, seg.id).issue);
        w.height = 1.9f;
        Assert.IsTrue(Single(f, seg.id).valid);
    }

    [Test] public void Overlap_SecondInListIsInvalid()
    {
        var b = Wall6(out var f, out var seg);
        var first = b.AddOpening(f, seg.id, Door(3f));
        var second = b.AddOpening(f, seg.id, Door(3.5f));
        var rects = OpeningLayout.Compute(f)[seg.id];
        Assert.AreEqual(2, rects.Count);
        Assert.IsTrue(rects[0].u0 <= rects[1].u0);
        Assert.IsTrue(rects.Find(r => r.opening == first).valid);
        Assert.AreEqual(OpeningIssue.Overlap, rects.Find(r => r.opening == second).issue);
    }

    [Test] public void TooSmall()
    {
        var b = Wall6(out var f, out var seg);
        b.AddOpening(f, seg.id, Door(3f, 0.2f));
        Assert.AreEqual(OpeningIssue.TooSmall, Single(f, seg.id).issue);
    }
}
