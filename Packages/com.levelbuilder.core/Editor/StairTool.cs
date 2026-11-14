using System.Collections.Generic;
using System.Text;
using LevelBuilder.Geometry;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Spanish texts for stair validation issues, shared by the stair tool and the inspector.</summary>
    static class StairIssueText
    {
        public const string NoFloorAboveHelp = "Añade una planta encima para crear una escalera";

        public static string Of(StairIssue issue)
        {
            switch (issue)
            {
                case StairIssue.RouteInvalid: return "Ruta no válida (giro demasiado cerrado o esquinas demasiado juntas)";
                case StairIssue.NoFloorAbove: return "No hay planta encima";
                case StairIssue.DepartureBlocked: return "La salida está bloqueada";
                case StairIssue.ArrivalBlocked: return "La llegada está bloqueada (pared delante)";
                case StairIssue.Collision: return "Los escalones atraviesan una pared";
                case StairIssue.HoleCrossesWall: return "El hueco del techo cruza una pared";
                default: return issue.ToString();
            }
        }

        /// <summary>One issue per line.</summary>
        public static string Join(IEnumerable<StairIssue> issues)
        {
            var sb = new StringBuilder();
            foreach (var i in issues)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Of(i));
            }
            return sb.ToString();
        }
    }

    /// <summary>Stair helpers shared by the stair tool and the inspector.</summary>
    static class StairEditorUtil
    {
        /// <summary>
        /// The renderer's validation of s (computed at its last regeneration); recomputed here when the renderer has
        /// none yet (e.g. right after a domain reload, before the first flush).
        /// </summary>
        public static StairValidation Validation(Building b, StairData s)
        {
            var r = b != null ? b.GetComponent<BuildingRenderer>() : null;
            if (r != null && s != null && s.id != null && r.StairValidations.TryGetValue(s.id, out var v) && v != null) return v;
            return StairValidator.Validate(b != null ? b.data : null, s);
        }

        public static bool PointInPolygon(Vector2 p, IList<Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var c = poly[j];
                if ((a.y > p.y) != (c.y > p.y) && p.x < (c.x - a.x) * (p.y - a.y) / (c.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>Width from the plan position of the width handle: twice its distance to the start centre along side.</summary>
        public static float WidthFromHandle(Vector2 handle, Vector2 center, Vector2 side) =>
            2f * Mathf.Abs(Vector2.Dot(handle - center, side.normalized));
    }

    /// <summary>
    /// Scene view tool to draw and edit stairs from the active floor to the one above.
    /// Drawing: click = Corner point, Shift+click = Smooth (curve) point, Backspace = remove the last point,
    /// Enter / double click = finish (one Undo step "Crear escalera"), Esc = cancel. Live preview of the pieces
    /// (green valid / red invalid, with the reasons) and the real run length. Points snap to the wall grid
    /// (Ctrl/Cmd: no snap).
    /// Editing: click a stair to select it; drag its points to move them (one Undo step per drag); click a point to
    /// select it (Delete removes it while at least 2 remain); Delete with no point selected deletes the stair;
    /// side handle at the start: width. Never deletes the Building. Without a floor above the active one the tool
    /// only shows a message and ignores clicks on empty space.
    /// </summary>
    [EditorTool("Escaleras", typeof(Building))]
    public class StairTool : EditorTool
    {
        const float PickPixels = 10f;
        const float DragPixels = 4f;

        static readonly int ToolHint = "LevelBuilder.StairTool".GetHashCode();
        static readonly int PointHint = "LevelBuilder.StairTool.Point".GetHashCode();
        static readonly Color StairColor = new Color(0.3f, 0.75f, 1f, 1f);
        static readonly Color ValidColor = new Color(0.2f, 1f, 0.4f, 1f);
        static readonly Color InvalidColor = new Color(1f, 0.25f, 0.2f, 1f);
        static readonly Color SelectedColor = new Color(1f, 0.85f, 0.1f, 1f);
        static readonly Color PointColor = new Color(1f, 1f, 1f, 0.9f);

        StairDrawSession session;
        Building sessionBuilding;

        // Cursor (plan, snapped), updated every event.
        bool hasCursor;
        Vector2 cursor;
        bool cursorSmooth;

        // Live preview: validated at most once per change of the route / hover point / data.
        readonly List<StairPoint> previewKey = new List<StairPoint>();
        StairData previewStair;
        StairValidation previewValidation;
        bool previewDirty = true;
        Building observed;

        // Point handle press: click selects the point, drag moves it in one Undo step.
        int pressedPoint = -1;
        string pressedStairId;
        Vector2 pressMouse;
        bool pressDragged;
        int pressUndoGroup = -1;

        // Width handle: one Undo group per drag, collapsed once released.
        int widthUndoGroup = -1;

        bool swallowDeleteCommand;

        public override GUIContent toolbarIcon =>
            new GUIContent(EditorGUIUtility.IconContent("TransformTool").image,
                "Escaleras (Level Builder): clic añade puntos, Shift+clic punto de curva, Enter / doble clic termina");

        public override void OnActivated()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            previewDirty = true;
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            Observe(null);
            session = null;
            ResetPress();
            FinishWidthUndo(true);
        }

        void OnUndoRedo()
        {
            // The session holds no data objects (only points), so drawing survives Undo/Redo.
            ResetPress();
            previewDirty = true;
            StairSelection.Validate();
            SceneView.RepaintAll();
        }

        void ResetPress()
        {
            pressedPoint = -1;
            pressedStairId = null;
            pressDragged = false;
            pressUndoGroup = -1;
        }

        void Observe(Building b)
        {
            if (observed == b) return;
            if (observed != null) observed.Changed -= OnBuildingChanged;
            observed = b;
            if (observed != null) observed.Changed += OnBuildingChanged;
            previewDirty = true;
        }

        void OnBuildingChanged() => previewDirty = true;

        bool Drawing => session != null && session.IsDrawing;

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView view)) return;
            var b = target as Building;
            int fi = BuildingEditorUtil.ActiveFloorIndex(b);
            if (fi < 0) return;
            Observe(b);
            var data = b.data;
            var f = data.floors[fi];
            bool hasAbove = fi + 1 < data.floors.Count;
            var e = Event.current;

            if (session == null || sessionBuilding != b || session.FromFloorIndex != fi)
            {
                session = new StairDrawSession(fi);
                sessionBuilding = b;
            }

            FinishWidthUndo(false);
            if (e.type == EventType.Layout) StairSelection.Validate();

            int id = GUIUtility.GetControlID(ToolHint, FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            UpdateCursor(b, f, e);

            // Handles before the default control so they win the nearest-control test.
            var selected = Selected(b, f);
            if (selected != null && !Drawing)
            {
                DoPointHandles(b, f, fi, selected, e);
                selected = Selected(b, f); // a handle may have changed it
                if (selected != null && pressedPoint < 0) DoWidthHandle(b, f, fi, selected);
            }

            HandleKeys(b, f, fi, hasAbove, e);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && HandleUtility.nearestControl == id && !e.alt)
                    {
                        GUIUtility.hotControl = id;
                        swallowDeleteCommand = false;
                        OnClick(b, f, fi, hasAbove, e);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id && e.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    DrawStairs(b, f);
                    DrawCursorAndPreview(b, data, f, hasAbove);
                    break;
            }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint();
        }

        static StairData Selected(Building b, FloorData f)
        {
            var s = StairSelection.Find(b);
            return s != null && s.fromFloorId == f.id ? s : null;
        }

        static Vector2 Snap(Event e, Vector2 p) => e.control || e.command ? p : GridSnap.Snap(p, WallTool.Grid);

        void UpdateCursor(Building b, FloorData f, Event e)
        {
            hasCursor = BuildingEditorUtil.GuiToPlan(b, e.mousePosition, f.elevation, out var p);
            if (hasCursor) cursor = Snap(e, p);
            cursorSmooth = e.shift;
        }

        void OnClick(Building b, FloorData f, int fi, bool hasAbove, Event e)
        {
            if (Drawing)
            {
                if (hasCursor) session.Click(cursor, e.shift);
                if (e.clickCount >= 2) TryFinish(b, fi);
                return;
            }

            string picked = PickStair(b, f, e.mousePosition);
            if (picked != null)
            {
                StairSelection.Select(b, picked);
                WallSelection.Clear();
                OpeningSelection.Clear();
                return;
            }
            StairSelection.Clear();
            if (!hasAbove || !hasCursor) return; // no floor above: only the message, clicks are ignored
            WallSelection.Clear();
            OpeningSelection.Clear();
            session.Click(cursor, e.shift);
        }

        void TryFinish(Building b, int fi)
        {
            if (!session.CanFinish || fi + 1 >= b.data.floors.Count) return;
            StairData created = null;
            Undo.IncrementCurrentGroup();
            BuildingEditorUtil.Mutate(b, "Crear escalera", fi, () => created = session.Finish(b.data));
            if (created != null) StairSelection.Select(b, created.id);
            previewDirty = true;
        }

        // Treads/landings are tested at their own height (what the user sees), the route line in screen pixels.
        static string PickStair(Building b, FloorData f, Vector2 mouse)
        {
            var stairs = b.data.stairs;
            for (int k = stairs.Count - 1; k >= 0; k--)
            {
                var s = stairs[k];
                if (s == null || s.fromFloorId != f.id) continue;
                var v = StairEditorUtil.Validation(b, s);
                if (v.layout != null && v.layout.ok)
                    foreach (var piece in v.layout.pieces)
                        if (BuildingEditorUtil.GuiToPlan(b, mouse, f.elevation + piece.height, out var p)
                            && StairEditorUtil.PointInPolygon(p, piece.polygon)) return s.id;
                for (int i = 0; i + 1 < s.route.Count; i++)
                {
                    var ga = HandleUtility.WorldToGUIPoint(BuildingEditorUtil.PlanToWorld(b, s.route[i].position, f.elevation));
                    var gb = HandleUtility.WorldToGUIPoint(BuildingEditorUtil.PlanToWorld(b, s.route[i + 1].position, f.elevation));
                    if (HandleUtility.DistancePointLine(mouse, ga, gb) <= PickPixels) return s.id;
                }
            }
            return null;
        }

        void DoPointHandles(Building b, FloorData f, int fi, StairData s, Event e)
        {
            // Copy: the control-id order must stay stable even if a mutation edits the route.
            var route = s.route.ToArray();
            string sid = s.id;
            for (int i = 0; i < route.Length; i++)
            {
                var world = BuildingEditorUtil.PlanToWorld(b, route[i].position, f.elevation);
                bool sel = StairSelection.pointIndex == i;
                float size = HandleUtility.GetHandleSize(world) * (sel ? 0.09f : 0.07f);
                Handles.color = sel ? SelectedColor : PointColor;
                Handles.CapFunction cap = route[i].kind == StairPointKind.Smooth ? Handles.SphereHandleCap : Handles.CubeHandleCap;

                int handleId = GUIUtility.GetControlID(PointHint, FocusType.Passive);
                var type = e.type;
                bool down = type == EventType.MouseDown && e.button == 0 && HandleUtility.nearestControl == handleId;
                bool up = type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl == handleId;
                Vector2 mouse = e.mousePosition;

                EditorGUI.BeginChangeCheck();
                Handles.FreeMoveHandle(handleId, world, size, Vector3.zero, cap);
                bool changed = EditorGUI.EndChangeCheck();

                if (down && GUIUtility.hotControl == handleId)
                {
                    swallowDeleteCommand = false;
                    StairSelection.Select(b, sid, i);
                    pressedPoint = i;
                    pressedStairId = sid;
                    pressMouse = mouse;
                    pressDragged = false;
                    pressUndoGroup = -1;
                }

                if (changed && pressedPoint == i && pressedStairId == sid)
                {
                    if (!pressDragged && (mouse - pressMouse).magnitude > DragPixels)
                    {
                        pressDragged = true;
                        Undo.IncrementCurrentGroup();
                        Undo.SetCurrentGroupName("Mover punto de escalera");
                        pressUndoGroup = Undo.GetCurrentGroup();
                    }
                    if (pressDragged && BuildingEditorUtil.GuiToPlan(b, mouse, f.elevation, out var p))
                    {
                        p = Snap(e, p);
                        int idx = i;
                        if ((p - route[i].position).sqrMagnitude > 1e-8f)
                            BuildingEditorUtil.Mutate(b, "Mover punto de escalera", fi, () => b.data.MoveStairPoint(sid, idx, p));
                    }
                }

                if (up && pressedPoint == i)
                {
                    // Every move of the drag recorded into one group: the whole drag is one Undo step.
                    if (pressDragged && pressUndoGroup >= 0) Undo.CollapseUndoOperations(pressUndoGroup);
                    ResetPress();
                }
            }
        }

        static bool StartFrame(Building b, StairData s, out Vector2 center, out Vector2 side)
        {
            center = default; side = default;
            if (s.route.Count < 2) return false;
            var v = StairEditorUtil.Validation(b, s);
            if (v.layout != null && v.layout.ok && (v.layout.bottomA - v.layout.bottomB).sqrMagnitude > 1e-8f)
            {
                center = (v.layout.bottomA + v.layout.bottomB) * 0.5f;
                side = (v.layout.bottomA - v.layout.bottomB).normalized; // bottomA is on the left looking up
                return true;
            }
            var d = s.route[1].position - s.route[0].position;
            if (d.sqrMagnitude < 1e-8f) return false;
            d.Normalize();
            center = s.route[0].position;
            side = new Vector2(-d.y, d.x);
            return true;
        }

        void DoWidthHandle(Building b, FloorData f, int fi, StairData s)
        {
            if (!StartFrame(b, s, out var center, out var side)) return;
            var t = b.transform;
            var pos = BuildingEditorUtil.PlanToWorld(b, center + side * (s.width * 0.5f), f.elevation);
            var dir = t.TransformDirection(new Vector3(side.x, 0f, side.y)).normalized;
            float size = HandleUtility.GetHandleSize(pos) * 0.08f;
            Handles.color = SelectedColor;
            EditorGUI.BeginChangeCheck();
            var moved = Handles.Slider(pos, dir, size, Handles.DotHandleCap, 0f);
            if (!EditorGUI.EndChangeCheck()) return;

            var l = t.InverseTransformPoint(moved);
            float w = StairEditorUtil.WidthFromHandle(new Vector2(l.x, l.z), center, side);
            if (float.IsNaN(w) || float.IsInfinity(w)) return;
            if (widthUndoGroup < 0)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Ancho de escalera");
                widthUndoGroup = Undo.GetCurrentGroup();
            }
            string sid = s.id;
            BuildingEditorUtil.Mutate(b, "Ancho de escalera", fi, () =>
            {
                var st = b.data.FindStair(sid);
                if (st == null) return;
                st.width = w;
                BuildingData.ClampStair(st);
            });
        }

        void FinishWidthUndo(bool force)
        {
            if (widthUndoGroup < 0 || (!force && GUIUtility.hotControl != 0)) return;
            Undo.CollapseUndoOperations(widthUndoGroup);
            widthUndoGroup = -1;
        }

        static bool IsDeleteKey(KeyCode k) => k == KeyCode.Delete || k == KeyCode.Backspace;

        static bool HasSelection(Building b, FloorData f) => Selected(b, f) != null;

        bool DeleteSelection(Building b, FloorData f, int fi)
        {
            var s = Selected(b, f);
            if (s == null) return false;
            string sid = s.id;
            int point = StairSelection.pointIndex;
            if (point >= 0 && point < s.route.Count)
            {
                // A point is selected: remove it only while the route keeps at least 2 points (never the stair).
                if (s.route.Count > 2)
                {
                    BuildingEditorUtil.Mutate(b, "Quitar punto de escalera", fi, () => b.data.RemoveStairPoint(sid, point));
                    StairSelection.Select(b, sid, -1);
                }
            }
            else
            {
                BuildingEditorUtil.Mutate(b, "Borrar escalera", fi, () => b.data.RemoveStair(sid));
                StairSelection.Clear();
            }
            ResetPress();
            return true;
        }

        void HandleKeys(Building b, FloorData f, int fi, bool hasAbove, Event e)
        {
            switch (e.type)
            {
                case EventType.KeyDown:
                    if (IsDeleteKey(e.keyCode))
                    {
                        // Delete/Backspace never reach Unity while this tool is active: they would delete the Building.
                        if (Drawing)
                        {
                            if (e.keyCode == KeyCode.Backspace) session.RemoveLastPoint();
                        }
                        else DeleteSelection(b, f, fi);
                        swallowDeleteCommand = true;
                        e.Use();
                    }
                    else
                    {
                        swallowDeleteCommand = false;
                        if (e.keyCode == KeyCode.Escape)
                        {
                            if (Drawing) { session.Cancel(); e.Use(); }
                            else if (StairSelection.stairId != null)
                            {
                                if (StairSelection.pointIndex >= 0) StairSelection.Select(StairSelection.building, StairSelection.stairId, -1);
                                else StairSelection.Clear();
                                e.Use();
                            }
                        }
                        else if (Drawing && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                        {
                            TryFinish(b, fi);
                            e.Use();
                        }
                    }
                    break;
                case EventType.KeyUp:
                    if (IsDeleteKey(e.keyCode)) swallowDeleteCommand = false;
                    break;
                // Delete also arrives as a command: take it while drawing, right after the keypress or when there is
                // a stair selected; otherwise the Building can be deleted normally (e.g. from the Hierarchy menu).
                case EventType.ValidateCommand:
                case EventType.ExecuteCommand:
                    if (e.commandName != "SoftDelete" && e.commandName != "Delete") break;
                    if (Drawing || swallowDeleteCommand) e.Use();
                    else if (HasSelection(b, f))
                    {
                        if (e.type == EventType.ExecuteCommand) DeleteSelection(b, f, fi);
                        e.Use();
                    }
                    break;
            }
        }

        void DrawStairs(Building b, FloorData f)
        {
            string selectedId = Selected(b, f)?.id;
            foreach (var s in b.data.stairs)
            {
                if (s == null || s.fromFloorId != f.id) continue;
                var v = StairEditorUtil.Validation(b, s);
                bool sel = s.id == selectedId;
                var c = !v.Valid ? InvalidColor : sel ? SelectedColor : StairColor;
                DrawStair(b, f, s.route, v, c, sel ? 4f : 2.5f);
                if (!v.Valid && s.route.Count > 0)
                    Handles.Label(BuildingEditorUtil.PlanToWorld(b, s.route[0].position, f.elevation + 0.3f),
                        StairIssueText.Join(v.issues), EditorStyles.whiteBoldLabel);
            }
        }

        static void DrawStair(Building b, FloorData f, IList<StairPoint> route, StairValidation v, Color c, float width)
        {
            if (v.layout != null && v.layout.ok)
                foreach (var piece in v.layout.pieces)
                {
                    var pts = new Vector3[piece.polygon.Count + 1];
                    for (int i = 0; i < piece.polygon.Count; i++)
                        pts[i] = BuildingEditorUtil.PlanToWorld(b, piece.polygon[i], f.elevation + piece.height);
                    pts[pts.Length - 1] = pts[0];
                    Handles.color = c;
                    Handles.DrawAAPolyLine(width, pts);
                }
            if (route.Count < 2) return;
            var line = new Vector3[route.Count];
            for (int i = 0; i < route.Count; i++) line[i] = BuildingEditorUtil.PlanToWorld(b, route[i].position, f.elevation);
            Handles.color = new Color(c.r, c.g, c.b, 0.6f);
            Handles.DrawAAPolyLine(2f, line);
        }

        void DrawCursorAndPreview(Building b, BuildingData data, FloorData f, bool hasAbove)
        {
            if (!hasCursor) return;
            var c = BuildingEditorUtil.PlanToWorld(b, cursor, f.elevation);
            if (!hasAbove && !Drawing)
            {
                Handles.Label(c, StairIssueText.NoFloorAboveHelp, EditorStyles.whiteBoldLabel);
                return;
            }
            var hover = new StairPoint { position = cursor, kind = cursorSmooth ? StairPointKind.Smooth : StairPointKind.Corner };
            if (!Drawing)
            {
                Handles.color = ValidColor;
                Handles.DrawSolidDisc(c, b.transform.up, HandleUtility.GetHandleSize(c) * 0.05f);
                return;
            }

            UpdatePreview(data, hover);
            if (previewStair == null || previewValidation == null) return;
            var color = previewValidation.Valid ? ValidColor : InvalidColor;
            DrawStair(b, f, previewStair.route, previewValidation, color, 3f);
            foreach (var p in session.Points)
            {
                var w = BuildingEditorUtil.PlanToWorld(b, p.position, f.elevation);
                Handles.color = color;
                Handles.DrawSolidDisc(w, b.transform.up, HandleUtility.GetHandleSize(w) * (p.kind == StairPointKind.Smooth ? 0.04f : 0.06f));
            }
            Handles.color = color;
            Handles.DrawSolidDisc(c, b.transform.up, HandleUtility.GetHandleSize(c) * 0.05f);

            var L = previewValidation.layout;
            string text = "Longitud: " + (L != null ? L.runLength : 0f).ToString("0.00") + " m";
            if (!previewValidation.Valid) text = StairIssueText.Join(previewValidation.issues) + "\n" + text;
            Handles.Label(BuildingEditorUtil.PlanToWorld(b, cursor, f.elevation + 0.3f), text, EditorStyles.whiteBoldLabel);
        }

        // Validates the preview only when the points, the hover point or the data changed.
        void UpdatePreview(BuildingData data, StairPoint hover)
        {
            int n = session.Points.Count;
            bool same = !previewDirty && previewStair != null && previewKey.Count == n + 1;
            for (int i = 0; same && i < n; i++) same = Equal(previewKey[i], session.Points[i]);
            if (same) same = Equal(previewKey[n], hover);
            if (same) return;

            previewKey.Clear();
            previewKey.AddRange(session.Points);
            previewKey.Add(hover);
            previewStair = session.Preview(data, hover);
            previewValidation = previewStair != null ? StairValidator.Validate(data, previewStair) : null;
            previewDirty = false;
        }

        static bool Equal(StairPoint a, StairPoint c) => a.kind == c.kind && a.position == c.position;
    }
}
