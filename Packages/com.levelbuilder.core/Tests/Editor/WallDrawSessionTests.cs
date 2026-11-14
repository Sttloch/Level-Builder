using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Editor;
using LevelBuilder.Geometry;

public class WallDrawSessionTests
{
    BuildingData data;
    FloorData f;

    [SetUp] public void SetUp()
    {
        data = new BuildingData();
        f = data.AddFloor();
    }

    [Test] public void Session_ThreeClicks_TwoSegments()
    {
        var s = new WallDrawSession(data, 0);
        s.Click(new Vector2(0, 0));
        s.Click(new Vector2(4, 0));
        s.Click(new Vector2(4, 3));
        Assert.AreEqual(2, f.segments.Count);
        Assert.AreEqual(3, f.nodes.Count);
        Assert.IsTrue(s.IsDrawing);
        Assert.AreEqual(new Vector2(4, 3), s.LastPoint.Value);
    }

    [Test] public void Session_ClickOnFirstPoint_ClosesAndFinishes()
    {
        var s = new WallDrawSession(data, 0);
        s.Click(new Vector2(0, 0));
        s.Click(new Vector2(8, 0));
        s.Click(new Vector2(8, 6));
        s.Click(new Vector2(0, 6));
        s.Click(new Vector2(0, 0));
        Assert.AreEqual(4, f.segments.Count);
        Assert.AreEqual(4, f.nodes.Count);
        Assert.IsFalse(s.IsDrawing);
        Assert.IsNull(s.LastPoint);
        Assert.AreEqual(1, PlanarFaces.Compute(f).regions.Count);
    }

    [Test] public void Session_FinishWithOnePoint_CreatesNothing()
    {
        var s = new WallDrawSession(data, 0);
        s.Click(new Vector2(1, 1));
        Assert.IsTrue(s.IsDrawing);
        s.Finish();
        Assert.IsFalse(s.IsDrawing);
        Assert.IsNull(s.LastPoint);
        Assert.AreEqual(0, f.segments.Count);
        Assert.AreEqual(0, f.nodes.Count);
    }

    [Test] public void Session_FinishKeepsOpenChain()
    {
        var s = new WallDrawSession(data, 0);
        s.Click(new Vector2(0, 0));
        s.Click(new Vector2(2, 0));
        s.Finish();
        Assert.AreEqual(1, f.segments.Count);
        s.Click(new Vector2(5, 5)); // starts a new chain, no segment from the old last point
        Assert.AreEqual(1, f.segments.Count);
        Assert.IsTrue(s.IsDrawing);
    }

    [Test] public void Session_RepeatedClick_IsIgnored()
    {
        var s = new WallDrawSession(data, 0);
        s.Click(new Vector2(0, 0));
        s.Click(new Vector2(0, 0));
        s.Click(new Vector2(3, 0));
        Assert.AreEqual(1, f.segments.Count);
    }

    [Test] public void Session_UsesGivenFloor()
    {
        var f1 = data.AddFloor();
        var s = new WallDrawSession(data, 1);
        s.Click(new Vector2(0, 0));
        s.Click(new Vector2(2, 0));
        Assert.AreEqual(0, f.segments.Count);
        Assert.AreEqual(1, f1.segments.Count);
    }
    [Test] public void Session_Rebind_WritesToNewDataObject()
    {
        var s = new WallDrawSession(data, 0);
        s.Click(new Vector2(0, 0));
        // Undo/Redo may deserialize a new BuildingData instance: the session must write to the current one.
        var replaced = new BuildingData();
        var rf = replaced.AddFloor();
        s.Rebind(replaced);
        Assert.AreSame(replaced, s.Data);
        s.Click(new Vector2(2, 0));
        Assert.AreEqual(1, rf.segments.Count);
        Assert.AreEqual(0, f.segments.Count);
    }

    [Test] public void Session_WouldAddSegment_OnlyAfterFirstPointAndNotRepeated()
    {
        var s = new WallDrawSession(data, 0);
        Assert.IsFalse(s.WouldAddSegment(new Vector2(0, 0)));
        s.Click(new Vector2(0, 0));
        Assert.IsFalse(s.WouldAddSegment(new Vector2(0, 0)));
        Assert.IsTrue(s.WouldAddSegment(new Vector2(1, 0)));
        Assert.AreEqual(1, s.PointCount);
    }

    [Test] public void Backspace_WithOnePoint_CancelsChain()
    {
        var s = new WallDrawSession(data, 0);
        s.Click(new Vector2(1, 1));
        Assert.IsFalse(s.RemoveLastPointChangesData);
        Assert.IsFalse(s.RemoveLastPoint());
        Assert.IsFalse(s.IsDrawing);
        Assert.AreEqual(0, f.nodes.Count);
    }

    [Test] public void Backspace_RemovesLastSegmentAndPoint()
    {
        var s = new WallDrawSession(data, 0);
        s.Click(new Vector2(0, 0));
        s.Click(new Vector2(4, 0));
        s.Click(new Vector2(4, 3));
        Assert.IsTrue(s.RemoveLastPointChangesData);
        Assert.IsTrue(s.RemoveLastPoint());
        Assert.AreEqual(1, f.segments.Count);
        Assert.AreEqual(2, f.nodes.Count);
        Assert.IsTrue(s.IsDrawing);
        Assert.AreEqual(2, s.PointCount);
        Assert.AreEqual(new Vector2(4, 0), s.LastPoint.Value);
        s.Click(new Vector2(0, 3)); // chain continues from the previous point
        Assert.AreEqual(2, f.segments.Count);
        Assert.IsTrue(f.nodes.Exists(n => n.position == new Vector2(0, 3)));
    }

    [Test] public void Backspace_AfterCrossingWall_RestoresCrossedWall()
    {
        var wall = data.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0))[0];
        string a = wall.startNodeId, b = wall.endNodeId;
        var s = new WallDrawSession(data, 0);
        s.Click(new Vector2(2, -2));
        s.Click(new Vector2(2, 2));
        Assert.AreEqual(4, f.segments.Count);
        s.RemoveLastPoint();
        Assert.AreEqual(1, f.segments.Count);
        Assert.AreEqual(2, f.nodes.Count);
        Assert.AreSame(wall, f.segments[0]);
        Assert.AreEqual(a, wall.startNodeId);
        Assert.AreEqual(b, wall.endNodeId);
        Assert.AreEqual(1, s.PointCount);
    }

    [Test] public void Backspace_AfterEndingOnWall_RejoinsWall()
    {
        var wall = data.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0))[0];
        var s = new WallDrawSession(data, 0);
        s.Click(new Vector2(2, 3));
        s.Click(new Vector2(2, 0));
        Assert.AreEqual(3, f.segments.Count);
        s.RemoveLastPoint();
        Assert.AreEqual(1, f.segments.Count);
        Assert.AreEqual(2, f.nodes.Count);
        Assert.AreEqual(new Vector2(4, 0), f.GetNode(wall.endNodeId).position);
    }
}
