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
        new StairData { id = "s", width = 1f, route = new List<StairPoint>(pts) };

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

    static Vector2 CenterlineTangentNear(List<Vector2> cl, Vector2 p)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i + 1 < cl.Count; i++)
        {
            var ab = cl[i + 1] - cl[i];
            float t = Mathf.Clamp01(Vector2.Dot(p - cl[i], ab) / ab.sqrMagnitude);
            float d = (cl[i] + ab * t - p).sqrMagnitude;
            if (d < bd) { bd = d; best = i; }
        }
        return (cl[best + 1] - cl[best]).normalized;
    }

    [Test] public void Curve_FanSteps_TreadOnCenterline()
    {
        var r = Compute(Stair(P(0, 0), P(0, 3, StairPointKind.Smooth), P(3, 3)));
        Assert.IsTrue(r.ok, r.error);
        Assert.AreEqual(16, r.pieces.Count);
        Assert.IsFalse(r.pieces.Exists(p => p.kind == StairPieceKind.Landing));
        Assert.Greater(r.centerline.Count, 3);
        int curved = 0;
        for (int i = 0; i < r.pieces.Count; i++)
        {
            var p = r.pieces[i];
            var mid = (p.frontA + p.frontB) * 0.5f;
            var edge = p.frontB - p.frontA;
            // Each riser edge is perpendicular to the centerline at its midpoint.
            float perp = Vector2.Angle(edge, CenterlineTangentNear(r.centerline, mid));
            Assert.AreEqual(90f, perp, 2f, "perpendicular, piece " + i);
            if (i == 0) continue;
            var prev = r.pieces[i - 1];
            var prevMid = (prev.frontA + prev.frontB) * 0.5f;
            Assert.AreEqual(0.28f, (mid - prevMid).magnitude, 0.02f, "tread, piece " + i);
            // On the bend (previous riser rotated 10°..80° from the first one) consecutive risers fan out.
            float turned = Vector2.Angle(r.pieces[0].frontB - r.pieces[0].frontA, prev.frontB - prev.frontA);
            if (turned > 10f && turned < 80f)
            {
                Assert.Greater(Vector2.Angle(prev.frontB - prev.frontA, edge), 1f, "fan, piece " + i);
                curved++;
            }
        }
        Assert.GreaterOrEqual(curved, 3);
    }

    static bool IsSimplePolygon(List<Vector2> p)
    {
        int n = p.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                Vector2 a = p[i], b = p[(i + 1) % n], c = p[j], d = p[(j + 1) % n];
                float d1 = Cr(b - a, c - a), d2 = Cr(b - a, d - a), d3 = Cr(d - c, a - c), d4 = Cr(d - c, b - c);
                if (d1 * d2 <= 0f && d3 * d4 <= 0f &&
                    Mathf.Max(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)) <= Mathf.Min(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)) + 1e-6f &&
                    Mathf.Max(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)) <= Mathf.Min(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)) + 1e-6f)
                    return false;
            }
        return true;
    }

    static float Cr(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    /// <summary>
    /// A piece is in the hole when the free height above the NEXT step up lacks headroom plus the capsule
    /// allowance (clearance is measured on the piece itself).
    /// </summary>
    static bool NeedsHole(StairLayoutResult r, StairPiece p) =>
        p.clearance - r.riser < StairLayout.Headroom + StairLayout.CapsuleAllowance;

    static void AssertHoleCoversNeeded(StairLayoutResult r)
    {
        Assert.IsNotNull(r.hole);
        Assert.Greater(Triangulator.SignedArea(r.hole), 0f);
        Assert.IsTrue(IsSimplePolygon(r.hole), "hole is simple");
        foreach (var p in r.pieces)
            if (NeedsHole(r, p))
                Assert.IsTrue(PlanarFaces.PointInPolygon(Centroid(p.polygon), r.hole), "piece at " + Centroid(p.polygon));
    }

    [Test] public void Hole_UStairTouchingFlights_IsUnionNotHull()
    {
        var r = Compute(Stair(P(0, 0), P(0, 4), P(1, 4), P(1, 0)));
        Assert.IsTrue(r.ok, r.error);
        Assert.AreEqual(2, r.pieces.FindAll(p => p.kind == StairPieceKind.Landing).Count);
        AssertHoleCoversNeeded(r);
        Assert.IsFalse(PlanarFaces.PointInPolygon(Centroid(r.arrivalZone), r.hole));
        float pieceArea = 0f;
        foreach (var p in r.pieces) if (NeedsHole(r, p)) pieceArea += Triangulator.SignedArea(p.polygon);
        Assert.AreEqual(pieceArea, Triangulator.SignedArea(r.hole), 1e-2f);
    }

    [Test] public void Hole_LStair_SpansLanding()
    {
        var r = Compute(Stair(P(0, 0), P(0, 2), P(3, 2)));
        var land = r.pieces.Find(p => p.kind == StairPieceKind.Landing);
        Assert.IsTrue(NeedsHole(r, land));
        AssertHoleCoversNeeded(r);
        Assert.IsTrue(PlanarFaces.PointInPolygon(Centroid(land.polygon), r.hole));
        Assert.IsFalse(PlanarFaces.PointInPolygon(Centroid(r.arrivalZone), r.hole));
    }

    [Test] public void LShapeLeftTurn_HasOneLandingAtCorner()
    {
        var r = Compute(Stair(P(0, 0), P(0, 2), P(-3, 2)));
        Assert.IsTrue(r.ok, r.error);
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
        Assert.AreEqual(-0.5f, next.frontA.x, Eps);
        Assert.AreEqual(-0.5f, next.frontB.x, Eps);
        Assert.AreEqual(1.5f, next.frontA.y, Eps); // left of (-1,0) is (0,-1)
        float prevX = -0.5f;
        for (int i = li + 2; i < r.pieces.Count; i++)
        {
            float x = (r.pieces[i].frontA.x + r.pieces[i].frontB.x) * 0.5f;
            Assert.Less(x, prevX);
            prevX = x;
        }
        Assert.AreEqual(-1f, r.endDir.x, Eps);
        Assert.AreEqual(0f, r.endDir.y, Eps);
        AssertHoleCoversNeeded(r);
    }

    [Test] public void CornersCloserThanWidth_AreRouteInvalid()
    {
        var r = Compute(Stair(P(0, 0), P(0, 4), P(0.8f, 4), P(0.8f, 0)));
        Assert.IsFalse(r.ok);
        Assert.AreEqual("RouteInvalid", r.error);
        Assert.IsTrue(Compute(Stair(P(0, 0), P(0, 4), P(1f, 4), P(1f, 0))).ok);
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
            if (NeedsHole(r, p))
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
        int fa = a.pieces.FindIndex(p => NeedsHole(a, p));
        int fb = b.pieces.FindIndex(p => NeedsHole(b, p));
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
    static bool HasEdge(List<Vector2> poly, Vector2 a, Vector2 b)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            var u = poly[i]; var v = poly[(i + 1) % poly.Count];
            if (((u - a).magnitude < Eps && (v - b).magnitude < Eps) || ((u - b).magnitude < Eps && (v - a).magnitude < Eps))
                return true;
        }
        return false;
    }

    static StairData Turn(float degrees, bool right, float leg = 2f)
    {
        float a = degrees * Mathf.Deg2Rad, sx = right ? 1f : -1f;
        return Stair(P(0, 0), P(0, leg), P(sx * 3f * Mathf.Sin(a), leg + 3f * Mathf.Cos(a)));
    }

    [Test] public void CornerLandings_ShareEntryAndExitEdges_NoOverlap()
    {
        foreach (float deg in new[] { 90f, 60f, 120f, 150f })
            foreach (bool right in new[] { true, false })
            {
                string tag = deg + (right ? " right" : " left");
                var r = Compute(Turn(deg, right, deg > 120f ? 3f : 2f));
                Assert.IsTrue(r.ok, tag + ": " + r.error);
                int li = r.pieces.FindIndex(p => p.kind == StairPieceKind.Landing);
                Assert.GreaterOrEqual(li, 0, tag);
                var land = r.pieces[li];
                Assert.Greater(Triangulator.SignedArea(land.polygon), 0f, tag);
                Assert.IsTrue(IsSimplePolygon(land.polygon), tag + " simple");
                Assert.IsTrue(HasEdge(land.polygon, land.frontA, land.frontB), tag + " entry edge");
                Vector2 na = li + 1 < r.pieces.Count ? r.pieces[li + 1].frontA : r.topA;
                Vector2 nb = li + 1 < r.pieces.Count ? r.pieces[li + 1].frontB : r.topB;
                Assert.IsTrue(HasEdge(land.polygon, na, nb), tag + " exit edge");
                for (int i = 0; i < r.pieces.Count; i++)
                {
                    var ci = Centroid(r.pieces[i].polygon);
                    for (int j = 0; j < r.pieces.Count; j++)
                        if (i != j)
                            Assert.IsFalse(PlanarFaces.PointInPolygon(ci, r.pieces[j].polygon), $"{tag}: piece {i} centroid inside piece {j}");
                }
            }
    }

    [Test] public void SingleCornerTurnOver150_IsRouteInvalid()
    {
        var r = Compute(Turn(160f, true, 3f));
        Assert.IsFalse(r.ok);
        Assert.AreEqual("RouteInvalid", r.error);
        Assert.IsTrue(Compute(Turn(150f, false, 3f)).ok);
    }

    [Test] public void Hole_EdgeLeavesCapsuleHeadroom([Values(2.7f, 3.0f, 3.3f)] float height)
    {
        // The slab edge sits on the front edge of the first piece in the hole, where the walking surface is at that
        // piece's height: the free height there must be at least Headroom + CapsuleAllowance (2.25 m).
        var r = Compute(Stair(P(0, 0), P(0, 10)), height);
        Assert.IsTrue(r.ok, r.error);
        Assert.IsNotNull(r.hole);
        float ceiling = height - 0.2f;
        int first = -1;
        for (int k = 0; k < r.pieces.Count; k++)
            if (PlanarFaces.PointInPolygon(Centroid(r.pieces[k].polygon), r.hole)) { first = k; break; }
        Assert.GreaterOrEqual(first, 0);
        for (int k = first; k < r.pieces.Count; k++)
            Assert.IsTrue(PlanarFaces.PointInPolygon(Centroid(r.pieces[k].polygon), r.hole), "piece " + k + " in hole");
        Assert.GreaterOrEqual(ceiling - r.pieces[first].height, StairLayout.Headroom + StairLayout.CapsuleAllowance - 1e-4f);
        // ... and the hole starts no earlier than needed.
        Assert.Less(ceiling - (r.pieces[first].height + r.riser), StairLayout.Headroom + StairLayout.CapsuleAllowance);
        // Hole front edge = first riser edge of that piece.
        Assert.AreEqual(r.pieces[first].frontA.y, MinY(r.hole), Eps);
    }

    [Test] public void SquareSpiral_OverlappingItsOwnRun_IsRouteInvalid()
    {
        // 2.6 m square spiral. On a 6 m floor (33 risers, 32 pieces) the fourth flight runs back over the first
        // one's start; on a 3 m floor (16 pieces) the stair ends on the third flight.
        var spiral = Stair(P(0, 0), P(0, 2.6f), P(2.6f, 2.6f), P(2.6f, 0), P(0, 0));
        var tall = StairLayout.Compute(spiral, From(6f), To(6f));
        Assert.IsFalse(tall.ok);
        Assert.AreEqual("RouteInvalid", tall.error);
        Assert.IsTrue(Compute(spiral).ok, Compute(spiral).error);
    }

    [Test] public void Hole_RampOff_AddsStepAllowance()
    {
        var on = Stair(P(0, 0), P(0, 10));
        var off = Stair(P(0, 0), P(0, 10));
        off.collisionRamp = false;
        var a = Compute(on, 3.3f);
        var c = Compute(off, 3.3f);
        float ceiling = 3.3f - 0.2f;
        int fa = a.pieces.FindIndex(p => PlanarFaces.PointInPolygon(Centroid(p.polygon), a.hole));
        int fc = c.pieces.FindIndex(p => PlanarFaces.PointInPolygon(Centroid(p.polygon), c.hole));
        Assert.Less(fc, fa);
        Assert.GreaterOrEqual(ceiling - c.pieces[fc].height,
            StairLayout.Headroom + StairLayout.CapsuleAllowance + StairLayout.StepAllowance - 1e-4f);
    }
}
