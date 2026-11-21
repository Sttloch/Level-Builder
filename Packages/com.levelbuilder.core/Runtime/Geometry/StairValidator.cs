using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    public enum StairIssue { RouteInvalid, NoFloorAbove, DepartureBlocked, ArrivalBlocked, Collision, HoleCrossesWall, HoleOutsideFloor }

    public class StairValidation
    {
        public StairLayoutResult layout;
        /// <summary>In enum order, no duplicates.</summary>
        public List<StairIssue> issues = new List<StairIssue>();
        public bool Valid => issues.Count == 0;
    }

    /// <summary>
    /// Checks a stair against the walls, floor regions and floor-level door clearances of floors N and N+1 and
    /// against the other stairs: their pieces on the same floor and their slab holes.
    /// </summary>
    public static class StairValidator
    {
        /// <summary>Overlaps up to this depth count as touching, not cutting.</summary>
        const float TouchTol = 1e-4f;
        const float OnEdgeTol = 1e-3f;
        /// <summary>Free depth kept in front of a floor-level door or empty opening, on both sides of the wall.</summary>
        public const float DoorClearance = 1.0f;
        /// <summary>Openings with a sill up to this height count as floor level.</summary>
        const float FloorSillTol = 0.01f;

        public static StairValidation Validate(BuildingData data, StairData s)
        {
            var v = new StairValidation();
            int fi = data != null && s != null ? data.FloorIndex(s.fromFloorId) : -1;
            int ti = data != null && s != null ? data.FloorIndex(s.toFloorId) : -1;
            if (fi < 0 || ti < 0 || ti != fi + 1)
            {
                v.layout = new StairLayoutResult { ok = false, error = "NoFloorAbove" };
                v.issues.Add(StairIssue.NoFloorAbove);
                return v;
            }
            var from = data.floors[fi];
            var to = data.floors[ti];
            var L = v.layout = StairLayout.Compute(s, from, to);
            if (!L.ok)
            {
                v.issues.Add(StairIssue.RouteInvalid);
                return v;
            }

            var wallsN = WallFootprints(from);
            var wallsUp = WallFootprints(to);
            var regionsN = PlanarFaces.Compute(from).regions;
            var regionsUp = PlanarFaces.Compute(to).regions;

            // Floor N: walls, door clearances, pieces of the other stairs leaving N and holes of the stairs arriving
            // at N. Floor N+1: the same for the stairs leaving / arriving at N+1.
            var obstaclesN = new List<List<Vector2>>(wallsN);
            obstaclesN.AddRange(DoorClearances(from));
            var obstaclesUp = new List<List<Vector2>>(wallsUp);
            obstaclesUp.AddRange(DoorClearances(to));
            var holesUp = new List<List<Vector2>>();
            foreach (var o in data.stairs ?? new List<StairData>())
            {
                // Same id: the stair itself (e.g. a candidate route of it being validated), never an obstacle.
                if (o == null || ReferenceEquals(o, s) || (!string.IsNullOrEmpty(s.id) && o.id == s.id)) continue;
                int ofi = data.FloorIndex(o.fromFloorId), oti = data.FloorIndex(o.toFloorId);
                if (ofi < 0 || oti != ofi + 1) continue;
                if (ofi != fi && ofi != ti && oti != fi && oti != ti) continue;
                var ol = StairLayout.Compute(o, data.floors[ofi], data.floors[oti]);
                if (!ol.ok) continue;
                foreach (var p in ol.pieces)
                {
                    if (ofi == fi) obstaclesN.Add(p.polygon);
                    if (ofi == ti) obstaclesUp.Add(p.polygon);
                }
                if (ol.hole == null) continue;
                if (oti == fi) obstaclesN.Add(ol.hole);
                if (oti == ti) { obstaclesUp.Add(ol.hole); holesUp.Add(ol.hole); }
            }

            if (CutsAny(L.departureZone, obstaclesN) || !InsideOneRegion(L.departureZone, regionsN))
                v.issues.Add(StairIssue.DepartureBlocked);
            // Arriving over its own hole is no arrival either.
            if (CutsAny(L.arrivalZone, obstaclesUp) || !InsideOneRegion(L.arrivalZone, regionsUp) ||
                (L.hole != null && CutsAny(L.arrivalZone, new List<List<Vector2>> { L.hole })))
                v.issues.Add(StairIssue.ArrivalBlocked);
            foreach (var p in L.pieces)
                if (CutsAny(p.polygon, obstaclesN)) { v.issues.Add(StairIssue.Collision); break; }
            if (L.hole != null)
            {
                if (CutsAny(L.hole, wallsUp) || CutsAny(L.hole, holesUp)) v.issues.Add(StairIssue.HoleCrossesWall);
                else if (!InsideOneRegion(L.hole, regionsUp)) v.issues.Add(StairIssue.HoleOutsideFloor);
            }
            return v;
        }

        /// <summary>
        /// CCW rectangle in front of each valid floor-level door or empty opening: the opening's width by
        /// DoorClearance beyond each face of the wall (through the wall).
        /// </summary>
        internal static List<List<Vector2>> DoorClearances(FloorData f)
        {
            var list = new List<List<Vector2>>();
            if (f == null || f.segments == null) return list;
            foreach (var kv in OpeningLayout.Compute(f))
            {
                var seg = f.GetSegment(kv.Key);
                var na = seg != null ? f.GetNode(seg.startNodeId) : null;
                var nb = seg != null ? f.GetNode(seg.endNodeId) : null;
                if (na == null || nb == null) continue;
                var d = nb.position - na.position;
                if (d.magnitude < 1e-6f) continue;
                d.Normalize();
                var n = new Vector2(-d.y, d.x) * (seg.thickness * 0.5f + DoorClearance);
                foreach (var r in kv.Value)
                {
                    var o = r.opening;
                    if (!r.valid || o == null || (o.type != OpeningType.Door && o.type != OpeningType.Empty)) continue;
                    if (!(Mathf.Abs(o.sill) <= FloorSillTol)) continue;
                    var a = na.position + d * r.u0;
                    var b = na.position + d * r.u1;
                    list.Add(new List<Vector2> { a - n, b - n, b + n, a + n });
                }
            }
            return list;
        }

        /// <summary>CCW rectangle of each wall: segment ± thickness/2, ends not extended.</summary>
        internal static List<List<Vector2>> WallFootprints(FloorData f)
        {
            var list = new List<List<Vector2>>();
            if (f == null || f.segments == null) return list;
            foreach (var seg in f.segments)
            {
                var na = f.GetNode(seg.startNodeId);
                var nb = f.GetNode(seg.endNodeId);
                if (na == null || nb == null) continue;
                if (seg.IsWallLess(f)) continue; // wall-less edge: not an obstacle
                var d = nb.position - na.position;
                if (d.magnitude < 1e-6f) continue;
                var n = new Vector2(-d.y, d.x).normalized * (seg.thickness * 0.5f);
                // a-n, b-n, b+n, a+n is CCW (n is to the left of a->b).
                list.Add(new List<Vector2> { na.position - n, nb.position - n, nb.position + n, na.position + n });
            }
            return list;
        }

        internal static bool CutsAny(List<Vector2> poly, List<List<Vector2>> others)
        {
            if (poly == null || poly.Count < 3) return false;
            var a = ConvexParts(poly);
            foreach (var o in others)
            {
                var b = ConvexParts(o);
                foreach (var pa in a)
                    foreach (var pb in b)
                        if (ConvexOverlap(pa, pb)) return true;
            }
            return false;
        }

        /// <summary>The polygon itself if convex, otherwise its triangles.</summary>
        internal static List<List<Vector2>> ConvexParts(List<Vector2> poly)
        {
            var parts = new List<List<Vector2>>();
            if (IsConvex(poly)) { parts.Add(poly); return parts; }
            var outer = new List<Vector2>(poly);
            if (Triangulator.SignedArea(outer) < 0f) outer.Reverse();
            var tris = Triangulator.Triangulate(outer, new List<IList<Vector2>>(), out var verts);
            for (int i = 0; i + 2 < tris.Count; i += 3)
                parts.Add(new List<Vector2> { verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]] });
            if (parts.Count == 0) parts.Add(poly);
            return parts;
        }

        static bool IsConvex(List<Vector2> p)
        {
            int n = p.Count, sign = 0;
            for (int i = 0; i < n; i++)
            {
                var a = p[i]; var b = p[(i + 1) % n]; var c = p[(i + 2) % n];
                float cr = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                if (Mathf.Abs(cr) < 1e-9f) continue;
                int sg = cr > 0f ? 1 : -1;
                if (sign == 0) sign = sg;
                else if (sg != sign) return false;
            }
            return true;
        }

        /// <summary>Separating axis test: true when the convex polygons overlap deeper than TouchTol on every axis.</summary>
        internal static bool ConvexOverlap(List<Vector2> a, List<Vector2> b)
        {
            return !HasSeparatingAxis(a, a, b) && !HasSeparatingAxis(b, a, b);
        }

        static bool HasSeparatingAxis(List<Vector2> edges, List<Vector2> a, List<Vector2> b)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[(i + 1) % edges.Count] - edges[i];
                if (e.sqrMagnitude < 1e-12f) continue;
                var axis = new Vector2(-e.y, e.x).normalized;
                Project(a, axis, out float minA, out float maxA);
                Project(b, axis, out float minB, out float maxB);
                if (Mathf.Min(maxA, maxB) - Mathf.Max(minA, minB) <= TouchTol) return true;
            }
            return false;
        }

        static void Project(List<Vector2> p, Vector2 axis, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            foreach (var v in p)
            {
                float d = Vector2.Dot(v, axis);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }

        /// <summary>Every vertex inside (or on) one region's outer polygon and not strictly inside its holes.</summary>
        internal static bool InsideOneRegion(List<Vector2> pts, List<Region> regions)
        {
            foreach (var r in regions)
            {
                bool all = true;
                foreach (var p in pts)
                {
                    if (!InsideOrOn(p, r.polygon)) { all = false; break; }
                    foreach (var h in r.holes)
                        if (PlanarFaces.PointInPolygon(p, h) && DistanceToBoundary(p, h) > OnEdgeTol) { all = false; break; }
                    if (!all) break;
                }
                if (all) return true;
            }
            return false;
        }

        static bool InsideOrOn(Vector2 p, List<Vector2> poly) =>
            PlanarFaces.PointInPolygon(p, poly) || DistanceToBoundary(p, poly) <= OnEdgeTol;

        internal static float DistanceToBoundary(Vector2 p, List<Vector2> poly)
        {
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
                best = Mathf.Min(best, PointSegment(p, poly[i], poly[(i + 1) % poly.Count]));
            return best;
        }

        static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude;
        }

        /// <summary>Shortest distance between two polygons' boundaries; 0 when they cut each other.</summary>
        internal static float PolygonDistance(List<Vector2> a, List<Vector2> b)
        {
            if (CutsAny(a, new List<List<Vector2>> { b })) return 0f;
            float best = float.MaxValue;
            foreach (var p in a) best = Mathf.Min(best, DistanceToBoundary(p, b));
            foreach (var p in b) best = Mathf.Min(best, DistanceToBoundary(p, a));
            return best;
        }
    }
}
