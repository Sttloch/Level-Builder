using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    /// <summary>
    /// Boolean difference of two polygons that are star-shaped about the same centre (the wall
    /// regions around a node). Works in angular sectors: inside each sector both boundaries are a
    /// single straight edge, so the difference is a quad or triangle.
    /// </summary>
    public static class StarRegion
    {
        const float AngleEps = 1e-6f;
        const float MaxSector = Mathf.PI / 3f;

        /// <summary>Triangles (3 points each, any winding) covering <paramref name="outer"/> minus <paramref name="inner"/>.</summary>
        public static List<Vector2> Difference(Vector2 c, IList<Vector2> outer, IList<Vector2> inner)
        {
            var tris = new List<Vector2>();
            if (outer == null || outer.Count < 3) return tris;

            var angles = new List<float>();
            AddAngles(c, outer, angles);
            if (inner != null) AddAngles(c, inner, angles);
            if (angles.Count == 0) return tris;
            angles.Sort();
            var uniq = new List<float>();
            foreach (var a in angles)
                if (uniq.Count == 0 || a - uniq[uniq.Count - 1] > AngleEps) uniq.Add(a);
            if (uniq.Count > 1 && uniq[0] + 2f * Mathf.PI - uniq[uniq.Count - 1] <= AngleEps) uniq.RemoveAt(uniq.Count - 1);

            for (int i = 0; i < uniq.Count; i++)
            {
                float a0 = uniq[i];
                float a1 = i + 1 < uniq.Count ? uniq[i + 1] : uniq[0] + 2f * Mathf.PI;
                int steps = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / MaxSector));
                for (int s = 0; s < steps; s++)
                    Sector(c, outer, inner, a0 + (a1 - a0) * s / steps, a0 + (a1 - a0) * (s + 1) / steps, tris);
            }
            return tris;
        }

        static void AddAngles(Vector2 c, IList<Vector2> poly, List<float> angles)
        {
            foreach (var p in poly)
            {
                var d = p - c;
                if (d.sqrMagnitude < 1e-12f) continue;
                float a = Mathf.Atan2(d.y, d.x);
                if (a < 0f) a += 2f * Mathf.PI;
                angles.Add(a);
            }
        }

        static Vector2 Dir(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static void Sector(Vector2 c, IList<Vector2> outer, IList<Vector2> inner, float a0, float a1, List<Vector2> tris)
        {
            var da = Dir(a0); var db = Dir(a1); var dm = Dir((a0 + a1) * 0.5f);
            if (!Hit(c, dm, outer, out var e0, out var e1, out float tm)) return;
            float oa = RayLine(c, da, e0, e1, tm), ob = RayLine(c, db, e0, e1, tm);

            float ia = 0f, ib = 0f;
            Vector2 f0 = default, f1 = default;
            float tn = 0f;
            bool hasInner = inner != null && inner.Count >= 3 && Hit(c, dm, inner, out f0, out f1, out tn);
            if (hasInner) { ia = RayLine(c, da, f0, f1, tn); ib = RayLine(c, db, f0, f1, tn); }

            const float eps = 1e-5f;
            bool okA = oa > ia + eps, okB = ob > ib + eps;
            Vector2 inA = c + da * ia, outA = c + da * oa, outB = c + db * ob, inB = c + db * ib;
            if (okA && okB)
            {
                AddTri(tris, inA, outA, outB);
                AddTri(tris, inA, outB, inB);
            }
            else if ((okA || okB) && hasInner && LineIntersect(e0, e1, f0, f1, out var x))
            {
                if (okA) AddTri(tris, inA, outA, x);
                else AddTri(tris, x, outB, inB);
            }
        }

        static void AddTri(List<Vector2> tris, Vector2 a, Vector2 b, Vector2 c)
        {
            if (Mathf.Abs(Cross(b - a, c - a)) < 1e-10f) return;
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        /// <summary>Farthest edge crossed by the ray from c along d.</summary>
        static bool Hit(Vector2 c, Vector2 d, IList<Vector2> poly, out Vector2 e0, out Vector2 e1, out float best)
        {
            e0 = e1 = default;
            best = -1f;
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                var pq = q - p;
                if (pq.sqrMagnitude < 1e-12f) continue;
                float denom = Cross(d, pq);
                if (Mathf.Abs(denom) < 1e-12f) continue;
                var w = p - c;
                float t = Cross(w, pq) / denom;
                float s = Cross(w, d) / denom;
                if (t <= 1e-7f || s < -1e-6f || s > 1f + 1e-6f) continue;
                if (t > best) { best = t; e0 = p; e1 = q; }
            }
            return best > 0f;
        }

        static float RayLine(Vector2 c, Vector2 d, Vector2 e0, Vector2 e1, float fallback)
        {
            var e = e1 - e0;
            float denom = Cross(d, e);
            if (Mathf.Abs(denom) < 1e-12f) return fallback;
            return Mathf.Max(0f, Cross(e0 - c, e) / denom);
        }

        static bool LineIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out Vector2 x)
        {
            x = default;
            var da = a1 - a0; var db = b1 - b0;
            float denom = Cross(da, db);
            if (Mathf.Abs(denom) < 1e-12f) return false;
            x = a0 + da * (Cross(b0 - a0, db) / denom);
            return true;
        }
    }
}
