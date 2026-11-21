using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Finds the generated face (face key) under a world ray by intersecting the triangles of the visible
    /// generated meshes that carry a <see cref="FaceKeyMap"/>.</summary>
    public static class FacePicker
    {
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
                    var d = tr.InverseTransformDirection(worldRay.direction); // unnormalized on purpose: t stays valid in world units
                    var verts = mesh.vertices;
                    for (int sm = 0; sm < mesh.subMeshCount; sm++)
                    {
                        var tris = mesh.GetTriangles(sm);
                        for (int t = 0; t + 2 < tris.Length; t += 3)
                        {
                            if (!RayTriangle(o, d, verts[tris[t]], verts[tris[t + 1]], verts[tris[t + 2]], out float dist)) continue;
                            if (dist >= best) continue;
                            var key = map.KeyAt(sm, t / 3);
                            if (key == null) continue;
                            best = dist;
                            faceKey = key;
                            floor = b.data.floors[index];
                            hitPoint = tr.TransformPoint(o + d * dist);
                        }
                    }
                }
            }
            return faceKey != null;
        }

        // Moller-Trumbore, double sided; dist is in units of the (local) direction vector.
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
