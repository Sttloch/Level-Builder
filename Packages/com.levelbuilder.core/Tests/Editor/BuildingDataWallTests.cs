using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using LevelBuilder.Geometry;

namespace LevelBuilder.Tests
{
    public class BuildingDataWallTests
    {
        BuildingData b;
        FloorData f;

        [SetUp]
        public void SetUp()
        {
            b = new BuildingData();
            f = b.AddFloor();
        }

        [Test]
        public void AddSegment_CreatesTwoNodesAndOneSegment()
        {
            var created = b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            Assert.AreEqual(1, created.Count);
            Assert.AreEqual(2, f.nodes.Count);
            Assert.AreEqual(1, f.segments.Count);
        }

        [Test]
        public void AddSegment_ReusesNodeWithinTolerance()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            b.AddSegment(f, new Vector2(4.005f, 0), new Vector2(4, 3));
            Assert.AreEqual(3, f.nodes.Count);
            Assert.AreEqual(2, f.segments.Count);
        }

        [Test]
        public void AddSegment_IgnoresZeroLength()
        {
            var created = b.AddSegment(f, new Vector2(1, 1), new Vector2(1.004f, 1));
            Assert.AreEqual(0, created.Count);
            Assert.AreEqual(0, f.segments.Count);
            Assert.AreEqual(0, f.nodes.Count);
        }

        [Test]
        public void AddSegment_IgnoresDuplicateReversed()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            var created = b.AddSegment(f, new Vector2(4, 0), new Vector2(0, 0));
            Assert.AreEqual(0, created.Count);
            Assert.AreEqual(1, f.segments.Count);
            Assert.AreEqual(2, f.nodes.Count);
        }

        [Test]
        public void AddSegment_CrossingSplitsBoth()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            var created = b.AddSegment(f, new Vector2(2, -2), new Vector2(2, 2));
            Assert.AreEqual(2, created.Count);
            Assert.AreEqual(4, f.segments.Count);
            Assert.AreEqual(5, f.nodes.Count);
            Assert.IsTrue(f.nodes.Exists(n => (n.position - new Vector2(2, 0)).magnitude < 1e-4f));
        }

        [Test]
        public void AddSegment_EndOnSegmentSplitsIt()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            b.AddSegment(f, new Vector2(2, 0), new Vector2(2, 3));
            Assert.AreEqual(3, f.segments.Count);
            Assert.AreEqual(4, f.nodes.Count);
        }

        [Test]
        public void RemoveSegment_RemovesOrphanNodes()
        {
            var created = b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            b.RemoveSegment(f, created[0].id);
            Assert.AreEqual(0, f.segments.Count);
            Assert.AreEqual(0, f.nodes.Count);
        }

        [Test]
        public void RemoveNode_RemovesItsSegments()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 3));
            var corner = f.nodes.Find(n => (n.position - new Vector2(4, 0)).magnitude < 1e-4f);
            b.RemoveNode(f, corner.id);
            Assert.AreEqual(0, f.segments.Count);
            Assert.AreEqual(0, f.nodes.Count);
        }

        [Test]
        public void MoveNode_UpdatesPosition()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            var n = f.nodes[0];
            b.MoveNode(f, n.id, new Vector2(1, 1));
            Assert.AreEqual(new Vector2(1, 1), f.GetNode(n.id).position);
        }

        [Test]
        public void SegmentHeight_UsesOverrideOrFloor()
        {
            var s = new WallSegment();
            Assert.AreEqual(f.height, s.Height(f));
            s.heightOverride = 2f;
            Assert.AreEqual(2f, s.Height(f));
        }

        [Test]
        public void AddFloor_StacksElevation()
        {
            var f2 = b.AddFloor();
            Assert.AreEqual(0f, f.elevation, 1e-5f);
            Assert.AreEqual(3f, f2.elevation, 1e-5f);
            Assert.AreEqual(2, b.floors.Count);
        }

        [Test]
        public void SplitSegment_AtEndpoint_ReturnsExistingNodeNoNewSegment()
        {
            var seg = b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0))[0];
            var end = f.GetNode(seg.endNodeId);
            var start = f.GetNode(seg.startNodeId);
            Assert.AreSame(end, b.SplitSegment(f, seg.id, new Vector2(4.005f, 0.3f)));
            Assert.AreSame(start, b.SplitSegment(f, seg.id, new Vector2(-1f, 0f)));
            Assert.AreEqual(2, f.nodes.Count);
            Assert.AreEqual(1, f.segments.Count);
        }
        // ---- AddSegment edge cases ----

        [Test]
        public void AddSegment_CrossingTwoParallelWalls_ThreePieces()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            b.AddSegment(f, new Vector2(0, 2), new Vector2(4, 2));
            var created = b.AddSegment(f, new Vector2(2, -1), new Vector2(2, 3));
            Assert.AreEqual(3, created.Count);
            Assert.AreEqual(8, f.nodes.Count);
            Assert.AreEqual(7, f.segments.Count);
            AssertNoDuplicateSegments();
        }

        [Test]
        public void AddSegment_CollinearOverlap_OnlyNewPiece()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            var created = b.AddSegment(f, new Vector2(2, 0), new Vector2(6, 0));
            Assert.AreEqual(1, created.Count);
            Assert.AreEqual(4, f.nodes.Count);
            Assert.AreEqual(3, f.segments.Count);
            AssertNoDuplicateSegments();
        }

        [Test]
        public void AddSegment_CollinearContaining_SkipsExistingPiece()
        {
            b.AddSegment(f, new Vector2(1, 0), new Vector2(3, 0));
            var created = b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            Assert.AreEqual(2, created.Count);
            Assert.AreEqual(4, f.nodes.Count);
            Assert.AreEqual(3, f.segments.Count);
            AssertNoDuplicateSegments();
        }

        [Test]
        public void AddSegment_EndingOnWall_SplitsIt()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            b.AddSegment(f, new Vector2(2, 3), new Vector2(2, 0));
            Assert.AreEqual(3, f.segments.Count);
            Assert.AreEqual(4, f.nodes.Count);
            Assert.AreEqual(3, Degree(Node(2, 0)));
        }

        [Test]
        public void AddSegment_ThroughExistingNode_SplitAtThatNode()
        {
            b.AddSegment(f, new Vector2(2, 0), new Vector2(2, 2));
            var created = b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            Assert.AreEqual(2, created.Count);
            Assert.AreEqual(4, f.nodes.Count);
            Assert.AreEqual(3, f.segments.Count);
            Assert.AreEqual(3, Degree(Node(2, 0)));
        }

        // ---- SegmentSplit event ----

        [Test]
        public void SplitSegment_RaisesSegmentSplit_OriginalKeepsStartHalf()
        {
            var seg = b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0))[0];
            var startId = seg.startNodeId;
            var events = Record();
            var mid = b.SplitSegment(f, seg.id, new Vector2(1, 0));
            Assert.AreEqual(1, events.Count);
            Assert.AreSame(f, events[0].floor);
            Assert.AreEqual(seg.id, events[0].original);
            var orig = f.GetSegment(events[0].original);
            var added = f.GetSegment(events[0].added);
            Assert.IsNotNull(added);
            Assert.AreEqual(startId, orig.startNodeId);
            Assert.AreEqual(mid.id, orig.endNodeId);
            Assert.AreEqual(mid.id, added.startNodeId);
        }

        [Test]
        public void AddSegment_Crossing_RaisesSegmentSplitForCrossedWall()
        {
            var seg = b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0))[0];
            var events = Record();
            b.AddSegment(f, new Vector2(2, -2), new Vector2(2, 2));
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(seg.id, events[0].original);
            var added = f.GetSegment(events[0].added);
            Assert.IsNotNull(added);
            Assert.AreEqual(new Vector2(2, 0), f.GetNode(added.startNodeId).position);
            Assert.AreEqual(new Vector2(4, 0), f.GetNode(added.endNodeId).position);
        }

        [Test]
        public void SegmentSplit_IsNotSerialized()
        {
            b.SegmentSplit += (fl, o, a) => { };
            var json = JsonUtility.ToJson(b);
            var back = JsonUtility.FromJson<BuildingData>(json);
            Assert.AreEqual(1, back.floors.Count);
            Assert.IsFalse(json.Contains("SegmentSplit"));
        }

        // ---- ResolveNode (node drag) ----

        void Square()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 4));
            b.AddSegment(f, new Vector2(4, 4), new Vector2(0, 4));
            b.AddSegment(f, new Vector2(0, 4), new Vector2(0, 0));
        }

        [Test]
        public void ResolveNode_DroppedOnNode_Merges()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 4));
            b.AddSegment(f, new Vector2(4, 4), new Vector2(0, 4));
            b.AddSegment(f, new Vector2(0, 3), new Vector2(0, 0));
            Assert.AreEqual(0, PlanarFaces.Compute(f).regions.Count);
            var loose = Node(0, 3);
            b.MoveNode(f, loose.id, new Vector2(0, 4.004f));
            var survivor = b.ResolveNode(f, loose.id);
            Assert.AreEqual(4, f.nodes.Count);
            Assert.AreEqual(4, f.segments.Count);
            Assert.IsNull(f.GetNode(loose.id));
            Assert.IsNotNull(survivor);
            Assert.AreEqual(new Vector2(0, 4), survivor.position);
            AssertNoDuplicateSegments();
            Assert.AreEqual(1, PlanarFaces.Compute(f).regions.Count);
        }

        [Test]
        public void ResolveNode_DroppedOnNeighbour_DropsZeroLengthAndDuplicate()
        {
            Square();
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 4)); // diagonal
            var corner = Node(4, 0);
            b.MoveNode(f, corner.id, new Vector2(4, 4));
            b.ResolveNode(f, corner.id);
            // (0,0)-(4,0) becomes a duplicate of the diagonal, (4,0)-(4,4) becomes zero length.
            Assert.AreEqual(3, f.nodes.Count);
            Assert.AreEqual(3, f.segments.Count);
            AssertNoDuplicateSegments();
            Assert.AreEqual(1, PlanarFaces.Compute(f).regions.Count);
        }

        [Test]
        public void ResolveNode_DroppedOnWall_SplitsIt_TJunction()
        {
            Square();
            b.AddSegment(f, new Vector2(2, 1), new Vector2(2, 3));
            var lower = Node(2, 1);
            var upper = Node(2, 3);
            b.MoveNode(f, lower.id, new Vector2(2, 0));
            b.ResolveNode(f, lower.id);
            b.MoveNode(f, upper.id, new Vector2(2, 4.005f));
            b.ResolveNode(f, upper.id);
            Assert.AreEqual(6, f.nodes.Count);
            Assert.AreEqual(7, f.segments.Count);
            Assert.AreEqual(3, Degree(f.GetNode(lower.id)));
            Assert.AreEqual(3, Degree(f.GetNode(upper.id)));
            Assert.AreEqual(2, PlanarFaces.Compute(f).regions.Count);
        }

        [Test]
        public void ResolveNode_DraggedAcrossWall_CreatesCrossingNode()
        {
            Square();
            b.AddSegment(f, new Vector2(2, 1), new Vector2(2, 3));
            var upper = Node(2, 3);
            var events = Record();
            b.MoveNode(f, upper.id, new Vector2(2, 6));
            b.ResolveNode(f, upper.id);
            Assert.AreEqual(7, f.nodes.Count);
            Assert.AreEqual(7, f.segments.Count);
            var cross = Node(2, 4);
            Assert.IsNotNull(cross);
            Assert.AreEqual(4, Degree(cross));
            Assert.AreEqual(2, events.Count); // top wall + dragged wall
            AssertNoDuplicateSegments();
        }

        [Test]
        public void ResolveNode_WallDraggedOntoNode_SplitsAtNode()
        {
            Square();
            b.AddSegment(f, new Vector2(2, 2), new Vector2(3, 2)); // inner stub, node (2,2)
            b.AddSegment(f, new Vector2(1, 1), new Vector2(1, 3)); // free wall
            var top = Node(1, 3);
            b.MoveNode(f, top.id, new Vector2(3, 3)); // (1,1)-(3,3) passes through (2,2)
            b.ResolveNode(f, top.id);
            Assert.AreEqual(3, Degree(Node(2, 2)));
            AssertNoDuplicateSegments();
        }

        [Test]
        public void ResolveLinkedColumn_CrossingOnEveryFloor()
        {
            Square();
            var f1 = b.AddFloor();
            var corner = Node(4, 4);
            // (0,4)-(2,-2) crosses the bottom wall (0,0)-(4,0) at (4/3, 0) on both floors.
            b.MoveNode(f, corner.id, new Vector2(2, -2));
            b.ResolveLinkedColumn(corner.id);
            foreach (var fl in new[] { f, f1 })
            {
                Assert.IsTrue(fl.nodes.Exists(n => (n.position - new Vector2(4f / 3f, 0)).magnitude < 1e-3f), fl.name);
                Assert.AreEqual(6, fl.segments.Count, fl.name);
            }
        }

        [Test]
        public void ResolveLinkedColumn_MergeOnEveryFloor_RepointsLinks()
        {
            Square();
            var f1 = b.AddFloor();
            var d = Node(0, 4);
            var c = Node(4, 4);
            b.MoveNode(f, d.id, new Vector2(4, 4));
            b.ResolveLinkedColumn(d.id);
            foreach (var fl in new[] { f, f1 })
            {
                Assert.AreEqual(3, fl.nodes.Count, fl.name);
                Assert.AreEqual(3, fl.segments.Count, fl.name);
                Assert.AreEqual(1, PlanarFaces.Compute(fl).regions.Count, fl.name);
            }
            Assert.IsNull(f.GetNode(d.id));
            foreach (var n in f1.nodes)
            {
                var below = f.GetNode(n.linkedBelowId);
                Assert.IsNotNull(below);
                Assert.AreEqual(below.position, n.position);
            }
            Assert.AreEqual(1, f1.nodes.Count(n => n.linkedBelowId == c.id));
        }

        // ---- helpers ----

        WallNode Node(float x, float y) =>
            f.nodes.Find(n => (n.position - new Vector2(x, y)).magnitude < 0.02f);

        int Degree(WallNode n) => f.segments.Count(s => s.startNodeId == n.id || s.endNodeId == n.id);

        void AssertNoDuplicateSegments()
        {
            var keys = new HashSet<string>();
            foreach (var s in f.segments)
            {
                Assert.AreNotEqual(s.startNodeId, s.endNodeId, "zero-length segment");
                string k = string.CompareOrdinal(s.startNodeId, s.endNodeId) < 0
                    ? s.startNodeId + "|" + s.endNodeId : s.endNodeId + "|" + s.startNodeId;
                Assert.IsTrue(keys.Add(k), "duplicate segment");
            }
        }

        void BuildRoomWithDivider(FloorData fl)
        {
            b.AddSegment(fl, new Vector2(0, 0), new Vector2(8, 0));
            b.AddSegment(fl, new Vector2(8, 0), new Vector2(8, 6));
            b.AddSegment(fl, new Vector2(8, 6), new Vector2(0, 6));
            b.AddSegment(fl, new Vector2(0, 6), new Vector2(0, 0));
            b.AddSegment(fl, new Vector2(4, 0), new Vector2(4, 6));
        }

        WallSegment DividerOf(FloorData fl) => fl.segments.First(s =>
            Mathf.Approximately(fl.GetNode(s.startNodeId).position.x, 4f) &&
            Mathf.Approximately(fl.GetNode(s.endNodeId).position.x, 4f));

        [Test]
        public void RemoveWall_MergesTwoRoomsIntoOneRegion()
        {
            BuildRoomWithDivider(f);
            Assert.AreEqual(2, PlanarFaces.Compute(f).regions.Count);
            Assert.IsTrue(b.RemoveWall(f, DividerOf(f).id));
            Assert.AreEqual(1, PlanarFaces.Compute(f).regions.Count);
        }

        [Test]
        public void RemoveWall_ReturnsFalseForMissingSegment()
        {
            Assert.IsFalse(b.RemoveWall(f, "nope"));
        }

        [Test]
        public void RemoveWall_JoinsCollinearLeftovers()
        {
            BuildRoomWithDivider(f);
            b.RemoveWall(f, DividerOf(f).id);
            Assert.AreEqual(4, f.segments.Count);
            Assert.AreEqual(4, f.nodes.Count);
            Assert.IsFalse(f.nodes.Any(n => (n.position - new Vector2(4, 0)).magnitude < 0.01f));
            Assert.IsFalse(f.nodes.Any(n => (n.position - new Vector2(4, 6)).magnitude < 0.01f));
        }

        [Test]
        public void RemoveWall_KeepsOpeningsOnJoinedWall()
        {
            BuildRoomWithDivider(f);
            var east = f.segments.First(s => f.GetNode(s.startNodeId).position == new Vector2(4, 0)
                                             && f.GetNode(s.endNodeId).position == new Vector2(8, 0));
            var door = new WallOpening { id = "d1", type = OpeningType.Door, center = 2f, width = 1f, height = 2f };
            b.AddOpening(f, east.id, door);
            b.RemoveWall(f, DividerOf(f).id);
            var (seg, o) = b.FindOpening(f, "d1");
            Assert.IsNotNull(seg);
            var a = f.GetNode(seg.startNodeId).position;
            var e = f.GetNode(seg.endNodeId).position;
            var abs = a + (e - a).normalized * o.center;
            Assert.AreEqual(6f, abs.x, 0.001f);
            Assert.AreEqual(0f, abs.y, 0.001f);
        }

        [Test]
        public void RemoveWall_KeepsOpeningsWhenLeftoversAreOppositelyOriented()
        {
            b.AddSegment(f, new Vector2(4, 0), new Vector2(0, 0));   // m -> a
            b.AddSegment(f, new Vector2(4, 0), new Vector2(8, 0));   // m -> b
            var div = b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 5))[0];
            var east = f.segments.First(s => f.GetNode(s.endNodeId).position == new Vector2(8, 0));
            b.AddOpening(f, east.id, new WallOpening { id = "d2", type = OpeningType.Window, center = 1f, width = 1f, height = 1f, sill = 1f });
            Assert.IsTrue(b.RemoveWall(f, div.id));
            Assert.AreEqual(1, f.segments.Count);
            var (seg, o) = b.FindOpening(f, "d2");
            var a = f.GetNode(seg.startNodeId).position;
            var e = f.GetNode(seg.endNodeId).position;
            var abs = a + (e - a).normalized * o.center;
            Assert.AreEqual(5f, abs.x, 0.001f);
        }

        [Test]
        public void RemoveWall_DoorOnReversedLeftover_ToggledHingeAndKeepsPosition()
        {
            var west = b.AddSegment(f, new Vector2(4, 0), new Vector2(0, 0))[0];   // m -> a, gets reversed
            b.AddSegment(f, new Vector2(4, 0), new Vector2(8, 0));
            var div = b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 5))[0];
            var o = new WallOpening { id = "d3", type = OpeningType.Door, center = 1f, width = 1f, height = 2f };
            o.door.hingeOnRight = false;
            b.AddOpening(f, west.id, o);
            Assert.IsTrue(b.RemoveWall(f, div.id));
            var (seg, o2) = b.FindOpening(f, "d3");
            var a = f.GetNode(seg.startNodeId).position;
            var e = f.GetNode(seg.endNodeId).position;
            Assert.AreEqual(3f, (a + (e - a).normalized * o2.center).x, 0.001f);
            Assert.IsTrue(o2.door.hingeOnRight);
        }

        [Test]
        public void RemoveWall_DifferentThickness_DoesNotMerge()
        {
            var s1 = b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0))[0];
            var s2 = b.AddSegment(f, new Vector2(4, 0), new Vector2(8, 0))[0];
            s2.thickness = s1.thickness + 0.1f;
            var div = b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 5))[0];
            b.RemoveWall(f, div.id);
            Assert.AreEqual(2, f.segments.Count);
            Assert.AreEqual(3, f.nodes.Count);
        }

        [Test]
        public void RemoveWall_DifferentHeightOverride_DoesNotMerge()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            var s2 = b.AddSegment(f, new Vector2(4, 0), new Vector2(8, 0))[0];
            s2.heightOverride = 1f;
            var div = b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 5))[0];
            b.RemoveWall(f, div.id);
            Assert.AreEqual(2, f.segments.Count);
            Assert.AreEqual(3, f.nodes.Count);
        }

        [Test]
        public void RemoveWall_InheritedAndExplicitFullHeight_Merge()
        {
            b.AddSegment(f, new Vector2(0, 0), new Vector2(4, 0));
            var s2 = b.AddSegment(f, new Vector2(4, 0), new Vector2(8, 0))[0];
            s2.heightOverride = f.height;
            var div = b.AddSegment(f, new Vector2(4, 0), new Vector2(4, 5))[0];
            b.RemoveWall(f, div.id);
            Assert.AreEqual(1, f.segments.Count);
            Assert.AreEqual(2, f.nodes.Count);
        }

        [Test]
        public void RemoveWall_KeepsLinkedNodes()
        {
            BuildRoomWithDivider(f);
            var upper = new FloorData { id = "upper", name = "Up" };
            b.floors.Add(upper);
            var below = f.nodes.First(n => (n.position - new Vector2(4, 0)).magnitude < 0.01f);
            var up = new WallNode { id = "up1", position = new Vector2(4, 0), linkedBelowId = below.id };
            upper.nodes.Add(up);
            b.RemoveWall(f, DividerOf(f).id);
            Assert.IsNotNull(f.GetNode(below.id));
            Assert.AreEqual(2, f.segments.Count(s => s.startNodeId == below.id || s.endNodeId == below.id));
            // the unlinked far end still joins
            Assert.IsFalse(f.nodes.Any(n => (n.position - new Vector2(4, 6)).magnitude < 0.01f));
        }

        List<(FloorData floor, string original, string added)> Record()
        {
            var list = new List<(FloorData floor, string original, string added)>();
            b.SegmentSplit += (fl, o, a) => list.Add((fl, o, a));
            return list;
        }
    }
}
