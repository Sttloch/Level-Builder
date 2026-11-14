using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder;
using LevelBuilder.Geometry;

public class StairLayoutTests
{
    const float Eps = 1e-3f;

    static StairPoint P(float x, float y, StairPointKind k = StairPointKind.Corner) =>
        new StairPoint { position = new Vector2(x, y), kind = k };

    static StairData Stair(params StairPoint[] pts) =>
        new StairData { id = "s", route = new List<StairPoint>(pts) };

    static FloorData From(float height = 3f) => new FloorData { elevation = 0f, height = height, slabThickness = 0.2f };
    static FloorData To(float elevation = 3f) => new FloorData { elevation = elevation, height = 3f, slabThickness = 0.2f };

    static StairLayoutResult Compute(StairData s, float height = 3f) => StairLayout.Compute(s, From(height), To(height));

    static Vector2 Centroid(List<Vector2> poly)
    {
        float a = 0f, cx = 0f, cy = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            var u = poly[i]; var v = poly[(i + 1) % poly.Count];
            float c = u.x * v.y - v.x * u.y;
            a += c; cx += (u.x + v.x) * c; cy += (u.y + v.y) * c;
        }
        a *= 0.5f;
        return new Vector2(cx / (6f * a), cy / (6f * a));
    }

    static bool OnBoundary(List<Vector2> poly, Vector2 p)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            if ((a + ab * t - p).magnitude < Eps) return true;
        }
        return false;
    }

    static bool InsideOrOn(List<Vector2> poly, Vector2 p) => PlanarFaces.PointInPolygon(p, poly) || OnBoundary(poly, p);

    static float MinY(List<Vector2> p) { float m = float.MaxValue; foreach (var v in p) m = Mathf.Min(m, v.y); return m; }
    static float MaxY(List<Vector2> p) { float m = float.MinValue; foreach (var v in p) m = Mathf.Max(m, v.y); return m; }

    [Test] public void Straight_RiserCountAndTread()
    {
        var r = Compute(Stair(P(0, 0), P(0, 10)));
        Assert.IsTrue(r.ok, r.error);
        Assert.AreEqual(17, r.risers);
        Assert.AreEqual(3f / 17f, r.riser, 1e-4f);
        Assert.AreEqual(16, r.pieces.Count);
        foreach (var p in r.pieces)
        {
            Assert.AreEqual(StairPieceKind.Tread, p.kind);
            Assert.AreEqual(0.28f, MaxY(p.polygon) - MinY(p.polygon), Eps);
            Assert.Greater(Triangulator.SignedArea(p.polygon), 0f);
        }
        Assert.AreEqual(4.48f, r.runLength, Eps);
        var p0 = r.pieces[0];
        Assert.AreEqual(0.17647f, p0.height, 1e-4f);
        Assert.AreEqual(0f, MinY(p0.polygon), Eps);
        Assert.AreEqual(0.28f, MaxY(p0.polygon), Eps);
        Assert.AreEqual(new Vector2(-0.5f, 0f).x, p0.frontA.x, Eps); // left of (0,1) is (-1,0)
        Assert.AreEqual(0.5f, p0.frontB.x, Eps);
        Assert.AreEqual(16f * 3f / 17f, r.pieces[15].height, 1e-4f);
    }

    [Test] public void Straight_ShortRoute_IsExtended()
    {
        var r = Compute(Stair(P(0, 0), P(0, 1)));
        Assert.IsTrue(r.ok, r.error);
        Assert.AreEqual(16, r.pieces.Count);
        Assert.AreEqual(4.48f, r.topA.y, Eps);
        Assert.AreEqual(4.48f, r.topB.y, Eps);
        Assert.AreEqual(-0.5f, r.topA.x, Eps);
        Assert.AreEqual(0.5f, r.topB.x, Eps);
    }

    [Test] public void LShape_HasOneLandingAtCorner()
    {
        var r = Compute(Stair(P(0, 0), P(0, 2), P(3, 2)));
        Assert.IsTrue(r.ok, r.error);
        Assert.AreEqual(16, r.pieces.Count);
        int li = r.pieces.FindIndex(p => p.kind == StairPieceKind.Landing);
        Assert.AreEqual(1, r.pieces.FindAll(p => p.kind == StairPieceKind.Landing).Count);
        var land = r.pieces[li].polygon;
        Assert.Greater(Triangulator.SignedArea(land), 0f);
        foreach (var c in new[] { new Vector2(-0.5f, 1.5f), new Vector2(0.5f, 1.5f), new Vector2(0.5f, 2.5f), new Vector2(-0.5f, 2.5f) })
            Assert.IsTrue(InsideOrOn(land, c), "corner " + c);
        float extent = MaxY(land) - MinY(land);
        Assert.GreaterOrEqual(extent, 1.0f - Eps);
        Assert.Less(extent, 1.28f);
        var next = r.pieces[li + 1];
        Assert.AreEqual(0.5f, next.frontA.x, Eps);
        Assert.AreEqual(0.5f, next.frontB.x, Eps);
        float prevX = 0.5f;
        for (int i = li + 2; i < r.pieces.Count; i++)
        {
            float x = (r.pieces[i].frontA.x + r.pieces[i].frontB.x) * 0.5f;
            Assert.Greater(x, prevX);
            prevX = x;
        }
        Assert.AreEqual(1f, r.endDir.x, Eps);
        Assert.AreEqual(0f, r.endDir.y, Eps);
    }

    [Test] public void SmallAngleCorner_IsSmooth()
    {
        float a = 20f * Mathf.Deg2Rad;
        var r = Compute(Stair(P(0, 0), P(0, 2), P(2f * Mathf.Sin(a), 2f + 2f * Mathf.Cos(a))));
        Assert.IsTrue(r.ok, r.error);
        Assert.AreEqual(16, r.pieces.Count);
        Assert.IsFalse(r.pieces.Exists(p => p.kind == StairPieceKind.Landing));
    }

    [Test] public void Curve_FanSteps_TreadOnCenterline()
    {
        var r = Compute(Stair(P(0, 0), P(0, 3, StairPointKind.Smooth), P(3, 3)));
        Assert.IsTrue(r.ok, r.error);
        Assert.AreEqual(16, r.pieces.Count);
        Assert.IsFalse(r.pieces.Exists(p => p.kind == StairPieceKind.Landing));
        // The midpoint of each riser edge lies on the centerline; consecutive ones are one tread apart.
        for (int i = 1; i < r.pieces.Count; i++)
        {
            var a = (r.pieces[i - 1].frontA + r.pieces[i - 1].frontB) * 0.5f;
            var b = (r.pieces[i].frontA + r.pieces[i].frontB) * 0.5f;
            Assert.AreEqual(0.28f, (b - a).magnitude, 0.02f, "piece " + i);
        }
        Assert.Greater(r.centerline.Count, 3);
    }

    [Test] public void Hole_CoversPiecesWithLowClearance()
    {
        var r = Compute(Stair(P(0, 0), P(0, 10)));
        Assert.IsNotNull(r.hole);
        Assert.Greater(Triangulator.SignedArea(r.hole), 0f);
        Assert.AreEqual(2.8f - 3f / 17f, r.pieces[0].clearance, 1e-3f);
        Assert.IsFalse(PlanarFaces.PointInPolygon(Centroid(r.pieces[0].polygon), r.hole));
        int inHole = 0;
        foreach (var p in r.pieces)
            if (p.clearance < StairLayout.Headroom)
            {
                Assert.IsTrue(PlanarFaces.PointInPolygon(Centroid(p.polygon), r.hole));
                inHole++;
            }
        Assert.Greater(inHole, 0);
    }

    [Test] public void LowerFloorHeight_GrowsHole()
    {
        // With H = 2.6 the stair itself is shorter (14 risers, 13 pieces), so the absolute hole area
        // (3.36 m²) is smaller than at 3.0 m (3.64 m²). What grows is the share of the stair that needs
        // the hole: it starts at an earlier piece and covers a larger fraction of the run.
        var a = Compute(Stair(P(0, 0), P(0, 10)), 3f);
        var b = Compute(Stair(P(0, 0), P(0, 10)), 2.6f);
        Assert.IsNotNull(a.hole); Assert.IsNotNull(b.hole);
        int fa = a.pieces.FindIndex(p => p.clearance < StairLayout.Headroom);
        int fb = b.pieces.FindIndex(p => p.clearance < StairLayout.Headroom);
        Assert.Less(fb, fa);
        float ra = Triangulator.SignedArea(a.hole) / (a.runLength * 1f);
        float rb = Triangulator.SignedArea(b.hole) / (b.runLength * 1f);
        Assert.Greater(rb, ra);
    }

    [Test] public void Zones_AreBehindStartAndBeyondEnd()
    {
        var r = Compute(Stair(P(0, 0), P(0, 10)));
        Assert.AreEqual(4, r.departureZone.Count);
        Assert.AreEqual(4, r.arrivalZone.Count);
        Assert.Greater(Triangulator.SignedArea(r.departureZone), 0f);
        Assert.Greater(Triangulator.SignedArea(r.arrivalZone), 0f);
        Assert.AreEqual(-1f, MinY(r.departureZone), Eps);
        Assert.AreEqual(0f, MaxY(r.departureZone), Eps);
        Assert.AreEqual(4.48f, MinY(r.arrivalZone), Eps);
        Assert.AreEqual(5.48f, MaxY(r.arrivalZone), Eps);
        Assert.AreEqual(1f, Triangulator.SignedArea(r.departureZone), Eps);
        Assert.AreEqual(0f, r.startDir.x, Eps); Assert.AreEqual(1f, r.startDir.y, Eps);
        Assert.AreEqual(-0.5f, r.bottomA.x, Eps); Assert.AreEqual(0.5f, r.bottomB.x, Eps);
    }

    [Test] public void DegenerateRoutes_NoThrow()
    {
        StairLayoutResult r = null;
        Assert.DoesNotThrow(() => r = Compute(Stair(P(0, 0), P(0, 0.005f), P(0, 0))));
        Assert.IsFalse(r.ok);
        Assert.AreEqual("RouteInvalid", r.error);

        Assert.DoesNotThrow(() => r = Compute(Stair(P(0, 0), P(0, 2), P(0, 1))));
        Assert.IsFalse(r.ok);
        Assert.AreEqual("RouteInvalid", r.error);

        Assert.DoesNotThrow(() => r = Compute(Stair(P(0, 0), P(float.NaN, 2))));
        Assert.IsFalse(r.ok);
        Assert.AreEqual("RouteInvalid", r.error);

        Assert.DoesNotThrow(() => r = Compute(Stair(P(0, 0), P(0, 3), P(2, 3), P(2, 1), P(-1, 1))));
        Assert.IsNotNull(r);

        Assert.DoesNotThrow(() => r = Compute(Stair(P(0, 0), P(0, 1, StairPointKind.Smooth), P(0.5f, 0.2f, StairPointKind.Smooth), P(1, 1))));
        Assert.IsNotNull(r);

        Assert.DoesNotThrow(() => r = StairLayout.Compute(new StairData { route = null }, From(), To()));
        Assert.IsFalse(r.ok);
    }
}
