using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    public class Region
    {
        public List<string> nodeIds = new List<string>();
        /// <summary>Counter-clockwise in the plan (x, y) plane.</summary>
        public List<Vector2> polygon = new List<Vector2>();
        public List<string> segmentIds = new List<string>();
        /// <summary>Sorted segment ids joined by "|"; stable while topology is unchanged.</summary>
        public string key;
    }

    public class PlanarResult
    {
        public List<Region> regions = new List<Region>();
        public HashSet<string> exteriorSegmentIds = new HashSet<string>();
    }

    public static class PlanarFaces
    {
        class HalfEdge
        {
            public string from, to, segmentId;
            public float angle;
            public HalfEdge twin, next;
            public bool visited;
        }

        public static PlanarResult Compute(FloorData f)
        {
            var result = new PlanarResult();
            if (f == null || f.segments == null || f.segments.Count == 0) return result;

            var pos = new Dictionary<string, Vector2>();
            foreach (var n in f.nodes) pos[n.id] = n.position;

            // Valid, de-duplicated segments.
            var segs = new List<WallSegment>();
            foreach (var s in f.segments)
            {
                if (s.startNodeId == s.endNodeId) continue;
                if (!pos.ContainsKey(s.startNodeId) || !pos.ContainsKey(s.endNodeId)) continue;
                segs.Add(s);
            }

            // Iteratively prune degree-1 nodes (dangling walls).
            var alive = new HashSet<WallSegment>(segs);
            var degree = new Dictionary<string, int>();
            foreach (var s in segs)
            {
                degree[s.startNodeId] = (degree.TryGetValue(s.startNodeId, out var d1) ? d1 : 0) + 1;
                degree[s.endNodeId] = (degree.TryGetValue(s.endNodeId, out var d2) ? d2 : 0) + 1;
            }
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var s in segs)
                {
                    if (!alive.Contains(s)) continue;
                    if (degree[s.startNodeId] <= 1 || degree[s.endNodeId] <= 1)
                    {
                        alive.Remove(s);
                        degree[s.startNodeId]--;
                        degree[s.endNodeId]--;
                        changed = true;
                    }
                }
            }
            if (alive.Count == 0) return result;

            // Half-edges, outgoing lists per node sorted by angle (CCW ascending).
            var outgoing = new Dictionary<string, List<HalfEdge>>();
            foreach (var s in segs)
            {
                if (!alive.Contains(s)) continue;
                var pa = pos[s.startNodeId]; var pb = pos[s.endNodeId];
                var h1 = new HalfEdge { from = s.startNodeId, to = s.endNodeId, segmentId = s.id, angle = Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) };
                var h2 = new HalfEdge { from = s.endNodeId, to = s.startNodeId, segmentId = s.id, angle = Mathf.Atan2(pa.y - pb.y, pa.x - pb.x) };
                h1.twin = h2; h2.twin = h1;
                AddOut(outgoing, h1); AddOut(outgoing, h2);
            }
            foreach (var kv in outgoing) kv.Value.Sort((a, b) => a.angle.CompareTo(b.angle));

            // next(h) = at h.to, the outgoing edge just clockwise of the reverse edge (previous in CCW order).
            foreach (var kv in outgoing)
                foreach (var h in kv.Value)
                {
                    // h goes from -> to; reverse edge (to -> from) is h.twin, sitting in outgoing[to].
                    var list = outgoing[h.to];
                    int idx = list.IndexOf(h.twin);
                    h.next = list[(idx - 1 + list.Count) % list.Count];
                }

            foreach (var kv in outgoing)
                foreach (var start in kv.Value)
                {
                    if (start.visited) continue;
                    var face = new List<HalfEdge>();
                    var h = start;
                    int guard = 0, limit = alive.Count * 2 + 2;
                    while (!h.visited && guard++ <= limit)
                    {
                        h.visited = true;
                        face.Add(h);
                        h = h.next;
                    }

                    var poly = new List<Vector2>(face.Count);
                    foreach (var e in face) poly.Add(pos[e.from]);
                    float area = SignedArea(poly);

                    if (area > 1e-6f)
                    {
                        var region = new Region();
                        var seen = new HashSet<string>();
                        foreach (var e in face)
                        {
                            region.nodeIds.Add(e.from);
                            if (seen.Add(e.segmentId)) region.segmentIds.Add(e.segmentId);
                        }
                        region.polygon = poly;
                        var sorted = new List<string>(region.segmentIds);
                        sorted.Sort(StringComparer.Ordinal);
                        region.key = string.Join("|", sorted);
                        result.regions.Add(region);
                    }
                    else
                    {
                        foreach (var e in face) result.exteriorSegmentIds.Add(e.segmentId);
                    }
                }

            return result;
        }

        static void AddOut(Dictionary<string, List<HalfEdge>> d, HalfEdge h)
        {
            if (!d.TryGetValue(h.from, out var l)) d[h.from] = l = new List<HalfEdge>();
            l.Add(h);
        }

        static float SignedArea(List<Vector2> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var u = p[i]; var v = p[(i + 1) % p.Count];
                a += (double)u.x * v.y - (double)v.x * u.y;
            }
            return (float)(a * 0.5);
        }
    }
}
