using System.Linq;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;

public class OpeningDataTests
{
    static WallOpening Make(OpeningType type, float center, float width = 0.9f) =>
        new WallOpening { type = type, center = center, width = width, height = 1.2f };

    static BuildingData Wall6(out FloorData f, out WallSegment seg)
    {
        var b = new BuildingData();
        f = b.AddFloor();
        seg = b.AddSegment(f, new Vector2(0, 0), new Vector2(6, 0))[0];
        return b;
    }

    [Test] public void AddOpening_AssignsIdAndStoresOnSegment()
    {
        var b = Wall6(out var f, out var seg);
        var o = b.AddOpening(f, seg.id, Make(OpeningType.Door, 1f));
        Assert.IsFalse(string.IsNullOrEmpty(o.id));
        Assert.AreEqual(1, seg.openings.Count);
        Assert.AreSame(o, b.FindOpening(f, o.id).o);
    }

    [Test] public void SplitSegment_MovesOpeningsToHalfContainingCenter()
    {
        var b = Wall6(out var f, out var seg);
        var d1 = b.AddOpening(f, seg.id, Make(OpeningType.Door, 1f));
        var d2 = b.AddOpening(f, seg.id, Make(OpeningType.Door, 4.5f));
        b.SplitSegment(f, seg.id, new Vector2(3, 0));
        var second = f.segments.First(s => s != seg);
        Assert.AreEqual(1, seg.openings.Count);
        Assert.AreSame(d1, seg.openings[0]);
        Assert.AreEqual(1f, d1.center, 0.0001f);
        Assert.AreEqual(1, second.openings.Count);
        Assert.AreSame(d2, second.openings[0]);
        Assert.AreEqual(1.5f, d2.center, 0.0001f);
    }

    [Test] public void SplitThroughOpening_KeepsItOnCenterHalf()
    {
        var b = Wall6(out var f, out var seg);
        var w = b.AddOpening(f, seg.id, Make(OpeningType.Window, 3.2f, 1.2f));
        b.SplitSegment(f, seg.id, new Vector2(3, 0));
        var second = f.segments.First(s => s != seg);
        Assert.AreEqual(0, seg.openings.Count);
        Assert.AreSame(w, second.openings.Single());
        Assert.AreEqual(0.2f, w.center, 0.0001f);
    }

    [Test] public void AddSegmentCrossing_SplitsAndKeepsOpenings()
    {
        var b = Wall6(out var f, out var seg);
        var o = b.AddOpening(f, seg.id, Make(OpeningType.Door, 4.5f));
        b.AddSegment(f, new Vector2(3, -2), new Vector2(3, 2));
        var (owner, found) = b.FindOpening(f, o.id);
        Assert.IsNotNull(found);
        Assert.AreEqual(1.5f, found.center, 0.0001f);
        Assert.AreEqual(3f, f.GetNode(owner.startNodeId).position.x, 0.0001f);
        Assert.AreEqual(6f, f.GetNode(owner.endNodeId).position.x, 0.0001f);
    }

    [Test] public void JoinSplit_RestoresOpeningOffsets()
    {
        var b = Wall6(out var f, out var seg);
        var o = b.AddOpening(f, seg.id, Make(OpeningType.Door, 4.5f));
        b.SplitSegment(f, seg.id, new Vector2(3, 0));
        var second = f.segments.First(s => s != seg);
        Assert.IsTrue(b.JoinSplit(f, seg.id, second.id));
        var (owner, found) = b.FindOpening(f, o.id);
        Assert.AreSame(seg, owner);
        Assert.AreEqual(4.5f, found.center, 0.0001f);
    }

    [Test] public void RemoveSegment_RemovesItsOpenings()
    {
        var b = Wall6(out var f, out var seg);
        var o = b.AddOpening(f, seg.id, Make(OpeningType.Door, 1f));
        b.RemoveSegment(f, seg.id);
        Assert.IsNull(b.FindOpening(f, o.id).o);
    }

    [Test] public void RemoveNode_RemovesOpeningsOfIncidentWalls()
    {
        var b = Wall6(out var f, out var seg);
        var o = b.AddOpening(f, seg.id, Make(OpeningType.Door, 1f));
        b.RemoveNode(f, seg.startNodeId);
        Assert.IsNull(b.FindOpening(f, o.id).o);
    }

    [Test] public void ResolveNode_DuplicateWallKeepsOpenings()
    {
        var b = new BuildingData();
        var f = b.AddFloor();
        var n1 = new WallNode { id = IdUtil.New(), position = new Vector2(0, 0) };
        var n2 = new WallNode { id = IdUtil.New(), position = new Vector2(6, 0) };
        f.nodes.Add(n1); f.nodes.Add(n2);
        var keep = new WallSegment { id = IdUtil.New(), startNodeId = n1.id, endNodeId = n2.id };
        var dup = new WallSegment { id = IdUtil.New(), startNodeId = n2.id, endNodeId = n1.id };
        f.segments.Add(keep); f.segments.Add(dup);
        var o = b.AddOpening(f, dup.id, Make(OpeningType.Window, 1f, 1.2f));
        b.ResolveNode(f, n1.id);
        Assert.AreEqual(1, f.segments.Count);
        var (owner, found) = b.FindOpening(f, o.id);
        Assert.AreSame(keep, owner);
        Assert.AreEqual(5f, found.center, 0.0001f);
    }

    [Test] public void ResolveNode_DuplicateSameDirectionKeepsCenter()
    {
        var b = new BuildingData();
        var f = b.AddFloor();
        var n1 = new WallNode { id = IdUtil.New(), position = new Vector2(0, 0) };
        var n2 = new WallNode { id = IdUtil.New(), position = new Vector2(6, 0) };
        f.nodes.Add(n1); f.nodes.Add(n2);
        var keep = new WallSegment { id = IdUtil.New(), startNodeId = n1.id, endNodeId = n2.id };
        var dup = new WallSegment { id = IdUtil.New(), startNodeId = n1.id, endNodeId = n2.id };
        f.segments.Add(keep); f.segments.Add(dup);
        var o = b.AddOpening(f, dup.id, Make(OpeningType.Door, 1f));
        b.ResolveNode(f, n2.id);
        Assert.AreEqual(1f, b.FindOpening(f, o.id).o.center, 0.0001f);
        Assert.AreSame(keep, b.FindOpening(f, o.id).seg);
    }

    [Test] public void MoveOpening_ToOtherSegment()
    {
        var b = Wall6(out var f, out var seg);
        var other = b.AddSegment(f, new Vector2(0, 4), new Vector2(6, 4))[0];
        var o = b.AddOpening(f, seg.id, Make(OpeningType.Door, 1f));
        Assert.IsTrue(b.MoveOpening(f, o.id, other.id, 2.5f));
        Assert.AreEqual(0, seg.openings.Count);
        Assert.AreSame(other, b.FindOpening(f, o.id).seg);
        Assert.AreEqual(2.5f, o.center, 0.0001f);
        Assert.IsFalse(b.MoveOpening(f, "missing", other.id, 1f));
    }

    [Test] public void Length_IsDistanceBetweenNodes()
    {
        var b = Wall6(out var f, out var seg);
        Assert.AreEqual(6f, BuildingData.Length(f, seg), 0.0001f);
        f.nodes.Clear();
        Assert.AreEqual(0f, BuildingData.Length(f, seg));
    }

    [Test] public void AddFloor_CopiesWindowsOfExteriorWallsOnly()
    {
        var b = new BuildingData();
        var f0 = b.AddFloor();
        b.AddSegment(f0, new Vector2(0, 0), new Vector2(4, 0));
        b.AddSegment(f0, new Vector2(4, 0), new Vector2(4, 4));
        b.AddSegment(f0, new Vector2(4, 4), new Vector2(0, 4));
        b.AddSegment(f0, new Vector2(0, 4), new Vector2(0, 0));
        b.AddSegment(f0, new Vector2(2, 0), new Vector2(2, 4));
        WallSegment Wall(Vector2 p, Vector2 q) => f0.segments.First(s =>
        {
            var a = f0.GetNode(s.startNodeId).position; var c = f0.GetNode(s.endNodeId).position;
            return (a == p && c == q) || (a == q && c == p);
        });
        var south = Wall(new Vector2(0, 0), new Vector2(2, 0));
        var west = Wall(new Vector2(0, 4), new Vector2(0, 0));
        var middle = Wall(new Vector2(2, 0), new Vector2(2, 4));
        var win = b.AddOpening(f0, south.id, Make(OpeningType.Window, 1f, 1.2f));
        win.sill = 0.9f;
        b.AddOpening(f0, west.id, Make(OpeningType.Door, 2f));
        b.AddOpening(f0, middle.id, Make(OpeningType.Window, 2f, 1.2f));

        var f1 = b.AddFloor();
        var copied = f1.segments.SelectMany(s => s.openings).ToList();
        Assert.AreEqual(1, copied.Count);
        Assert.AreEqual(OpeningType.Window, copied[0].type);
        Assert.AreNotEqual(win.id, copied[0].id);
        Assert.AreEqual(1f, copied[0].center, 0.0001f);
        Assert.AreEqual(1.2f, copied[0].width, 0.0001f);
        Assert.AreEqual(0.9f, copied[0].sill, 0.0001f);
        Assert.AreEqual(1, f0.segments.SelectMany(s => s.openings).Count(o => o.id == win.id));
    }

    [Test] public void Clone_NewIdFreshEvents()
    {
        var o = Make(OpeningType.Door, 1f);
        o.id = "x"; o.door.locked = true; o.door.onOpened.AddListener(() => { });
        var c = o.Clone();
        Assert.AreNotEqual("x", c.id);
        Assert.IsFalse(string.IsNullOrEmpty(c.id));
        Assert.IsTrue(c.door.locked);
        Assert.AreEqual(0, c.door.onOpened.GetPersistentEventCount());
        Assert.AreNotSame(o.door, c.door);
    }

    [Test] public void OpeningPresets_DefaultsMatchSpec()
    {
        var door = OpeningPresets.For(OpeningType.Door)[0];
        Assert.AreEqual((0.9f, 2.1f, 0f), (door.width, door.height, door.sill));
        var win = OpeningPresets.For(OpeningType.Window)[0];
        Assert.AreEqual((1.2f, 1.2f, 0.9f), (win.width, win.height, win.sill));
        var gap = OpeningPresets.For(OpeningType.Empty)[0];
        Assert.AreEqual((0.9f, 2.1f, 0f), (gap.width, gap.height, gap.sill));
        var dbl = OpeningPresets.For(OpeningType.Door)[1];
        Assert.AreEqual((1.6f, 2.1f, 0f), (dbl.width, dbl.height, dbl.sill));
        var high = OpeningPresets.For(OpeningType.Window)[1];
        Assert.AreEqual((0.6f, 0.6f, 1.6f), (high.width, high.height, high.sill));
        var made = OpeningPresets.Create(door, 2f);
        Assert.AreEqual(2f, made.center);
        Assert.IsFalse(string.IsNullOrEmpty(made.id));
    }
}
