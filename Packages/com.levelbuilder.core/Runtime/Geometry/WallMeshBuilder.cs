using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    /// <summary>
    /// Extrudes a floor's wall graph into a mesh. Plan (x, y) maps to world (x, height, y).
    /// Face keys: "{segmentId}/L", "/R" (sides looking start→end), "/start", "/end" (caps), "/top".
    /// </summary>
    public static class WallMeshBuilder
    {
        const float MinLength = 1e-5f;
        const float SameEps = 1e-5f;

        public static Vector3 ToWorld(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        public static MeshData Build(FloorData f, float baseY)
        {
            var md = new MeshData();
            var joins = WallJoins.Compute(f);
            var degree = new Dictionary<string, int>();
            foreach (var s in f.segments)
            {
                degree[s.startNodeId] = (degree.TryGetValue(s.startNodeId, out var a) ? a : 0) + 1;
                degree[s.endNodeId] = (degree.TryGetValue(s.endNodeId, out var b) ? b : 0) + 1;
            }

            foreach (var s in f.segments)
            {
                var na = f.GetNode(s.startNodeId);
                var nb = f.GetNode(s.endNodeId);
                if (na == null || nb == null) continue;
                var A = na.position;
                var d = nb.position - A;
                float len = d.magnitude;
                float h = s.Height(f);
                if (len < MinLength || h <= 0f) continue;
                if (!joins.TryGetValue((s.id, true), out var js) || !joins.TryGetValue((s.id, false), out var je)) continue;
                d /= len;
                var n = WallJoins.LeftPerp(d);
                float half = s.thickness * 0.5f;

                AddSide(md, s.id + "/L", A, d, n * half, Vector2.Dot(js.left - A, d), Vector2.Dot(je.left - A, d), baseY, h, n);
                AddSide(md, s.id + "/R", A, d, -n * half, Vector2.Dot(js.right - A, d), Vector2.Dot(je.right - A, d), baseY, h, -n);

                if (IsOpenEnd(degree, s.startNodeId, A, n, half, js))
                    AddCap(md, s.id + "/start", js.right, js.left, baseY, h, -d);
                if (IsOpenEnd(degree, s.endNodeId, nb.position, n, half, je))
                    AddCap(md, s.id + "/end", je.left, je.right, baseY, h, d);

                AddTop(md, s.id + "/top", new List<Vector2> { js.right, je.right, je.left, js.left }, baseY + h);
            }
            return md;
        }

        /// <summary>A cap is needed at a free end, or where the miter limit fell back to square points.</summary>
        static bool IsOpenEnd(Dictionary<string, int> degree, string nodeId, Vector2 node, Vector2 n, float half,
            (Vector2 left, Vector2 right) j)
        {
            if (degree.TryGetValue(nodeId, out var dg) && dg <= 1) return true;
            return (j.left - (node + n * half)).magnitude < SameEps && (j.right - (node - n * half)).magnitude < SameEps;
        }

        /// <summary>Side face as a 2D polygon in wall coords (u along the wall from the start node, v up), triangulated.</summary>
        static void AddSide(MeshData md, string key, Vector2 A, Vector2 d, Vector2 offset, float u0, float u1,
            float baseY, float h, Vector2 normal2D)
        {
            var poly = new List<Vector2> { new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, h), new Vector2(u0, h) };
            var tris = Triangulator.Triangulate(poly, null, out var verts);
            var pts = new List<Vector3>(verts.Count);
            foreach (var uv in verts) pts.Add(ToWorld(A + d * uv.x + offset, baseY + uv.y));
            md.AddFace(key, pts, verts, tris, ToWorld(normal2D, 0f));
        }

        static void AddCap(MeshData md, string key, Vector2 p0, Vector2 p1, float baseY, float h, Vector2 normal2D)
        {
            float w = (p1 - p0).magnitude;
            var pts = new List<Vector3>
            {
                ToWorld(p0, baseY), ToWorld(p1, baseY), ToWorld(p1, baseY + h), ToWorld(p0, baseY + h)
            };
            var uvs = new List<Vector2> { new Vector2(0f, 0f), new Vector2(w, 0f), new Vector2(w, h), new Vector2(0f, h) };
            md.AddFace(key, pts, uvs, new[] { 0, 1, 2, 0, 2, 3 }, ToWorld(normal2D, 0f));
        }

        /// <summary>Top face triangulated in plan; UVs are world xz in metres.</summary>
        static void AddTop(MeshData md, string key, List<Vector2> poly, float y)
        {
            var tris = Triangulator.Triangulate(poly, null, out var verts);
            var pts = new List<Vector3>(verts.Count);
            foreach (var p in verts) pts.Add(ToWorld(p, y));
            md.AddFace(key, pts, verts, tris, Vector3.up);
        }
    }
}
