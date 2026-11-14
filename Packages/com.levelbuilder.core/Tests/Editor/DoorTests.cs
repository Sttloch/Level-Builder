using NUnit.Framework;
using UnityEngine;

namespace LevelBuilder.Tests
{
    public class DoorTests
    {
        GameObject _go;

        [TearDown] public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        Door Make(DoorSettings s, float w = 0.9f)
        {
            _go = new GameObject("door");
            var leaf = new GameObject("leaf");
            leaf.transform.SetParent(_go.transform, false);
            leaf.transform.localPosition = new Vector3(0, 1.05f, 0);
            var d = _go.AddComponent<Door>();
            d.settings = s;
            d.leaf = leaf.transform;
            d.leafSize = new Vector3(w, 2.1f, 0.05f);
            return d;
        }

        [Test] public void DoorMotion_OpensOverTime()
        {
            var m = new DoorMotion();
            m.Open(1);
            Assert.IsTrue(m.Step(0.25f, 2f, out var o, out var c));
            Assert.AreEqual(0.5f, m.Progress, 1e-5f);
            Assert.IsFalse(o);
            m.Step(0.5f, 2f, out o, out c);
            Assert.AreEqual(1f, m.Progress, 1e-5f);
            Assert.IsTrue(o);
            m.Step(0.5f, 2f, out o, out c);
            Assert.IsFalse(o);
        }

        [Test] public void DoorMotion_Locked_DoesNotOpen()
        {
            var m = new DoorMotion { Locked = true };
            m.Open(1);
            m.Step(1f, 2f, out _, out _);
            Assert.AreEqual(0f, m.Progress);
            Assert.IsFalse(m.IsOpen);
        }

        [Test] public void DoorMotion_Unlock_ThenOpens()
        {
            var m = new DoorMotion { Locked = true };
            m.Locked = false;
            m.Open(1);
            m.Step(1f, 2f, out _, out _);
            Assert.AreEqual(1f, m.Progress, 1e-5f);
        }

        [Test] public void DoorMotion_DirectionOnlyChangesWhenClosed()
        {
            var m = new DoorMotion();
            m.Open(1);
            m.Step(0.25f, 2f, out _, out _);
            m.Open(-1);
            Assert.AreEqual(1, m.Direction);
            m.Close();
            m.Step(2f, 2f, out _, out _);
            Assert.AreEqual(0f, m.Progress);
            m.Open(-1);
            Assert.AreEqual(-1, m.Direction);
        }

        [Test] public void Door_Swing_RotatesLeafAroundHinge()
        {
            var d = Make(new DoorSettings());
            var hingeBefore = d.leaf.TransformPoint(new Vector3(-0.45f, 0, 0));
            d.Open();
            d.Tick(1f);
            Assert.AreEqual(95f, Quaternion.Angle(Quaternion.identity, d.leaf.localRotation), 1e-2f);
            Assert.AreEqual(95f, d.leaf.localRotation.eulerAngles.y, 1e-2f);
            var hingeAfter = d.leaf.TransformPoint(new Vector3(-0.45f, 0, 0));
            Assert.AreEqual(0f, Vector3.Distance(hingeBefore, hingeAfter), 1e-4f);
        }

        float FreeEdgeZ(Door d, bool hingeRight)
        {
            var p = d.leaf.TransformPoint(new Vector3(hingeRight ? -0.45f : 0.45f, 0, 0));
            return d.transform.InverseTransformPoint(p).z;
        }

        [Test] public void Door_BothWays_OpensAwayFromPosition()
        {
            var d = Make(new DoorSettings { bothWays = true });
            d.Open(d.transform.TransformPoint(new Vector3(0, 0, 2)));
            d.Tick(1f);
            Assert.Less(FreeEdgeZ(d, false), -0.1f);
            Object.DestroyImmediate(_go);

            d = Make(new DoorSettings { bothWays = true });
            d.Open(d.transform.TransformPoint(new Vector3(0, 0, -2)));
            d.Tick(1f);
            Assert.Greater(FreeEdgeZ(d, false), 0.1f);
            Object.DestroyImmediate(_go);

            d = Make(new DoorSettings { bothWays = true, hingeOnRight = true });
            d.Open(d.transform.TransformPoint(new Vector3(0, 0, 2)));
            d.Tick(1f);
            Assert.Less(FreeEdgeZ(d, true), -0.1f);
        }

        [Test] public void Door_Sliding_MovesLeaf()
        {
            var d = Make(new DoorSettings { kind = DoorKind.Sliding });
            float x0 = d.leaf.localPosition.x;
            d.Open();
            d.Tick(1f);
            Assert.AreEqual(0.855f, Mathf.Abs(d.leaf.localPosition.x - x0), 1e-4f);
        }

        [Test] public void Door_EventsFire()
        {
            var d = Make(new DoorSettings());
            int opened = 0, closed = 0;
            d.onOpened.AddListener(() => opened++);
            d.onClosed.AddListener(() => closed++);
            d.Open(); d.Tick(1f); d.Tick(1f);
            Assert.AreEqual(1, opened);
            d.Close(); d.Tick(1f); d.Tick(1f);
            Assert.AreEqual(1, closed);
            Assert.AreEqual(1, opened);
        }

        [Test] public void Door_Locked_UnlockOpens()
        {
            var d = Make(new DoorSettings { locked = true });
            d.Open(); d.Tick(1f);
            Assert.IsFalse(d.IsOpen);
            d.Unlock(); d.Open(); d.Tick(1f);
            Assert.IsTrue(d.IsOpen);
        }

        [Test] public void Door_ProximityAddsTrigger()
        {
            var d = Make(new DoorSettings { activation = DoorActivation.Proximity });
            d.EnsureTrigger(); // OnEnable already ran with default settings
            var box = d.GetComponent<BoxCollider>();
            Assert.IsNotNull(box);
            Assert.IsTrue(box.isTrigger);
        }
    }
}
