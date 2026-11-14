using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>
    /// Point-by-point wall chain drawing on one floor. Pure logic: the caller handles Undo, snapping and repaint.
    /// Each click after the first adds a segment from the previous point; clicking the chain's first point
    /// (after at least two segments) closes the loop and ends the chain. Finish() ends without closing.
    /// RemoveLastPoint() (Backspace) reverts the last click. The floor is resolved from Data at every click;
    /// callers should Rebind() to the building's current data object before clicking, because Undo/Redo may
    /// replace the deserialized instance.
    /// </summary>
    public class WallDrawSession
    {
        /// <summary>What one click changed: segments it created and walls it split (original id, new id).</summary>
        class Step
        {
            public readonly List<string> created = new List<string>();
            public readonly List<KeyValuePair<string, string>> splits = new List<KeyValuePair<string, string>>();
            public bool ChangedData => created.Count > 0 || splits.Count > 0;
        }

        readonly int floorIndex;
        readonly List<Vector2> points = new List<Vector2>();
        readonly List<Step> steps = new List<Step>(); // steps[i] is the click that added points[i + 1]

        /// <summary>Distance under which a click counts as hitting the first point / repeating the last one.</summary>
        public float tolerance = BuildingData.MergeTolerance;

        public WallDrawSession(BuildingData data, int floorIndex)
        {
            Data = data;
            this.floorIndex = floorIndex;
        }

        public BuildingData Data { get; private set; }
        public int FloorIndex => floorIndex;
        public Vector2? LastPoint => points.Count > 0 ? points[points.Count - 1] : (Vector2?)null;
        public Vector2? FirstPoint => points.Count > 0 ? points[0] : (Vector2?)null;
        public bool IsDrawing => points.Count > 0;
        /// <summary>Points in the current chain (0 when not drawing).</summary>
        public int PointCount => points.Count;

        /// <summary>Points the session at the current data object, keeping the chain state. Recorded steps refer to
        /// the old object, so on a different object they are forgotten (Backspace then only drops points).</summary>
        public void Rebind(BuildingData data)
        {
            if (data == Data) return;
            Data = data;
            for (int i = 0; i < steps.Count; i++) steps[i] = new Step();
        }

        /// <summary>True if clicking p now would close the chain.</summary>
        public bool WouldClose(Vector2 p) =>
            points.Count >= 3 && (p - points[0]).magnitude <= tolerance;

        /// <summary>True if clicking p now would modify the data (so the caller should record Undo first).</summary>
        public bool WouldAddSegment(Vector2 p) => IsDrawing && (p - LastPoint.Value).magnitude > tolerance;

        public void Click(Vector2 p)
        {
            if (!IsDrawing)
            {
                points.Add(p);
                return;
            }
            if (!WouldAddSegment(p)) return;

            bool close = WouldClose(p);
            if (close) p = points[0];
            var f = Data.floors[floorIndex];
            var step = new Step();
            System.Action<FloorData, string, string> onSplit = (fl, original, added) =>
            {
                if (fl == f) step.splits.Add(new KeyValuePair<string, string>(original, added));
            };
            Data.SegmentSplit += onSplit;
            try
            {
                foreach (var s in Data.AddSegment(f, LastPoint.Value, p)) step.created.Add(s.id);
            }
            finally { Data.SegmentSplit -= onSplit; }
            if (close) { Finish(); return; }
            points.Add(p);
            steps.Add(step);
        }

        /// <summary>True if RemoveLastPoint() would modify the data (so the caller should record Undo first).</summary>
        public bool RemoveLastPointChangesData => steps.Count > 0 && steps[steps.Count - 1].ChangedData;

        /// <summary>
        /// Backspace: drops the chain's last point, removing the walls its click created and re-joining walls it
        /// split. With a single point the chain is cancelled. Returns true if the data changed.
        /// </summary>
        public bool RemoveLastPoint()
        {
            if (points.Count <= 1) { Finish(); return false; }
            var step = steps[steps.Count - 1];
            steps.RemoveAt(steps.Count - 1);
            points.RemoveAt(points.Count - 1);
            if (!step.ChangedData) return false;
            var f = Data.floors[floorIndex];
            foreach (var id in step.created) Data.RemoveSegment(f, id);
            for (int i = step.splits.Count - 1; i >= 0; i--)
                Data.JoinSplit(f, step.splits[i].Key, step.splits[i].Value);
            return true;
        }

        public void Finish()
        {
            points.Clear();
            steps.Clear();
        }
    }
}
