using System.Collections.Generic;
using UnityEngine;
using LevelBuilder.Geometry;

namespace LevelBuilder
{
    /// <summary>
    /// Meshes for generated door leaves and windows, in the local space of the object at the base-centre of the
    /// opening: x across the opening (centred), y up from the base, z through the wall thickness (centred).
    /// </summary>
    public static class OpeningBuilder
    {
        /// <summary>A box spanning x ∈ [−w/2, w/2], y ∈ [0, h], z ∈ [−t/2, t/2].</summary>
        public static MeshData DefaultLeaf(float width, float height, float thickness = 0.04f)
        {
            var md = new MeshData();
            AddBox(md, "leaf", new Vector3(-width * 0.5f, 0f, -thickness * 0.5f), new Vector3(width * 0.5f, height, thickness * 0.5f));
            return md;
        }

        /// <summary>Rectangular frame (four bars) spanning x ∈ [−w/2, w/2], y ∈ [0, h], z ∈ [−depth/2, depth/2].</summary>
        public static MeshData WindowFrame(float width, float height, float bar = 0.05f, float depth = 0.08f)
        {
            var md = new MeshData();
            float hw = width * 0.5f, hd = depth * 0.5f;
            bar = Mathf.Min(bar, width * 0.5f, height * 0.5f);
            AddBox(md, "frame", new Vector3(-hw, 0f, -hd), new Vector3(hw, bar, hd));                       // bottom
            AddBox(md, "frame", new Vector3(-hw, height - bar, -hd), new Vector3(hw, height, hd));          // top
            AddBox(md, "frame", new Vector3(-hw, bar, -hd), new Vector3(-hw + bar, height - bar, hd));      // left
            AddBox(md, "frame", new Vector3(hw - bar, bar, -hd), new Vector3(hw, height - bar, hd));        // right
            return md;
        }

        /// <summary>Double-sided pane at z = 0 spanning x ∈ [−w/2, w/2], y ∈ [0, h].</summary>
        public static MeshData Glass(float width, float height)
        {
            var md = new MeshData();
            float hw = width * 0.5f;
            var pts = new[] { new Vector3(-hw, 0f, 0f), new Vector3(hw, 0f, 0f), new Vector3(hw, height, 0f), new Vector3(-hw, height, 0f) };
            var uvs = new[] { new Vector2(0f, 0f), new Vector2(width, 0f), new Vector2(width, height), new Vector2(0f, height) };
            var tris = new[] { 0, 1, 2, 0, 2, 3 };
            md.AddFace("glass", pts, uvs, tris, Vector3.forward);
            md.AddFace("glass", pts, uvs, tris, Vector3.back);
            return md;
        }

        /// <summary>
        /// Scales <paramref name="instance"/> so the combined bounds of its renderers (in its parent's space) are
        /// size.x wide and size.y tall (z scale unchanged), then moves it so the bounds' bottom-centre sits at the
        /// parent's origin. Does nothing if it has no renderers or degenerate bounds.
        /// </summary>
        public static void FitToBox(GameObject instance, Vector3 size)
        {
            if (instance == null) return;
            var t = instance.transform;
            if (!TryLocalBounds(t, out var b) || b.size.x < 1e-6f || b.size.y < 1e-6f) return;
            var s = t.localScale;
            t.localScale = new Vector3(s.x * size.x / b.size.x, s.y * size.y / b.size.y, s.z);
            if (!TryLocalBounds(t, out b)) return;
            t.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        }

        // Combined renderer bounds in the space of t's parent (world if it has none).
        internal static bool TryLocalBounds(Transform t, out Bounds result)
        {
            result = default;
            bool any = false;
            var parent = t.parent;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                Bounds lb;
                if (mf != null) { if (mf.sharedMesh == null) continue; lb = mf.sharedMesh.bounds; }
                else lb = r.localBounds;
                var m = r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var w = m.MultiplyPoint3x4(c);
                    var p = parent != null ? parent.InverseTransformPoint(w) : w;
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return any;
        }

        static void AddBox(MeshData md, string key, Vector3 a, Vector3 b)
        {
            var tris = new[] { 0, 1, 2, 0, 2, 3 };
            // ±X
            Quad(md, key, tris, Vector3.right, new Vector3(b.x, a.y, a.z), new Vector3(b.x, a.y, b.z), new Vector3(b.x, b.y, b.z), new Vector3(b.x, b.y, a.z), p => new Vector2(p.z, p.y));
            Quad(md, key, tris, Vector3.left, new Vector3(a.x, a.y, a.z), new Vector3(a.x, a.y, b.z), new Vector3(a.x, b.y, b.z), new Vector3(a.x, b.y, a.z), p => new Vector2(p.z, p.y));
            // ±Y
            Quad(md, key, tris, Vector3.up, new Vector3(a.x, b.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(b.x, b.y, b.z), new Vector3(a.x, b.y, b.z), p => new Vector2(p.x, p.z));
            Quad(md, key, tris, Vector3.down, new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, a.y, b.z), new Vector3(a.x, a.y, b.z), p => new Vector2(p.x, p.z));
            // ±Z
            Quad(md, key, tris, Vector3.forward, new Vector3(a.x, a.y, b.z), new Vector3(b.x, a.y, b.z), new Vector3(b.x, b.y, b.z), new Vector3(a.x, b.y, b.z), p => new Vector2(p.x, p.y));
            Quad(md, key, tris, Vector3.back, new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(a.x, b.y, a.z), p => new Vector2(p.x, p.y));
        }

        // MeshData.AddFace re-winds each triangle to agree with the normal.
        static void Quad(MeshData md, string key, int[] tris, Vector3 normal, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            System.Func<Vector3, Vector2> uv)
        {
            var pts = new List<Vector3> { p0, p1, p2, p3 };
            var uvs = new List<Vector2> { uv(p0), uv(p1), uv(p2), uv(p3) };
            md.AddFace(key, pts, uvs, tris, normal);
        }
    }
}
