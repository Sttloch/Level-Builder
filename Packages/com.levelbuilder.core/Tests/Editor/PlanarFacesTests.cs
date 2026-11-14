using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

public class PlanarFacesTests
{
    static float SignedArea(List<Vector2> p)
    {
        float a = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var u = p[i]; var v = p[(i + 1) % p.Count];
            a += u.x * v.y - v.x * u.y;
        }
        return a * 0.5f;
    }

    static void AddSquare(BuildingData b, FloorData f, float x0, float y0, float s)
    {
        b.AddSegment(f, new Vector2(x0, y0), new Vector2(x0 + s, y0));
        b.AddSegment(f, new Vector2(x0 + s, y0), new Vector2(x0 + s, y0 + s));
        b.AddSegment(f, new Vector2(x0 + s, y0 + s), new Vector2(x0, y0 + s));
        b.AddSegment(f, new Vector2(x0, y0 + s), new Vector2(x0, y0));
    }

    [Test] public void Square_OneRegion_AllFourExterior()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        var r = PlanarFaces.Compute(f);
        Assert.AreEqual(1, r.regions.Count);
        Assert.AreEqual(16f, SignedArea(r.regions[0].polygon), 0.001f);
        Assert.AreEqual(4, r.exteriorSegmentIds.Count);
    }

    [Test] public void SquareWithMiddleWall_TwoRegions_MiddleNotExterior()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        var mid = b.AddSegment(f, new Vector2(2, 0), new Vector2(2, 4));
        Assert.AreEqual(7, f.segments.Count);
        var r = PlanarFaces.Compute(f);
        Assert.AreEqual(2, r.regions.Count);
        Assert.AreEqual(6, r.exteriorSegmentIds.Count);
        foreach (var s in mid) Assert.IsFalse(r.exteriorSegmentIds.Contains(s.id));
    }

    [Test] public void OpenL_NoRegions_NoThrow()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
        b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 4));
        var r = PlanarFaces.Compute(f);
        Assert.AreEqual(0, r.regions.Count);
        Assert.AreEqual(0, r.exteriorSegmentIds.Count);
    }

    [Test] public void EmptyAndIsolatedNodes_NoThrow()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        Assert.AreEqual(0, PlanarFaces.Compute(f).regions.Count);
        f.nodes.Add(new WallNode { id = "x", position = Vector2.one });
        Assert.AreEqual(0, PlanarFaces.Compute(f).regions.Count);
    }

    [Test] public void SquareWithDanglingWall_OneRegion()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        var tail = b.AddSegment(f, new Vector2(4, 2), new Vector2(7, 2));
        var r = PlanarFaces.Compute(f);
        Assert.AreEqual(1, r.regions.Count);
        foreach (var s in tail) Assert.IsFalse(r.exteriorSegmentIds.Contains(s.id));
        Assert.AreEqual(5, r.exteriorSegmentIds.Count);
    }

    [Test] public void TwoSeparateSquares_TwoRegions()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        AddSquare(b, f, 10, 0, 4);
        var r = PlanarFaces.Compute(f);
        Assert.AreEqual(2, r.regions.Count);
        Assert.AreEqual(8, r.exteriorSegmentIds.Count);
    }

    [Test] public void RegionPolygon_IsCounterClockwise()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        // drawn clockwise on purpose
        b.AddSegment(f, new Vector2(0, 0), new Vector2(0, 4));
        b.AddSegment(f, new Vector2(0, 4), new Vector2(4, 4));
        b.AddSegment(f, new Vector2(4, 4), new Vector2(4, 0));
        b.AddSegment(f, new Vector2(4, 0), new Vector2(0, 0));
        var r = PlanarFaces.Compute(f);
        Assert.AreEqual(1, r.regions.Count);
        Assert.Greater(SignedArea(r.regions[0].polygon), 0f);
        Assert.IsFalse(string.IsNullOrEmpty(r.regions[0].key));
    }

    static bool HasRepeats(Region r)
    {
        var seen = new HashSet<string>();
        foreach (var id in r.nodeIds) if (!seen.Add(id)) return true;
        return false;
    }

    [Test] public void PillarInsideRoom_RoomHasOneHole_PillarNotExterior()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        var room = new HashSet<string>();
        foreach (var s in f.segments) room.Add(s.id);
        AddSquare(b, f, 1.5f, 1.5f, 1);
        var r = PlanarFaces.Compute(f);
        Region big = null;
        foreach (var reg in r.regions) if (SignedArea(reg.polygon) > 10f) big = reg;
        Assert.IsNotNull(big);
        Assert.AreEqual(2, r.regions.Count);
        Assert.AreEqual(1, big.holes.Count);
        Assert.AreEqual(4, r.exteriorSegmentIds.Count);
        foreach (var id in r.exteriorSegmentIds) Assert.IsTrue(room.Contains(id));
    }

    [Test] public void PillarConnectedByWall_StillHole_PolygonSimple()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        AddSquare(b, f, 1.5f, 1.5f, 1);
        var link = b.AddSegment(f, new Vector2(2, 1.5f), new Vector2(2, 0));
        var r = PlanarFaces.Compute(f);
        Region big = null;
        foreach (var reg in r.regions)
        {
            Assert.IsFalse(HasRepeats(reg));
            if (SignedArea(reg.polygon) > 10f) big = reg;
        }
        Assert.IsNotNull(big);
        Assert.AreEqual(1, big.holes.Count);
        foreach (var s in link) Assert.IsFalse(r.exteriorSegmentIds.Contains(s.id));
    }

    [Test] public void TwoRoomsJoinedByWall_BothRegionsSimple()
    {
        var b = new BuildingData(); var f = b.AddFloor();
        AddSquare(b, f, 0, 0, 4);
        AddSquare(b, f, 10, 0, 4);
        var link = b.AddSegment(f, new Vector2(4, 2), new Vector2(10, 2));
        var r = PlanarFaces.Compute(f);
        Assert.AreEqual(2, r.regions.Count);
        foreach (var reg in r.regions) Assert.IsFalse(HasRepeats(reg));
        foreach (var s in link) Assert.IsFalse(r.exteriorSegmentIds.Contains(s.id));
    }
}
