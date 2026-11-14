using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Shared helpers for the building editor tools: plan/world conversion and undoable mutations.</summary>
    public static class BuildingEditorUtil
    {
        public static int ActiveFloorIndex(Building b)
        {
            if (b == null || b.data == null || b.data.floors.Count == 0) return -1;
            return Mathf.Clamp(b.activeFloor, 0, b.data.floors.Count - 1);
        }

        /// <summary>Plan (x, y) at the given elevation to world position, through the building's transform.</summary>
        public static Vector3 PlanToWorld(Building b, Vector2 p, float elevation) =>
            b.transform.TransformPoint(new Vector3(p.x, elevation, p.y));

        /// <summary>Intersects a GUI-point ray with the building-local plane y = elevation.</summary>
        public static bool GuiToPlan(Building b, Vector2 guiPoint, float elevation, out Vector2 plan)
        {
            plan = default;
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            var t = b.transform;
            var o = t.InverseTransformPoint(ray.origin);
            var d = t.InverseTransformPoint(ray.origin + ray.direction) - o;
            if (Mathf.Abs(d.y) < 1e-6f) return false;
            float k = (elevation - o.y) / d.y;
            if (k < 0f) return false;
            var hit = o + d * k;
            plan = new Vector2(hit.x, hit.z);
            return true;
        }

        /// <summary>Records Undo, applies the mutation, then marks the building and its scene dirty.</summary>
        public static void Mutate(Building b, string undoName, int floorIndex, Action mutation)
        {
            Undo.RecordObject(b, undoName);
            mutation();
            b.MarkDirty(floorIndex);
            EditorUtility.SetDirty(b);
            if (!Application.isPlaying && b.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
        }

        /// <summary>True if the node forms a column with nodes on other floors.</summary>
        public static bool IsLinked(BuildingData data, string nodeId)
        {
            int n = 0;
            foreach (var _ in data.GetLinkedColumn(nodeId))
                if (++n > 1) return true;
            return false;
        }

        /// <summary>Ids of the floor's nodes that are part of a cross-floor column.</summary>
        public static HashSet<string> LinkedNodeIds(BuildingData data, FloorData f)
        {
            var ids = new HashSet<string>();
            var own = new HashSet<string>();
            foreach (var n in f.nodes) own.Add(n.id);
            foreach (var n in f.nodes)
                if (!string.IsNullOrEmpty(n.linkedBelowId)) ids.Add(n.id);
            foreach (var fl in data.floors)
            {
                if (fl == f) continue;
                foreach (var n in fl.nodes)
                    if (!string.IsNullOrEmpty(n.linkedBelowId) && own.Contains(n.linkedBelowId)) ids.Add(n.linkedBelowId);
            }
            return ids;
        }
    }

    /// <summary>Node / segment picked with the wall tool. Editor-session state, never serialized.</summary>
    static class WallSelection
    {
        public static Building building;
        public static string floorId;
        public static string nodeId;
        public static string segmentId;

        public static event Action Changed;

        public static void SelectNode(Building b, FloorData f, string id) => Set(b, f, id, null);
        public static void SelectSegment(Building b, FloorData f, string id) => Set(b, f, null, id);
        public static void Clear() => Set(null, null, null, null);

        static void Set(Building b, FloorData f, string node, string seg)
        {
            if (building == b && floorId == f?.id && nodeId == node && segmentId == seg) return;
            building = b;
            floorId = f?.id;
            nodeId = node;
            segmentId = seg;
            Changed?.Invoke();
        }

        public static FloorData Floor(Building b) =>
            b != null && b == building && b.data != null ? b.data.floors.Find(x => x.id == floorId) : null;

        public static WallNode Node(Building b) => nodeId == null ? null : Floor(b)?.GetNode(nodeId);
        public static WallSegment Segment(Building b) => segmentId == null ? null : Floor(b)?.GetSegment(segmentId);
    }

    /// <summary>
    /// Opening picked with the opening tool. Editor-session state, never serialized. Cleared when the opening no
    /// longer exists (deleted, or gone after Undo/Redo).
    /// </summary>
    [InitializeOnLoad]
    static class OpeningSelection
    {
        public static Building building;
        public static string floorId;
        public static string openingId;

        public static event Action Changed;

        static OpeningSelection()
        {
            Undo.undoRedoPerformed += Validate;
        }

        public static void Select(Building b, FloorData f, string id) => Set(b, f, id);
        public static void Clear() => Set(null, null, null);

        static void Set(Building b, FloorData f, string id)
        {
            if (building == b && floorId == f?.id && openingId == id) return;
            building = b;
            floorId = f?.id;
            openingId = id;
            Changed?.Invoke();
        }

        public static FloorData Floor(Building b) =>
            b != null && b == building && b.data != null ? b.data.floors.Find(x => x.id == floorId) : null;

        /// <summary>The selected opening and its segment on b, or (null, null).</summary>
        public static (WallSegment seg, WallOpening o) Find(Building b)
        {
            var f = Floor(b);
            return f == null || openingId == null ? (null, null) : b.data.FindOpening(f, openingId);
        }

        /// <summary>Clears the selection if its building or opening is gone.</summary>
        public static void Validate()
        {
            if (openingId == null) return;
            if (building == null || Find(building).o == null) Clear();
        }
    }

    /// <summary>
    /// Stair picked with the stair tool, and optionally one of its route points (pointIndex, -1 = none).
    /// Editor-session state, never serialized. Cleared when the stair no longer exists (deleted, or gone after
    /// Undo/Redo); pointIndex is reset when it falls outside the route.
    /// </summary>
    [InitializeOnLoad]
    static class StairSelection
    {
        public static Building building;
        public static string stairId;
        public static int pointIndex = -1;

        public static event Action Changed;

        static StairSelection()
        {
            Undo.undoRedoPerformed += Validate;
        }

        public static void Select(Building b, string id, int point = -1)
        {
            if (building == b && stairId == id && pointIndex == point) return;
            building = b;
            stairId = id;
            pointIndex = id == null ? -1 : point;
            Changed?.Invoke();
        }

        public static void Clear() => Select(null, null);

        /// <summary>The selected stair on b, or null.</summary>
        public static StairData Find(Building b) =>
            b != null && b == building && b.data != null && stairId != null ? b.data.FindStair(stairId) : null;

        /// <summary>Clears the selection if its building or stair is gone; drops a point index out of range.</summary>
        public static void Validate()
        {
            if (stairId == null) return;
            var s = building != null ? Find(building) : null;
            if (s == null) { Clear(); return; }
            if (pointIndex >= s.route.Count) Select(building, stairId, -1);
        }
    }

    /// <summary>
    /// Editor-only display of the active floor: generated renderers of floors above the active one are disabled
    /// while the Building is selected. Never serialized (generated objects are DontSave). Every Building that is
    /// not selected is restored on selection change, after a domain reload and when entering play mode (no
    /// static bookkeeping that a reload could lose). Re-applied on every Scene view repaint because regeneration
    /// recreates the generated children.
    /// </summary>
    [InitializeOnLoad]
    static class FloorVisibility
    {
        static FloorVisibility()
        {
            SceneView.duringSceneGui += OnSceneGui;
            Selection.selectionChanged += RestoreUnselected;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.delayCall += RestoreUnselected; // after a domain reload, once scenes are loaded
        }

        static void OnSceneGui(SceneView view)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var b in SelectedBuildings()) Apply(b);
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
                foreach (var b in SceneBuildings()) SetVisible(b, _ => true);
        }

        static void RestoreUnselected()
        {
            var selected = new HashSet<Building>(SelectedBuildings());
            foreach (var b in SceneBuildings())
                if (!selected.Contains(b)) SetVisible(b, _ => true);
            SceneView.RepaintAll();
        }

        static IEnumerable<Building> SelectedBuildings()
        {
            foreach (var go in Selection.gameObjects)
            {
                var b = go != null ? go.GetComponent<Building>() : null;
                if (b != null) yield return b;
            }
        }

        // Works on 2022.3 and Unity 6 without obsolete APIs; skips assets/prefab assets.
        static IEnumerable<Building> SceneBuildings()
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Building>())
                if (b != null && !EditorUtility.IsPersistent(b) && b.gameObject.scene.IsValid()) yield return b;
        }

        static void Apply(Building b)
        {
            int active = BuildingEditorUtil.ActiveFloorIndex(b);
            if (active < 0) return;
            SetVisible(b, i => i <= active);
        }

        static void SetVisible(Building b, Func<int, bool> visible)
        {
            var t = b.transform;
            for (int c = 0; c < t.childCount; c++)
            {
                var child = t.GetChild(c);
                if (!BuildingRenderer.IsGeneratedFloor(child, out int i)) continue;
                bool on = visible(i);
                foreach (var mr in child.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.enabled != on) mr.enabled = on;
            }
        }
    }
}
