using System;
using System.Collections.Generic;
using UnityEngine;
using LevelBuilder.Geometry;

namespace LevelBuilder
{
    [Serializable]
    public class BuildingData
    {
        public const float MergeTolerance = 0.01f;

        public List<FloorData> floors = new List<FloorData>();

        public FloorData AddFloor()
        {
            var f = new FloorData { id = IdUtil.New(), name = "Planta " + floors.Count };
            if (floors.Count > 0)
            {
                var prev = floors[floors.Count - 1];
                f.elevation = prev.elevation + prev.height;
                f.height = prev.height;
                f.slabThickness = prev.slabThickness;
                CopyExteriorFrom(prev, f);
            }
            floors.Add(f);
            return f;
        }

        /// <summary>Removes the top floor. The last remaining floor is never removed; returns false then.</summary>
        public bool RemoveTopFloor()
        {
            if (floors.Count <= 1) return false;
            floors.RemoveAt(floors.Count - 1);
            return true;
        }

        /// <summary>Sets a floor's height and restacks the elevations of the floors above it.</summary>
        public void SetFloorHeight(int index, float height)
        {
            if (index < 0 || index >= floors.Count) return;
            floors[index].height = height;
            for (int i = index + 1; i < floors.Count; i++)
                floors[i].elevation = floors[i - 1].elevation + floors[i - 1].height;
        }

        void CopyExteriorFrom(FloorData below, FloorData f)
        {
            var exterior = PlanarFaces.Compute(below).exteriorSegmentIds;
            var map = new Dictionary<string, WallNode>();
            WallNode Clone(string belowId)
            {
                if (map.TryGetValue(belowId, out var existing)) return existing;
                var src = below.GetNode(belowId);
                var n = new WallNode { id = IdUtil.New(), position = src.position, linkedBelowId = src.id };
                f.nodes.Add(n);
                map[belowId] = n;
                return n;
            }
            foreach (var s in below.segments)
            {
                if (!exterior.Contains(s.id)) continue;
                if (below.GetNode(s.startNodeId) == null || below.GetNode(s.endNodeId) == null) continue;
                f.segments.Add(new WallSegment
                {
                    id = IdUtil.New(),
                    startNodeId = Clone(s.startNodeId).id,
                    endNodeId = Clone(s.endNodeId).id,
                    thickness = s.thickness,
                    heightOverride = s.heightOverride
                });
            }
        }

        /// <summary>All nodes connected to nodeId through linkedBelowId links (including itself), across floors.</summary>
        public IEnumerable<(FloorData floor, WallNode node)> GetLinkedColumn(string nodeId)
        {
            var result = new List<(FloorData, WallNode)>();
            var all = new Dictionary<string, (FloorData floor, WallNode node)>();
            foreach (var fl in floors)
                foreach (var n in fl.nodes) all[n.id] = (fl, n);
            if (!all.ContainsKey(nodeId)) return result;
            var seen = new HashSet<string> { nodeId };
            var queue = new Queue<string>();
            queue.Enqueue(nodeId);
            while (queue.Count > 0)
            {
                var cur = all[queue.Dequeue()];
                result.Add(cur);
                if (!string.IsNullOrEmpty(cur.node.linkedBelowId)
                    && all.ContainsKey(cur.node.linkedBelowId) && seen.Add(cur.node.linkedBelowId))
                    queue.Enqueue(cur.node.linkedBelowId);
                foreach (var kv in all)
                    if (kv.Value.node.linkedBelowId == cur.node.id && seen.Add(kv.Key))
                        queue.Enqueue(kv.Key);
            }
            return result;
        }

        /// <summary>Breaks the node's link with the floor below and the links of nodes above that reference it.</summary>
        public void UnlinkNode(FloorData f, string nodeId)
        {
            var n = f.GetNode(nodeId);
            if (n == null) return;
            n.linkedBelowId = null;
            ClearLinksTo(nodeId);
        }

        void ClearLinksTo(string nodeId)
        {
            foreach (var fl in floors)
                foreach (var n in fl.nodes)
                    if (n.linkedBelowId == nodeId) n.linkedBelowId = null;
        }

        /// <summary>Reuses a node within tolerance; if p lies on a segment, splits it there; otherwise creates a node.</summary>
        public WallNode GetOrAddNode(FloorData f, Vector2 p)
        {
            WallNode best = null;
            float bestD = MergeTolerance;
            foreach (var n in f.nodes)
            {
                float d = (n.position - p).magnitude;
                if (d <= bestD) { bestD = d; best = n; }
            }
            if (best != null) return best;

            WallSegment hit = null;
            float hitD = MergeTolerance;
            foreach (var s in f.segments)
            {
                var a = f.GetNode(s.startNodeId);
                var b = f.GetNode(s.endNodeId);
                if (a == null || b == null) continue;
                float d = Seg2D.DistanceToSegment(p, a.position, b.position, out _);
                if (d <= hitD) { hitD = d; hit = s; }
            }
            if (hit != null) return SplitSegment(f, hit.id, p);

            var node = new WallNode { id = IdUtil.New(), position = p };
            f.nodes.Add(node);
            return node;
        }

        /// <summary>Splits a segment at the projection of p onto it (keeps the wall straight). Both halves keep thickness/height.
        /// If the projection is within MergeTolerance of an endpoint, returns that endpoint and changes nothing.</summary>
        public WallNode SplitSegment(FloorData f, string segmentId, Vector2 p)
        {
            var s = f.GetSegment(segmentId);
            if (s == null) return null;
            var a = f.GetNode(s.startNodeId);
            var b = f.GetNode(s.endNodeId);
            Seg2D.DistanceToSegment(p, a.position, b.position, out float t);
            var at = Vector2.Lerp(a.position, b.position, t);
            // Never create a node on top of an endpoint (it would leave a zero-length segment).
            if ((at - a.position).magnitude <= MergeTolerance) return a;
            if ((at - b.position).magnitude <= MergeTolerance) return b;
            var node = new WallNode { id = IdUtil.New(), position = at };
            f.nodes.Add(node);
            var second = new WallSegment
            {
                id = IdUtil.New(),
                startNodeId = node.id,
                endNodeId = s.endNodeId,
                thickness = s.thickness,
                heightOverride = s.heightOverride
            };
            s.endNodeId = node.id;
            f.segments.Add(second);
            return node;
        }

        public List<WallSegment> AddSegment(FloorData f, Vector2 a, Vector2 b)
        {
            var created = new List<WallSegment>();
            if ((b - a).magnitude < MergeTolerance) return created;

            // Crossings against the segments that exist before this call.
            var crossings = new List<KeyValuePair<float, Vector2>>();
            foreach (var s in f.segments)
            {
                var sa = f.GetNode(s.startNodeId);
                var sb = f.GetNode(s.endNodeId);
                if (sa == null || sb == null) continue;
                if (Seg2D.Intersect(a, b, sa.position, sb.position, out var p, out var ta, out _))
                    crossings.Add(new KeyValuePair<float, Vector2>(ta, p));
            }

            var na = GetOrAddNode(f, a);
            var nb = GetOrAddNode(f, b);
            if (na == nb) return created;

            var chain = new List<KeyValuePair<float, WallNode>>();
            foreach (var c in crossings)
                chain.Add(new KeyValuePair<float, WallNode>(c.Key, GetOrAddNode(f, c.Value)));

            // Existing nodes lying on the interior of the new wall (T-junctions from the other side).
            foreach (var n in f.nodes)
            {
                if (n == na || n == nb) continue;
                float d = Seg2D.DistanceToSegment(n.position, na.position, nb.position, out float t);
                if (d <= MergeTolerance && t > 0f && t < 1f)
                    chain.Add(new KeyValuePair<float, WallNode>(t, n));
            }

            chain.Sort((x, y) => x.Key.CompareTo(y.Key));
            var ordered = new List<WallNode> { na };
            foreach (var c in chain)
                if (ordered[ordered.Count - 1] != c.Value && c.Value != nb) ordered.Add(c.Value);
            ordered.Add(nb);

            for (int i = 0; i < ordered.Count - 1; i++)
            {
                var seg = TryCreateSegment(f, ordered[i], ordered[i + 1]);
                if (seg != null) created.Add(seg);
            }
            return created;
        }

        WallSegment TryCreateSegment(FloorData f, WallNode a, WallNode b)
        {
            if (a == b) return null;
            foreach (var s in f.segments)
            {
                if ((s.startNodeId == a.id && s.endNodeId == b.id) || (s.startNodeId == b.id && s.endNodeId == a.id))
                    return null;
            }
            var seg = new WallSegment { id = IdUtil.New(), startNodeId = a.id, endNodeId = b.id };
            f.segments.Add(seg);
            return seg;
        }

        public void MoveNode(FloorData f, string nodeId, Vector2 p)
        {
            var n = f.GetNode(nodeId);
            if (n == null) return;
            n.position = p;
            foreach (var (_, linked) in GetLinkedColumn(nodeId)) linked.position = p;
        }

        public void RemoveSegment(FloorData f, string segmentId)
        {
            var s = f.GetSegment(segmentId);
            if (s == null) return;
            f.segments.Remove(s);
            RemoveIfOrphan(f, s.startNodeId);
            RemoveIfOrphan(f, s.endNodeId);
        }

        public void RemoveNode(FloorData f, string nodeId)
        {
            var incident = f.segments.FindAll(s => s.startNodeId == nodeId || s.endNodeId == nodeId);
            foreach (var s in incident) RemoveSegment(f, s.id);
            DeleteNode(f, nodeId);
        }

        void DeleteNode(FloorData f, string nodeId)
        {
            f.nodes.RemoveAll(n => n.id == nodeId);
            ClearLinksTo(nodeId);
        }

        void RemoveIfOrphan(FloorData f, string nodeId)
        {
            if (!f.segments.Exists(s => s.startNodeId == nodeId || s.endNodeId == nodeId))
                DeleteNode(f, nodeId);
        }
    }
}
