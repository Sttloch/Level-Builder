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

        // Hinge edge in leaf-local x: hingeOnRight = right of a viewer on the door's +Z (R) side facing the wall,
        // i.e. door-local -X; hinge on the left = +X.
        static float HingeX(bool hingeRight) => hingeRight ? -0.45f : 0.45f;

        float FreeEdgeZ(Door d, bool hingeRight)
        {
            var p = d.leaf.TransformPoint(new Vector3(-HingeX(hingeRight), 0, 0));
            return d.transform.InverseTransformPoint(p).z;
        }

        [Test] public void Door_Swing_RotatesLeafAroundHinge()
        {
            var d = Make(new DoorSettings());
            var hingeBefore = d.leaf.TransformPoint(new Vector3(HingeX(false), 0, 0));
            d.Open();
            d.Tick(1f);
            Assert.AreEqual(95f, Quaternion.Angle(Quaternion.identity, d.leaf.localRotation), 1e-2f);
            var hingeAfter = d.leaf.TransformPoint(new Vector3(HingeX(false), 0, 0));
            Assert.AreEqual(0f, Vector3.Distance(hingeBefore, hingeAfter), 1e-4f);
            Assert.Less(FreeEdgeZ(d, false), -0.1f); // Open() with no position swings toward -Z (away from the R side)
        }

        // A viewer on the +Z side facing the wall looks along -Z: their right is -X (Unity is left-handed).
        [Test] public void Door_HingeOnRight_IsRightOfViewerOnRSide()
        {
            foreach (bool right in new[] { true, false })
            {
                var d = Make(new DoorSettings { hingeOnRight = right, bothWays = false });
                var minus = d.leaf.TransformPoint(new Vector3(-0.45f, 0, 0));
                var plus = d.leaf.TransformPoint(new Vector3(0.45f, 0, 0));
                d.Open();
                d.Tick(1f);
                float movedMinus = Vector3.Distance(minus, d.leaf.TransformPoint(new Vector3(-0.45f, 0, 0)));
                float movedPlus = Vector3.Distance(plus, d.leaf.TransformPoint(new Vector3(0.45f, 0, 0)));
                if (right) { Assert.AreEqual(0f, movedMinus, 1e-4f); Assert.Greater(movedPlus, 0.5f); }
                else { Assert.AreEqual(0f, movedPlus, 1e-4f); Assert.Greater(movedMinus, 0.5f); }
                Assert.Less(FreeEdgeZ(d, right), -0.1f, "one-way doors swing toward -Z");
                Object.DestroyImmediate(_go);
            }
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
            Object.DestroyImmediate(_go);

            d = Make(new DoorSettings { bothWays = true, hingeOnRight = true });
            d.Open(d.transform.TransformPoint(new Vector3(0, 0, -2)));
            d.Tick(1f);
            Assert.Greater(FreeEdgeZ(d, true), 0.1f);
        }

        [Test] public void Door_Sliding_MovesLeaf()
        {
            var d = Make(new DoorSettings { kind = DoorKind.Sliding });
            float x0 = d.leaf.localPosition.x;
            d.Open();
            d.Tick(1f);
            Assert.AreEqual(0.855f, Mathf.Abs(d.leaf.localPosition.x - x0), 1e-4f);
            // Slides from the hinge side toward the free edge: hinge on the left (+X) -> moves toward -X.
            Assert.Less(d.leaf.localPosition.x - x0, 0f);
            Object.DestroyImmediate(_go);

            d = Make(new DoorSettings { kind = DoorKind.Sliding, hingeOnRight = true });
            x0 = d.leaf.localPosition.x;
            d.Open();
            d.Tick(1f);
            Assert.AreEqual(0.855f, d.leaf.localPosition.x - x0, 1e-4f);
        }

        [Test] public void Door_NegativeSpeed_ProgressStaysInRange()
        {
            var m = new DoorMotion();
            m.Open(1);
            m.Step(0.5f, -2f, out _, out _);
            Assert.That(m.Progress, Is.InRange(0f, 1f));

            var d = Make(new DoorSettings { speed = -2f });
            d.Open();
            d.Tick(0.5f);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, d.leaf.localRotation), 1e-3f,
                "leaf must not rotate past closed");
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
            Assert.AreEqual(new Vector3(0.9f, 2.1f, 3f), box.size);
            Assert.AreEqual(new Vector3(0f, 1.05f, 0f), box.center);
        }

        readonly System.Collections.Generic.List<GameObject> _extras = new System.Collections.Generic.List<GameObject>();

        [TearDown] public void TearDownExtras()
        {
            foreach (var g in _extras) if (g != null) Object.DestroyImmediate(g);
            _extras.Clear();
        }

        Collider Occupant(string tag = "Player")
        {
            var g = new GameObject("occ");
            g.tag = tag;
            _extras.Add(g);
            return g.AddComponent<BoxCollider>();
        }

        Door Prox(string playerTag = "Player")
        {
            var d = Make(new DoorSettings { activation = DoorActivation.Proximity, playerTag = playerTag });
            d.EnsureTrigger();
            return d;
        }

        [Test] public void Door_Proximity_TagMatching()
        {
            var d = Prox();
            d.HandleEnter(Occupant("Untagged"));
            Assert.IsFalse(d.IsOpen);
            d.HandleEnter(Occupant("Player"));
            Assert.IsTrue(d.IsOpen);

            var any = Prox("");
            any.HandleEnter(Occupant("Untagged"));
            Assert.IsTrue(any.IsOpen);
        }

        [Test] public void Door_Proximity_ClosesOnlyAfterLastExit()
        {
            var d = Prox();
            var a = Occupant(); var b = Occupant();
            d.HandleEnter(a); d.HandleEnter(b);
            Assert.IsTrue(d.IsOpen);
            d.HandleExit(a);
            Assert.IsTrue(d.IsOpen);
            d.HandleExit(b);
            Assert.IsFalse(d.IsOpen);
        }

        [Test] public void Door_Proximity_DestroyedOrDisabledOccupantCloses()
        {
            var d = Prox();
            var a = Occupant();
            d.HandleEnter(a); d.Poll();
            Assert.IsTrue(d.IsOpen);
            Object.DestroyImmediate(a.gameObject);
            d.Poll();
            Assert.IsFalse(d.IsOpen);

            var b = Occupant();
            d.HandleEnter(b); d.Poll();
            Assert.IsTrue(d.IsOpen);
            b.gameObject.SetActive(false);
            d.Poll();
            Assert.IsFalse(d.IsOpen);

            var c = Occupant();
            d.HandleEnter(c); d.Poll();
            c.enabled = false;
            d.Poll();
            Assert.IsFalse(d.IsOpen);
        }

        [Test] public void Door_Interact_TogglesOnlyWithOccupant()
        {
            var d = Make(new DoorSettings { activation = DoorActivation.Interact });
            d.EnsureTrigger();
            d.interactSource = _ => true;
            d.Poll();
            Assert.IsFalse(d.IsOpen);               // nobody inside
            var a = Occupant();
            d.HandleEnter(a);
            Assert.IsFalse(d.IsOpen);               // Interact does not open on enter
            d.Poll();
            Assert.IsTrue(d.IsOpen);
            d.Poll();
            Assert.IsFalse(d.IsOpen);
            a.enabled = false;                      // disabled occupant does not count
            d.Poll();
            Assert.IsFalse(d.IsOpen);
        }

        // Leaf colliders (kinematic bodies) sit inside door triggers: neither the door's own leaf nor another
        // door's leaf is an occupant, even when playerTag is empty ("any collider").
        [Test] public void Door_IgnoresOwnAndOtherDoorLeaves()
        {
            var d = Prox("");
            var ownLeaf = d.leaf.gameObject.AddComponent<BoxCollider>();
            d.HandleEnter(ownLeaf);
            d.HandleEnter(d.GetComponent<BoxCollider>()); // its own trigger
            Assert.IsFalse(d.IsOpen);

            var other = new GameObject("otherDoor");
            _extras.Add(other);
            var otherLeaf = new GameObject("Leaf");
            otherLeaf.transform.SetParent(other.transform, false);
            other.AddComponent<Door>().leaf = otherLeaf.transform;
            d.HandleEnter(otherLeaf.AddComponent<BoxCollider>());
            Assert.IsFalse(d.IsOpen);
            d.Poll();
            Assert.IsFalse(d.IsOpen);

            d.HandleEnter(Occupant("Untagged"));
            Assert.IsTrue(d.IsOpen);
        }

        [Test] public void Door_Interact_OpensAwayFromOccupant()
        {
            foreach (float z in new[] { 2f, -2f })
            {
                var d = Make(new DoorSettings { activation = DoorActivation.Interact, bothWays = true });
                d.EnsureTrigger();
                d.interactSource = _ => true;
                var a = Occupant();
                a.transform.position = d.transform.TransformPoint(new Vector3(0, 0, z));
                d.HandleEnter(a);
                d.Poll();
                Assert.IsTrue(d.IsOpen);
                d.Tick(1f);
                if (z > 0f) Assert.Less(FreeEdgeZ(d, false), -0.1f, "occupant in front -> opens to the back");
                else Assert.Greater(FreeEdgeZ(d, false), 0.1f, "occupant behind -> opens to the front");
                d.Poll();
                Assert.IsFalse(d.IsOpen, "second press closes");
                Object.DestroyImmediate(_go);
            }
        }

        [Test] public void Door_Proximity_AcceptsTagOnAttachedRigidbody()
        {
            var d = Prox();
            var player = new GameObject("player");
            player.tag = "Player";
            _extras.Add(player);
            player.AddComponent<Rigidbody>().isKinematic = true;
            var part = new GameObject("part"); // untagged child collider of the tagged body
            part.transform.SetParent(player.transform, false);
            var col = part.AddComponent<BoxCollider>();
            Assume.That(col.attachedRigidbody, Is.Not.Null);
            d.HandleEnter(col);
            Assert.IsTrue(d.IsOpen);
        }

        [Test] public void Door_LockedProximity_UnlockWithOccupantInsideOpens()
        {
            var d = Make(new DoorSettings { activation = DoorActivation.Proximity, locked = true });
            d.EnsureTrigger();
            var a = Occupant();
            a.transform.position = d.transform.TransformPoint(new Vector3(0, 0, -2));
            d.HandleEnter(a);
            d.Poll();
            Assert.IsFalse(d.IsOpen);
            d.Unlock();
            Assert.IsTrue(d.IsOpen);
            d.Tick(1f);
            Assert.Greater(FreeEdgeZ(d, false), 0.1f, "opens away from the occupant");
        }

        [Test] public void Door_HandleEnter_CountsAsHadOccupant()
        {
            var d = Prox();
            var a = Occupant();
            d.HandleEnter(a);
            Assert.IsTrue(d.IsOpen);
            Object.DestroyImmediate(a.gameObject); // gone before any Poll ran
            d.Poll();
            Assert.IsFalse(d.IsOpen);
        }
    }
}
