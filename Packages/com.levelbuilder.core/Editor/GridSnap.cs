using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Plan-space (x, y) snapping helpers for the wall tools.</summary>
    public static class GridSnap
    {
        /// <summary>Rounds each coordinate to the nearest multiple of grid. grid &lt;= 0 returns p unchanged.</summary>
        public static Vector2 Snap(Vector2 p, float grid)
        {
            if (grid <= 0f) return p;
            return new Vector2(Mathf.Round(p.x / grid) * grid, Mathf.Round(p.y / grid) * grid);
        }

        /// <summary>Rotates (to - from) to the nearest multiple of stepDegrees, keeping its length.</summary>
        public static Vector2 SnapAngle(Vector2 from, Vector2 to, float stepDegrees)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 1e-6f || stepDegrees <= 0f) return to;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float snapped = Mathf.Round(angle / stepDegrees) * stepDegrees * Mathf.Deg2Rad;
            return from + new Vector2(Mathf.Cos(snapped), Mathf.Sin(snapped)) * len;
        }
    }
}
