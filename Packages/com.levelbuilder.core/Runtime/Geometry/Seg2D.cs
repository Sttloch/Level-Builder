using UnityEngine;

namespace LevelBuilder.Geometry
{
    public static class Seg2D
    {
        const float Eps = 1e-6f;

        static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        /// <summary>Proper intersection only: ta, tb strictly inside (eps, 1-eps). Parallel/collinear returns false.</summary>
        public static bool Intersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2,
            out Vector2 p, out float ta, out float tb)
        {
            p = default; ta = 0f; tb = 0f;
            var d1 = a2 - a1;
            var d2 = b2 - b1;
            float denom = Cross(d1, d2);
            if (Mathf.Abs(denom) < 1e-9f) return false;
            var w = b1 - a1;
            ta = Cross(w, d2) / denom;
            tb = Cross(w, d1) / denom;
            if (ta <= Eps || ta >= 1f - Eps || tb <= Eps || tb >= 1f - Eps) return false;
            p = a1 + d1 * ta;
            return true;
        }

        /// <summary>Distance from p to segment ab; t is the clamped projection parameter in [0,1].</summary>
        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            t = len2 < 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return (p - (a + ab * t)).magnitude;
        }
    }
}
