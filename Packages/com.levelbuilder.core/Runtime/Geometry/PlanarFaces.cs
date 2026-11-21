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
        /// <summary>Boundaries of free-standing components nested inside this region (clockwise).</summary>
        public List<List<Vector2>> holes = new List<List<Vector2>>();
        /// <summary>Sorted segment ids joined by "|"; stable while topology is unchanged.</summary>
        public string key;
        /// <summary>True when some boundary segment is lower than the floor height (balcony, walkway): floor slab but no ceiling or roof.</summary>
        public bool open;
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

        /// <summary>Same as <see cref="Compute"/> but using only the full-height segments of the floor.</summary>
        public static PlanarResult ComputeFullHeight(FloorData f)
        {
            if (f == null || f.segments == null) return new PlanarResult();
            var copy = new FloorData { id = f.id, name = f.name, elevation = f.elevation, height = f.height,
                slabThickness = f.slabThickness, nodes = f.nodes,
                segments = f.segments.FindAll(s => s.IsFullHeight(f)) };
            return ComputeCore(copy);
        }

        /// <summary>
        /// Regions of the floor. A region is open (balcony, walkway) when it is not enclosed by full-height
        /// walls: a point inside it lies in no region of <see cref="ComputeFullHeight"/>.
        /// </summary>
        public static PlanarResult Compute(FloorData f)
        {
            var result = ComputeCore(f);
            if (result.regions.Count == 0) return result;
            var full = ComputeFullHeight(f).regions;
            foreach (var r in result.regions)
            {
                if (!InteriorPoint(r, out var p)) continue;
                r.open = !full.Exists(fr => PlanarFaces.PointInPolygon(p, fr.polygon) &&
                                            !fr.holes.Exists(h => PlanarFaces.PointInPolygon(p, h)));
            }
            return result;
        }

        /// <summary>A point strictly inside the region (outside its holes): centroid of a triangle of it.</summary>
        internal static bool InteriorPoint(Region r, out Vector2 p)
        {
            p = default;
            var holes = new List<IList<Vector2>>();
            foreach (var h in r.holes) holes.Add(h);
            var tris = Triangulator.Triangulate(r.polygon, holes, out var verts);
            if (tris == null || tris.Count < 3) return false;
            // Largest triangle: its centroid is the furthest from the boundary among the candidates.
            int best = 0; float bestA = -1f;
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                var a = verts[tris[i]]; var b = verts[tris[i + 1]]; var c = verts[tris[i + 2]];
                float area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
                if (area > bestA) { bestA = area; best = i; }
            }
            p = (verts[tris[best]] + verts[tris[best + 1]] + verts[tris[best + 2]]) / 3f;
            return true;
        }

        static PlanarResult ComputeCore(FloorData f)
        {
            var result = new PlanarResult();
            if (f == null || f.segments == null || f.segments.Count == 0) return result;

            var pos = new Dictionary<string, Vector2>();
            foreach (var n in f.nodes) pos[n.id] = n.position;

            // Valid, de-duplicated segments.
            var segs = new List<WallSegment>();
            var pairs = new HashSet<string>();
            foreach (var s in f.segments)
            {
                if (s.startNodeId == s.endNodeId) continue;
                if (!pos.ContainsKey(s.startNodeId) || !pos.ContainsKey(s.endNodeId)) continue;
                string pk = string.CompareOrdinal(s.startNodeId, s.endNodeId) < 0
                    ? s.startNodeId + "|" + s.endNodeId : s.endNodeId + "|" + s.startNodeId;
                if (!pairs.Add(pk)) continue;
                segs.Add(s);
            }

            // Remove bridges (this includes dangling chains): they never bound a region and would make
            // face polygons non-simple. Bridges are not exterior.
            var alive = new HashSet<WallSegment>(segs);
            foreach (var br in FindBridges(segs)) alive.Remove(br);
            if (alive.Count == 0) return result;

            // Connected components of what remains.
            var parent = new Dictionary<string, string>();
            foreach (var s in alive) { parent[s.startNodeId] = s.startNodeId; parent[s.endNodeId] = s.endNodeId; }
            foreach (var s in alive) Union(parent, s.startNodeId, s.endNodeId);

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

            var regionComp = new List<string>();
            var regionArea = new List<float>();
            var outerPolys = new List<List<Vector2>>();
            var outerComps = new List<string>();
            var outerSegs = new List<List<string>>();

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
                    string comp = Find(parent, start.from);

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
                        regionComp.Add(comp);
                        regionArea.Add(area);
                    }
                    else
                    {
                        var ids = new List<string>();
                        foreach (var e in face) ids.Add(e.segmentId);
                        outerPolys.Add(poly);
                        outerComps.Add(comp);
                        outerSegs.Add(ids);
                    }
                }

            // Outer boundaries nested in another component's region become holes; others are exterior.
            for (int o = 0; o < outerPolys.Count; o++)
            {
                var poly = outerPolys[o];
                int best = -1;
                for (int i = 0; i < result.regions.Count; i++)
                {
                    if (regionComp[i] == outerComps[o]) continue;
                    if (!PointInPolygon(poly[0], result.regions[i].polygon)) continue;
                    if (best < 0 || regionArea[i] < regionArea[best]) best = i;
                }
                if (best >= 0) result.regions[best].holes.Add(poly);
                else foreach (var id in outerSegs[o]) result.exteriorSegmentIds.Add(id);
            }

            return result;
        }

        static string Find(Dictionary<string, string> p, string x)
        {
            while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; }
            return x;
        }

        static void Union(Dictionary<string, string> p, string a, string b)
        {
            a = Find(p, a); b = Find(p, b);
            if (a != b) p[a] = b;
        }

        internal static bool PointInPolygon(Vector2 pt, List<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > pt.y) != (b.y > pt.y) &&
                    pt.x < (b.x - a.x) * (pt.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        static List<WallSegment> FindBridges(List<WallSegment> segs)
        {
            var adj = new Dictionary<string, List<WallSegment>>();
            foreach (var s in segs)
            {
                if (!adj.TryGetValue(s.startNodeId, out var l1)) adj[s.startNodeId] = l1 = new List<WallSegment>();
                if (!adj.TryGetValue(s.endNodeId, out var l2)) adj[s.endNodeId] = l2 = new List<WallSegment>();
                l1.Add(s); l2.Add(s);
            }
            var disc = new Dictionary<string, int>();
            var low = new Dictionary<string, int>();
            var bridges = new List<WallSegment>();
            int time = 0;
            foreach (var root in new List<string>(adj.Keys))
            {
                if (disc.ContainsKey(root)) continue;
                var stack = new Stack<KeyValuePair<string, WallSegment>>();
                var idx = new Dictionary<string, int>();
                disc[root] = low[root] = time++;
                idx[root] = 0;
                stack.Push(new KeyValuePair<string, WallSegment>(root, null));
                while (stack.Count > 0)
                {
                    var top = stack.Peek();
                    string u = top.Key;
                    var list = adj[u];
                    if (idx[u] < list.Count)
                    {
                        var e = list[idx[u]++];
                        if (e == top.Value) continue;
                        string v = e.startNodeId == u ? e.endNodeId : e.startNodeId;
                        if (disc.ContainsKey(v)) low[u] = Math.Min(low[u], disc[v]);
                        else
                        {
                            disc[v] = low[v] = time++;
                            idx[v] = 0;
                            stack.Push(new KeyValuePair<string, WallSegment>(v, e));
                        }
                    }
                    else
                    {
                        stack.Pop();
                        if (stack.Count > 0)
                        {
                            string par = stack.Peek().Key;
                            low[par] = Math.Min(low[par], low[u]);
                            if (low[u] > disc[par]) bridges.Add(top.Value);
                        }
                    }
                }
            }
            return bridges;
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
