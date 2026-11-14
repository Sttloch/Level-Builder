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
}
