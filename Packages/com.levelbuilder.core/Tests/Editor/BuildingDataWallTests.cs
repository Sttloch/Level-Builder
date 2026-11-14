using NUnit.Framework;
using UnityEngine;

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
    }
}
