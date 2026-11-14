using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    /// <summary>One wall end seen from its node: direction points away from the node.</summary>
    public class JoinEnd
    {
        public WallSegment seg;
        public bool atStart;
        public Vector2 dir;        // unit, away from the node
        public float half;         // thickness / 2
        public float angle;
        public Vector2 outLeft;    // corner on the left of dir
        public Vector2 outRight;   // corner on the right of dir
        public bool leftFallback;  // outLeft is the perpendicular (square) point
        public bool rightFallback; // outRight is the perpendicular (square) point

        public JoinEnd Clone() => (JoinEnd)MemberwiseClone();

        /// <summary>Corners in the segment's own frame (left/right looking start→end).</summary>
        public (Vector2 left, Vector2 right) SegmentCorners => atStart ? (outLeft, outRight) : (outRight, outLeft);
    }

    /// <summary>
    /// Mitred wall corners. For every segment end, the plan-space points where its left and right
    /// side faces end ("left" = the (-dy, dx) side looking from start to end).
    /// </summary>
    public static class WallJoins
    {
        public const float MiterLimitFactor = 3f;
        const float ParallelEps = 1e-4f;
        const float MinLength = 1e-5f;

        public static Vector2 LeftPerp(Vector2 d) => new Vector2(-d.y, d.x);

        public static Dictionary<(string segmentId, bool atStart), (Vector2 left, Vector2 right)> Compute(FloorData f)
        {
            var result = new Dictionary<(string, bool), (Vector2, Vector2)>();
            foreach (var kv in CollectEnds(f, out var nodePos))
            {
                SolveNode(nodePos[kv.Key], kv.Value);
                foreach (var e in kv.Value) result[(e.seg.id, e.atStart)] = e.SegmentCorners;
            }
            return result;
        }

        /// <summary>Wall ends grouped by node id (segments with missing nodes or zero length are skipped).</summary>
        public static Dictionary<string, List<JoinEnd>> CollectEnds(FloorData f, out Dictionary<string, Vector2> nodePos)
        {
            nodePos = new Dictionary<string, Vector2>();
            foreach (var n in f.nodes) nodePos[n.id] = n.position;
            var byNode = new Dictionary<string, List<JoinEnd>>();
            foreach (var s in f.segments)
            {
                if (!nodePos.TryGetValue(s.startNodeId, out var a) || !nodePos.TryGetValue(s.endNodeId, out var b)) continue;
                var d = b - a;
                if (d.magnitude < MinLength) continue;
                d.Normalize();
                Add(byNode, s.startNodeId, new JoinEnd { seg = s, atStart = true, dir = d, half = s.thickness * 0.5f });
                Add(byNode, s.endNodeId, new JoinEnd { seg = s, atStart = false, dir = -d, half = s.thickness * 0.5f });
            }
            return byNode;
        }

        /// <summary>
        /// Sorts the ends counter-clockwise by angle and fills their corners. For each consecutive pair
        /// (e_i, e_i+1) the shared corner is the intersection of e_i's left offset line with e_i+1's right
        /// offset line; parallel lines or a corner beyond the miter limit fall back to the perpendicular points.
        /// </summary>
        public static void SolveNode(Vector2 p, List<JoinEnd> ends)
        {
            foreach (var e in ends) e.angle = Mathf.Atan2(e.dir.y, e.dir.x);
            ends.Sort((x, y) => x.angle.CompareTo(y.angle));

            int count = ends.Count;
            for (int i = 0; i < count; i++)
            {
                var si = ends[i];
                var sj = ends[(i + 1) % count];
                var leftPerpPoint = p + LeftPerp(si.dir) * si.half;
                var rightPerpPoint = p - LeftPerp(sj.dir) * sj.half;

                bool fallback = true;
                Vector2 corner = default;
                if (si != sj && TryIntersectLines(leftPerpPoint, si.dir, rightPerpPoint, sj.dir, out var x))
                {
                    float limit = MiterLimitFactor * 2f * Mathf.Max(si.half, sj.half);
                    if ((x - p).magnitude <= limit) { corner = x; fallback = false; }
                }

                si.outLeft = fallback ? leftPerpPoint : corner;
                si.leftFallback = fallback;
                sj.outRight = fallback ? rightPerpPoint : corner;
                sj.rightFallback = fallback;
            }
        }

        static void Add(Dictionary<string, List<JoinEnd>> map, string nodeId, JoinEnd e)
        {
            if (!map.TryGetValue(nodeId, out var list)) map[nodeId] = list = new List<JoinEnd>();
            list.Add(e);
        }

        static bool TryIntersectLines(Vector2 a, Vector2 da, Vector2 b, Vector2 db, out Vector2 x)
        {
            x = default;
            float denom = da.x * db.y - da.y * db.x;
            if (Mathf.Abs(denom) < ParallelEps) return false;
            var w = b - a;
            float t = (w.x * db.y - w.y * db.x) / denom;
            x = a + da * t;
            return true;
        }
    }
}
