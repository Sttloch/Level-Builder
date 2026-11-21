using UnityEngine;
using LevelBuilder.Geometry;

namespace LevelBuilder.Editor
{
    /// <summary>
    /// Wall tool snapping: existing node (closest) beats existing wall axis (grid crossing or projection),
    /// which beats the plain grid.
    /// </summary>
    public static class WallSnap
    {
        public const float NodeRadius = 0.3f;
        public const float WallRadius = 0.25f;
        const float CrossingRange = 0.25f;

        public enum Kind { None, Node, Wall, Grid }

        /// <summary>ignoreNodeId excludes that node and the segments touching it (the node being dragged).</summary>
        public static Vector2 Snap(FloorData f, Vector2 p, float grid, out Kind kind, out string targetId,
            string ignoreNodeId = null)
        {
            kind = Kind.None; targetId = null;
            if (f != null)
            {
                WallNode bestNode = null;
                float bestD = NodeRadius;
                foreach (var n in f.nodes)
                {
                    if (n.id == ignoreNodeId) continue;
                    float d = (n.position - p).magnitude;
                    if (d <= bestD) { bestD = d; bestNode = n; }
                }
                if (bestNode != null)
                {
                    kind = Kind.Node; targetId = bestNode.id;
                    return bestNode.position;
                }

                WallSegment bestSeg = null;
                Vector2 bestA = default, bestB = default;
                bestD = WallRadius;
                foreach (var s in f.segments)
                {
                    if (ignoreNodeId != null && (s.startNodeId == ignoreNodeId || s.endNodeId == ignoreNodeId)) continue;
                    var na = f.GetNode(s.startNodeId);
                    var nb = f.GetNode(s.endNodeId);
                    if (na == null || nb == null) continue;
                    float d = Seg2D.DistanceToSegment(p, na.position, nb.position, out _);
                    if (d <= bestD) { bestD = d; bestSeg = s; bestA = na.position; bestB = nb.position; }
                }
                if (bestSeg != null)
                {
                    kind = Kind.Wall; targetId = bestSeg.id;
                    return OntoWall(p, bestA, bestB, grid);
                }
            }
            kind = Kind.Grid;
            return GridSnap.Snap(p, grid);
        }

        static Vector2 OntoWall(Vector2 p, Vector2 a, Vector2 b, float grid)
        {
            Seg2D.DistanceToSegment(p, a, b, out float t);
            var proj = Vector2.Lerp(a, b, t);
            if (grid <= 0f) return proj;
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-6f) return proj;

            Vector2 best = proj;
            float bestAlong = CrossingRange;
            // Vertical line x = k*grid and horizontal line y = k*grid nearest to the projection.
            float lineX = Mathf.Round(proj.x / grid) * grid;
            float lineY = Mathf.Round(proj.y / grid) * grid;
            if (Mathf.Abs(d.x) > 1e-6f) Try(Mathf.Clamp01((lineX - a.x) / d.x));
            if (Mathf.Abs(d.y) > 1e-6f) Try(Mathf.Clamp01((lineY - a.y) / d.y));
            return best;

            void Try(float tc)
            {
                var c = Vector2.Lerp(a, b, tc);
                float along = Mathf.Abs(tc - t) * len;
                if (along <= bestAlong) { bestAlong = along; best = c; }
            }
        }
    }
}
