using System.Linq;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;

public class FloorLinkTests
{
    static void AddSquareWithMiddle(BuildingData b, FloorData f)
    {
        b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
        b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 4));
        b.AddSegment(f, new Vector2(4, 4), new Vector2(0, 4));
        b.AddSegment(f, new Vector2(0, 4), new Vector2(0, 0));
        b.AddSegment(f, new Vector2(2, 0), new Vector2(2, 4));
    }

    static WallNode At(FloorData f, float x, float y) =>
        f.nodes.First(n => (n.position - new Vector2(x, y)).magnitude < 0.001f);

    [Test] public void AddFloor_CopiesOnlyExteriorWalls()
    {
        var b = new BuildingData(); var f0 = b.AddFloor();
        AddSquareWithMiddle(b, f0);
        f0.segments[0].thickness = 0.3f;
        var f1 = b.AddFloor();
        Assert.AreEqual(6, f1.segments.Count);
        Assert.AreEqual(6, f1.nodes.Count);
        Assert.AreEqual(f0.height, f1.height);
        Assert.AreEqual(f0.elevation + f0.height, f1.elevation, 0.0001f);
        Assert.IsTrue(f1.segments.Any(s => Mathf.Approximately(s.thickness, 0.3f)));
        foreach (var s in f1.segments)
            Assert.IsFalse(f0.segments.Any(o => o.id == s.id));
    }

    [Test] public void AddFloor_CopiedNodesAreLinked()
    {
        var b = new BuildingData(); var f0 = b.AddFloor();
        AddSquareWithMiddle(b, f0);
        var f1 = b.AddFloor();
        foreach (var n in f1.nodes)
        {
            Assert.IsFalse(string.IsNullOrEmpty(n.linkedBelowId));
            var below = f0.GetNode(n.linkedBelowId);
            Assert.IsNotNull(below);
            Assert.AreEqual(below.position, n.position);
            Assert.AreNotEqual(below.id, n.id);
        }
    }

    [Test] public void MoveNode_OnGroundMovesUpperToo()
    {
        var b = new BuildingData(); var f0 = b.AddFloor();
        AddSquareWithMiddle(b, f0);
        var f1 = b.AddFloor(); var f2 = b.AddFloor();
        var corner = At(f0, 4, 4);
        b.MoveNode(f0, corner.id, new Vector2(5, 5));
        Assert.AreEqual(new Vector2(5, 5), corner.position);
        Assert.AreEqual(1, f1.nodes.Count(n => n.position == new Vector2(5, 5)));
        Assert.AreEqual(1, f2.nodes.Count(n => n.position == new Vector2(5, 5)));
        Assert.AreEqual(0, f1.nodes.Count(n => n.position == new Vector2(4, 4)));
    }

    [Test] public void MoveNode_OnUpperMovesGroundToo()
    {
        var b = new BuildingData(); var f0 = b.AddFloor();
        AddSquareWithMiddle(b, f0);
        var f1 = b.AddFloor(); var f2 = b.AddFloor();
        var up = At(f2, 0, 0);
        b.MoveNode(f2, up.id, new Vector2(-1, -1));
        Assert.AreEqual(1, f0.nodes.Count(n => n.position == new Vector2(-1, -1)));
        Assert.AreEqual(1, f1.nodes.Count(n => n.position == new Vector2(-1, -1)));
        Assert.AreEqual(1, f2.nodes.Count(n => n.position == new Vector2(-1, -1)));
    }

    [Test] public void UnlinkNode_ThenMove_OnlyThatFloorMoves()
    {
        var b = new BuildingData(); var f0 = b.AddFloor();
        AddSquareWithMiddle(b, f0);
        var f1 = b.AddFloor(); var f2 = b.AddFloor();
        var mid = At(f1, 4, 4);
        b.UnlinkNode(f1, mid.id);
        Assert.IsTrue(string.IsNullOrEmpty(mid.linkedBelowId));
        Assert.IsTrue(f2.nodes.All(n => n.linkedBelowId != mid.id));
        b.MoveNode(f1, mid.id, new Vector2(6, 6));
        Assert.AreEqual(new Vector2(6, 6), mid.position);
        Assert.AreEqual(1, f0.nodes.Count(n => n.position == new Vector2(4, 4)));
        Assert.AreEqual(1, f2.nodes.Count(n => n.position == new Vector2(4, 4)));
        Assert.AreEqual(1, b.GetLinkedColumn(mid.id).Count());
    }

    [Test] public void GetLinkedColumn_ReturnsAllFloors()
    {
        var b = new BuildingData(); var f0 = b.AddFloor();
        AddSquareWithMiddle(b, f0);
        b.AddFloor(); var f2 = b.AddFloor();
        var col = b.GetLinkedColumn(At(f2, 0, 0).id).ToList();
        Assert.AreEqual(3, col.Count);
        Assert.AreEqual(3, col.Select(c => c.floor.id).Distinct().Count());
    }

    [Test] public void RemoveNode_Below_ClearsUpperLink()
    {
        var b = new BuildingData(); var f0 = b.AddFloor();
        AddSquareWithMiddle(b, f0);
        var f1 = b.AddFloor();
        var below = At(f0, 0, 0);
        var upper = At(f1, 0, 0);
        b.RemoveNode(f0, below.id);
        Assert.IsNotNull(f1.GetNode(upper.id));
        Assert.IsTrue(string.IsNullOrEmpty(upper.linkedBelowId));
    }

    [Test] public void RemoveSegment_OrphanBelow_ClearsUpperLink()
    {
        var b = new BuildingData(); var f0 = b.AddFloor();
        AddSquareWithMiddle(b, f0);
        var f1 = b.AddFloor();
        foreach (var seg in f0.segments.ToList()) b.RemoveSegment(f0, seg.id);
        Assert.AreEqual(0, f0.nodes.Count);
        Assert.AreEqual(6, f1.nodes.Count);
        Assert.IsTrue(f1.nodes.All(n => string.IsNullOrEmpty(n.linkedBelowId)));
    }
}
