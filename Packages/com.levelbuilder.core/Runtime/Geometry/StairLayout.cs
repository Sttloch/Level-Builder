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
        /// <summary>
        /// Extra free height the hole leaves over a character capsule (skin and slope lift of a default
        /// CharacterController).
        /// </summary>
        public const float CapsuleAllowance = 0.15f;
        /// <summary>
        /// Extra free height over the hole edge for a CharacterController's step offset (0.3 by default): it first
        /// moves up by it on every grounded move, climbing the steps or walking down the collision ramp.
        /// </summary>
        public const float StepAllowance = 0.3f;
        /// <summary>Default StairData.headroom (Headroom + CapsuleAllowance), used when a stair carries none.</summary>
        public const float DefaultHeadroom = Headroom + CapsuleAllowance;
        /// <summary>Radius plus skin width of a default CharacterController (0.5 + 0.08).</summary>
        public const float CapsuleRadius = 0.58f;
        public const float CornerAngle = 30f;
        /// <summary>Largest turn a single Corner point may make (U-stairs use two corners).</summary>
        public const float MaxCornerTurn = 150f;

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
                if (sharp[i] && turn > MaxCornerTurn + Tol) return Fail(r, "RouteInvalid");
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
            // Landing reach along each leg from its corner: h up to 90°, h·tan(turn/2) beyond, so the outgoing
            // flight's inner side clears the incoming flight.
            var reach = new float[cornerIdx.Count];
            for (int i = 0; i < cornerIdx.Count; i++)
            {
                int vi = cornerIdx[i];
                float turn = Vector2.Angle(cl.SegDir(vi - 1), cl.SegDir(vi));
                reach[i] = h * Mathf.Max(1f, Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad));
            }
            // The first landing must fit after the start, and two landings must not overlap
            // (for two 90° corners: closer than the width).
            if (cornerIdx.Count > 0 && cl.cum[cornerIdx[0]] < reach[0] - Tol) return Fail(r, "RouteInvalid");
            for (int i = 1; i < cornerIdx.Count; i++)
                if (cl.cum[cornerIdx[i]] - cl.cum[cornerIdx[i - 1]] < reach[i - 1] + reach[i] - Tol) return Fail(r, "RouteInvalid");

            // 3. Walk the centerline.
            float ceiling = to.elevation - to.slabThickness;
            float sPos = 0f;
            int ci = 0;
            // Per piece: centerline start (for the hole extension strip).
            var starts = new List<float>();
            for (int k = 0; k < n - 1; k++)
            {
                while (ci < cornerIdx.Count && cl.cum[cornerIdx[ci]] <= sPos + Tol) ci++;
                var piece = new StairPiece { height = (k + 1) * r.riser };
                piece.clearance = ceiling - (from.elevation + piece.height);
                cl.Section(sPos, h, out piece.frontA, out piece.frontB);
                List<Vector2> left, right;
                starts.Add(sPos);
                if (ci < cornerIdx.Count && sPos + tread > cl.cum[cornerIdx[ci]] - reach[ci] + Tol)
                {
                    float e = reach[ci];
                    int vi = cornerIdx[ci++];
                    piece.kind = StairPieceKind.Landing;
                    Landing(cl, vi, sPos, h, e, out left, out right);
                    sPos = cl.cum[vi] + e;
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

            // 4. A route that runs back over itself (non-adjacent pieces overlapping) is not buildable.
            if (SelfOverlaps(r.pieces)) return Fail(r, "RouteInvalid");

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
            // A piece needs the hole when the free height above the NEXT step up is under s.headroom (default
            // Headroom + CapsuleAllowance) + StepAllowance: the slab edge sits on the piece's front edge, where the walking surface (ramp
            // through the nosings) is already at the piece's height, so the previous piece, left out, guarantees
            // that much free height under the edge.
            // StepAllowance applies with the collision ramp too: a CharacterController lifts by its step offset on
            // every grounded move, and walking DOWN its leading side reaches the edge while it still stands uphill.
            float riser = r.riser;
            // Clamped like BuildingData.ClampStair (hand-edited data); NaN / non-positive headroom -> default.
            float headroom = s.headroom > 0f ? Mathf.Clamp(s.headroom, 1.5f, 4.0f) : DefaultHeadroom;
            float margin = s.holeMargin > 0f ? Mathf.Clamp(s.holeMargin, 0f, 1.0f) : 0f;
            float need = headroom + StepAllowance;
            int first = r.pieces.FindIndex(p => p.clearance - riser < need);
            if (first >= 0)
            {
                // Extension before the stair: when the hole starts at the first piece, its edge would sit on the
                // bottom riser edge, where the walking surface is one riser up (ramp nosing / first step). If the
                // free height there, (ceiling - from.elevation) - riser, is under need, the hole runs back along
                // -startDir by ext. The ramp run-in rises linearly from 0 one tread before the edge to riser at it:
                // surface(-x) = riser * (1 - x / tread). With room = (ceiling - from.elevation) - need:
                //   room >= 0: ext = tread * (1 - room / riser), where the surface drops to room (conservative:
                //              the capsule clears the edge with need at the surface there);
                //   room <  0: the shortfall is at floor level, ext = tread + CapsuleRadius (whole run-in plus the
                //              capsule's leading side).
                float ext = 0f;
                float free0 = ceiling - from.elevation;
                if (first == 0 && free0 - riser < need)
                {
                    float room = free0 - need;
                    ext = room >= 0f ? tread * Mathf.Clamp01(1f - room / riser) : tread + CapsuleRadius;
                }
                // Base hole (margin 0): exact union of the needed pieces plus the extension strip at stair width.
                var polys = new List<List<Vector2>>();
                for (int k = first; k < r.pieces.Count; k++) polys.Add(r.pieces[k].polygon);
                if (ext > Tol)
                {
                    // Strip behind the first piece's front edge (straight: the centerline extends before 0).
                    float s1 = starts[first], s0 = s1 - ext;
                    cl.Section(s0, h, out var l0, out var r0);
                    cl.Section(s1, h, out var l1, out var r1);
                    polys.Add(Outline(new List<Vector2> { r0, r1 }, new List<Vector2> { l0, l1 }));
                }
                r.hole = UnionOutline(polys);
                if (r.hole == null)
                {
                    var all = new List<Vector2>();
                    foreach (var p in polys) all.AddRange(p);
                    r.hole = ConvexHull(all);
                }
                // holeMargin: outward offset of that single outline (back and sides), except along the arrival
                // zone's boundary (its near side is the top riser edge; on U-stairs a side can run beside a flight):
                // the arrival must stay clear of the hole.
                if (margin > 0f) r.hole = OffsetOutline(r.hole, margin, r.arrivalZone);
            }

            r.ok = true;
            return r;
        }

        /// <summary>
        /// Outward offset of a CCW simple polygon by d, except edges lying on an edge of keep (offset 0).
        /// Each vertex goes to the intersection of its two offset edge lines (miter); at convex vertices a miter
        /// farther than 2*d from the vertex is bevelled (one point per edge). Falls back to the convex hull of the
        /// offset points if the result is not simple.
        /// </summary>
        internal static List<Vector2> OffsetOutline(List<Vector2> poly, float d, List<Vector2> keep)
        {
            // Split the outline's edges at keep's vertices, so only the part of an edge along keep stays put.
            if (keep != null)
            {
                var split = new List<Vector2>();
                for (int i = 0; i < poly.Count; i++)
                {
                    var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                    split.Add(a);
                    var ab = b - a;
                    float len2 = ab.sqrMagnitude;
                    if (len2 < 1e-12f) continue;
                    var cuts = new List<float>();
                    foreach (var k in keep)
                    {
                        float t = Vector2.Dot(k - a, ab) / len2;
                        if (t > 1e-3f && t < 1f - 1e-3f && (a + ab * t - k).sqrMagnitude < SnapTol * SnapTol) cuts.Add(t);
                    }
                    cuts.Sort();
                    foreach (var t in cuts) split.Add(a + ab * t);
                }
                poly = split;
            }
            int n = poly.Count;
            var dist = new float[n]; // edge i = poly[i] -> poly[i + 1]
            for (int i = 0; i < n; i++)
            {
                dist[i] = d;
                if (keep == null) continue;
                for (int j = 0; j < keep.Count; j++)
                {
                    var ka = keep[j]; var kb = keep[(j + 1) % keep.Count];
                    if (OnSegment(ka, kb, poly[i]) && OnSegment(ka, kb, poly[(i + 1) % n])) { dist[i] = 0f; break; }
                }
            }
            var res = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                var v = poly[i];
                float d1 = dist[(i + n - 1) % n], d2 = dist[i];
                var e1 = (v - poly[(i + n - 1) % n]).normalized;
                var e2 = (poly[(i + 1) % n] - v).normalized;
                var n1 = new Vector2(e1.y, -e1.x);
                var n2 = new Vector2(e2.y, -e2.x);
                float det = Cross(n1, n2);
                if (Mathf.Abs(det) < 1e-4f)
                {
                    // (Nearly) collinear edges: a step when the offsets differ.
                    AddUnique(res, v + n1 * d1);
                    AddUnique(res, v + n2 * d2);
                    continue;
                }
                // x with dot(x, n1) = d1 and dot(x, n2) = d2.
                var x = new Vector2((d1 * n2.y - d2 * n1.y) / det, (n1.x * d2 - n2.x * d1) / det);
                bool reflex = Cross(e1, e2) < 0f;
                if (reflex || x.magnitude <= 2f * d + Tol) AddUnique(res, v + x);
                else
                {
                    AddUnique(res, v + n1 * d1);
                    AddUnique(res, v + n2 * d2);
                }
            }
            while (res.Count > 1 && (res[0] - res[res.Count - 1]).sqrMagnitude < Tol * Tol) res.RemoveAt(res.Count - 1);
            return res.Count >= 3 && IsSimple(res) ? res : ConvexHull(res);
        }

        static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-12f) return false;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return (a + ab * t - p).sqrMagnitude < SnapTol * SnapTol;
        }

        /// <summary>True when two non-adjacent pieces overlap deeper than the validator's touch tolerance.</summary>
        static bool SelfOverlaps(List<StairPiece> pieces)
        {
            int n = pieces.Count;
            if (n < 3) return false;
            var min = new Vector2[n];
            var max = new Vector2[n];
            var parts = new List<List<Vector2>>[n];
            for (int i = 0; i < n; i++)
            {
                min[i] = new Vector2(float.MaxValue, float.MaxValue);
                max[i] = new Vector2(float.MinValue, float.MinValue);
                foreach (var v in pieces[i].polygon) { min[i] = Vector2.Min(min[i], v); max[i] = Vector2.Max(max[i], v); }
            }
            for (int i = 0; i < n; i++)
                for (int j = i + 2; j < n; j++)
                {
                    if (min[i].x >= max[j].x || min[j].x >= max[i].x || min[i].y >= max[j].y || min[j].y >= max[i].y) continue;
                    if (parts[i] == null) parts[i] = StairValidator.ConvexParts(pieces[i].polygon);
                    if (parts[j] == null) parts[j] = StairValidator.ConvexParts(pieces[j].polygon);
                    foreach (var a in parts[i])
                        foreach (var b in parts[j])
                            if (StairValidator.ConvexOverlap(a, b)) return true;
                }
            return false;
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
        /// Landing at centerline vertex vi from centerline position s (entry riser edge) to cum[vi] + reach
        /// (exit edge = the next flight's first riser edge). Outer side: along the incoming outer offset line to
        /// its intersection with the outgoing one (miter) when that lies within the width of the corner,
        /// else bevelled through the corner square's two outer corners. Inner side: through the inner miter for
        /// turns under 90°; from 90° on the exit inner point already lies on the incoming inner line (reach is
        /// h·tan(turn/2)), so the inner side is straight. Callers guarantee s ≤ cum[vi] − reach, so nothing
        /// lies behind the entry edge. Chains run forward.
        /// </summary>
        static void Landing(Centerline cl, int vi, float s, float h, float reach, out List<Vector2> left, out List<Vector2> right)
        {
            var c = cl.pts[vi];
            var t1 = cl.SegDir(vi - 1);
            var t2 = cl.SegDir(vi);
            var l1 = Left(t1);
            var l2 = Left(t2);
            cl.Section(s, h, out var ls, out var rs);
            cl.Section(cl.cum[vi] + reach, h, out var le, out var re);
            float turn = Vector2.Angle(t1, t2);
            bool rightTurn = Cross(t1, t2) < 0f;
            // Outer side = opposite to the turn; inner side = towards it.
            Vector2 o1 = rightTurn ? l1 : -l1, o2 = rightTurn ? l2 : -l2;
            var miter = (o1 + o2) * (h / Mathf.Max(1f + Vector2.Dot(o1, o2), 1e-4f));
            var outer = new List<Vector2> { rightTurn ? ls : rs };
            if (miter.magnitude <= 2f * h + Tol) outer.Add(c + miter);
            else
            {
                outer.Add(c + o1 * h + t1 * h);
                outer.Add(c + o2 * h - t2 * h);
            }
            outer.Add(rightTurn ? le : re);
            var inner = new List<Vector2> { rightTurn ? rs : ls };
            if (turn < 90f - 0.1f) inner.Add(c - miter);
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
