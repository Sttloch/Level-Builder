using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Finds the generated face (face key) under a world ray by intersecting the triangles of the visible
    /// generated meshes that carry a <see cref="FaceKeyMap"/>.</summary>
    public static class FacePicker
    {
        // Generated meshes are never modified after creation (a regeneration creates new Mesh instances), so the
        // CPU copy of vertices/triangles is cached per Mesh instance; entries of destroyed meshes are dropped.
        class Cached { public Vector3[] verts; public int[][] tris; }
        static readonly Dictionary<Mesh, Cached> cache = new Dictionary<Mesh, Cached>();
        static readonly List<Mesh> dead = new List<Mesh>();

        static Cached Get(Mesh mesh)
        {
            if (cache.TryGetValue(mesh, out var c)) return c;
            dead.Clear();
            foreach (var k in cache.Keys) if (k == null) dead.Add(k);
            foreach (var k in dead) cache.Remove(k);
            c = new Cached { verts = mesh.vertices, tris = new int[mesh.subMeshCount][] };
            for (int i = 0; i < c.tris.Length; i++) c.tris[i] = mesh.GetTriangles(i);
            cache[mesh] = c;
            return c;
        }

        public static bool Pick(Building b, Ray worldRay, out string faceKey, out FloorData floor, out Vector3 hitPoint)
        {
            faceKey = null; floor = null; hitPoint = default;
            if (b == null || b.data == null) return false;

            float best = float.MaxValue;
            var root = b.transform;
            for (int c = 0; c < root.childCount; c++)
            {
                var child = root.GetChild(c);
                if (!BuildingRenderer.IsGeneratedFloor(child, out int index)) continue;
                if (index < 0 || index >= b.data.floors.Count) continue;
                if (!child.gameObject.activeInHierarchy) continue;

                foreach (var map in child.GetComponentsInChildren<FaceKeyMap>(false))
                {
                    var mr = map.GetComponent<MeshRenderer>();
                    var mf = map.GetComponent<MeshFilter>();
                    if (mr == null || !mr.enabled || mf == null || mf.sharedMesh == null) continue;
                    var mesh = mf.sharedMesh;
                    var tr = map.transform;
                    var o = tr.InverseTransformPoint(worldRay.origin);
                    var d = tr.InverseTransformVector(worldRay.direction); // not normalized: o + d*t is the same point for any scale
                    if (!BoundsHit(mesh.bounds, o, d)) continue;
                    var cm = Get(mesh);
                    var verts = cm.verts;
                    for (int sm = 0; sm < cm.tris.Length; sm++)
                    {
                        var tris = cm.tris[sm];
                        for (int t = 0; t + 2 < tris.Length; t += 3)
                        {
                            if (!RayTriangle(o, d, verts[tris[t]], verts[tris[t + 1]], verts[tris[t + 2]], out float lt)) continue;
                            var wp = tr.TransformPoint(o + d * lt);
                            float dist = (wp - worldRay.origin).magnitude; // world distance, comparable across objects
                            if (dist >= best) continue;
                            var key = map.KeyAt(sm, t / 3);
                            if (key == null) continue;
                            best = dist;
                            faceKey = key;
                            floor = b.data.floors[index];
                            hitPoint = wp;
                        }
                    }
                }
            }
            return faceKey != null;
        }

        // Slab test of the local ray against the mesh bounds (a ray starting inside counts as a hit).
        static bool BoundsHit(Bounds bb, Vector3 o, Vector3 d)
        {
            float tmin = 0f, tmax = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float lo = bb.min[i], hi = bb.max[i];
                if (Mathf.Abs(d[i]) < 1e-12f) { if (o[i] < lo || o[i] > hi) return false; continue; }
                float t1 = (lo - o[i]) / d[i], t2 = (hi - o[i]) / d[i];
                if (t1 > t2) { var tmp = t1; t1 = t2; t2 = tmp; }
                tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) return false;
            }
            return true;
        }

        // Moller-Trumbore, double sided; t is in units of the (local) direction vector.
        static bool RayTriangle(Vector3 o, Vector3 d, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0f;
            var e1 = v1 - v0; var e2 = v2 - v0;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            var s = o - v0;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 1e-5f;
        }
    }
}
