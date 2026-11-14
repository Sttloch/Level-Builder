using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Editor;

public class StairDrawSessionTests
{
    GameObject go;

    [TearDown] public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
    }

    static BuildingData TwoFloors()
    {
        var b = new BuildingData();
        b.AddFloor(); b.AddFloor();
        return b;
    }

    [Test] public void Click_AddsCornerOrSmoothPoints()
    {
        var s = new StairDrawSession(0);
        s.Click(new Vector2(1, 2), false);
        s.Click(new Vector2(4, 2), true);
        s.Click(new Vector2(4, 5), false);
        Assert.AreEqual(3, s.Points.Count);
        Assert.AreEqual(new Vector2(1, 2), s.Points[0].position);
        Assert.AreEqual(StairPointKind.Corner, s.Points[0].kind);
        Assert.AreEqual(new Vector2(4, 2), s.Points[1].position);
        Assert.AreEqual(StairPointKind.Smooth, s.Points[1].kind);
        Assert.AreEqual(StairPointKind.Corner, s.Points[2].kind);
    }

    [Test] public void Click_NearPreviousPoint_Ignored()
    {
        var s = new StairDrawSession(0);
        s.Click(new Vector2(1, 2), false);
        s.Click(new Vector2(1.005f, 2), true);
        Assert.AreEqual(1, s.Points.Count);
        Assert.AreEqual(StairPointKind.Corner, s.Points[0].kind);
        s.Click(new Vector2(1.5f, 2), false);
        Assert.AreEqual(2, s.Points.Count);
    }

    [Test] public void RemoveLastPoint_ThenFinishNeedsTwo()
    {
        var s = new StairDrawSession(0);
        Assert.IsFalse(s.RemoveLastPoint());
        s.Click(new Vector2(0, 0), false);
        s.Click(new Vector2(3, 0), false);
        Assert.IsTrue(s.CanFinish);
        Assert.IsTrue(s.RemoveLastPoint());
        Assert.AreEqual(1, s.Points.Count);
        Assert.IsFalse(s.CanFinish);
        Assert.IsNull(s.Finish(TwoFloors()));
    }

    [Test] public void Preview_DoesNotTouchData()
    {
        var data = TwoFloors();
        var s = new StairDrawSession(0);
        s.Click(new Vector2(0, 0), false);
        s.Click(new Vector2(3, 0), false);
        var p = s.Preview(data);
        Assert.IsNotNull(p);
        Assert.AreEqual(0, data.stairs.Count);
        Assert.AreEqual(data.floors[0].id, p.fromFloorId);
        Assert.AreEqual(data.floors[1].id, p.toFloorId);
        Assert.AreEqual(2, p.route.Count);
        // Changing the preview never changes the session.
        p.route.Clear();
        Assert.AreEqual(2, s.Points.Count);
    }

    [Test] public void Preview_WithHover_AddsProvisionalPoint()
    {
        var data = TwoFloors();
        var s = new StairDrawSession(0);
        s.Click(new Vector2(0, 0), false);
        var p = s.Preview(data, new StairPoint { position = new Vector2(3, 0), kind = StairPointKind.Smooth });
        Assert.AreEqual(2, p.route.Count);
        Assert.AreEqual(StairPointKind.Smooth, p.route[1].kind);
        Assert.AreEqual(1, s.Points.Count);
        // A hover on the last point is not a new point.
        var q = s.Preview(data, new StairPoint { position = new Vector2(0.005f, 0) });
        Assert.AreEqual(1, q.route.Count);
    }

    [Test] public void Finish_AddsStair_OneUndoStep()
    {
        go = BuildingMenu.CreateBuilding(null);
        var b = go.GetComponent<Building>();
        b.data.AddFloor();
        Undo.ClearAll();
        Undo.IncrementCurrentGroup();

        var s = new StairDrawSession(0);
        s.Click(new Vector2(0, 0), false);
        s.Click(new Vector2(4, 0), false);
        StairData created = null;
        BuildingEditorUtil.Mutate(b, "Crear escalera", 0, () => created = s.Finish(b.data));
        Assert.IsNotNull(created);
        Assert.AreEqual(1, b.data.stairs.Count);
        Assert.AreEqual(0, s.Points.Count); // the session is reset after finishing

        Undo.PerformUndo();
        Assert.AreEqual(0, b.data.stairs.Count); // data may be a new instance
        Undo.PerformRedo();
        Assert.AreEqual(1, b.data.stairs.Count);
        Assert.AreEqual(created.id, b.data.stairs[0].id);
        Assert.AreEqual(2, b.data.stairs[0].route.Count);
    }

    [Test] public void Finish_WithoutFloorAbove_ReturnsNull()
    {
        var data = new BuildingData();
        data.AddFloor();
        var s = new StairDrawSession(0);
        s.Click(new Vector2(0, 0), false);
        s.Click(new Vector2(4, 0), false);
        Assert.IsNull(s.Finish(data));
        Assert.AreEqual(0, data.stairs.Count);
        Assert.AreEqual(2, s.Points.Count); // nothing lost: a floor can still be added
    }
}
