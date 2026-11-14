using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

public class StairMeshTests
{
    const float BaseY = 2f;

    static StairPoint P(float x, float y, StairPointKind k = StairPointKind.Corner) =>
        new StairPoint { position = new Vector2(x, y), kind = k };

    static StairData Stair(params StairPoint[] pts) =>
        new StairData { id = "s", route = new List<StairPoint>(pts) };

    static StairLayoutResult Layout(StairData s)
    {
        var from = new FloorData { elevation = 0f, height = 3f, slabThickness = 0.2f };
        var to = new FloorData { elevation = 3f, height = 3f, slabThickness = 0.2f };
        var r = StairLayout.Compute(s, from, to);
        Assert.IsTrue(r.ok, r.error);
        return r;
    }

    static StairData Straight() => Stair(P(0, 0), P(0, 10));

    /// <summary>Straight, L landing (both turn directions), U (two landings), 120° turn (both directions) and smooth fan.</summary>
    static IEnumerable<StairData> AllShapes()
    {
        yield return Straight();
        yield return Stair(P(0, 0), P(0, 2), P(3, 2));
        yield return Stair(P(0, 0), P(0, 2), P(-3, 2));
        yield return Stair(P(0, 0), P(0, 4), P(1, 4), P(1, 0));
        float a = 120f * Mathf.Deg2Rad;
        yield return Stair(P(0, 0), P(0, 2), P(3f * Mathf.Sin(a), 2f + 3f * Mathf.Cos(a)));
        yield return Stair(P(0, 0), P(0, 2), P(-3f * Mathf.Sin(a), 2f + 3f * Mathf.Cos(a)));
        yield return Stair(P(0, 0), P(0, 3, StairPointKind.Smooth), P(3, 3));
    }

    static FaceGroup Group(MeshData md, string key) => md.groups.Find(g => g.key == key);

    static float Area(MeshData md, string key)
    {
        var g = Group(md, key);
        if (g == null) return 0f;
        float area = 0f;
        for (int i = 0; i < g.triangles.Count; i += 3)
        {
            var a = md.vertices[g.triangles[i]]; var b = md.vertices[g.triangles[i + 1]]; var c = md.vertices[g.triangles[i + 2]];
            area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
        }
        return area;
    }

    static Vector2 Plan(Vector3 v) => new Vector2(v.x, v.z);

    static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float t = ab.sqrMagnitude < 1e-12f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return (a + ab * t - p).magnitude;
    }

    static float DistToTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a);
        if (n.sqrMagnitude > 1e-12f)
        {
            n.Normalize();
            float d = Vector3.Dot(p - a, n);
            var q = p - n * d;
            bool inside = Vector3.Dot(Vector3.Cross(b - a, q - a), n) >= 0f &&
                          Vector3.Dot(Vector3.Cross(c - b, q - b), n) >= 0f &&
                          Vector3.Dot(Vector3.Cross(a - c, q - c), n) >= 0f;
            if (inside) return Mathf.Abs(d);
        }
        return Mathf.Min(DistToSegment(p, a, b), Mathf.Min(DistToSegment(p, b, c), DistToSegment(p, c, a)));
    }

    /// <summary>Strictly inside (by more than 1e-4 m from every edge).</summary>
    static bool StrictlyInside(List<Vector2> poly, Vector2 p)
    {
        if (!PlanarFaces.PointInPolygon(p, poly)) return false;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            if ((a + ab * t - p).magnitude < 1e-4f) return false;
        }
        return true;
    }

    [Test] public void Steps_TopsAtPieceHeights()
    {
        var s = Straight();
        var l = Layout(s);
        Assert.AreEqual(17, l.risers);
        var md = StairMeshBuilder.Steps(s, l, BaseY);
        var g = Group(md, "stair:s/tread");
        Assert.IsNotNull(g);
        var hit = new bool[l.pieces.Count];
        for (int i = 0; i < g.triangles.Count; i += 3)
        {
            var a = md.vertices[g.triangles[i]]; var b = md.vertices[g.triangles[i + 1]]; var c = md.vertices[g.triangles[i + 2]];
            Assert.Greater(md.normals[g.triangles[i]].y, 0.99f);
            var centroid = Plan((a + b + c) / 3f);
            int k = l.pieces.FindIndex(p => PlanarFaces.PointInPolygon(centroid, p.polygon));
            Assert.GreaterOrEqual(k, 0);
            hit[k] = true;
            float y = BaseY + (k + 1) * l.riser;
            Assert.AreEqual(y, a.y, 1e-4f); Assert.AreEqual(y, b.y, 1e-4f); Assert.AreEqual(y, c.y, 1e-4f);
        }
        CollectionAssert.DoesNotContain(hit, false);
        Assert.AreEqual(16 * 0.28f * 1f, Area(md, "stair:s/tread"), 1e-3f);

        float maxRiser = float.MinValue;
        var rg = Group(md, "stair:s/riser");
        foreach (int idx in rg.triangles) maxRiser = Mathf.Max(maxRiser, md.vertices[idx].y);
        Assert.AreEqual(BaseY + 3f, maxRiser, 1e-4f);
    }

    [Test] public void Steps_RiserAreas()
    {
        foreach (var s in AllShapes())
        {
            var l = Layout(s);
            var md = StairMeshBuilder.Steps(s, l, BaseY);
            Assert.AreEqual(s.width * 3f, Area(md, "stair:s/riser"), 1e-3f);
            if (l.pieces.Exists(p => p.kind == StairPieceKind.Landing))
                Assert.Greater(Area(md, "stair:s/landing"), 0f);
            Assert.Greater(Area(md, "stair:s/side"), 0f);
        }
    }

    [Test] public void Steps_WindingMatchesNormals()
    {
        foreach (var s in AllShapes())
        {
            var l = Layout(s);
            WallMeshBuilderTests.AssertWindingMatchesNormals(StairMeshBuilder.Steps(s, l, BaseY));
            WallMeshBuilderTests.AssertWindingMatchesNormals(StairMeshBuilder.Ramp(l, BaseY));
            WallMeshBuilderTests.AssertWindingMatchesNormals(StairMeshBuilder.Rails(s, l, BaseY));
        }
    }

    [Test] public void Ramp_PassesThroughNosings()
    {
        foreach (var s in AllShapes())
        {
            var l = Layout(s);
            var md = StairMeshBuilder.Ramp(l, BaseY);
            Assert.Greater(md.vertices.Count, 0);
            var mids = new List<Vector3>();
            foreach (var p in l.pieces)
            {
                var m = (p.frontA + p.frontB) * 0.5f;
                mids.Add(new Vector3(m.x, BaseY + p.height, m.y));
            }
            foreach (var mid in mids)
            {
                float best = float.MaxValue;
                foreach (var g in md.groups)
                    for (int i = 0; i < g.triangles.Count; i += 3)
                    {
                        int a = g.triangles[i];
                        if (md.normals[a].y <= 0f) continue; // top faces only
                        best = Mathf.Min(best, DistToTriangle(mid, md.vertices[a], md.vertices[g.triangles[i + 1]], md.vertices[g.triangles[i + 2]]));
                    }
                Assert.LessOrEqual(best, 0.01f, $"nosing {mid}");
            }
            // The ramp reaches the arrival edge at H.
            float maxY = float.MinValue;
            foreach (var v in md.vertices) maxY = Mathf.Max(maxY, v.y);
            Assert.AreEqual(BaseY + 3f, maxY, 1e-4f);
        }
    }

    [Test] public void Ramp_NoVerticalSteps_StartsOneTreadBefore()
    {
        foreach (var s in AllShapes())
        {
            var l = Layout(s);
            var flights = StairMeshBuilder.FlightSections(l);
            Assert.Greater(flights.Count, 0);
            var first = flights[0][0];
            Assert.AreEqual(0f, first.y, 1e-5f);
            Assert.Less((first.a - (l.bottomA - l.startDir * s.tread)).magnitude, 1e-3f, "run-in A");
            Assert.Less((first.b - (l.bottomB - l.startDir * s.tread)).magnitude, 1e-3f, "run-in B");
            foreach (var f in flights)
            {
                Assert.GreaterOrEqual(f.Count, 2);
                for (int i = 0; i + 1 < f.Count; i++)
                {
                    float plan = ((f[i + 1].a + f[i + 1].b) - (f[i].a + f[i].b)).magnitude * 0.5f;
                    Assert.Greater(f[i + 1].y, f[i].y, "height rises");
                    Assert.Greater(plan, 1e-3f, "no vertical step");
                }
            }
            var lastFlight = flights[flights.Count - 1];
            Assert.AreEqual(3f, lastFlight[lastFlight.Count - 1].y, 1e-4f);
        }
    }

    [Test] public void SingleRiser_RampSlab_NoRails()
    {
        var s = Straight();
        s.riserTarget = 3f;
        var l = Layout(s);
        Assert.AreEqual(1, l.risers);
        Assert.AreEqual(0, l.pieces.Count);
        var flights = StairMeshBuilder.FlightSections(l);
        Assert.AreEqual(1, flights.Count);
        Assert.AreEqual(2, flights[0].Count);
        Assert.Less((flights[0][0].a - (l.bottomA - l.startDir * 0.28f)).magnitude, 1e-3f);
        Assert.AreEqual(0f, flights[0][0].y, 1e-5f);
        Assert.Less((flights[0][1].a - l.topA).magnitude, 1e-3f);
        Assert.Less((flights[0][1].b - l.topB).magnitude, 1e-3f);
        Assert.AreEqual(3f, flights[0][1].y, 1e-4f);
        var ramp = StairMeshBuilder.Ramp(l, BaseY);
        Assert.Greater(ramp.vertices.Count, 0);
        WallMeshBuilderTests.AssertWindingMatchesNormals(ramp);
        Assert.AreEqual(0, StairMeshBuilder.Rails(s, l, BaseY).vertices.Count);
        Assert.AreEqual(s.width * 3f, Area(StairMeshBuilder.Steps(s, l, BaseY), "stair:s/riser"), 1e-3f);
    }

    [Test] public void Rails_StayWithinStair()
    {
        foreach (var s in AllShapes())
        {
            var l = Layout(s);
            var md = StairMeshBuilder.Rails(s, l, BaseY);
            var g = Group(md, "stair:s/rail");
            Assert.IsNotNull(g);
            foreach (int idx in g.triangles)
            {
                var p = Plan(md.vertices[idx]);
                Assert.IsFalse(StrictlyInside(l.departureZone, p), $"departure {p}");
                Assert.IsFalse(StrictlyInside(l.arrivalZone, p), $"arrival {p}");
            }
            // Rail top reaches piece height + 0.9 somewhere above the first piece.
            float maxY = float.MinValue;
            foreach (int idx in g.triangles) maxY = Mathf.Max(maxY, md.vertices[idx].y);
            Assert.AreEqual(BaseY + 3f + 0.9f, maxY, 1e-3f);
        }

        var none = Straight();
        none.railLeft = none.railRight = false;
        Assert.AreEqual(0, StairMeshBuilder.Rails(none, Layout(none), BaseY).vertices.Count);
    }

    [Test] public void Rails_OneSideOnly_IsHalf()
    {
        var both = Straight();
        var left = Straight();
        left.railRight = false;
        float a2 = Area(StairMeshBuilder.Rails(both, Layout(both), BaseY), "stair:s/rail");
        float a1 = Area(StairMeshBuilder.Rails(left, Layout(left), BaseY), "stair:s/rail");
        Assert.Greater(a1, 0f);
        Assert.AreEqual(a2 * 0.5f, a1, 1e-3f);
        // Left rail sits on the frontA side (x < 0 for a stair climbing +y).
        var md = StairMeshBuilder.Rails(left, Layout(left), BaseY);
        foreach (var v in md.vertices) Assert.Less(v.x, 0f);
    }

    [Test] public void InvalidLayout_ReturnsEmpty()
    {
        var s = Straight();
        var bad = new StairLayoutResult { ok = false };
        Assert.AreEqual(0, StairMeshBuilder.Steps(s, bad, 0f).vertices.Count);
        Assert.AreEqual(0, StairMeshBuilder.Ramp(bad, 0f).vertices.Count);
        Assert.AreEqual(0, StairMeshBuilder.Rails(s, bad, 0f).vertices.Count);
    }
}
