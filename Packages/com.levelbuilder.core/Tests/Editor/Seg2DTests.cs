using NUnit.Framework;
using UnityEngine;
using LevelBuilder.Geometry;

namespace LevelBuilder.Tests
{
    public class Seg2DTests
    {
        [Test]
        public void Intersect_CrossingInX_ReturnsPoint()
        {
            bool hit = Seg2D.Intersect(new Vector2(0, 0), new Vector2(4, 0), new Vector2(2, -2), new Vector2(2, 2),
                out var p, out var ta, out var tb);
            Assert.IsTrue(hit);
            Assert.AreEqual(2f, p.x, 1e-5f);
            Assert.AreEqual(0f, p.y, 1e-5f);
            Assert.AreEqual(0.5f, ta, 1e-5f);
            Assert.AreEqual(0.5f, tb, 1e-5f);
        }

        [Test]
        public void Intersect_Parallel_ReturnsFalse()
        {
            Assert.IsFalse(Seg2D.Intersect(new Vector2(0, 0), new Vector2(4, 0), new Vector2(0, 1), new Vector2(4, 1),
                out _, out _, out _));
        }

        [Test]
        public void Intersect_TouchingAtEndpoint_ReturnsFalse()
        {
            Assert.IsFalse(Seg2D.Intersect(new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 0), new Vector2(4, 3),
                out _, out _, out _));
        }

        [Test]
        public void DistanceToSegment_ProjectsAndClamps()
        {
            float d = Seg2D.DistanceToSegment(new Vector2(2, 3), new Vector2(0, 0), new Vector2(4, 0), out var t);
            Assert.AreEqual(3f, d, 1e-5f);
            Assert.AreEqual(0.5f, t, 1e-5f);
            d = Seg2D.DistanceToSegment(new Vector2(6, 0), new Vector2(0, 0), new Vector2(4, 0), out t);
            Assert.AreEqual(2f, d, 1e-5f);
            Assert.AreEqual(1f, t, 1e-5f);
        }
    }
}
