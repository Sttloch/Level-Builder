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
        public List<StairData> stairs = new List<StairData>();

        /// <summary>
        /// Raised for every wall split (floor, original segment id, new segment id). The original id stays on
        /// the start-side half; the new segment is the end-side half. Lets later systems copy per-wall data
        /// (materials, openings) to the new half. Never serialized.
        /// </summary>
        [field: NonSerialized]
        public event Action<FloorData, string, string> SegmentSplit;

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
            stairs.RemoveAll(st => FloorIndex(st.fromFloorId) < 0 || FloorIndex(st.toFloorId) < 0);
            return true;
        }

        /// <summary>Index of the floor with this id, or -1.</summary>
        public int FloorIndex(string floorId)
        {
            for (int i = 0; i < floors.Count; i++)
                if (floors[i].id == floorId) return i;
            return -1;
        }

        public StairData FindStair(string id) => stairs.Find(s => s.id == id);

        /// <summary>Creates a stair from floor fromFloorIndex to the one above. Null if there is no floor above or the route has fewer than 2 points.</summary>
        public StairData AddStair(int fromFloorIndex, IList<StairPoint> route)
        {
            if (fromFloorIndex < 0 || fromFloorIndex + 1 >= floors.Count) return null;
            if (route == null || route.Count < 2) return null;
            var s = new StairData
            {
                id = IdUtil.New(),
                fromFloorId = floors[fromFloorIndex].id,
                toFloorId = floors[fromFloorIndex + 1].id
            };
            foreach (var p in route) s.route.Add(p);
            ClampStair(s);
            stairs.Add(s);
            return s;
        }

        public bool RemoveStair(string id)
        {
            var s = FindStair(id);
            return s != null && stairs.Remove(s);
        }

        public bool MoveStairPoint(string id, int index, Vector2 p)
        {
            var s = FindStair(id);
            if (s == null || index < 0 || index >= s.route.Count) return false;
            var pt = s.route[index]; pt.position = p; s.route[index] = pt;
            return true;
        }

        /// <summary>Inserts before index (index == Count appends).</summary>
        public bool InsertStairPoint(string id, int index, StairPoint p)
        {
            var s = FindStair(id);
            if (s == null || index < 0 || index > s.route.Count) return false;
            s.route.Insert(index, p);
            return true;
        }

        public bool RemoveStairPoint(string id, int index)
        {
            var s = FindStair(id);
            if (s == null || index < 0 || index >= s.route.Count || s.route.Count <= 2) return false;
            s.route.RemoveAt(index);
            return true;
        }

        public bool SetStairPointKind(string id, int index, StairPointKind k)
        {
            var s = FindStair(id);
            if (s == null || index < 0 || index >= s.route.Count) return false;
            var pt = s.route[index]; pt.kind = k; s.route[index] = pt;
            return true;
        }

        public static void ClampStair(StairData s)
        {
            s.width = Mathf.Clamp(s.width, 0.6f, 3.0f);
            s.riserTarget = Mathf.Clamp(s.riserTarget, 0.12f, 0.20f);
            s.tread = Mathf.Clamp(s.tread, 0.25f, 0.32f);
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
                var copy = new WallSegment
                {
                    id = IdUtil.New(),
                    startNodeId = Clone(s.startNodeId).id,
                    endNodeId = Clone(s.endNodeId).id,
                    thickness = s.thickness,
                    heightOverride = s.heightOverride
                };
                foreach (var o in s.openings)
                    if (o.type == OpeningType.Window) copy.openings.Add(o.Clone());
                f.segments.Add(copy);
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
            var best = NearestNode(f, p, null);
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
            SplitSegmentAt(f, s, node);
            return node;
        }

        /// <summary>
        /// Splits s at an existing node: s keeps its id and becomes start -> node, a new segment node -> end is
        /// added (same thickness/height) and SegmentSplit is raised. Returns the new half, or null (nothing
        /// changed) if the node is an endpoint or within MergeTolerance of one.
        /// </summary>
        WallSegment SplitSegmentAt(FloorData f, WallSegment s, WallNode node)
        {
            var a = f.GetNode(s.startNodeId);
            var b = f.GetNode(s.endNodeId);
            if (a == null || b == null || node == a || node == b) return null;
            if ((node.position - a.position).magnitude <= MergeTolerance
                || (node.position - b.position).magnitude <= MergeTolerance) return null;
            var second = new WallSegment
            {
                id = IdUtil.New(),
                startNodeId = node.id,
                endNodeId = s.endNodeId,
                thickness = s.thickness,
                heightOverride = s.heightOverride
            };
            s.endNodeId = node.id;
            float splitLen = (node.position - a.position).magnitude;
            var kept = new List<WallOpening>();
            foreach (var o in s.openings)
            {
                if (o.center >= splitLen) { o.center -= splitLen; second.openings.Add(o); }
                else kept.Add(o);
            }
            s.openings = kept;
            f.segments.Add(second);
            SegmentSplit?.Invoke(f, s.id, second.id);
            return second;
        }

        /// <summary>Reverts a split: if the original ends where the added half starts and that node joins nothing
        /// else, the original is extended to the added half's end and the added half and middle node are removed.
        /// Returns false (nothing changed) otherwise.</summary>
        public bool JoinSplit(FloorData f, string originalId, string addedId)
        {
            var s = f.GetSegment(originalId);
            var t = f.GetSegment(addedId);
            if (s == null || t == null || s.endNodeId != t.startNodeId) return false;
            string mid = s.endNodeId;
            if (f.segments.Exists(x => x != s && x != t && (x.startNodeId == mid || x.endNodeId == mid))) return false;
            float len = Length(f, s);
            foreach (var o in t.openings) { o.center += len; s.openings.Add(o); }
            t.openings.Clear();
            s.endNodeId = t.endNodeId;
            f.segments.Remove(t);
            DeleteNode(f, mid);
            return true;
        }

        public List<WallSegment> AddSegment(FloorData f, Vector2 a, Vector2 b)
        {
            var created = new List<WallSegment>();
            if ((b - a).magnitude < MergeTolerance) return created;

            var na = GetOrAddNode(f, a);
            var nb = GetOrAddNode(f, b);
            if (na == nb) return created;

            var ordered = new List<WallNode> { na };
            ordered.AddRange(SplitNodesAlong(f, na, nb));
            ordered.Add(nb);
            for (int i = 0; i < ordered.Count - 1; i++)
            {
                var seg = TryCreateSegment(f, ordered[i], ordered[i + 1]);
                if (seg != null) created.Add(seg);
            }
            return created;
        }

        /// <summary>
        /// Every node where the straight wall a -> b must be split to keep the floor planar, sorted from a.
        /// Each proper crossing with another wall first gets a node (the crossed wall is split there, raising
        /// SegmentSplit); then all nodes lying on the interior of a -> b (crossings, T-junctions from the other
        /// side, collinear overlaps) are returned. a and b themselves are excluded.
        /// Shared by AddSegment (new walls) and ResolveNode (moved walls).
        /// </summary>
        List<WallNode> SplitNodesAlong(FloorData f, WallNode a, WallNode b)
        {
            var crossings = new List<KeyValuePair<WallSegment, Vector2>>();
            foreach (var s in f.segments)
            {
                var sa = f.GetNode(s.startNodeId);
                var sb = f.GetNode(s.endNodeId);
                if (sa == null || sb == null) continue;
                if (Seg2D.Intersect(a.position, b.position, sa.position, sb.position, out var p, out _, out _))
                    crossings.Add(new KeyValuePair<WallSegment, Vector2>(s, p));
            }
            foreach (var c in crossings) NodeOnSegment(f, c.Key, c.Value);

            var found = new List<KeyValuePair<float, WallNode>>();
            foreach (var n in f.nodes)
            {
                if (n == a || n == b) continue;
                if ((n.position - a.position).magnitude <= MergeTolerance
                    || (n.position - b.position).magnitude <= MergeTolerance) continue;
                float d = Seg2D.DistanceToSegment(n.position, a.position, b.position, out float t);
                if (d <= MergeTolerance) found.Add(new KeyValuePair<float, WallNode>(t, n));
            }
            found.Sort((x, y) => x.Key.CompareTo(y.Key));
            var result = new List<WallNode>(found.Count);
            foreach (var kv in found) result.Add(kv.Value);
            return result;
        }

        /// <summary>A node at p on segment s: reuses a node within tolerance (splitting s there unless it is an
        /// endpoint), otherwise splits s at p.</summary>
        WallNode NodeOnSegment(FloorData f, WallSegment s, Vector2 p)
        {
            var existing = NearestNode(f, p, null);
            if (existing == null) return SplitSegment(f, s.id, p);
            SplitSegmentAt(f, s, existing);
            return existing;
        }

        WallNode NearestNode(FloorData f, Vector2 p, WallNode exclude)
        {
            WallNode best = null;
            float bestD = MergeTolerance;
            foreach (var n in f.nodes)
            {
                if (n == exclude) continue;
                float d = (n.position - p).magnitude;
                if (d <= bestD) { bestD = d; best = n; }
            }
            return best;
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

        /// <summary>
        /// Restores planarity around a node after it moved (e.g. at the end of a drag): (a) merges it into another
        /// node within MergeTolerance (segments re-pointed, zero-length and duplicate segments dropped, links kept),
        /// (b) splits walls it lies on, (c) splits its walls wherever they cross or touch other walls.
        /// Returns the surviving node, or null if nothing is left of it.
        /// </summary>
        public WallNode ResolveNode(FloorData f, string nodeId)
        {
            var n = f.GetNode(nodeId);
            if (n == null) return null;
            var other = NearestNode(f, n.position, n);
            if (other != null)
            {
                MergeNode(f, n, other);
                n = other;
            }
            SplitWallsThrough(f, n);
            foreach (var s in f.segments.FindAll(x => x.startNodeId == n.id || x.endNodeId == n.id))
            {
                var a = f.GetNode(s.startNodeId);
                var b = f.GetNode(s.endNodeId);
                if (a == null || b == null || a == b) continue;
                SplitWallsThrough(f, a == n ? b : a); // collinear overlap reaching past the far end
                var cur = s;
                foreach (var k in SplitNodesAlong(f, a, b))
                    cur = SplitSegmentAt(f, cur, k) ?? cur;
            }
            RemoveDegenerateSegments(f);
            if (!f.segments.Exists(x => x.startNodeId == n.id || x.endNodeId == n.id))
            {
                DeleteNode(f, n.id);
                return null;
            }
            return n;
        }

        /// <summary>ResolveNode on every floor of the node's linked column (after the column moved together).</summary>
        public void ResolveLinkedColumn(string nodeId)
        {
            foreach (var (fl, node) in new List<(FloorData, WallNode)>(GetLinkedColumn(nodeId)))
                ResolveNode(fl, node.id);
        }

        // Re-points from's segments and upper links to into, then removes from. into keeps its own link below
        // if it has one, otherwise inherits from's.
        void MergeNode(FloorData f, WallNode from, WallNode into)
        {
            foreach (var s in f.segments)
            {
                if (s.startNodeId == from.id) s.startNodeId = into.id;
                if (s.endNodeId == from.id) s.endNodeId = into.id;
            }
            if (string.IsNullOrEmpty(into.linkedBelowId)) into.linkedBelowId = from.linkedBelowId;
            foreach (var fl in floors)
                foreach (var up in fl.nodes)
                    if (up.linkedBelowId == from.id) up.linkedBelowId = into.id;
            f.nodes.Remove(from);
        }

        // Splits every wall not incident to node that passes within MergeTolerance of it.
        void SplitWallsThrough(FloorData f, WallNode node)
        {
            foreach (var s in f.segments.ToArray())
            {
                if (s.startNodeId == node.id || s.endNodeId == node.id) continue;
                var a = f.GetNode(s.startNodeId);
                var b = f.GetNode(s.endNodeId);
                if (a == null || b == null) continue;
                if (Seg2D.DistanceToSegment(node.position, a.position, b.position, out _) <= MergeTolerance)
                    SplitSegmentAt(f, s, node);
            }
        }

        // Drops zero-length segments and duplicates (same node pair, either direction), keeping the first.
        // A removed duplicate's openings move to the survivor (center mirrored if the direction is opposite).
        void RemoveDegenerateSegments(FloorData f)
        {
            var seen = new Dictionary<string, WallSegment>();
            f.segments.RemoveAll(s =>
            {
                if (s.startNodeId == s.endNodeId) return true;
                string key = string.CompareOrdinal(s.startNodeId, s.endNodeId) < 0
                    ? s.startNodeId + "|" + s.endNodeId
                    : s.endNodeId + "|" + s.startNodeId;
                if (!seen.TryGetValue(key, out var keeper)) { seen[key] = s; return false; }
                bool opposite = keeper.startNodeId != s.startNodeId;
                float len = Length(f, keeper);
                foreach (var o in s.openings)
                {
                    if (opposite) o.center = len - o.center;
                    keeper.openings.Add(o);
                }
                s.openings.Clear();
                return true;
            });
        }

        /// <summary>Distance between the segment's node positions (0 if a node is missing).</summary>
        public static float Length(FloorData f, WallSegment s)
        {
            var a = f.GetNode(s.startNodeId);
            var b = f.GetNode(s.endNodeId);
            return a == null || b == null ? 0f : (b.position - a.position).magnitude;
        }

        public WallOpening AddOpening(FloorData f, string segmentId, WallOpening o)
        {
            var s = f.GetSegment(segmentId);
            if (s == null || o == null) return o;
            if (string.IsNullOrEmpty(o.id)) o.id = IdUtil.New();
            s.openings.Add(o);
            return o;
        }

        public bool RemoveOpening(FloorData f, string openingId)
        {
            var (seg, o) = FindOpening(f, openingId);
            return o != null && seg.openings.Remove(o);
        }

        public (WallSegment seg, WallOpening o) FindOpening(FloorData f, string openingId)
        {
            foreach (var s in f.segments)
                foreach (var o in s.openings)
                    if (o.id == openingId) return (s, o);
            return (null, null);
        }

        public bool MoveOpening(FloorData f, string openingId, string toSegmentId, float center)
        {
            var (seg, o) = FindOpening(f, openingId);
            var to = f.GetSegment(toSegmentId);
            if (o == null || to == null) return false;
            seg.openings.Remove(o);
            o.center = center;
            to.openings.Add(o);
            return true;
        }

        public void RemoveSegment(FloorData f, string segmentId)
        {
            var s = f.GetSegment(segmentId);
            if (s == null) return;
            f.segments.Remove(s);
            RemoveIfOrphan(f, s.startNodeId);
            RemoveIfOrphan(f, s.endNodeId);
        }

        /// <summary>Removes a wall and then merges, at each end node that is left with exactly two collinear
        /// segments (under 0.5 degrees) and no floor links, those two into one (openings keep their position).
        /// Returns false if the segment does not exist.</summary>
        public bool RemoveWall(FloorData f, string segmentId)
        {
            var s = f.GetSegment(segmentId);
            if (s == null) return false;
            string n1 = s.startNodeId, n2 = s.endNodeId;
            RemoveSegment(f, segmentId);
            TryMergeAt(f, n1);
            TryMergeAt(f, n2);
            return true;
        }

        void TryMergeAt(FloorData f, string nodeId)
        {
            var m = f.GetNode(nodeId);
            if (m == null || IsLinkedAnywhere(nodeId)) return;
            var inc = f.segments.FindAll(x => x.startNodeId == nodeId || x.endNodeId == nodeId);
            if (inc.Count != 2) return;
            var first = inc[0];
            var second = inc[1];
            var pa = f.GetNode(first.startNodeId == nodeId ? first.endNodeId : first.startNodeId);
            var pb = f.GetNode(second.startNodeId == nodeId ? second.endNodeId : second.startNodeId);
            if (pa == null || pb == null || pa == pb) return;
            if (!Mathf.Approximately(first.thickness, second.thickness)
                || !Mathf.Approximately(first.heightOverride, second.heightOverride)) return;
            if (Vector2.Angle(pa.position - m.position, m.position - pb.position) >= 0.5f) return;
            // JoinSplit needs first.end == m and second.start == m.
            if (first.startNodeId == nodeId) ReverseSegment(f, first);
            if (second.endNodeId == nodeId) ReverseSegment(f, second);
            JoinSplit(f, first.id, second.id);
        }

        bool IsLinkedAnywhere(string nodeId)
        {
            foreach (var fl in floors)
                foreach (var n in fl.nodes)
                    if (n.id == nodeId ? !string.IsNullOrEmpty(n.linkedBelowId) : n.linkedBelowId == nodeId) return true;
            return false;
        }

        /// <summary>Swaps start/end and mirrors the openings so they stay in the same place (door hinges keep their physical side).</summary>
        void ReverseSegment(FloorData f, WallSegment s)
        {
            float len = Length(f, s);
            (s.startNodeId, s.endNodeId) = (s.endNodeId, s.startNodeId);
            foreach (var o in s.openings)
            {
                o.center = len - o.center;
                if (o.type == OpeningType.Door && o.door != null) o.door.hingeOnRight = !o.door.hingeOnRight;
            }
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
