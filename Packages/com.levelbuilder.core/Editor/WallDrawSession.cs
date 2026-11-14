using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>
    /// Point-by-point wall chain drawing on one floor. Pure logic: the caller handles Undo, snapping and repaint.
    /// Each click after the first adds a segment from the previous point; clicking the chain's first point
    /// (after at least two segments) closes the loop and ends the chain. Finish() ends without closing.
    /// The floor is resolved from Data at every click; callers should Rebind() to the building's current data
    /// object before clicking, because Undo/Redo may replace the deserialized instance.
    /// </summary>
    public class WallDrawSession
    {
        readonly int floorIndex;
        Vector2? firstPoint;

        /// <summary>Distance under which a click counts as hitting the first point / repeating the last one.</summary>
        public float tolerance = BuildingData.MergeTolerance;

        public WallDrawSession(BuildingData data, int floorIndex)
        {
            Data = data;
            this.floorIndex = floorIndex;
        }

        public BuildingData Data { get; private set; }
        public int FloorIndex => floorIndex;
        public Vector2? LastPoint { get; private set; }
        public Vector2? FirstPoint => firstPoint;
        public bool IsDrawing => LastPoint.HasValue;
        /// <summary>Points in the current chain (0 when not drawing).</summary>
        public int PointCount { get; private set; }

        /// <summary>Points the session at the current data object, keeping the chain state.</summary>
        public void Rebind(BuildingData data) => Data = data;

        /// <summary>True if clicking p now would close the chain.</summary>
        public bool WouldClose(Vector2 p) =>
            firstPoint.HasValue && PointCount >= 3 && (p - firstPoint.Value).magnitude <= tolerance;

        /// <summary>True if clicking p now would modify the data (so the caller should record Undo first).</summary>
        public bool WouldAddSegment(Vector2 p) => IsDrawing && (p - LastPoint.Value).magnitude > tolerance;

        public void Click(Vector2 p)
        {
            if (!IsDrawing)
            {
                firstPoint = p;
                LastPoint = p;
                PointCount = 1;
                return;
            }
            if (!WouldAddSegment(p)) return;

            bool close = WouldClose(p);
            if (close) p = firstPoint.Value;
            Data.AddSegment(Data.floors[floorIndex], LastPoint.Value, p);
            if (close) { Finish(); return; }
            LastPoint = p;
            PointCount++;
        }

        public void Finish()
        {
            LastPoint = null;
            firstPoint = null;
            PointCount = 0;
        }
    }
}
