using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>
    /// Point-by-point drawing of a stair route from floor FromFloorIndex to the one above. Pure logic: the caller
    /// handles snapping, Undo and repaint. Nothing touches the data until Finish(), which adds the stair with
    /// BuildingData.AddStair (the caller wraps it in one Undo step). A click closer than MinPointDistance to the
    /// previous point is ignored (so the second click of a double click adds nothing).
    /// </summary>
    public class StairDrawSession
    {
        public const float MinPointDistance = BuildingData.MergeTolerance;

        readonly List<StairPoint> points = new List<StairPoint>();

        public StairDrawSession(int fromFloorIndex)
        {
            FromFloorIndex = fromFloorIndex;
        }

        public int FromFloorIndex { get; }
        public IReadOnlyList<StairPoint> Points => points;
        public bool IsDrawing => points.Count > 0;
        /// <summary>At least two points.</summary>
        public bool CanFinish => points.Count >= 2;

        /// <summary>True if p would be a new point (not within MinPointDistance of the last one).</summary>
        public bool Accepts(Vector2 p) =>
            points.Count == 0 || (p - points[points.Count - 1].position).magnitude >= MinPointDistance;

        /// <summary>Adds a Corner point, or a Smooth one (curve) when smooth is true.</summary>
        public void Click(Vector2 p, bool smooth)
        {
            if (!Accepts(p)) return;
            points.Add(new StairPoint { position = p, kind = smooth ? StairPointKind.Smooth : StairPointKind.Corner });
        }

        /// <summary>Drops the last point. False if there was none.</summary>
        public bool RemoveLastPoint()
        {
            if (points.Count == 0) return false;
            points.RemoveAt(points.Count - 1);
            return true;
        }

        public void Cancel() => points.Clear();

        /// <summary>A stair with the current route, not added to data (toFloorId empty without a floor above).</summary>
        public StairData Preview(BuildingData data) => Build(data, null);

        /// <summary>Like Preview(data), with extra (e.g. the hovered point) as a provisional last point.</summary>
        public StairData Preview(BuildingData data, StairPoint extra) => Build(data, extra);

        StairData Build(BuildingData data, StairPoint? extra)
        {
            if (data == null || FromFloorIndex < 0 || FromFloorIndex >= data.floors.Count) return null;
            var s = new StairData
            {
                id = "preview",
                fromFloorId = data.floors[FromFloorIndex].id,
                toFloorId = FromFloorIndex + 1 < data.floors.Count ? data.floors[FromFloorIndex + 1].id : ""
            };
            s.route.AddRange(points);
            if (extra.HasValue && Accepts(extra.Value.position)) s.route.Add(extra.Value);
            BuildingData.ClampStair(s);
            return s;
        }

        /// <summary>
        /// Adds the stair to data and resets the session. Null (session kept) with fewer than two points or without a
        /// floor above.
        /// </summary>
        public StairData Finish(BuildingData data)
        {
            if (!CanFinish || data == null) return null;
            var s = data.AddStair(FromFloorIndex, points);
            if (s != null) points.Clear();
            return s;
        }
    }
}
