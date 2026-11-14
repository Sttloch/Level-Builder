using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;

public class StairDataTests
{
    static List<StairPoint> Route(params Vector2[] pts)
    {
        var l = new List<StairPoint>();
        foreach (var p in pts) l.Add(new StairPoint { position = p, kind = StairPointKind.Corner });
        return l;
    }

    static BuildingData TwoFloors()
    {
        var b = new BuildingData();
        b.AddFloor(); b.AddFloor();
        return b;
    }

    [Test] public void AddStair_RequiresFloorAbove()
    {
        var b = new BuildingData(); b.AddFloor();
        Assert.IsNull(b.AddStair(0, Route(Vector2.zero, new Vector2(4, 0))));
        b.AddFloor();
        var s = b.AddStair(0, Route(Vector2.zero, new Vector2(4, 0)));
        Assert.IsNotNull(s);
        Assert.AreEqual(b.floors[0].id, s.fromFloorId);
        Assert.AreEqual(b.floors[1].id, s.toFloorId);
        Assert.IsFalse(string.IsNullOrEmpty(s.id));
        Assert.AreSame(s, b.FindStair(s.id));
        Assert.AreEqual(1, b.FloorIndex(b.floors[1].id));
        Assert.AreEqual(-1, b.FloorIndex("nope"));
    }

    [Test] public void AddStair_RejectsShortRoute()
    {
        var b = TwoFloors();
        Assert.IsNull(b.AddStair(0, Route(Vector2.zero)));
        Assert.AreEqual(0, b.stairs.Count);
    }

    [Test] public void AddStair_ClampsValues()
    {
        var s = new StairData { width = 0.2f, riserTarget = 0.3f, tread = 0.4f };
        BuildingData.ClampStair(s);
        Assert.AreEqual(0.6f, s.width, 1e-5f);
        Assert.AreEqual(0.20f, s.riserTarget, 1e-5f);
        Assert.AreEqual(0.32f, s.tread, 1e-5f);
        var d = new StairData();
        Assert.AreEqual(1.2f, d.width); Assert.AreEqual(0.18f, d.riserTarget); Assert.AreEqual(0.28f, d.tread);
    }

    [Test] public void RemoveTopFloor_RemovesStairsUsingIt()
    {
        var b = TwoFloors();
        b.AddStair(0, Route(Vector2.zero, new Vector2(4, 0)));
        Assert.IsTrue(b.RemoveTopFloor());
        Assert.AreEqual(0, b.stairs.Count);
    }

    [Test] public void RemoveStairPoint_NeverBelowTwo()
    {
        var b = TwoFloors();
        var s = b.AddStair(0, Route(Vector2.zero, new Vector2(4, 0), new Vector2(4, 4)));
        Assert.IsTrue(b.RemoveStairPoint(s.id, 1));
        Assert.IsFalse(b.RemoveStairPoint(s.id, 0));
        Assert.AreEqual(2, s.route.Count);
        Assert.IsFalse(b.RemoveStairPoint("nope", 0));
        Assert.IsFalse(b.RemoveStairPoint(s.id, 5));
    }

    [Test] public void MoveInsertSetKind_EditRoute()
    {
        var b = TwoFloors();
        var s = b.AddStair(0, Route(Vector2.zero, new Vector2(4, 0)));
        Assert.IsTrue(b.MoveStairPoint(s.id, 1, new Vector2(5, 1)));
        Assert.AreEqual(new Vector2(5, 1), s.route[1].position);
        Assert.IsTrue(b.InsertStairPoint(s.id, 1, new StairPoint { position = new Vector2(2, 0), kind = StairPointKind.Smooth }));
        Assert.AreEqual(3, s.route.Count);
        Assert.AreEqual(new Vector2(2, 0), s.route[1].position);
        Assert.IsTrue(b.InsertStairPoint(s.id, 3, new StairPoint { position = new Vector2(9, 9) }));
        Assert.AreEqual(new Vector2(9, 9), s.route[3].position);
        Assert.IsTrue(b.SetStairPointKind(s.id, 1, StairPointKind.Corner));
        Assert.AreEqual(StairPointKind.Corner, s.route[1].kind);
        Assert.IsFalse(b.MoveStairPoint(s.id, 9, Vector2.zero));
        Assert.IsFalse(b.InsertStairPoint(s.id, 9, new StairPoint()));
        Assert.IsFalse(b.SetStairPointKind("nope", 0, StairPointKind.Smooth));
    }

    [Test] public void AddStair_CopiesRoute()
    {
        var b = TwoFloors();
        var r = Route(Vector2.zero, new Vector2(4, 0));
        var s = b.AddStair(0, r);
        r[0] = new StairPoint { position = new Vector2(7, 7) };
        Assert.AreEqual(Vector2.zero, s.route[0].position);
    }

    [Test] public void RemoveStair_RemovesById()
    {
        var b = TwoFloors();
        var s = b.AddStair(0, Route(Vector2.zero, new Vector2(4, 0)));
        Assert.IsFalse(b.RemoveStair("nope"));
        Assert.IsTrue(b.RemoveStair(s.id));
        Assert.IsNull(b.FindStair(s.id));
    }

    [Test] public void StairData_SurvivesJsonRoundTrip()
    {
        var b = TwoFloors();
        var r = Route(Vector2.zero, new Vector2(4, 0), new Vector2(4, 4));
        r[1] = new StairPoint { position = new Vector2(4, 0), kind = StairPointKind.Smooth };
        var s = b.AddStair(0, r);
        var back = JsonUtility.FromJson<BuildingData>(JsonUtility.ToJson(b));
        Assert.AreEqual(1, back.stairs.Count);
        var t = back.stairs[0];
        Assert.AreEqual(s.id, t.id);
        Assert.AreEqual(s.fromFloorId, t.fromFloorId);
        Assert.AreEqual(s.toFloorId, t.toFloorId);
        Assert.AreEqual(3, t.route.Count);
        Assert.AreEqual(StairPointKind.Smooth, t.route[1].kind);
        Assert.AreEqual(new Vector2(4, 4), t.route[2].position);
    }
}
