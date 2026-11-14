using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    /// <summary>
    /// Floor and ceiling slabs, one per closed region (polygon = wall axes; the walls cover the rim).
    /// Plan (x, y) maps to world (x, height, y); horizontal UVs are world xz, side UVs are
    /// (metres along the edge, world height). Face keys (one per surface, so each can get its own material):
    /// floor slab "region:{key}/floor" (top), "/floor/under", "/floor/edge"; ceiling "region:{key}/ceiling";
    /// roof slab (top floor) "region:{key}/roof" (top), "/roof/edge".
    /// Region holes (pillars) and the optional extra holes are cut out of every horizontal face.
    /// </summary>
    public static class SlabMeshBuilder
    {
        /// <summary>Slab occupying [elevation - slab, elevation]: top, bottom and edge faces.</summary>
        public static MeshData BuildFloor(FloorData f, IList<IList<Vector2>> holes = null)
        {
            var md = new MeshData();
            foreach (var r in PlanarFaces.Compute(f).regions)
            {
                if (!Prepare(r, holes, out var outer, out var allHoles, out var verts, out var tris)) continue;
                string key = "region:" + r.key + "/floor";
                AddHorizontal(md, key, verts, tris, f.elevation, Vector3.up);
                AddHorizontal(md, key + "/under", verts, tris, f.elevation - f.slabThickness, Vector3.down);
                AddSides(md, key + "/edge", outer, allHoles, f.elevation - f.slabThickness, f.elevation);
            }
            return md;
        }

        /// <summary>
        /// Underside facing down at elevation + height (minus the slab thickness unless this is the top
        /// floor: otherwise the next floor's slab occupies that space). The top floor also gets a roof
        /// slab [top, top + slab] (top and edge faces; its underside is the ceiling face itself).
        /// </summary>
        public static MeshData BuildCeiling(FloorData f, bool isTopFloor, IList<IList<Vector2>> holes = null)
        {
            var md = new MeshData();
            float top = f.elevation + f.height;
            foreach (var r in PlanarFaces.Compute(f).regions)
            {
                if (!Prepare(r, holes, out var outer, out var allHoles, out var verts, out var tris)) continue;
                string key = "region:" + r.key;
                AddHorizontal(md, key + "/ceiling", verts, tris, isTopFloor ? top : top - f.slabThickness, Vector3.down);
                if (isTopFloor)
                {
                    AddHorizontal(md, key + "/roof", verts, tris, top + f.slabThickness, Vector3.up);
                    AddSides(md, key + "/roof/edge", outer, allHoles, top, top + f.slabThickness);
                }
            }
            return md;
        }

        static bool Prepare(Region r, IList<IList<Vector2>> extraHoles, out List<Vector2> outer,
            out List<IList<Vector2>> holes, out List<Vector2> verts, out List<int> tris)
        {
            outer = null; holes = null; verts = null; tris = null;
            if (r.polygon == null || r.polygon.Count < 3) return false;
            outer = new List<Vector2>(r.polygon);
            if (Triangulator.SignedArea(outer) < 0f) outer.Reverse();

            // Holes wound CW, so the right-hand normal of every contour faces away from the slab.
            holes = new List<IList<Vector2>>();
            foreach (var h in r.holes) holes.Add(Clockwise(h));
            if (extraHoles != null) foreach (var h in extraHoles) holes.Add(Clockwise(h));

            tris = Triangulator.Triangulate(outer, holes, out verts);
            return tris.Count > 0;
        }

        static List<Vector2> Clockwise(IList<Vector2> h)
        {
            var l = new List<Vector2>(h);
            if (Triangulator.SignedArea(l) > 0f) l.Reverse();
            return l;
        }

        static void AddHorizontal(MeshData md, string key, List<Vector2> verts, List<int> tris, float y, Vector3 normal)
        {
            var pts = new List<Vector3>(verts.Count);
            var uvs = new List<Vector2>(verts.Count);
            foreach (var v in verts) { pts.Add(new Vector3(v.x, y, v.y)); uvs.Add(v); }
            md.AddFace(key, pts, uvs, tris, normal);
        }

        static void AddSides(MeshData md, string key, IList<Vector2> outer, IList<IList<Vector2>> holes, float y0, float y1)
        {
            AddContourSides(md, key, outer, y0, y1);
            foreach (var h in holes) AddContourSides(md, key, h, y0, y1);
        }

        static void AddContourSides(MeshData md, string key, IList<Vector2> contour, float y0, float y1)
        {
            var tri = new[] { 0, 1, 2, 0, 2, 3 };
            for (int i = 0; i < contour.Count; i++)
            {
                var a = contour[i]; var b = contour[(i + 1) % contour.Count];
                var e = b - a;
                float len = e.magnitude;
                if (len < 1e-6f) continue;
                var n = new Vector3(e.y, 0f, -e.x) / len; // right-hand side of the walk direction
                var pts = new[]
                {
                    new Vector3(a.x, y0, a.y), new Vector3(b.x, y0, b.y),
                    new Vector3(b.x, y1, b.y), new Vector3(a.x, y1, a.y),
                };
                var uvs = new[] { new Vector2(0, y0), new Vector2(len, y0), new Vector2(len, y1), new Vector2(0, y1) };
                md.AddFace(key, pts, uvs, tri, n);
            }
        }
    }
}
