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
        // Hidden in the hierarchy: generated objects are not user content (scene clicks select the Building).
        const HideFlags GenFlags = HideFlags.DontSave | HideFlags.NotEditable | HideFlags.HideInHierarchy;
        const string FallbackName = "LevelBuilder Default";
        static Material fallbackMaterial;

        public Material defaultMaterial;
        /// <summary>Window glass. Null: a transparent clone of the default material, created once and cached.</summary>
        public Material glassMaterial;

        const string GlassPrefix = "LevelBuilder Glass ";
        Material generatedGlass;

        Building building;
        bool pending;
        readonly List<int> tmp = new List<int>();
        readonly SortedSet<int> toBuild = new SortedSet<int>();

        Building B => building != null ? building : (building = GetComponent<Building>());

#if UNITY_EDITOR
        /// <summary>
        /// Editor only: a persistent material asset to serialize into defaultMaterial at creation, so player builds
        /// never depend on RenderPipelineAsset.defaultMaterial (editor-only) or Shader.Find (may be stripped).
        /// </summary>
        public static Material EditorDefaultMaterial()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null && rp.defaultMaterial != null) return rp.defaultMaterial;
            return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        }

        void Reset()
        {
            defaultMaterial = EditorDefaultMaterial();
            if (isActiveAndEnabled) B.MarkDirty(); // re-skin already generated parts (deferred)
        }
#endif

        // Last resort when no material was serialized (e.g. component added from code in a player).
        static Material DefaultMaterial()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null && rp.defaultMaterial != null) return rp.defaultMaterial;
            if (fallbackMaterial != null) return fallbackMaterial;
            // Reuse one that survived a domain reload (DontSave objects are never unloaded).
            foreach (var existing in Resources.FindObjectsOfTypeAll<Material>())
                if (existing != null && existing.name == FallbackName) return fallbackMaterial = existing;
            var sh = Shader.Find("Standard");
            if (sh == null) return null;
            fallbackMaterial = new Material(sh) { name = FallbackName, hideFlags = HideFlags.DontSave };
            return fallbackMaterial;
        }

        /// <summary>Only DontSave children named "Floor {n}" are generated floors; user children are never touched.</summary>
        public static bool IsGeneratedFloor(Transform t, out int index)
        {
            index = -1;
            return (t.gameObject.hideFlags & HideFlags.DontSave) == HideFlags.DontSave
                && t.name.StartsWith("Floor ", System.StringComparison.Ordinal)
                && int.TryParse(t.name.Substring(6), out index);
        }

        void OnEnable()
        {
            B.Changed += OnChanged;
            B.ConsumeDirty(tmp); // pending state is covered by the full regenerate below
#if UNITY_EDITOR
            // OnEnable can run while the hierarchy is being activated (SetActive, Undo re-activation, domain
            // reload): destroying the existing generated children then is an error. Keep them and regenerate
            // on the next editor update instead.
            if (HasGeneratedChildren()) { B.MarkDirty(); return; }
#endif
            Regenerate();
        }

        bool HasGeneratedChildren()
        {
            for (int c = 0; c < transform.childCount; c++)
                if (IsGeneratedFloor(transform.GetChild(c), out _)) return true;
            return false;
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
            if (generatedGlass == null) generatedGlass = FindGeneratedGlass(null);
            if (generatedGlass != null) { DestroyObj(generatedGlass); generatedGlass = null; }
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
            var marker = root.AddComponent<GeneratedFloorMarker>();
            marker.ownerId = GeneratedFloorMarker.KeyOf(this);
            marker.hideFlags = GenFlags | HideFlags.HideInInspector;
            AddPart(root.transform, "Walls", WallMeshBuilder.Build(f, f.elevation));
            AddPart(root.transform, "Floor", SlabMeshBuilder.BuildFloor(f));
            AddPart(root.transform, "Ceiling", SlabMeshBuilder.BuildCeiling(f, top));
            BuildOpenings(root.transform, f);
        }

        Material BaseMaterial => defaultMaterial != null ? defaultMaterial : DefaultMaterial();

        void AddPart(Transform parent, string name, MeshData md)
        {
            var go = AddMeshObject(parent, name, md, BaseMaterial, out var mesh);
            var mc = go.AddComponent<MeshCollider>();
            if (mesh.vertexCount > 0) mc.sharedMesh = mesh;
        }

        static GameObject AddMeshObject(Transform parent, string name, MeshData md, Material material, out Mesh mesh)
        {
            var go = NewChild(parent, name);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mesh = md.ToMesh();
            mesh.hideFlags = GenFlags;
            mesh.name = name;
            mf.sharedMesh = mesh;
            mr.sharedMaterial = material;
            return go;
        }

        static GameObject NewChild(Transform parent, string name)
        {
            var go = new GameObject(name) { hideFlags = GenFlags };
            go.transform.SetParent(parent, false);
            return go;
        }

        // ---- Doors and windows (valid openings only) ----

        const float LeafGap = 0.01f;
        const float LeafThickness = 0.04f;
        const float FrameBar = 0.05f;

        void BuildOpenings(Transform root, FloorData f)
        {
            Transform doors = null, windows = null;
            foreach (var kv in OpeningLayout.Compute(f))
            {
                var s = f.GetSegment(kv.Key);
                var a = s != null ? f.GetNode(s.startNodeId) : null;
                var e = s != null ? f.GetNode(s.endNodeId) : null;
                if (a == null || e == null) continue;
                var d = e.position - a.position;
                if (d.sqrMagnitude < 1e-10f) continue;
                d.Normalize();
                // Forward = normal of the /R face, −LeftPerp(d); plan (x, y) -> local (x, elevation, y).
                var rot = Quaternion.LookRotation(new Vector3(d.y, 0f, -d.x), Vector3.up);
                foreach (var r in kv.Value)
                {
                    var o = r.opening;
                    if (!r.valid || o == null || string.IsNullOrEmpty(o.id)) continue;
                    if (o.type != OpeningType.Door && o.type != OpeningType.Window) continue;
                    var p = a.position + d * ((r.u0 + r.u1) * 0.5f);
                    var pos = new Vector3(p.x, f.elevation, p.y);
                    if (o.type == OpeningType.Door)
                    {
                        if (doors == null) doors = NewChild(root, "Doors").transform;
                        BuildDoor(doors, f, o, pos, rot);
                    }
                    else
                    {
                        if (windows == null) windows = NewChild(root, "Windows").transform;
                        BuildWindow(windows, o, pos, rot);
                    }
                }
            }
        }

        void BuildDoor(Transform parent, FloorData f, WallOpening o, Vector3 pos, Quaternion rot)
        {
            var go = NewChild(parent, o.id);
            go.SetActive(false); // configure fully before Door.Awake/OnEnable run (players, play mode)
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            var settings = o.door != null ? o.door.Clone() : new DoorSettings();
            var size = new Vector3(o.width - 2f * LeafGap, o.height - LeafGap, LeafThickness);

            GameObject leaf;
            if (settings.leafPrefab != null)
            {
                leaf = NewChild(go.transform, "Leaf");
                var inst = Instantiate(settings.leafPrefab, leaf.transform, false);
                inst.name = settings.leafPrefab.name;
                inst.transform.localPosition = Vector3.zero;
                SetGenFlags(inst);
                OpeningBuilder.FitToBox(inst, size);
                if (OpeningBuilder.TryLocalBounds(inst.transform, out var fitted) && fitted.size.z > 1e-4f) size.z = fitted.size.z;
            }
            else
                leaf = AddMeshObject(go.transform, "Leaf", OpeningBuilder.DefaultLeaf(size.x, size.y, size.z), BaseMaterial, out _);

            var box = leaf.AddComponent<BoxCollider>();
            box.size = size;
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            // Kinematic body: moving the leaf's collider must not count as moving a static collider.
            var body = leaf.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var door = go.AddComponent<Door>();
            door.settings = settings;
            door.leaf = leaf.transform;
            door.leafSize = size;
            door.EnsureTrigger();
            // Look the opening up by id when invoked: building.data may have been replaced (Undo) since generation.
            var bld = B;
            string floorId = f.id, openingId = o.id;
            door.onOpened.AddListener(() => Forward(bld, floorId, openingId, true));
            door.onClosed.AddListener(() => Forward(bld, floorId, openingId, false));
            go.SetActive(true);
        }

        static void Forward(Building bld, string floorId, string openingId, bool opened)
        {
            if (bld == null || bld.data == null) return;
            var f = bld.data.floors.Find(x => x != null && x.id == floorId);
            if (f == null) return;
            var (_, o) = bld.data.FindOpening(f, openingId);
            if (o == null || o.door == null) return;
            var ev = opened ? o.door.onOpened : o.door.onClosed;
            ev?.Invoke();
        }

        void BuildWindow(Transform parent, WallOpening o, Vector3 pos, Quaternion rot)
        {
            var go = NewChild(parent, o.id);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            if (o.windowPrefab != null)
            {
                var inst = Instantiate(o.windowPrefab, go.transform, false);
                inst.name = o.windowPrefab.name;
                inst.transform.localPosition = Vector3.zero;
                SetGenFlags(inst);
                OpeningBuilder.FitToBox(inst, new Vector3(o.width, o.height, 0f));
                inst.transform.localPosition += new Vector3(0f, o.sill, 0f);
                return;
            }
            var frame = AddMeshObject(go.transform, "Frame", OpeningBuilder.WindowFrame(o.width, o.height, FrameBar), BaseMaterial, out _);
            frame.transform.localPosition = new Vector3(0f, o.sill, 0f);
            float gw = Mathf.Max(0f, o.width - 2f * FrameBar), gh = Mathf.Max(0f, o.height - 2f * FrameBar);
            var glass = AddMeshObject(go.transform, "Glass", OpeningBuilder.Glass(gw, gh), GlassFor(), out _);
            glass.transform.localPosition = new Vector3(0f, o.sill + FrameBar, 0f);
            var box = glass.AddComponent<BoxCollider>(); // the window blocks passage like the wall it sits in
            box.size = new Vector3(gw, gh, 0.02f);
            box.center = new Vector3(0f, gh * 0.5f, 0f);
        }

        static void SetGenFlags(GameObject g)
        {
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = GenFlags;
        }

        Material GlassFor()
        {
            if (glassMaterial != null) return glassMaterial;
            var basis = BaseMaterial;
            // The cached clone survives domain reloads as a DontSave object: find it by name instead of leaking one.
            if (generatedGlass == null) generatedGlass = FindGeneratedGlass(basis);
            if (generatedGlass != null && generatedGlass.name != GlassName(basis))
            {
                DestroyObj(generatedGlass); // base material changed
                generatedGlass = null;
            }
            if (generatedGlass == null)
            {
                generatedGlass = GlassMaterial.Create(basis);
                if (generatedGlass != null) generatedGlass.name = GlassName(basis);
            }
            return generatedGlass;
        }

        string GlassName(Material basis) =>
            GlassPrefix + GeneratedFloorMarker.KeyOf(this) + "/" + (basis != null ? GeneratedFloorMarker.KeyOf(basis) : 0UL);

        // basis == null: any glass this renderer created, whatever it was based on.
        Material FindGeneratedGlass(Material basis)
        {
            string exact = basis != null ? GlassName(basis) : null;
            string prefix = GlassPrefix + GeneratedFloorMarker.KeyOf(this) + "/";
            Material any = null;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || (m.hideFlags & HideFlags.DontSave) == 0 || !m.name.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                if (m.name == exact) return m;
                if (any == null) any = m;
            }
            return any;
        }

        // Only meshes this instance created are destroyed: inherited (e.g. Instantiate-copied) ones are shared,
        // and meshes without DontSave (a user prefab's assets) are never touched.
        void DestroyFloorObject(GameObject root)
        {
            var marker = root.GetComponent<GeneratedFloorMarker>();
            if (marker != null && marker.ownerId == GeneratedFloorMarker.KeyOf(this))
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && (mf.sharedMesh.hideFlags & HideFlags.DontSave) == HideFlags.DontSave)
                        DestroyObj(mf.sharedMesh);
            }
            DestroyObj(root);
        }

        static void DestroyObj(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
