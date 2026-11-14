using LevelBuilder.Geometry;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Plan-space picking, fitting and resizing of wall openings for the opening tool. No Scene view state.</summary>
    public static class OpeningPlacement
    {
        /// <summary>Default pick distance from a wall centreline, in metres.</summary>
        public const float PickDistance = 0.5f;
        /// <summary>Extra distance beyond half the wall thickness that still counts as "inside" an opening.</summary>
        public const float GapTolerance = 0.2f;
        public const float CenterStep = 0.1f;

        /// <summary>Nearest segment within maxDistance of the plan point; center = projection in metres from its start node.</summary>
        public static bool Pick(FloorData f, Vector2 plan, float maxDistance, out string segmentId, out float center)
        {
            segmentId = null;
            center = 0f;
            float best = maxDistance;
            foreach (var s in f.segments)
            {
                if (!Frame(f, s, out var a, out var dir, out float len)) continue;
                float d = Seg2D.DistanceToSegment(plan, a, a + dir * len, out float t);
                if (d > best) continue;
                best = d;
                segmentId = s.id;
                center = t * len;
            }
            return segmentId != null;
        }

        public static float SnapCenter(float center, float step = CenterStep) =>
            step > 0f ? Mathf.Round(center / step) * step : center;

        /// <summary>
        /// Whether the probe would be valid on the segment, against the floor's other openings. An existing opening
        /// with the probe's id (the one being moved) is ignored. The data is restored before returning.
        /// </summary>
        public static bool Fits(FloorData f, string segmentId, WallOpening probe)
        {
            var target = f.GetSegment(segmentId);
            if (target == null || probe == null) return false;

            WallSegment ownerSeg = null;
            WallOpening own = null;
            int ownIndex = -1;
            if (!string.IsNullOrEmpty(probe.id))
                foreach (var s in f.segments)
                {
                    int i = s.openings.FindIndex(o => o.id == probe.id);
                    if (i < 0) continue;
                    ownerSeg = s;
                    own = s.openings[i];
                    ownIndex = i;
                    break;
                }

            if (ownerSeg != null) ownerSeg.openings.RemoveAt(ownIndex);
            target.openings.Add(probe);
            try
            {
                var layout = OpeningLayout.Compute(f);
                if (!layout.TryGetValue(segmentId, out var rects)) return false;
                foreach (var r in rects)
                    if (r.opening == probe) return r.valid;
                return false;
            }
            finally
            {
                target.openings.Remove(probe);
                if (ownerSeg != null) ownerSeg.openings.Insert(ownIndex, own);
            }
        }

        /// <summary>
        /// Id of the opening whose gap contains the plan point: along the wall within [u0, u1], across within half the
        /// wall thickness + GapTolerance (capped at maxDistance). Nearest to the centreline wins; null if none.
        /// </summary>
        public static string PickOpening(FloorData f, Vector2 plan, float maxDistance)
        {
            string best = null;
            float bestD = float.MaxValue;
            foreach (var s in f.segments)
            {
                if (s.openings.Count == 0 || !Frame(f, s, out var a, out var dir, out _)) continue;
                var rel = plan - a;
                float u = Vector2.Dot(rel, dir);
                float across = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
                float tol = Mathf.Min(maxDistance, s.thickness * 0.5f + GapTolerance);
                if (across > tol) continue;
                foreach (var o in s.openings)
                {
                    float half = o.width * 0.5f;
                    if (u < o.center - half || u > o.center + half || across >= bestD) continue;
                    bestD = across;
                    best = o.id;
                }
            }
            return best;
        }

        /// <summary>Start position, unit direction and length of the segment in plan; false if degenerate.</summary>
        public static bool Frame(FloorData f, WallSegment s, out Vector2 a, out Vector2 dir, out float len)
        {
            a = default;
            dir = default;
            len = 0f;
            var na = f.GetNode(s.startNodeId);
            var nb = f.GetNode(s.endNodeId);
            if (na == null || nb == null) return false;
            a = na.position;
            var d = nb.position - a;
            len = d.magnitude;
            if (len < 1e-6f) return false;
            dir = d / len;
            return true;
        }

        /// <summary>
        /// Building-local ray against the vertical centreline planes of the floor's walls (within length and height):
        /// plan point of the nearest hit. Falls back to the floor plane (y = elevation).
        /// </summary>
        public static bool RayToPlan(FloorData f, Vector3 origin, Vector3 direction, out Vector2 plan)
        {
            plan = default;
            float bestT = float.MaxValue;
            var o2 = new Vector2(origin.x, origin.z);
            var d2 = new Vector2(direction.x, direction.z);
            foreach (var s in f.segments)
            {
                if (!Frame(f, s, out var a, out var dir, out float len)) continue;
                var n = new Vector2(-dir.y, dir.x);
                float denom = Vector2.Dot(d2, n);
                if (Mathf.Abs(denom) < 1e-6f) continue;
                float t = Vector2.Dot(a - o2, n) / denom;
                if (t <= 0f || t >= bestT) continue;
                var hit = o2 + d2 * t;
                float u = Vector2.Dot(hit - a, dir);
                float y = origin.y + direction.y * t - f.elevation;
                if (u < 0f || u > len || y < 0f || y > s.Height(f)) continue;
                bestT = t;
                plan = hit;
            }
            if (bestT < float.MaxValue) return true;
            if (Mathf.Abs(direction.y) < 1e-6f) return false;
            float k = (f.elevation - origin.y) / direction.y;
            if (k < 0f) return false;
            var p = origin + direction * k;
            plan = new Vector2(p.x, p.z);
            return true;
        }

        /// <summary>Symmetric width from an edge dragged to u (centre fixed), at least MinSize.</summary>
        public static float WidthFromEdge(float center, float u) =>
            Mathf.Max(OpeningLayout.MinSize, 2f * Mathf.Abs(u - center));

        /// <summary>Height from the top edge dragged to v (sill fixed), at least MinSize.</summary>
        public static float HeightFromTop(float sill, float v) =>
            Mathf.Max(OpeningLayout.MinSize, v - sill);

        /// <summary>Sill from the bottom edge dragged to v, keeping the top fixed; 0 ≤ sill ≤ top − MinSize.</summary>
        public static void SillFromBottom(float sill, float height, float v, out float newSill, out float newHeight)
        {
            float top = sill + height;
            newSill = Mathf.Clamp(v, 0f, Mathf.Max(0f, top - OpeningLayout.MinSize));
            newHeight = top - newSill;
        }

        public static string TypeName(OpeningType t) =>
            t == OpeningType.Door ? "puerta" : t == OpeningType.Window ? "ventana" : "hueco";

        public static string IssueText(OpeningIssue issue)
        {
            switch (issue)
            {
                case OpeningIssue.TooSmall: return "Demasiado pequeña (mínimo 0.3 m)";
                case OpeningIssue.OutsideWall: return "Fuera de la pared";
                case OpeningIssue.IntoJoint: return "Invade la unión con otra pared";
                case OpeningIssue.TooTall: return "Demasiado alta para la pared";
                case OpeningIssue.Overlap: return "Se solapa con otra abertura";
                default: return "";
            }
        }

        /// <summary>Adds the opening to the segment as one Undo step ("Colocar puerta/ventana/hueco").</summary>
        public static WallOpening Place(Building b, int floorIndex, string segmentId, WallOpening o)
        {
            var f = b.data.floors[floorIndex];
            if (f.GetSegment(segmentId) == null || o == null) return null;
            BuildingEditorUtil.Mutate(b, "Colocar " + TypeName(o.type), floorIndex,
                () => b.data.AddOpening(f, segmentId, o));
            return o;
        }

        /// <summary>Removes the opening as one Undo step.</summary>
        public static bool Remove(Building b, int floorIndex, string openingId)
        {
            var f = b.data.floors[floorIndex];
            if (b.data.FindOpening(f, openingId).o == null) return false;
            BuildingEditorUtil.Mutate(b, "Borrar abertura", floorIndex, () => b.data.RemoveOpening(f, openingId));
            return true;
        }
    }
}
