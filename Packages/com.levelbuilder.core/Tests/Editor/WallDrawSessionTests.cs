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
}
