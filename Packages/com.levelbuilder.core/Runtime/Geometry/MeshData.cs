using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelBuilder.Geometry
{
    /// <summary>Triangles of one logical face (e.g. "{segmentId}/L"), the unit later painted with a material.</summary>
    public class FaceGroup
    {
        public string key;
        public List<int> triangles = new List<int>();
    }

    /// <summary>Engine-agnostic mesh buffers with triangles grouped by face key.</summary>
    public class MeshData
    {
        public List<Vector3> vertices = new List<Vector3>();
        public List<Vector3> normals = new List<Vector3>();
        public List<Vector2> uvs = new List<Vector2>();
        public List<FaceGroup> groups = new List<FaceGroup>();

        readonly Dictionary<string, FaceGroup> byKey = new Dictionary<string, FaceGroup>();

        public FaceGroup GetOrAddGroup(string key)
        {
            if (byKey.TryGetValue(key, out var g)) return g;
            g = groups.Find(x => x.key == key);
            if (g == null)
            {
                g = new FaceGroup { key = key };
                groups.Add(g);
            }
            byKey[key] = g;
            return g;
        }

        /// <summary>
        /// Adds a flat face: one vertex per point, all with <paramref name="normal"/>. Triangles index into
        /// <paramref name="points"/>; each is re-wound if needed so Cross(b-a, c-a) agrees with the normal
        /// (the convention Mesh.RecalculateNormals uses).
        /// </summary>
        public void AddFace(string key, IList<Vector3> points, IList<Vector2> faceUvs, IList<int> triangles, Vector3 normal)
        {
            int baseIndex = vertices.Count;
            var kept = new List<int>(triangles.Count);
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                var n = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                if (n.sqrMagnitude < DegenerateEps) continue; // zero-area triangle
                if (Vector3.Dot(n, normal) < 0f) { int t = b; b = c; c = t; }
                kept.Add(baseIndex + a);
                kept.Add(baseIndex + b);
                kept.Add(baseIndex + c);
            }
            if (kept.Count == 0) return;

            for (int i = 0; i < points.Count; i++)
            {
                vertices.Add(points[i]);
                normals.Add(normal);
                uvs.Add(faceUvs[i]);
            }
            GetOrAddGroup(key).triangles.AddRange(kept);
        }

        const float DegenerateEps = 1e-14f;

        /// <summary>
        /// Builds a Unity mesh. In this plan every group is combined into a single submesh
        /// (per-material submeshes come later).
        /// </summary>
        public Mesh ToMesh()
        {
            var mesh = new Mesh();
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            var all = new List<int>();
            foreach (var g in groups) all.AddRange(g.triangles);
            mesh.subMeshCount = 1;
            mesh.SetTriangles(all, 0, true);
            if (all.Count > 0) mesh.RecalculateTangents(); // after normals/uvs; normal-mapped materials need them
            return mesh;
        }
    
        /// <summary>
        /// Builds a Unity mesh with one submesh per distinct material (in order of first use by the groups).
        /// Each group's UVs (metres) are divided by its material's size (metres covered by one texture repeat);
        /// groups never share vertices (AddFace adds them per face). map: per group its key, submesh and triangle
        /// range (triangle indices within the submesh).
        /// </summary>
        public Mesh ToMesh(Func<string, Material> materialOf, Func<Material, Vector2> sizeOf, out Material[] materials,
            out List<(string key, int submesh, int firstTri, int triCount)> map)
        {
            var mats = new List<Material>();
            var perSub = new List<List<int>>();
            map = new List<(string key, int submesh, int firstTri, int triCount)>();
            var scaled = new List<Vector2>(uvs);
            var done = new HashSet<int>();
            foreach (var g in groups)
            {
                if (g.triangles.Count == 0) continue;
                var m = materialOf != null ? materialOf(g.key) : null;
                int sub = -1;
                for (int i = 0; i < mats.Count; i++) if (ReferenceEquals(mats[i], m)) { sub = i; break; }
                if (sub < 0) { sub = mats.Count; mats.Add(m); perSub.Add(new List<int>()); }
                var size = sizeOf != null ? sizeOf(m) : Vector2.one;
                if (Mathf.Abs(size.x) < 1e-6f) size.x = 1f;
                if (Mathf.Abs(size.y) < 1e-6f) size.y = 1f;
                foreach (int v in g.triangles)
                    if (done.Add(v)) scaled[v] = new Vector2(uvs[v].x / size.x, uvs[v].y / size.y);
                map.Add((g.key, sub, perSub[sub].Count / 3, g.triangles.Count / 3));
                perSub[sub].AddRange(g.triangles);
            }

            var mesh = new Mesh();
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, scaled);
            mesh.subMeshCount = Mathf.Max(1, perSub.Count);
            for (int i = 0; i < perSub.Count; i++) mesh.SetTriangles(perSub[i], i, true);
            if (perSub.Count > 0) mesh.RecalculateTangents();
            materials = mats.ToArray();
            return mesh;
        }
    }
}
