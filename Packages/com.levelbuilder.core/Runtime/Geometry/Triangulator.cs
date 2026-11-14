using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    /// <summary>
    /// Ear-clipping triangulation of a simple polygon with optional holes. Holes are joined to the
    /// outer contour by a bridge from their rightmost vertex to a visible contour vertex.
    /// Output triangles are counter-clockwise in 2D and index into <c>verts</c> (the merged contour,
    /// in which bridge endpoints appear twice).
    /// </summary>
    public static class Triangulator
    {
        const float Eps = 1e-7f;

        public static List<int> Triangulate(IList<Vector2> outer, IList<IList<Vector2>> holes, out List<Vector2> verts)
        {
            verts = new List<Vector2>();
            var tris = new List<int>();
            if (outer == null || outer.Count < 3) return tris;

            var contour = new List<Vector2>(outer);
            if (SignedArea(contour) < 0f) contour.Reverse();

            if (holes != null && holes.Count > 0)
            {
                var hs = new List<List<Vector2>>();
                foreach (var h in holes)
                {
                    if (h == null || h.Count < 3) continue;
                    var hl = new List<Vector2>(h);
                    if (SignedArea(hl) > 0f) hl.Reverse();
                    hs.Add(hl);
                }
                // Rightmost holes first so later bridges can cross into already-merged ones correctly.
                hs.Sort((a, b) => MaxX(b).CompareTo(MaxX(a)));
                foreach (var h in hs) MergeHole(contour, h);
            }

            verts = contour;
            EarClip(contour, tris);
            return tris;
        }

        public static float SignedArea(IList<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                var u = p[i]; var v = p[(i + 1) % p.Count];
                a += u.x * v.y - v.x * u.y;
            }
            return a * 0.5f;
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        static float MaxX(List<Vector2> p)
        {
            float m = float.MinValue;
            foreach (var v in p) m = Mathf.Max(m, v.x);
            return m;
        }

        static void MergeHole(List<Vector2> contour, List<Vector2> hole)
        {
            int mi = 0;
            for (int i = 1; i < hole.Count; i++)
                if (hole[i].x > hole[mi].x) mi = i;
            var m = hole[mi];

            int bridge = FindBridgeVertex(contour, m);
            if (bridge < 0) return; // hole not inside contour; ignore it

            var merged = new List<Vector2>(contour.Count + hole.Count + 2);
            for (int i = 0; i <= bridge; i++) merged.Add(contour[i]);
            for (int k = 0; k <= hole.Count; k++) merged.Add(hole[(mi + k) % hole.Count]);
            merged.Add(contour[bridge]);
            for (int i = bridge + 1; i < contour.Count; i++) merged.Add(contour[i]);
            contour.Clear();
            contour.AddRange(merged);
        }

        /// <summary>Eberly's method: cast a ray to +x from m, take the hit edge's endpoint, refine by reflex vertices.</summary>
        static int FindBridgeVertex(List<Vector2> c, Vector2 m)
        {
            int n = c.Count;
            float bestX = float.MaxValue;
            int edge = -1;
            Vector2 hit = default;
            for (int i = 0; i < n; i++)
            {
                var a = c[i]; var b = c[(i + 1) % n];
                if ((a.y > m.y) == (b.y > m.y)) continue;
                float t = (m.y - a.y) / (b.y - a.y);
                float x = a.x + t * (b.x - a.x);
                if (x < m.x - Eps || x >= bestX) continue;
                bestX = x; edge = i; hit = new Vector2(x, m.y);
            }
            if (edge < 0) return -1;

            var ea = c[edge]; var eb = c[(edge + 1) % n];
            int candidate = ea.x > eb.x ? edge : (edge + 1) % n;
            var p = c[candidate];
            if ((hit - p).sqrMagnitude < Eps) return candidate;

            // Any reflex contour vertex inside triangle (m, hit, p) blocks visibility; pick the one
            // with the smallest angle to the ray (ties: closest).
            float bestAngle = float.MaxValue, bestDist = float.MaxValue;
            int result = candidate;
            for (int i = 0; i < n; i++)
            {
                if (i == candidate) continue;
                var v = c[i];
                if (!IsReflex(c, i)) continue;
                if (!PointInTriangleInclusive(v, m, hit, p)) continue;
                var d = v - m;
                float angle = Mathf.Abs(Mathf.Atan2(d.y, d.x));
                float dist = d.sqrMagnitude;
                if (angle < bestAngle - Eps || (Mathf.Abs(angle - bestAngle) <= Eps && dist < bestDist))
                {
                    bestAngle = angle; bestDist = dist; result = i;
                }
            }
            return result;
        }

        static bool IsReflex(List<Vector2> c, int i)
        {
            int n = c.Count;
            return Cross(c[(i - 1 + n) % n], c[i], c[(i + 1) % n]) < 0f;
        }

        static bool PointInTriangleInclusive(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float area = Cross(a, b, c);
            if (Mathf.Abs(area) < Eps) return false;
            float s = Mathf.Sign(area);
            return s * Cross(a, b, p) >= -Eps && s * Cross(b, c, p) >= -Eps && s * Cross(c, a, p) >= -Eps;
        }

        static bool PointStrictlyInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            // a, b, c are CCW. Boundary points count as inside unless they coincide with a corner,
            // so bridge duplicates and touching vertices do not block an ear wrongly but crossings do.
            if (Same(p, a) || Same(p, b) || Same(p, c)) return false;
            return Cross(a, b, p) >= -Eps && Cross(b, c, p) >= -Eps && Cross(c, a, p) >= -Eps;
        }

        static bool Same(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-10f;

        static void EarClip(List<Vector2> v, List<int> tris)
        {
            var idx = new List<int>(v.Count);
            for (int i = 0; i < v.Count; i++) idx.Add(i);

            int guard = 0;
            int maxIter = v.Count * v.Count + 10;
            int cur = 0;
            int sinceLastEar = 0;
            while (idx.Count > 3 && guard++ < maxIter)
            {
                int n = idx.Count;
                int ip = idx[(cur - 1 + n) % n], ic = idx[cur % n], inx = idx[(cur + 1) % n];
                var a = v[ip]; var b = v[ic]; var c = v[inx];
                float cr = Cross(a, b, c);

                bool ear = false;
                if (Mathf.Abs(cr) <= Eps)
                {
                    // Degenerate (collinear, duplicate or zero-width spike) vertex: removing it does not
                    // change the covered area, so drop it without emitting a triangle.
                    idx.RemoveAt(cur % n);
                    cur = Mathf.Max(0, (cur % n) - 1);
                    sinceLastEar = 0;
                    continue;
                }
                else if (cr > 0f)
                {
                    ear = true;
                    for (int k = 0; k < n; k++)
                    {
                        int vi = idx[k];
                        if (vi == ip || vi == ic || vi == inx) continue;
                        if (PointStrictlyInTriangle(v[vi], a, b, c)) { ear = false; break; }
                    }
                }

                if (ear || sinceLastEar > n)
                {
                    // Fallback when no ear is found in a whole pass (bad input): clip anyway to terminate.
                    if (cr > Eps) { tris.Add(ip); tris.Add(ic); tris.Add(inx); }
                    idx.RemoveAt(cur % n);
                    cur = Mathf.Max(0, (cur % n) - 1);
                    sinceLastEar = 0;
                }
                else
                {
                    cur = (cur + 1) % n;
                    sinceLastEar++;
                }
            }
            if (idx.Count == 3 && Cross(v[idx[0]], v[idx[1]], v[idx[2]]) > Eps)
            {
                tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]);
            }
        }
    }
}
