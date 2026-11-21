using System.Collections.Generic;

namespace LevelBuilder.Geometry
{
    public enum OpeningIssue { None, TooSmall, OutsideWall, IntoJoint, TooTall, Overlap }

    /// <summary>An opening placed on its wall: u = metres from the start node, v = height above the floor.</summary>
    public struct OpeningRect
    {
        public WallOpening opening;
        public float u0, u1, v0, v1;
        public bool valid;
        public OpeningIssue issue;
    }

    /// <summary>Resolves where each opening sits on its wall and whether it fits.</summary>
    public static class OpeningLayout
    {
        public const float Margin = 0.05f;
        public const float MinSize = 0.3f;

        /// <summary>Rects per segment id (only segments with openings), ordered by u0.</summary>
        public static Dictionary<string, List<OpeningRect>> Compute(FloorData f)
        {
            var result = new Dictionary<string, List<OpeningRect>>();
            var cuts = WallMeshBuilder.ComputeCuts(f);
            foreach (var s in f.segments)
            {
                if (s.openings == null || s.openings.Count == 0) continue;
                bool built = cuts.TryGetValue(s.id, out var cut);
                float len = built ? BuildingData.Length(f, s) : 0f;
                float h = s.Height(f);
                var list = new List<OpeningRect>();
                var accepted = new List<OpeningRect>();
                foreach (var o in s.openings)
                {
                    var r = new OpeningRect
                    {
                        opening = o,
                        u0 = o.center - o.width * 0.5f, u1 = o.center + o.width * 0.5f,
                        v0 = o.sill, v1 = o.sill + o.height,
                    };
                    r.issue = Classify(r, o, built, len, cut, h, accepted);
                    r.valid = r.issue == OpeningIssue.None;
                    if (r.valid) accepted.Add(r);
                    list.Add(r);
                }
                list.Sort((a, b) => a.u0.CompareTo(b.u0)); // List.Sort is unstable; ties are irrelevant for drawing
                result[s.id] = list;
            }
            return result;
        }

        static OpeningIssue Classify(OpeningRect r, WallOpening o, bool built, float len, (float start, float end) cut,
            float h, List<OpeningRect> accepted)
        {
            // Written as !(x >= limit) so NaN fails every check (and ±∞ falls outside the wall).
            if (!(h > 0.01f)) return OpeningIssue.TooTall; // wall-less edge: nothing to open
            if (!(o.width >= MinSize) || !(o.height >= MinSize)) return OpeningIssue.TooSmall;
            if (!built || !(r.u0 >= 0f) || !(r.u1 <= len)) return OpeningIssue.OutsideWall;
            if (r.u0 < cut.start + Margin || r.u1 > len - cut.end - Margin) return OpeningIssue.IntoJoint;
            if (!(o.sill >= 0f) || !(r.v1 <= h - Margin)) return OpeningIssue.TooTall;
            foreach (var a in accepted)
                if (r.u0 < a.u1 + Margin && a.u0 < r.u1 + Margin) return OpeningIssue.Overlap;
            return OpeningIssue.None;
        }
    }
}
