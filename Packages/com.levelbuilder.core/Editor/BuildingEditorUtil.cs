using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Shared helpers for the building editor tools: plan/world conversion and undoable mutations.</summary>
    static class BuildingEditorUtil
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
    /// Editor-only display of the active floor: generated renderers of floors above the active one are disabled
    /// while the Building is selected. Never serialized (generated objects are DontSave); everything is restored
    /// on deselection and when entering play mode. Re-applied on every Scene view repaint because regeneration
    /// recreates the generated children.
    /// </summary>
    [InitializeOnLoad]
    static class FloorVisibility
    {
        static readonly HashSet<Building> hidden = new HashSet<Building>();

        static FloorVisibility()
        {
            SceneView.duringSceneGui += OnSceneGui;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnSceneGui(SceneView view)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var go in Selection.gameObjects)
            {
                var b = go != null ? go.GetComponent<Building>() : null;
                if (b != null) Apply(b);
            }
        }

        static void OnSelectionChanged()
        {
            var keep = new HashSet<Building>();
            foreach (var go in Selection.gameObjects)
            {
                var b = go != null ? go.GetComponent<Building>() : null;
                if (b != null) keep.Add(b);
            }
            foreach (var b in new List<Building>(hidden))
                if (!keep.Contains(b)) Restore(b);
            SceneView.RepaintAll();
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) RestoreAll();
        }

        static void RestoreAll()
        {
            foreach (var b in new List<Building>(hidden)) Restore(b);
            hidden.Clear();
        }

        static void Apply(Building b)
        {
            int active = BuildingEditorUtil.ActiveFloorIndex(b);
            if (active < 0) return;
            hidden.Add(b);
            SetVisible(b, i => i <= active);
        }

        static void Restore(Building b)
        {
            hidden.Remove(b);
            if (b != null) SetVisible(b, _ => true);
        }

        static void SetVisible(Building b, Func<int, bool> visible)
        {
            var t = b.transform;
            for (int c = 0; c < t.childCount; c++)
            {
                var child = t.GetChild(c);
                if ((child.gameObject.hideFlags & HideFlags.DontSave) != HideFlags.DontSave) continue;
                if (!child.name.StartsWith("Floor ") || !int.TryParse(child.name.Substring(6), out int i)) continue;
                bool on = visible(i);
                foreach (var mr in child.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.enabled != on) mr.enabled = on;
            }
        }
    }
}
