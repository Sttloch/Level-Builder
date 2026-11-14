using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>
    /// Point-by-point wall chain drawing on one floor. Pure logic: the caller handles Undo, snapping and repaint.
    /// Each click after the first adds a segment from the previous point; clicking the chain's first point
    /// (after at least two segments) closes the loop and ends the chain. Finish() ends without closing.
    /// </summary>
    public class WallDrawSession
    {
        readonly BuildingData data;
        readonly int floorIndex;
        Vector2? firstPoint;
        int pointCount;

        /// <summary>Distance under which a click counts as hitting the first point / repeating the last one.</summary>
        public float tolerance = BuildingData.MergeTolerance;

        public WallDrawSession(BuildingData data, int floorIndex)
        {
            this.data = data;
            this.floorIndex = floorIndex;
        }

        public int FloorIndex => floorIndex;
        public Vector2? LastPoint { get; private set; }
        public Vector2? FirstPoint => firstPoint;
        public bool IsDrawing => LastPoint.HasValue;

        /// <summary>True if clicking p now would close the chain.</summary>
        public bool WouldClose(Vector2 p) =>
            firstPoint.HasValue && pointCount >= 3 && (p - firstPoint.Value).magnitude <= tolerance;

        public void Click(Vector2 p)
        {
            if (!IsDrawing)
            {
                firstPoint = p;
                LastPoint = p;
                pointCount = 1;
                return;
            }
            if ((p - LastPoint.Value).magnitude <= tolerance) return;

            bool close = WouldClose(p);
            if (close) p = firstPoint.Value;
            data.AddSegment(data.floors[floorIndex], LastPoint.Value, p);
            if (close) { Finish(); return; }
            LastPoint = p;
            pointCount++;
        }

        public void Finish()
        {
            LastPoint = null;
            firstPoint = null;
            pointCount = 0;
        }
    }
}
