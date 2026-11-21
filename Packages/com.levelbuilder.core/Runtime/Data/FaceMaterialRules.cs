using System;
using System.Collections.Generic;
using UnityEngine;
using LevelBuilder.Geometry;

namespace LevelBuilder
{
    /// <summary>
    /// Maps face keys ({seg}/L|R|start|end|top, node:{id}/top|side, region:{key}/floor|floor/under|floor/edge|
    /// ceiling|roof|roof/edge, {opening}/jamb|sill|threshold, stair:{id}/tread|riser|side|landing|rail) to the
    /// stored material fields. Null = the renderer's default material.
    /// </summary>
    public static class FaceMaterialRules
    {
        const string NodePrefix = "node:", RegionPrefix = "region:", StairPrefix = "stair:";
        static readonly string[] WallParts = { "L", "R", "start", "end", "top" };
        static readonly string[] RegionParts = { "floor", "floor/under", "floor/edge", "ceiling", "roof", "roof/edge" };
        static readonly string[] OpeningParts = { "jamb", "sill", "threshold" };
        static readonly string[] StairParts = { "tread", "riser", "side", "landing", "rail" };

        enum RegionField { None, Floor, Ceiling, Edge }

        static readonly (string suffix, RegionField field)[] RegionSuffixes =
        {
            ("/floor/under", RegionField.Floor), ("/floor/edge", RegionField.Edge), ("/roof/edge", RegionField.Edge),
            ("/floor", RegionField.Floor), ("/ceiling", RegionField.Ceiling), ("/roof", RegionField.Ceiling)
        };

        public static Material Get(BuildingData d, FloorData f, string faceKey) => Get(d, f, faceKey, null);

        /// <summary>As <see cref="Get(BuildingData, FloorData, string)"/>; regions = PlanarFaces.Compute(f).regions,
        /// precomputed by the caller (null: computed when a region key needs them). When several paints lie in the
        /// region (merged rooms), each field takes the last non-null value.</summary>
        public static Material Get(BuildingData d, FloorData f, string faceKey, List<Region> regions)
        {
            if (d == null || f == null || string.IsNullOrEmpty(faceKey)) return null;
            if (faceKey.StartsWith(RegionPrefix, StringComparison.Ordinal))
            {
                if (!SplitRegion(faceKey, out var key, out var field)) return null;
                var r = FindRegion(f, key, regions);
                if (r == null || f.regionPaints == null) return null;
                for (int i = f.regionPaints.Count - 1; i >= 0; i--)
                {
                    var p = f.regionPaints[i];
                    if (p == null || !Contains(r, p.seed)) continue;
                    var m = field == RegionField.Floor ? p.floor : field == RegionField.Ceiling ? p.ceiling : p.edge;
                    if (m != null) return m;
                }
                return null;
            }
            if (!SplitLast(faceKey, out var id, out var part)) return null;
            if (faceKey.StartsWith(NodePrefix, StringComparison.Ordinal))
            {
                string nodeId = id.Substring(NodePrefix.Length);
                foreach (var s in f.segments)
                    if ((s.startNodeId == nodeId || s.endNodeId == nodeId) && s.materials != null && s.materials.top != null)
                        return s.materials.top;
                return null;
            }
            if (faceKey.StartsWith(StairPrefix, StringComparison.Ordinal))
            {
                var st = d.FindStair(id.Substring(StairPrefix.Length));
                return st?.materials != null ? StairField(st.materials, part, null, false) : null;
            }
            var (_, o) = d.FindOpening(f, id);
            if (o != null) return o.materials?.jamb;
            var seg = f.GetSegment(id);
            return seg?.materials != null ? WallField(seg.materials, part, null, false) : null;
        }

        /// <summary>Stores m in the field the key maps to. For region keys the paint is anchored at regionSeed
        /// (default: an interior point of the region); an existing paint of that region is updated instead.
        /// node:X/top writes the top of every incident segment; node:X/side is ignored.</summary>
        public static void Set(BuildingData d, FloorData f, string faceKey, Material m, Vector2? regionSeed = null) =>
            Set(d, f, faceKey, m, null, regionSeed);

        /// <summary>As the convenience overload with precomputed regions (null: computed when needed).</summary>
        public static void Set(BuildingData d, FloorData f, string faceKey, Material m, List<Region> regions, Vector2? regionSeed = null)
        {
            if (d == null || f == null || string.IsNullOrEmpty(faceKey)) return;
            if (faceKey.StartsWith(RegionPrefix, StringComparison.Ordinal))
            {
                if (!SplitRegion(faceKey, out var key, out var field)) return;
                var r = FindRegion(f, key, regions);
                if (r == null) return;
                bool seedOk = regionSeed.HasValue && Contains(r, regionSeed.Value);
                var p = PaintIn(f, r);
                if (p == null)
                {
                    Vector2 seed;
                    if (seedOk) seed = regionSeed.Value;
                    else if (!PlanarFaces.InteriorPoint(r, out seed)) return;
                    p = new RegionPaint { seed = seed };
                    f.regionPaints.Add(p);
                }
                else if (seedOk) p.seed = regionSeed.Value;
                if (field == RegionField.Floor) p.floor = m;
                else if (field == RegionField.Ceiling) p.ceiling = m;
                else p.edge = m;
                return;
            }
            if (!SplitLast(faceKey, out var id, out var part)) return;
            if (faceKey.StartsWith(NodePrefix, StringComparison.Ordinal))
            {
                // A node cap shows the top material of its segments: paint the top of every incident segment.
                if (part != "top") return;
                string nodeId = id.Substring(NodePrefix.Length);
                foreach (var s in f.segments)
                {
                    if (s.startNodeId != nodeId && s.endNodeId != nodeId) continue;
                    if (s.materials == null) s.materials = new WallFaceMaterials();
                    s.materials.top = m;
                }
                return;
            }
            if (faceKey.StartsWith(StairPrefix, StringComparison.Ordinal))
            {
                var st = d.FindStair(id.Substring(StairPrefix.Length));
                if (st == null) return;
                if (st.materials == null) st.materials = new StairFaceMaterials();
                StairField(st.materials, part, m, true);
                return;
            }
            var (_, o) = d.FindOpening(f, id);
            if (o != null)
            {
                if (o.materials == null) o.materials = new OpeningFaceMaterials();
                o.materials.jamb = m;
                return;
            }
            var seg = f.GetSegment(id);
            if (seg == null) return;
            if (seg.materials == null) seg.materials = new WallFaceMaterials();
            WallField(seg.materials, part, m, true);
        }

        /// <summary>False when Set(faceKey) can never change anything (node side faces, nodes without segments).</summary>
        public static bool IsWritable(FloorData f, string faceKey)
        {
            if (f == null || string.IsNullOrEmpty(faceKey)) return false;
            if (!faceKey.StartsWith(NodePrefix, StringComparison.Ordinal)) return true;
            if (!SplitLast(faceKey, out var id, out var part) || part != "top") return false;
            string nodeId = id.Substring(NodePrefix.Length);
            return f.segments.Exists(s => s.startNodeId == nodeId || s.endNodeId == nodeId);
        }

        /// <summary>Every face key of the piece the face belongs to (wall, region slab, opening, stair).
        /// A node key returns only itself.</summary>
        public static IEnumerable<string> PieceKeys(BuildingData d, FloorData f, string faceKey)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(faceKey)) return result;
            string prefix; string[] parts;
            if (faceKey.StartsWith(RegionPrefix, StringComparison.Ordinal))
            {
                if (!SplitRegion(faceKey, out var key, out _)) return result;
                prefix = RegionPrefix + key; parts = RegionParts;
            }
            else
            {
                if (!SplitLast(faceKey, out var id, out _)) return result;
                prefix = id;
                if (faceKey.StartsWith(NodePrefix, StringComparison.Ordinal)) { result.Add(faceKey); return result; }
                if (faceKey.StartsWith(StairPrefix, StringComparison.Ordinal)) parts = StairParts;
                else if (d != null && f != null && d.FindOpening(f, id).o != null) parts = OpeningParts;
                else parts = WallParts;
            }
            foreach (var p in parts) result.Add(prefix + "/" + p);
            return result;
        }

        static bool SplitLast(string key, out string id, out string part)
        {
            int i = key.LastIndexOf('/');
            id = i > 0 ? key.Substring(0, i) : null;
            part = i > 0 ? key.Substring(i + 1) : null;
            return i > 0;
        }

        static bool SplitRegion(string faceKey, out string key, out RegionField field)
        {
            key = null; field = RegionField.None;
            string rest = faceKey.Substring(RegionPrefix.Length);
            foreach (var (suffix, fld) in RegionSuffixes)
            {
                if (!rest.EndsWith(suffix, StringComparison.Ordinal)) continue;
                key = rest.Substring(0, rest.Length - suffix.Length);
                field = fld;
                return true;
            }
            return false;
        }

        static Region FindRegion(FloorData f, string key, List<Region> regions) =>
            (regions ?? PlanarFaces.Compute(f).regions).Find(r => r.key == key);

        static bool Contains(Region r, Vector2 p) =>
            PlanarFaces.PointInPolygon(p, r.polygon) && !r.holes.Exists(h => PlanarFaces.PointInPolygon(p, h));

        /// <summary>The last paint whose seed lies in the region, or null.</summary>
        static RegionPaint PaintIn(FloorData f, Region r)
        {
            if (f.regionPaints == null) f.regionPaints = new List<RegionPaint>();
            for (int i = f.regionPaints.Count - 1; i >= 0; i--)
                if (f.regionPaints[i] != null && Contains(r, f.regionPaints[i].seed)) return f.regionPaints[i];
            return null;
        }

        static Material WallField(WallFaceMaterials w, string part, Material m, bool set)
        {
            switch (part)
            {
                case "L": if (set) w.left = m; return w.left;
                case "R": if (set) w.right = m; return w.right;
                case "start": case "end": if (set) w.caps = m; return w.caps;
                case "top": if (set) w.top = m; return w.top;
                default: return null;
            }
        }

        static Material StairField(StairFaceMaterials s, string part, Material m, bool set)
        {
            switch (part)
            {
                case "tread": if (set) s.tread = m; return s.tread;
                case "riser": if (set) s.riser = m; return s.riser;
                case "side": if (set) s.side = m; return s.side;
                case "landing": if (set) s.landing = m; return s.landing;
                case "rail": if (set) s.rail = m; return s.rail;
                default: return null;
            }
        }
    }
}
