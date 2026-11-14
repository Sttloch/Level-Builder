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
        static Material fallbackMaterial;

        public Material defaultMaterial;

        Building building;
        bool pending;
        readonly List<int> tmp = new List<int>();
        readonly HashSet<Mesh> owned = new HashSet<Mesh>();
        readonly SortedSet<int> toBuild = new SortedSet<int>();

        Building B => building != null ? building : (building = GetComponent<Building>());

        static Material DefaultMaterial()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null && rp.defaultMaterial != null) return rp.defaultMaterial;
            if (fallbackMaterial != null) return fallbackMaterial;
            var sh = Shader.Find("Standard");
            if (sh == null) return null;
            fallbackMaterial = new Material(sh) { name = "LevelBuilder Default", hideFlags = HideFlags.DontSave };
            return fallbackMaterial;
        }

        // Only DontSave children named "Floor {n}" are ours; user children are never touched.
        static bool IsGeneratedFloor(Transform t, out int index)
        {
            index = -1;
            return (t.gameObject.hideFlags & HideFlags.DontSave) == HideFlags.DontSave
                && t.name.StartsWith("Floor ")
                && int.TryParse(t.name.Substring(6), out index);
        }

        void OnEnable()
        {
            B.Changed += OnChanged;
            B.ConsumeDirty(tmp); // pending state is covered by the full regenerate below
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

        void OnDestroy()
        {
            for (int c = transform.childCount - 1; c >= 0; c--)
            {
                var t = transform.GetChild(c);
                if (IsGeneratedFloor(t, out _)) DestroyFloorObject(t.gameObject);
            }
            foreach (var m in owned) if (m != null) DestroyObj(m);
            owned.Clear();
        }

        void OnChanged()
        {
#if UNITY_EDITOR
            // Always defer in the editor (also in play mode): OnValidate cannot create/destroy
            // GameObjects, and this batches same-frame changes. EditorApplication.update ticks in play mode.
            if (!pending) { pending = true; EditorApplication.update += EditorFlush; }
#else
            Flush();
#endif
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
            toBuild.Clear();
            foreach (var i in tmp) AddWithNeighbours(i);
            BuildSet();
        }

        void AddWithNeighbours(int i)
        {
            for (int k = i - 1; k <= i + 1; k++) toBuild.Add(k);
        }

        /// <summary>Rebuilds a floor (and its neighbours, whose ceiling/slab depend on it). -1 = all floors.</summary>
        public void Regenerate(int floorIndex = -1)
        {
            if (B.data == null) return;
            toBuild.Clear();
            if (floorIndex < 0)
            {
                RemoveGenerated(-1);
                for (int i = 0; i < B.data.floors.Count; i++) toBuild.Add(i);
            }
            else AddWithNeighbours(floorIndex);
            BuildSet();
        }

        void BuildSet()
        {
            int n = B.data.floors.Count;
            RemoveGenerated(n);
            foreach (var i in toBuild)
                if (i >= 0 && i < n) BuildFloorObject(i);
            toBuild.Clear();
        }

        // Removes generated floors with index >= n (stale); n < 0 removes all generated floors.
        void RemoveGenerated(int n)
        {
            for (int c = transform.childCount - 1; c >= 0; c--)
            {
                var t = transform.GetChild(c);
                if (IsGeneratedFloor(t, out int idx) && (n < 0 || idx >= n))
                    DestroyFloorObject(t.gameObject);
            }
        }

        void BuildFloorObject(int i)
        {
            for (int c = transform.childCount - 1; c >= 0; c--)
            {
                var t = transform.GetChild(c);
                if (IsGeneratedFloor(t, out int idx) && idx == i) DestroyFloorObject(t.gameObject);
            }

            var f = B.data.floors[i];
            bool top = i == B.data.floors.Count - 1;

            var root = new GameObject("Floor " + i) { hideFlags = GenFlags };
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
            owned.Add(mesh);
            mf.sharedMesh = mesh;
            mr.sharedMaterial = defaultMaterial != null ? defaultMaterial : DefaultMaterial();
            if (mesh.vertexCount > 0) mc.sharedMesh = mesh;
        }

        // Only meshes this instance created are destroyed; inherited (e.g. Instantiate-copied) ones are shared.
        void DestroyFloorObject(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m != null && owned.Remove(m)) DestroyObj(m);
            }
            DestroyObj(root);
        }

        static void DestroyObj(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
