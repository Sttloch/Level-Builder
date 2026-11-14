using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    public enum StairPieceKind { Tread, Landing }

    /// <summary>One walkable piece of a stair (tread or landing), in plan coordinates.</summary>
    public class StairPiece
    {
        public StairPieceKind kind;
        /// <summary>CCW plan polygon.</summary>
        public List<Vector2> polygon;
        /// <summary>Height above floor N.</summary>
        public float height;
        /// <summary>Edge of the riser that climbs onto this piece, left→right looking up the stair.</summary>
        public Vector2 frontA, frontB;
        /// <summary>Free height up to the underside of the upper slab.</summary>
        public float clearance;
    }

    public class StairLayoutResult
    {
        public bool ok;
        public string error;
        public int risers;
        public float riser;
        public float runLength;
        public List<Vector2> centerline = new List<Vector2>();
        public List<StairPiece> pieces = new List<StairPiece>();
        /// <summary>Edge of the first riser (left→right looking up).</summary>
        public Vector2 bottomA, bottomB;
        /// <summary>Edge of the last riser, the one reaching floor N+1 (left→right looking up).</summary>
        public Vector2 topA, topB;
        public Vector2 startDir, endDir;
        /// <summary>CCW rectangles.</summary>
        public List<Vector2> departureZone = new List<Vector2>(), arrivalZone = new List<Vector2>();
        /// <summary>CCW outline of the slab hole; null when no piece needs one.</summary>
        public List<Vector2> hole;
    }

    /// <summary>Pure plan geometry of a stair: risers, treads, landings, zones and slab hole.</summary>
    public static class StairLayout
    {
        public const float Headroom = 2.1f;
        public const float CornerAngle = 30f;

        const float MergeDist = 0.01f;
        const float MaxTurn = 179f;
        const float SampleStep = 0.05f;
        const float TangentDelta = 0.025f;
        const float Tol = 1e-4f;
        const int MaxRisers = 10000;

        public static StairLayoutResult Compute(StairData s, FloorData from, FloorData to)
        {
            var r = new StairLayoutResult();
            if (s == null || from == null || to == null) return Fail(r, "RouteInvalid");

            // 1. Clean the route.
            var pts = new List<Vector2>();
            var kinds = new List<StairPointKind>();
            if (s.route != null)
                foreach (var p in s.route)
                {
                    if (!Finite(p.position)) return Fail(r, "RouteInvalid");
                    if (pts.Count > 0 && (p.position - pts[pts.Count - 1]).magnitude < MergeDist) continue;
                    pts.Add(p.position);
                    kinds.Add(p.kind);
                }
            if (pts.Count < 2) return Fail(r, "RouteInvalid");
            var sharp = new bool[pts.Count];
            for (int i = 1; i < pts.Count - 1; i++)
            {
                float turn = Vector2.Angle(pts[i] - pts[i - 1], pts[i + 1] - pts[i]);
                if (!(turn < MaxTurn)) return Fail(r, "RouteInvalid");
                sharp[i] = kinds[i] == StairPointKind.Corner && turn > CornerAngle;
            }

            float H = to.elevation - from.elevation;
            float w = s.width, tread = s.tread;
            if (!(H > 0f) || !(s.riserTarget > 0f) || !(w > 0f) || !(tread > 0f) || float.IsInfinity(H) ||
                float.IsInfinity(w) || float.IsInfinity(tread))
                return Fail(r, "InvalidParameters");
            float nf = Mathf.Round(H / s.riserTarget);
            if (!(nf <= MaxRisers)) return Fail(r, "InvalidParameters");
            int n = Mathf.Max(1, (int)nf);
            r.risers = n;
            r.riser = H / n;
            float h = w * 0.5f;

            // 2. Centerline.
            var cl = new Centerline();
            var cornerIdx = new List<int>();
            cl.pts.Add(pts[0]);
            int a = 0;
            for (int b = 1; b < pts.Count; b++)
            {
                if (b < pts.Count - 1 && !sharp[b]) continue;
                if (b - a == 1) cl.Add(pts[b]);
                else AppendCatmullRom(cl, pts, a, b);
                if (b < pts.Count - 1) cornerIdx.Add(cl.pts.Count - 1);
                a = b;
            }
            cl.Finish();
            r.centerline = cl.pts;
            // Two landings closer than the width would overlap: the second corner could not get its own landing.
            for (int i = 1; i < cornerIdx.Count; i++)
                if (cl.cum[cornerIdx[i]] - cl.cum[cornerIdx[i - 1]] < w - Tol) return Fail(r, "RouteInvalid");

            // 3. Walk the centerline.
            float ceiling = to.elevation - to.slabThickness;
            float sPos = 0f;
            int ci = 0;
            for (int k = 0; k < n - 1; k++)
            {
                while (ci < cornerIdx.Count && cl.cum[cornerIdx[ci]] <= sPos + Tol) ci++;
                var piece = new StairPiece { height = (k + 1) * r.riser };
                piece.clearance = ceiling - (from.elevation + piece.height);
                cl.Section(sPos, h, out piece.frontA, out piece.frontB);
                List<Vector2> left, right;
                if (ci < cornerIdx.Count && sPos + tread > cl.cum[cornerIdx[ci]] - h + Tol)
                {
                    int vi = cornerIdx[ci++];
                    piece.kind = StairPieceKind.Landing;
                    Landing(cl, vi, sPos, h, out left, out right);
                    sPos = cl.cum[vi] + h;
                }
                else
                {
                    piece.kind = StairPieceKind.Tread;
                    cl.Section(sPos + tread, h, out var l1, out var r1);
                    left = new List<Vector2> { piece.frontA, l1 };
                    right = new List<Vector2> { piece.frontB, r1 };
                    sPos += tread;
                }
                piece.polygon = Outline(right, left);
                r.pieces.Add(piece);
            }

            r.runLength = sPos;
            r.startDir = cl.Tangent(0f);
            r.endDir = cl.Tangent(sPos);
            cl.Section(0f, h, out r.bottomA, out r.bottomB);
            cl.Section(sPos, h, out r.topA, out r.topB);

            // 5. Zones.
            float depth = Mathf.Max(w, 1f);
            var back = r.startDir * depth;
            r.departureZone = new List<Vector2> { r.bottomA - back, r.bottomB - back, r.bottomB, r.bottomA };
            var fwd = r.endDir * depth;
            r.arrivalZone = new List<Vector2> { r.topB, r.topB + fwd, r.topA + fwd, r.topA };

            // 6. Hole: boundary of the union of the pieces that lack headroom (the last riser has no plan area).
            int first = r.pieces.FindIndex(p => p.clearance < Headroom);
            if (first >= 0)
            {
                var polys = new List<List<Vector2>>();
                for (int k = first; k < r.pieces.Count; k++) polys.Add(r.pieces[k].polygon);
                r.hole = UnionOutline(polys);
                if (r.hole == null)
                {
                    var all = new List<Vector2>();
                    foreach (var p in polys) all.AddRange(p);
                    r.hole = ConvexHull(all);
                }
            }

            r.ok = true;
            return r;
        }

        static StairLayoutResult Fail(StairLayoutResult r, string error)
        {
            r.ok = false;
            r.error = error;
            return r;
        }

        static bool Finite(Vector2 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y);

        static Vector2 Left(Vector2 d) => new Vector2(-d.y, d.x);

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>
        /// Landing at centerline vertex vi, starting at centerline position s: covers the incoming leg up to the
        /// corner, the square of side 2h around the corner and h of the outgoing leg. Chains run forward.
        /// </summary>
        static void Landing(Centerline cl, int vi, float s, float h, out List<Vector2> left, out List<Vector2> right)
        {
            var c = cl.pts[vi];
            var t1 = cl.SegDir(vi - 1);
            var t2 = cl.SegDir(vi);
            var l1 = Left(t1);
            var l2 = Left(t2);
            cl.Section(s, h, out var ls, out var rs);
            var le = c + t2 * h + l2 * h;
            var re = c + t2 * h - l2 * h;
            float turn = Vector2.Angle(t1, t2);
            bool rightTurn = Cross(t1, t2) < 0f;
            // Outer side = opposite to the turn; inner side = towards it.
            Vector2 o1 = rightTurn ? l1 : -l1, o2 = rightTurn ? l2 : -l2;
            var outer = new List<Vector2> { rightTurn ? ls : rs, c + o1 * h + t1 * h };
            if (turn > 90f + 0.1f) outer.Add(c + o2 * h - t2 * h);
            outer.Add(rightTurn ? le : re);
            var inner = new List<Vector2> { rightTurn ? rs : ls };
            if (turn < 90f - 0.1f)
            {
                var m = -(o1 + o2) * (h / (1f + Vector2.Dot(o1, o2))); // inner miter
                inner.Add(c + m);
            }
            inner.Add(rightTurn ? re : le);
            left = rightTurn ? outer : inner;
            right = rightTurn ? inner : outer;
        }

        /// <summary>Right chain forward + left chain backward, deduplicated, CCW; convex hull if not simple.</summary>
        static List<Vector2> Outline(List<Vector2> right, List<Vector2> left)
        {
            var poly = new List<Vector2>();
            foreach (var p in right) AddUnique(poly, p);
            for (int i = left.Count - 1; i >= 0; i--) AddUnique(poly, left[i]);
            while (poly.Count > 1 && (poly[0] - poly[poly.Count - 1]).sqrMagnitude < Tol * Tol) poly.RemoveAt(poly.Count - 1);
            if (poly.Count < 3 || !IsSimple(poly)) return ConvexHull(poly);
            if (Triangulator.SignedArea(poly) < 0f) poly.Reverse();
            return poly;
        }

        const float SnapTol = 1e-3f;

        /// <summary>
        /// Outline of the union of CCW polygons that touch along edges (no proper overlaps): edges are split at
        /// each other's vertices, coincident opposite edges cancel, and the rest is chained into loops.
        /// Returns the single outer loop (CCW, collinear vertices merged), or null when there is not exactly
        /// one outer loop or the result is not simple.
        /// </summary>
        internal static List<Vector2> UnionOutline(List<List<Vector2>> polys)
        {
            var verts = new List<Vector2>();
            int VertexOf(Vector2 p)
            {
                for (int i = 0; i < verts.Count; i++)
                    if ((verts[i] - p).sqrMagnitude < SnapTol * SnapTol) return i;
                verts.Add(p);
                return verts.Count - 1;
            }
            var raw = new List<(int a, int b)>();
            foreach (var poly in polys)
            {
                if (poly == null || poly.Count < 3) continue;
                var idx = new int[poly.Count];
                for (int i = 0; i < poly.Count; i++) idx[i] = VertexOf(poly[i]);
                for (int i = 0; i < idx.Length; i++)
                    if (idx[i] != idx[(i + 1) % idx.Length]) raw.Add((idx[i], idx[(i + 1) % idx.Length]));
            }

            // Split every edge at the vertices lying on it, then cancel shared (opposite) edges.
            var set = new HashSet<(int, int)>();
            var order = new List<(int a, int b)>();
            var cuts = new List<(float t, int v)>();
            foreach (var (a, b) in raw)
            {
                var pa = verts[a]; var ab = verts[b] - pa;
                float len2 = ab.sqrMagnitude;
                cuts.Clear();
                for (int v = 0; v < verts.Count; v++)
                {
                    if (v == a || v == b) continue;
                    float t = Vector2.Dot(verts[v] - pa, ab) / len2;
                    if (t <= 0f || t >= 1f) continue;
                    if ((pa + ab * t - verts[v]).sqrMagnitude < SnapTol * SnapTol) cuts.Add((t, v));
                }
                cuts.Sort((x, y) => x.t.CompareTo(y.t));
                int prev = a;
                for (int i = 0; i <= cuts.Count; i++)
                {
                    int next = i < cuts.Count ? cuts[i].v : b;
                    if (next == prev) continue;
                    if (!set.Remove((next, prev)) && set.Add((prev, next))) order.Add((prev, next));
                    prev = next;
                }
            }

            var outgoing = new Dictionary<int, List<int>>();
            int remaining = 0;
            foreach (var e in order)
            {
                if (!set.Contains(e)) continue;
                if (!outgoing.TryGetValue(e.a, out var l)) outgoing[e.a] = l = new List<int>();
                l.Add(e.b);
                remaining++;
            }

            // Chain into loops; at a pinch vertex take the leftmost turn so touching regions stay separate loops
            // (and then fall back to the hull).
            List<Vector2> outer = null;
            int outerCount = 0;
            foreach (var e in order)
            {
                if (!set.Contains(e)) continue;
                var loop = new List<Vector2> { verts[e.a] };
                set.Remove(e);
                int start = e.a, prevV = e.a, cur = e.b, guard = remaining + 1;
                while (cur != start)
                {
                    if (--guard < 0) return null;
                    loop.Add(verts[cur]);
                    var din = verts[cur] - verts[prevV];
                    int best = -1;
                    float bestAngle = float.MinValue;
                    if (outgoing.TryGetValue(cur, out var outs))
                        foreach (int nb in outs)
                        {
                            if (!set.Contains((cur, nb))) continue;
                            float ang = Vector2.SignedAngle(din, verts[nb] - verts[cur]);
                            if (ang > bestAngle) { bestAngle = ang; best = nb; }
                        }
                    if (best < 0) return null;
                    set.Remove((cur, best));
                    prevV = cur;
                    cur = best;
                }
                if (Triangulator.SignedArea(loop) > 0f) { outer = loop; outerCount++; }
            }
            if (outerCount != 1) return null;

            // Merge collinear consecutive vertices.
            bool changed = true;
            while (changed && outer.Count > 3)
            {
                changed = false;
                for (int i = 0; i < outer.Count && outer.Count > 3; i++)
                {
                    var p0 = outer[(i + outer.Count - 1) % outer.Count];
                    var p1 = outer[i];
                    var p2 = outer[(i + 1) % outer.Count];
                    var u = (p1 - p0).normalized; var v = (p2 - p1).normalized;
                    if (Mathf.Abs(Cross(u, v)) < 1e-4f && Vector2.Dot(u, v) > 0f)
                    {
                        outer.RemoveAt(i);
                        changed = true;
                        i--;
                    }
                }
            }
            return IsSimple(outer) ? outer : null;
        }

        static void AddUnique(List<Vector2> l, Vector2 p)
        {
            if (l.Count == 0 || (l[l.Count - 1] - p).sqrMagnitude >= Tol * Tol) l.Add(p);
        }

        static bool IsSimple(List<Vector2> p)
        {
            int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                var a = p[i]; var b = p[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i || (j + 1) % n == i || j == (i + 1) % n) continue; // adjacent edges share a vertex
                    if (SegmentsTouch(a, b, p[j], p[(j + 1) % n])) return false;
                }
            }
            return true;
        }

        static bool SegmentsTouch(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(b - a, c - a), d2 = Cross(b - a, d - a);
            float d3 = Cross(d - c, a - c), d4 = Cross(d - c, b - c);
            const float e = 1e-7f;
            if (((d1 > e && d2 < -e) || (d1 < -e && d2 > e)) && ((d3 > e && d4 < -e) || (d3 < -e && d4 > e))) return true;
            return (Mathf.Abs(d1) <= e && OnSeg(a, b, c)) || (Mathf.Abs(d2) <= e && OnSeg(a, b, d)) ||
                   (Mathf.Abs(d3) <= e && OnSeg(c, d, a)) || (Mathf.Abs(d4) <= e && OnSeg(c, d, b));
        }

        static bool OnSeg(Vector2 a, Vector2 b, Vector2 p) =>
            p.x >= Mathf.Min(a.x, b.x) - 1e-6f && p.x <= Mathf.Max(a.x, b.x) + 1e-6f &&
            p.y >= Mathf.Min(a.y, b.y) - 1e-6f && p.y <= Mathf.Max(a.y, b.y) + 1e-6f;

        /// <summary>Andrew's monotone chain, CCW, collinear points dropped.</summary>
        static List<Vector2> ConvexHull(List<Vector2> pts)
        {
            var p = new List<Vector2>(pts);
            p.Sort((u, v) => u.x != v.x ? u.x.CompareTo(v.x) : u.y.CompareTo(v.y));
            if (p.Count < 3) return p;
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    var q = pass == 0 ? p[i] : p[p.Count - 1 - i];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], q - hull[hull.Count - 2]) <= 0f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(q);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        /// <summary>Centripetal Catmull-Rom through pts[a..b] (phantom ends mirrored), appended to the centerline.</summary>
        static void AppendCatmullRom(Centerline cl, List<Vector2> pts, int a, int b)
        {
            for (int i = a; i < b; i++)
            {
                var p0 = i > a ? pts[i - 1] : 2f * pts[a] - pts[a + 1];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = i + 2 <= b ? pts[i + 2] : 2f * pts[b] - pts[b - 1];
                float t0 = 0f;
                float t1 = t0 + Mathf.Sqrt((p1 - p0).magnitude);
                float t2 = t1 + Mathf.Sqrt((p2 - p1).magnitude);
                float t3 = t2 + Mathf.Sqrt((p3 - p2).magnitude);
                int steps = Mathf.Clamp(Mathf.CeilToInt((p2 - p1).magnitude / SampleStep), 1, 100000);
                for (int k = 1; k < steps; k++)
                {
                    float t = Mathf.Lerp(t1, t2, (float)k / steps);
                    var a1 = Mix(p0, p1, t0, t1, t);
                    var a2 = Mix(p1, p2, t1, t2, t);
                    var a3 = Mix(p2, p3, t2, t3, t);
                    var b1 = Mix(a1, a2, t0, t2, t);
                    var b2 = Mix(a2, a3, t1, t3, t);
                    cl.Add(Mix(b1, b2, t1, t2, t));
                }
                cl.Add(p2);
            }
        }

        static Vector2 Mix(Vector2 p, Vector2 q, float ta, float tb, float t) =>
            tb - ta < 1e-12f ? p : p * ((tb - t) / (tb - ta)) + q * ((t - ta) / (tb - ta));

        /// <summary>Polyline with arc-length lookup, extended straight beyond both ends.</summary>
        class Centerline
        {
            public readonly List<Vector2> pts = new List<Vector2>();
            public float[] cum;

            public void Add(Vector2 p)
            {
                if ((p - pts[pts.Count - 1]).sqrMagnitude > 1e-10f) pts.Add(p);
            }

            public void Finish()
            {
                cum = new float[pts.Count];
                for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + (pts[i] - pts[i - 1]).magnitude;
            }

            public float Length => cum[cum.Length - 1];

            public Vector2 SegDir(int i)
            {
                i = Mathf.Clamp(i, 0, pts.Count - 2);
                return (pts[i + 1] - pts[i]).normalized;
            }

            public Vector2 PointAt(float s)
            {
                if (s <= 0f) return pts[0] + SegDir(0) * s;
                if (s >= Length) return pts[pts.Count - 1] + SegDir(pts.Count - 2) * (s - Length);
                int lo = 0, hi = pts.Count - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (cum[mid] <= s) lo = mid; else hi = mid;
                }
                float len = cum[hi] - cum[lo];
                return len < 1e-12f ? pts[lo] : Vector2.Lerp(pts[lo], pts[hi], (s - cum[lo]) / len);
            }

            public Vector2 Tangent(float s)
            {
                var d = PointAt(s + TangentDelta) - PointAt(s - TangentDelta);
                return d.sqrMagnitude < 1e-12f ? SegDir(0) : d.normalized;
            }

            /// <summary>Cross-section at s: left and right points at half-width h.</summary>
            public void Section(float s, float h, out Vector2 left, out Vector2 right)
            {
                var c = PointAt(s);
                var l = Left(Tangent(s)) * h;
                left = c + l;
                right = c - l;
            }
        }
    }
}
