using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    /// <summary>
    /// Mitred wall corners. For every segment end, the plan-space points where its left and right
    /// side faces end ("left" = the (-dy, dx) side looking from start to end).
    /// </summary>
    public static class WallJoins
    {
        public const float MiterLimitFactor = 3f;
        const float ParallelEps = 1e-4f;
        const float MinLength = 1e-5f;

        struct Incidence
        {
            public WallSegment seg;
            public bool atStart;
            public Vector2 dir;       // unit, pointing away from the node
            public float half;        // thickness / 2
            public float angle;
            public Vector2 outLeft;   // corner on the left of dir
            public Vector2 outRight;  // corner on the right of dir
        }

        public static Vector2 LeftPerp(Vector2 d) => new Vector2(-d.y, d.x);

        public static Dictionary<(string segmentId, bool atStart), (Vector2 left, Vector2 right)> Compute(FloorData f)
        {
            var result = new Dictionary<(string, bool), (Vector2, Vector2)>();
            var nodePos = new Dictionary<string, Vector2>();
            foreach (var n in f.nodes) nodePos[n.id] = n.position;

            var byNode = new Dictionary<string, List<Incidence>>();
            foreach (var s in f.segments)
            {
                if (!nodePos.TryGetValue(s.startNodeId, out var a) || !nodePos.TryGetValue(s.endNodeId, out var b)) continue;
                var d = b - a;
                if (d.magnitude < MinLength) continue;
                d.Normalize();
                Add(byNode, s.startNodeId, new Incidence { seg = s, atStart = true, dir = d, half = s.thickness * 0.5f });
                Add(byNode, s.endNodeId, new Incidence { seg = s, atStart = false, dir = -d, half = s.thickness * 0.5f });
            }

            foreach (var kv in byNode)
            {
                var p = nodePos[kv.Key];
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    var inc = list[i];
                    inc.angle = Mathf.Atan2(inc.dir.y, inc.dir.x);
                    list[i] = inc;
                }
                list.Sort((x, y) => x.angle.CompareTo(y.angle));

                int count = list.Count;
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    var si = list[i];
                    var sj = list[j];
                    var leftPerpPoint = p + LeftPerp(si.dir) * si.half;
                    var rightPerpPoint = p - LeftPerp(sj.dir) * sj.half;

                    Vector2 cornerI = leftPerpPoint, cornerJ = rightPerpPoint;
                    if (i != j && TryIntersectLines(leftPerpPoint, si.dir, rightPerpPoint, sj.dir, out var x))
                    {
                        float limit = MiterLimitFactor * 2f * Mathf.Max(si.half, sj.half);
                        if ((x - p).magnitude <= limit) { cornerI = x; cornerJ = x; }
                    }

                    si.outLeft = cornerI;
                    list[i] = si;
                    sj = list[j]; // re-read: i == j for degree 1
                    sj.outRight = cornerJ;
                    list[j] = sj;
                }

                foreach (var inc in list)
                {
                    // Outgoing left/right equal segment left/right at the start; swapped at the end.
                    result[(inc.seg.id, inc.atStart)] = inc.atStart
                        ? (inc.outLeft, inc.outRight)
                        : (inc.outRight, inc.outLeft);
                }
            }
            return result;
        }

        static void Add(Dictionary<string, List<Incidence>> map, string nodeId, Incidence inc)
        {
            if (!map.TryGetValue(nodeId, out var list)) map[nodeId] = list = new List<Incidence>();
            list.Add(inc);
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
