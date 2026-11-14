using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace LevelBuilder
{
    /// <summary>
    /// Runtime door. The transform sits at the base-centre of the opening, forward = wall normal.
    /// <see cref="leaf"/> is a child whose pose at first use is the closed pose.
    /// <para>Hinge: <see cref="DoorSettings.hingeOnRight"/> is the right of a viewer standing on the door's +Z side
    /// (the wall's R face) looking at the wall, i.e. door-local −X; otherwise the hinge is at +X.
    /// <see cref="Open()"/> and one-way doors swing toward −Z (away from that viewer); <see cref="Open(Vector3)"/>
    /// on a two-way door swings away from the given position. Sliding leaves move from the hinge side toward the
    /// free edge.</para>
    /// <para>Interact activation reads <see cref="DoorSettings.key"/> through <see cref="DoorInput"/>: with only the
    /// new Input System, keys outside A–Z, 0–9, Space and Return are not mapped, and the gamepad West button
    /// always interacts.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class Door : MonoBehaviour
    {
        public DoorSettings settings = new DoorSettings();
        public Transform leaf;
        public Vector3 leafSize = new Vector3(0.9f, 2.1f, 0.05f);
        public UnityEvent onOpened = new UnityEvent();
        public UnityEvent onClosed = new UnityEvent();

        DoorMotion _motion;
        Transform _capturedLeaf;
        Vector3 _closedPos;
        Quaternion _closedRot = Quaternion.identity;
        readonly HashSet<Collider> _inside = new HashSet<Collider>();

        public bool IsOpen => Motion.IsOpen;
        public bool Locked { get => Motion.Locked; set => Motion.Locked = value; }

        DoorMotion Motion
        {
            get
            {
                if (_motion == null) _motion = new DoorMotion { Locked = settings != null && settings.locked };
                return _motion;
            }
        }

        void Awake() { EnsureTrigger(); }
        void OnEnable() { EnsureTrigger(); }
        void OnDisable() { _inside.Clear(); }

        bool HingeOnRight => settings != null && settings.hingeOnRight;

        // Door-local x sign of the hinge edge: right of a viewer on the +Z side facing -Z is -X (left-handed frame).
        float HingeSign => HingeOnRight ? -1f : 1f;

        // Swing direction whose free edge ends on the given local z side (+1 / -1). Rotating by +angle about +Y
        // moves the free edge (at -HingeSign from the hinge) toward z = HingeSign.
        int DirectionToward(float zSide) => zSide * HingeSign >= 0f ? 1 : -1;

        /// <summary>Opens toward −Z (or keeps the current swing while not fully closed).</summary>
        public void Open() { Motion.Open(Motion.Progress > 0f ? Motion.Direction : DirectionToward(-1f)); }

        /// <summary>Two-way doors swing away from <paramref name="from"/>; one-way doors behave like <see cref="Open()"/>.</summary>
        public void Open(Vector3 from)
        {
            if (settings == null || !settings.bothWays) { Open(); return; }
            float side = transform.InverseTransformPoint(from).z >= 0f ? 1f : -1f;
            Motion.Open(DirectionToward(-side));
        }

        public void Close() { Motion.Close(); }
        public void Toggle() { if (Motion.IsOpen) Close(); else Open(); }

        /// <summary>Unlocks; a Proximity door with someone already inside opens away from them.</summary>
        public void Unlock()
        {
            Motion.Locked = false;
            if (settings != null && settings.activation == DoorActivation.Proximity && TryOccupant(out var occ))
                Open(occ.transform.position);
        }

        void Update()
        {
            Tick(Time.deltaTime);
            Poll();
        }

        internal System.Func<KeyCode, bool> interactSource = DoorInput.InteractPressed;
        bool _hadOccupant;

        /// <summary>Per-frame occupancy pass: prunes destroyed/disabled occupants, closes an emptied Proximity door, handles Interact.</summary>
        internal void Poll()
        {
            bool has = HasOccupant();
            if (settings != null)
            {
                if (settings.activation == DoorActivation.Proximity)
                {
                    if (_hadOccupant && !has) Close();
                }
                else if (settings.activation == DoorActivation.Interact && has && interactSource(settings.key))
                {
                    if (Motion.IsOpen) Close();
                    else if (TryOccupant(out var occ)) Open(occ.transform.position);
                }
            }
            _hadOccupant = has;
        }

        public void Tick(float dt)
        {
            var m = Motion;
            float speed = settings != null ? Mathf.Max(0f, settings.speed) : 2f;
            m.Step(dt, speed, out bool opened, out bool closed);
            ApplyPose();
            if (opened) onOpened?.Invoke();
            if (closed) onClosed?.Invoke();
        }

        void CaptureClosedPose()
        {
            if (leaf == _capturedLeaf) return;
            _capturedLeaf = leaf;
            if (leaf != null) { _closedPos = leaf.localPosition; _closedRot = leaf.localRotation; }
        }

        void ApplyPose()
        {
            CaptureClosedPose();
            if (leaf == null) return;
            var m = Motion;
            float hingeSign = HingeSign;
            if (settings != null && settings.kind == DoorKind.Sliding)
            {
                float dist = -hingeSign * leafSize.x * 0.95f * m.Progress;
                leaf.localPosition = _closedPos + new Vector3(dist, 0f, 0f);
                leaf.localRotation = _closedRot;
            }
            else
            {
                float openAngle = settings != null ? settings.openAngle : 95f;
                var rot = Quaternion.AngleAxis(m.Direction * openAngle * m.Progress, Vector3.up);
                var hinge = _closedPos + new Vector3(hingeSign * leafSize.x * 0.5f, 0f, 0f);
                leaf.localPosition = hinge + rot * (_closedPos - hinge);
                leaf.localRotation = rot * _closedRot;
            }
        }

        /// <summary>Adds/updates the trigger collider from settings and leafSize (called on Awake/OnEnable).</summary>
        public void EnsureTrigger()
        {
            if (settings == null || settings.activation == DoorActivation.Script) return;
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(leafSize.x, leafSize.y, 3f);
            box.center = new Vector3(0f, leafSize.y * 0.5f, 0f);
        }

        bool Matches(Collider other)
        {
            if (other == null) return false;
            // Leaf colliders (kinematic bodies) sit inside door triggers: this door's own parts and any other
            // door's leaf are never occupants.
            if (other.transform.IsChildOf(transform) || other.GetComponentInParent<Door>() != null) return false;
            string tag = settings != null ? settings.playerTag : null;
            if (string.IsNullOrEmpty(tag) || other.gameObject.tag == tag) return true;
            // Compound players: the tag is often on the Rigidbody's object, not on the child collider.
            var body = other.attachedRigidbody;
            return body != null && body.gameObject.tag == tag;
        }

        bool HasOccupant()
        {
            _inside.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
            return _inside.Count > 0;
        }

        bool TryOccupant(out Collider occupant)
        {
            occupant = null;
            if (!HasOccupant()) return false;
            foreach (var c in _inside) { occupant = c; return true; }
            return false;
        }

        void OnTriggerEnter(Collider other) { HandleEnter(other); }
        void OnTriggerExit(Collider other) { HandleExit(other); }

        internal void HandleEnter(Collider other)
        {
            if (!Matches(other)) return;
            _inside.Add(other);
            _hadOccupant = true;
            if (settings != null && settings.activation == DoorActivation.Proximity)
                Open(other.transform.position);
        }

        internal void HandleExit(Collider other)
        {
            if (!_inside.Remove(other)) return;
            if (settings != null && settings.activation == DoorActivation.Proximity && !HasOccupant())
                Close();
            _hadOccupant = HasOccupant();
        }
    }
}
