using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using LevelBuilder.Geometry;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LevelBuilder
{
    /// <summary>Generates non-saved meshes from the Building data.</summary>
    [ExecuteAlways, RequireComponent(typeof(Building))]
    public class BuildingRenderer : MonoBehaviour
    {
        const HideFlags GenFlags = HideFlags.DontSave | HideFlags.NotEditable;
        static Material cachedDefault;

        public Material defaultMaterial;

        Building building;
        bool pending;
        readonly List<int> tmp = new List<int>();

        Building B => building != null ? building : (building = GetComponent<Building>());

        static Material DefaultMaterial()
        {
            if (cachedDefault != null) return cachedDefault;
            Material m = null;
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null) m = rp.defaultMaterial;
            if (m == null)
            {
                var sh = Shader.Find("Standard");
                if (sh == null) return null;
                m = new Material(sh) { name = "LevelBuilder Default", hideFlags = HideFlags.DontSave };
            }
            cachedDefault = m;
            return m;
        }

        void OnEnable()
        {
            B.Changed += OnChanged;
            Regenerate();
        }

        void OnDisable()
        {
            if (building != null) building.Changed -= OnChanged;
#if UNITY_EDITOR
            EditorApplication.update -= EditorFlush;
#endif
            pending = false;
        }

        void OnChanged()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Defer: OnValidate cannot create/destroy GameObjects, and this batches same-frame changes.
                if (!pending) { pending = true; EditorApplication.update += EditorFlush; }
                return;
            }
#endif
            Flush();
        }

#if UNITY_EDITOR
        void EditorFlush()
        {
            EditorApplication.update -= EditorFlush;
            if (this == null) return;
            Flush();
        }
#endif

        void Flush()
        {
            pending = false;
            if (this == null || B == null) return;
            bool all = B.ConsumeDirty(tmp);
            if (all) { Regenerate(-1); return; }
            foreach (var i in tmp) Regenerate(i);
        }

        /// <summary>Rebuilds a floor (and its neighbours, whose ceiling/slab depend on it). -1 = all floors.</summary>
        public void Regenerate(int floorIndex = -1)
        {
            var data = B.data;
            if (data == null) return;
            int n = data.floors.Count;

            if (floorIndex < 0)
            {
                for (int c = transform.childCount - 1; c >= 0; c--)
                {
                    var t = transform.GetChild(c);
                    if (t.name.StartsWith("Floor ")) DestroyFloorObject(t.gameObject);
                }
                for (int i = 0; i < n; i++) BuildFloorObject(i);
                return;
            }

            for (int c = transform.childCount - 1; c >= 0; c--)
            {
                var t = transform.GetChild(c);
                if (t.name.StartsWith("Floor ") && int.TryParse(t.name.Substring(6), out int idx) && idx >= n)
                    DestroyFloorObject(t.gameObject);
            }
            for (int i = floorIndex - 1; i <= floorIndex + 1; i++)
                if (i >= 0 && i < n) BuildFloorObject(i);
        }

        void BuildFloorObject(int i)
        {
            string name = "Floor " + i;
            for (int c = transform.childCount - 1; c >= 0; c--)
            {
                var t = transform.GetChild(c);
                if (t.name == name) DestroyFloorObject(t.gameObject);
            }

            var data = B.data;
            var f = data.floors[i];
            bool top = i == data.floors.Count - 1;

            var root = new GameObject(name) { hideFlags = GenFlags };
            root.transform.SetParent(transform, false);
            AddPart(root.transform, "Walls", WallMeshBuilder.Build(f, f.elevation));
            AddPart(root.transform, "Floor", SlabMeshBuilder.BuildFloor(f));
            AddPart(root.transform, "Ceiling", SlabMeshBuilder.BuildCeiling(f, top));
        }

        void AddPart(Transform parent, string name, MeshData md)
        {
            var go = new GameObject(name) { hideFlags = GenFlags };
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mc = go.AddComponent<MeshCollider>();
            var mesh = md.ToMesh();
            mesh.hideFlags = GenFlags;
            mesh.name = name;
            mf.sharedMesh = mesh;
            mr.sharedMaterial = defaultMaterial != null ? defaultMaterial : DefaultMaterial();
            if (mesh.vertexCount > 0) mc.sharedMesh = mesh;
        }

        static void DestroyFloorObject(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) DestroyObj(mf.sharedMesh);
            DestroyObj(root);
        }

        static void DestroyObj(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
