using System.Collections.Generic;
using LevelBuilder.Geometry;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>
    /// Scene view tool to place and edit doors, windows and empty openings on the walls of the active floor.
    /// Hover a wall: preview of the active preset (green if it fits, red if not). Click: select the opening under
    /// the mouse, or place a new one (centre snapped to 0.1 m along the wall; Ctrl/Cmd: no snap). Drag a selected
    /// opening: move it along its wall or onto another (one Undo step). Handles: width (symmetric), height (top)
    /// and, on windows and empty openings, sill (bottom, top fixed). Selected doors show their hinge edge. Delete/Backspace: delete the selected opening (never the Building).
    /// Esc: deselect. Type and preset are set from the overlay.
    /// </summary>
    [EditorTool("Aberturas", typeof(Building))]
    public class OpeningTool : EditorTool
    {
        const float DragPixels = 4f;

        static readonly int ToolHint = "LevelBuilder.OpeningTool".GetHashCode();
        static readonly Color ValidColor = new Color(0.3f, 0.75f, 1f, 1f);
        static readonly Color InvalidColor = new Color(1f, 0.25f, 0.2f, 1f);
        static readonly Color SelectedColor = new Color(1f, 0.85f, 0.1f, 1f);
        static readonly Color FitsColor = new Color(0.2f, 1f, 0.4f, 1f);
        static readonly Color WallColor = new Color(1f, 1f, 1f, 0.5f);

        static OpeningType type = OpeningType.Door;
        static int presetIndex;

        /// <summary>Opening type placed by the tool (set from the overlay).</summary>
        public static OpeningType Type
        {
            get => type;
            set { if (type != value) { type = value; presetIndex = 0; } }
        }

        /// <summary>Index into OpeningPresets.For(Type).</summary>
        public static int PresetIndex
        {
            get => Mathf.Clamp(presetIndex, 0, OpeningPresets.For(type).Count - 1);
            set => presetIndex = Mathf.Clamp(value, 0, OpeningPresets.For(type).Count - 1);
        }

        public static OpeningPresets.Preset ActivePreset => OpeningPresets.For(type)[PresetIndex];

        public static void Activate(OpeningType t)
        {
            Type = t;
            ToolManager.SetActiveTool<OpeningTool>();
            SceneView.RepaintAll();
        }

        // Layout cache: OpeningLayout.Compute solves the wall joins, so it runs only when the data changed.
        Dictionary<string, List<OpeningRect>> layout;
        FloorData layoutFloor;
        Building observed;
        bool layoutDirty = true;

        // Hover preview (updated on MouseMove, drawn on Repaint).
        string hoverSegmentId;
        float hoverCenter;
        bool hoverFits;
        string hoverOpeningId;

        // Press / drag of an opening: one Undo group for the whole drag.
        string pressedOpeningId;
        Vector2 pressMouse;
        bool pressDragged;
        int dragUndoGroup = -1;

        // Size handles: one Undo group per handle drag, collapsed once the handle is released.
        int handleUndoGroup = -1;

        bool swallowDeleteCommand;

        public override GUIContent toolbarIcon =>
            new GUIContent(EditorGUIUtility.IconContent("RectTool").image,
                "Aberturas (Level Builder): clic en pared coloca, clic en abertura la selecciona, arrastra para moverla");

        public override void OnActivated()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            layoutDirty = true;
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            Observe(null);
            pressedOpeningId = null;
            hoverSegmentId = hoverOpeningId = null;
            FinishHandleUndo(true);
        }

        void OnUndoRedo()
        {
            ResetPress();
            hoverSegmentId = hoverOpeningId = null;
            layoutDirty = true;
            OpeningSelection.Validate();
            SceneView.RepaintAll();
        }

        // Forget a press/drag in progress (its opening may be gone): the next drag opens a new Undo group.
        void ResetPress()
        {
            pressedOpeningId = null;
            pressDragged = false;
            dragUndoGroup = -1;
        }

        void Observe(Building b)
        {
            if (observed == b) return;
            if (observed != null) observed.Changed -= OnBuildingChanged;
            observed = b;
            if (observed != null) observed.Changed += OnBuildingChanged;
            layoutDirty = true;
        }

        void OnBuildingChanged() => layoutDirty = true;

        Dictionary<string, List<OpeningRect>> Layout(FloorData f)
        {
            if (layout == null || layoutDirty || layoutFloor != f)
            {
                layout = OpeningLayout.Compute(f);
                layoutFloor = f;
                layoutDirty = false;
            }
            return layout;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView view)) return;
            var b = target as Building;
            int fi = BuildingEditorUtil.ActiveFloorIndex(b);
            if (fi < 0) return;
            Observe(b);
            var data = b.data;
            var f = data.floors[fi];
            var e = Event.current;

            FinishHandleUndo(false);
            if (e.type == EventType.Layout) OpeningSelection.Validate();

            int id = GUIUtility.GetControlID(ToolHint, FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            var (selSeg, selOpening) = OpeningSelection.Floor(b) == f ? OpeningSelection.Find(b) : (null, null);

            // Handles before the default control so they win the nearest-control test.
            if (selOpening != null && pressedOpeningId == null) DoSizeHandles(b, f, fi, selSeg, selOpening, e);

            HandleKeys(b, f, fi, e);

            switch (e.type)
            {
                case EventType.MouseMove:
                    UpdateHover(b, f, e);
                    view.Repaint();
                    break;
                case EventType.MouseDown:
                    if (e.button == 0 && HandleUtility.nearestControl == id && !e.alt)
                    {
                        GUIUtility.hotControl = id;
                        OnPress(b, f, fi, e);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id && pressedOpeningId != null)
                    {
                        OnDrag(b, f, fi, e);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id && e.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        if (pressDragged && dragUndoGroup >= 0) Undo.CollapseUndoOperations(dragUndoGroup);
                        pressedOpeningId = null;
                        pressDragged = false;
                        dragUndoGroup = -1;
                        UpdateHover(b, f, e);
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    Draw(b, f, Layout(f));
                    break;
            }
        }

        bool MousePlan(Building b, FloorData f, Vector2 mouse, out Vector2 plan)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var t = b.transform;
            var o = t.InverseTransformPoint(ray.origin);
            var d = t.InverseTransformPoint(ray.origin + ray.direction) - o;
            return OpeningPlacement.RayToPlan(f, o, d, out plan);
        }

        static float Snap(Event e, float center) =>
            e.control || e.command ? center : OpeningPlacement.SnapCenter(center);

        void UpdateHover(Building b, FloorData f, Event e)
        {
            hoverSegmentId = hoverOpeningId = null;
            if (!MousePlan(b, f, e.mousePosition, out var plan)) return;
            hoverOpeningId = OpeningPlacement.PickOpening(f, plan, OpeningPlacement.PickDistance);
            if (hoverOpeningId != null) return;
            if (!OpeningPlacement.Pick(f, plan, OpeningPlacement.PickDistance, out var segId, out float center)) return;
            hoverSegmentId = segId;
            hoverCenter = Snap(e, center);
            hoverFits = OpeningPlacement.Fits(f, segId, OpeningPresets.Create(ActivePreset, hoverCenter));
        }

        void OnPress(Building b, FloorData f, int fi, Event e)
        {
            swallowDeleteCommand = false;
            if (!MousePlan(b, f, e.mousePosition, out var plan)) { OpeningSelection.Clear(); return; }

            string picked = OpeningPlacement.PickOpening(f, plan, OpeningPlacement.PickDistance);
            if (picked != null)
            {
                OpeningSelection.Select(b, f, picked);
                WallSelection.Clear();
                pressedOpeningId = picked;
                pressMouse = e.mousePosition;
                pressDragged = false;
                dragUndoGroup = -1;
                return;
            }

            if (OpeningPlacement.Pick(f, plan, OpeningPlacement.PickDistance, out var segId, out float center))
            {
                var o = OpeningPlacement.Place(b, fi, segId, OpeningPresets.Create(ActivePreset, Snap(e, center)));
                layoutDirty = true;
                if (o != null)
                {
                    OpeningSelection.Select(b, f, o.id);
                    WallSelection.Clear();
                }
                hoverSegmentId = null;
                return;
            }
            OpeningSelection.Clear();
        }

        void OnDrag(Building b, FloorData f, int fi, Event e)
        {
            if (!pressDragged)
            {
                if ((e.mousePosition - pressMouse).magnitude <= DragPixels) return;
                pressDragged = true;
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Mover abertura");
                dragUndoGroup = Undo.GetCurrentGroup();
            }
            if (!MousePlan(b, f, e.mousePosition, out var plan)) return;
            if (!OpeningPlacement.Pick(f, plan, OpeningPlacement.PickDistance, out var segId, out float center)) return;
            center = Snap(e, center);
            var (seg, o) = b.data.FindOpening(f, pressedOpeningId);
            if (o == null) { pressedOpeningId = null; return; }
            if (seg.id == segId && Mathf.Abs(o.center - center) < 1e-5f) return;
            string oid = o.id;
            BuildingEditorUtil.Mutate(b, "Mover abertura", fi, () => b.data.MoveOpening(f, oid, segId, center));
            layoutDirty = true;
        }

        void FinishHandleUndo(bool force)
        {
            if (handleUndoGroup < 0 || (!force && GUIUtility.hotControl != 0)) return;
            Undo.CollapseUndoOperations(handleUndoGroup);
            handleUndoGroup = -1;
        }

        void BeginHandleUndo()
        {
            if (handleUndoGroup >= 0) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Tamaño de abertura");
            handleUndoGroup = Undo.GetCurrentGroup();
        }

        void DoSizeHandles(Building b, FloorData f, int fi, WallSegment seg, WallOpening o, Event e)
        {
            if (!OpeningPlacement.Frame(f, seg, out var a, out var dir, out _)) return;
            var t = b.transform;
            var along = t.TransformDirection(new Vector3(dir.x, 0f, dir.y)).normalized;
            var up = t.up;
            float u0 = o.center - o.width * 0.5f, u1 = o.center + o.width * 0.5f;
            float v0 = o.sill, v1 = o.sill + o.height, vm = (v0 + v1) * 0.5f;
            Vector3 W(float u, float v) => BuildingEditorUtil.PlanToWorld(b, a + dir * u, f.elevation + v);
            string oid = o.id;

            Handles.color = SelectedColor;
            if (Slide(W(u0, vm), along, out var p))
                SetSize(b, f, fi, oid, x => x.width = OpeningPlacement.WidthFromEdge(x.center, U(t, p, a, dir)));
            if (Slide(W(u1, vm), along, out p))
                SetSize(b, f, fi, oid, x => x.width = OpeningPlacement.WidthFromEdge(x.center, U(t, p, a, dir)));
            if (Slide(W(o.center, v1), up, out p))
                SetSize(b, f, fi, oid, x => x.height = OpeningPlacement.HeightFromTop(x.sill, V(t, p, f)));
            if (OpeningPlacement.HasSill(o.type) && Slide(W(o.center, v0), up, out p))
                SetSize(b, f, fi, oid, x =>
                {
                    OpeningPlacement.SillFromBottom(x.sill, x.height, V(t, p, f), out float s, out float h);
                    x.sill = s;
                    x.height = h;
                });
        }

        static float U(Transform t, Vector3 world, Vector2 a, Vector2 dir)
        {
            var l = t.InverseTransformPoint(world);
            return Vector2.Dot(new Vector2(l.x, l.z) - a, dir);
        }

        static float V(Transform t, Vector3 world, FloorData f) => t.InverseTransformPoint(world).y - f.elevation;

        static bool Slide(Vector3 pos, Vector3 direction, out Vector3 moved)
        {
            float size = HandleUtility.GetHandleSize(pos) * 0.08f;
            EditorGUI.BeginChangeCheck();
            moved = Handles.Slider(pos, direction, size, Handles.DotHandleCap, 0f);
            return EditorGUI.EndChangeCheck();
        }

        void SetSize(Building b, FloorData f, int fi, string oid, System.Action<WallOpening> change)
        {
            BeginHandleUndo();
            BuildingEditorUtil.Mutate(b, "Tamaño de abertura", fi, () =>
            {
                var o = b.data.FindOpening(f, oid).o;
                if (o != null) change(o);
            });
            layoutDirty = true;
        }

        static bool IsDeleteKey(KeyCode k) => k == KeyCode.Delete || k == KeyCode.Backspace;

        static bool HasSelection(Building b, FloorData f) =>
            OpeningSelection.Floor(b) == f && OpeningSelection.Find(b).o != null;

        bool DeleteSelection(Building b, FloorData f, int fi)
        {
            if (!HasSelection(b, f)) return false;
            OpeningPlacement.Remove(b, fi, OpeningSelection.openingId);
            OpeningSelection.Clear();
            ResetPress();
            layoutDirty = true;
            return true;
        }

        void HandleKeys(Building b, FloorData f, int fi, Event e)
        {
            switch (e.type)
            {
                case EventType.KeyDown:
                    if (IsDeleteKey(e.keyCode))
                    {
                        // Delete/Backspace never reach Unity while this tool is active: they would delete the Building.
                        DeleteSelection(b, f, fi);
                        swallowDeleteCommand = true;
                        e.Use();
                    }
                    else
                    {
                        swallowDeleteCommand = false;
                        if (e.keyCode == KeyCode.Escape && OpeningSelection.openingId != null)
                        {
                            OpeningSelection.Clear();
                            e.Use();
                        }
                    }
                    break;
                case EventType.KeyUp:
                    if (IsDeleteKey(e.keyCode)) swallowDeleteCommand = false;
                    break;
                // Delete also arrives as a command: take it right after the keypress or when there is an opening
                // selected; otherwise the Building can be deleted normally (e.g. from the Hierarchy menu).
                case EventType.ValidateCommand:
                case EventType.ExecuteCommand:
                    if (e.commandName != "SoftDelete" && e.commandName != "Delete") break;
                    if (swallowDeleteCommand) e.Use();
                    else if (HasSelection(b, f))
                    {
                        if (e.type == EventType.ExecuteCommand) DeleteSelection(b, f, fi);
                        e.Use();
                    }
                    break;
            }
        }

        void Draw(Building b, FloorData f, Dictionary<string, List<OpeningRect>> rects)
        {
            // Wall centrelines for orientation.
            Handles.color = WallColor;
            foreach (var s in f.segments)
                if (OpeningPlacement.Frame(f, s, out var a, out var dir, out float len))
                    Handles.DrawAAPolyLine(2f, BuildingEditorUtil.PlanToWorld(b, a, f.elevation),
                        BuildingEditorUtil.PlanToWorld(b, a + dir * len, f.elevation));

            string selected = OpeningSelection.Floor(b) == f ? OpeningSelection.openingId : null;
            foreach (var kv in rects)
            {
                var s = f.GetSegment(kv.Key);
                if (s == null || !OpeningPlacement.Frame(f, s, out var a, out var dir, out _)) continue;
                foreach (var r in kv.Value)
                {
                    bool sel = r.opening.id == selected;
                    bool hover = r.opening.id == hoverOpeningId;
                    var c = !r.valid ? InvalidColor : sel ? SelectedColor : ValidColor;
                    DrawRect(b, f, a, dir, r.u0, r.u1, r.v0, r.v1, c, sel || hover ? 4f : 2.5f);
                    if (sel && r.opening.type == OpeningType.Door) DrawHinge(b, f, a, dir, r, s.thickness);
                    if (!r.valid)
                        Handles.Label(BuildingEditorUtil.PlanToWorld(b, a + dir * ((r.u0 + r.u1) * 0.5f), f.elevation + r.v1 + 0.15f),
                            OpeningPlacement.IssueText(r.issue), EditorStyles.whiteBoldLabel);
                }
            }

            if (hoverSegmentId != null && pressedOpeningId == null)
            {
                var s = f.GetSegment(hoverSegmentId);
                if (s != null && OpeningPlacement.Frame(f, s, out var a, out var dir, out _))
                {
                    var p = ActivePreset;
                    DrawRect(b, f, a, dir, hoverCenter - p.width * 0.5f, hoverCenter + p.width * 0.5f, p.sill, p.sill + p.height,
                        hoverFits ? FitsColor : InvalidColor, 3f);
                }
            }
        }

        // Hinge edge of the selected door: a post through the wall thickness plus a dot at its foot.
        static void DrawHinge(Building b, FloorData f, Vector2 a, Vector2 dir, OpeningRect r, float thickness)
        {
            float u = OpeningPlacement.HingeU(r.opening);
            var n = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);
            var p = a + dir * u;
            Handles.color = SelectedColor;
            Handles.DrawAAPolyLine(6f, BuildingEditorUtil.PlanToWorld(b, p, f.elevation + r.v0),
                BuildingEditorUtil.PlanToWorld(b, p, f.elevation + r.v1));
            Handles.DrawAAPolyLine(4f, BuildingEditorUtil.PlanToWorld(b, p - n, f.elevation + r.v0),
                BuildingEditorUtil.PlanToWorld(b, p + n, f.elevation + r.v0));
            var foot = BuildingEditorUtil.PlanToWorld(b, p, f.elevation + r.v0);
            Handles.DrawSolidDisc(foot, b.transform.up, HandleUtility.GetHandleSize(foot) * 0.05f);
            Handles.Label(BuildingEditorUtil.PlanToWorld(b, p, f.elevation + r.v1 + 0.15f), "Bisagra", EditorStyles.whiteMiniLabel);
        }

        static void DrawRect(Building b, FloorData f, Vector2 a, Vector2 dir, float u0, float u1, float v0, float v1, Color c, float width)
        {
            Vector3 W(float u, float v) => BuildingEditorUtil.PlanToWorld(b, a + dir * u, f.elevation + v);
            var p0 = W(u0, v0); var p1 = W(u1, v0); var p2 = W(u1, v1); var p3 = W(u0, v1);
            Handles.color = new Color(c.r, c.g, c.b, 0.15f);
            Handles.DrawAAConvexPolygon(p0, p1, p2, p3);
            Handles.color = c;
            Handles.DrawAAPolyLine(width, p0, p1, p2, p3, p0);
        }
    }
}
