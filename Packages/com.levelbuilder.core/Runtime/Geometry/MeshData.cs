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
            var g = GetOrAddGroup(key);
            int baseIndex = vertices.Count;
            for (int i = 0; i < points.Count; i++)
            {
                vertices.Add(points[i]);
                normals.Add(normal);
                uvs.Add(faceUvs[i]);
            }
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                var n = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                if (Vector3.Dot(n, normal) < 0f) { int t = b; b = c; c = t; }
                g.triangles.Add(baseIndex + a);
                g.triangles.Add(baseIndex + b);
                g.triangles.Add(baseIndex + c);
            }
        }

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
            return mesh;
        }
    }
}
