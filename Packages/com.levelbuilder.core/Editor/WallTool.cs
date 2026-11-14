using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>
    /// Scene view tool to draw and edit the walls of the active floor.
    /// Click: add point (double click / Esc / Enter finishes, click on the first point closes).
    /// Ctrl: no grid snap. Shift while drawing: 15 degree angles. Shift+click (not drawing): select segment.
    /// Not drawing: double click on a segment splits it; drag a node to move it (linked columns move together);
    /// click a node (no drag) to select it and start a wall from it; Delete removes the selected node / segment.
    /// </summary>
    [EditorTool("Paredes", typeof(Building))]
    public class WallTool : EditorTool
    {
        public const float Grid = 0.5f;
        public const float AngleStep = 15f;
        const float PickPixels = 10f;
        const float DragPixels = 4f;
        const double DeleteSwallowSeconds = 0.5;

        static readonly int ToolHint = "LevelBuilder.WallTool".GetHashCode();
        static readonly int NodeHint = "LevelBuilder.WallTool.Node".GetHashCode();
        static readonly Color WallColor = new Color(1f, 1f, 1f, 0.9f);
        static readonly Color SelectedColor = new Color(1f, 0.85f, 0.1f, 1f);
        static readonly Color LinkedColor = new Color(0.3f, 0.8f, 1f, 1f);
        static readonly Color PreviewColor = new Color(0.2f, 1f, 0.4f, 1f);

        WallDrawSession session;
        Building sessionBuilding;

        // Node handle press state: click without drag starts a wall, drag moves the node in one Undo step.
        string pressedNodeId;
        Vector2 pressMouse;
        bool pressDragged;
        int pressUndoGroup = -1;

        double lastKeyDeleteTime = -1;

        public override GUIContent toolbarIcon =>
            new GUIContent(EditorGUIUtility.IconContent("EditCollider").image,
                "Paredes (Level Builder): clic dibuja, doble clic en pared la divide, arrastra nodos para moverlos");

        public override void OnActivated()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            session = null;
            pressedNodeId = null;
        }

        void OnUndoRedo()
        {
            // Undo/Redo may deserialize new data instances: drop the session; it is rebuilt from the building.
            session = null;
            pressedNodeId = null;
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView view)) return;
            var b = target as Building;
            int fi = BuildingEditorUtil.ActiveFloorIndex(b);
            if (fi < 0) return;
            var data = b.data;
            var f = data.floors[fi];

            if (session == null || sessionBuilding != b || session.FloorIndex != fi)
            {
                session = new WallDrawSession(data, fi);
                sessionBuilding = b;
            }
            session.Rebind(data); // never hold a data object across events

            var e = Event.current;
            int id = GUIUtility.GetControlID(ToolHint, FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            bool hasCursor = TryCursor(b, f, e, out Vector2 cursor);

            DrawSegments(b, f);
            if (!session.IsDrawing) DoNodeHandles(b, data, f, fi, e);

            HandleKeys(b, data, f, fi, e);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && hasCursor && HandleUtility.nearestControl == id && !e.alt)
                    {
                        GUIUtility.hotControl = id;
                        OnClick(b, data, f, fi, e, cursor);
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
            }

            if (hasCursor && e.type == EventType.Repaint) DrawPreview(b, f, cursor);
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint();
        }

        void OnClick(Building b, BuildingData data, FloorData f, int fi, Event e, Vector2 cursor)
        {
            if (e.clickCount >= 2)
            {
                // Double click on a segment with no chain in progress (the first click only started one): split.
                if (session.PointCount <= 1 && PickSegment(b, f, e.mousePosition, out var seg))
                {
                    session.Finish();
                    TrySplit(b, data, f, fi, seg, e);
                }
                else session.Finish();
                return;
            }
            if (e.shift && !session.IsDrawing && PickSegment(b, f, e.mousePosition, out var picked))
            {
                WallSelection.SelectSegment(b, f, picked.id);
                return;
            }
            WallSelection.Clear();
            if (session.WouldAddSegment(cursor))
                BuildingEditorUtil.Mutate(b, "Dibujar pared", fi, () => session.Click(cursor));
            else
                session.Click(cursor); // first point / repeated point: no data change, no Undo entry
        }

        bool TryCursor(Building b, FloorData f, Event e, out Vector2 p)
        {
            if (!BuildingEditorUtil.GuiToPlan(b, e.mousePosition, f.elevation, out p)) return false;
            // Existing nodes win over the grid (also with Ctrl) so chains can start/close on off-grid nodes.
            if (NearNode(b, f, e.mousePosition, out var node)) p = node.position;
            else if (!(e.control || e.command)) p = GridSnap.Snap(p, Grid);
            if (e.shift && session.IsDrawing)
                p = GridSnap.SnapAngle(session.LastPoint.Value, p, AngleStep);
            return true;
        }

        static bool NearNode(Building b, FloorData f, Vector2 mouse, out WallNode best)
        {
            best = null;
            float bestD = PickPixels;
            foreach (var n in f.nodes)
            {
                var gui = HandleUtility.WorldToGUIPoint(BuildingEditorUtil.PlanToWorld(b, n.position, f.elevation));
                float d = (gui - mouse).magnitude;
                if (d <= bestD) { bestD = d; best = n; }
            }
            return best != null;
        }

        static bool PickSegment(Building b, FloorData f, Vector2 mouse, out WallSegment best)
        {
            best = null;
            float bestD = PickPixels;
            foreach (var s in f.segments)
            {
                var na = f.GetNode(s.startNodeId);
                var nb = f.GetNode(s.endNodeId);
                if (na == null || nb == null) continue;
                var ga = HandleUtility.WorldToGUIPoint(BuildingEditorUtil.PlanToWorld(b, na.position, f.elevation));
                var gb = HandleUtility.WorldToGUIPoint(BuildingEditorUtil.PlanToWorld(b, nb.position, f.elevation));
                float d = HandleUtility.DistancePointLine(mouse, ga, gb);
                if (d <= bestD) { bestD = d; best = s; }
            }
            return best != null;
        }

        static void TrySplit(Building b, BuildingData data, FloorData f, int fi, WallSegment seg, Event e)
        {
            // Raw plane point (grid snapped unless Ctrl), not the node-snapped cursor; refuse near endpoints.
            if (!BuildingEditorUtil.GuiToPlan(b, e.mousePosition, f.elevation, out var p)) return;
            if (!(e.control || e.command)) p = GridSnap.Snap(p, Grid);
            var a = f.GetNode(seg.startNodeId).position;
            var c = f.GetNode(seg.endNodeId).position;
            Geometry.Seg2D.DistanceToSegment(p, a, c, out float t);
            var at = Vector2.Lerp(a, c, t);
            if ((at - a).magnitude <= BuildingData.MergeTolerance || (at - c).magnitude <= BuildingData.MergeTolerance)
                return;
            WallNode created = null;
            BuildingEditorUtil.Mutate(b, "Dividir pared", fi, () => created = data.SplitSegment(f, seg.id, at));
            if (created != null) WallSelection.SelectNode(b, f, created.id);
        }

        void HandleKeys(Building b, BuildingData data, FloorData f, int fi, Event e)
        {
            if (e.type == EventType.KeyDown)
            {
                if (session.IsDrawing && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    session.Finish();
                    e.Use();
                }
                else if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && DeleteSelection(b, data, f, fi))
                {
                    lastKeyDeleteTime = EditorApplication.timeSinceStartup;
                    e.Use();
                }
            }
            // Delete also arrives as a command. Only take it when there is a wall selection to delete (or the
            // KeyDown just deleted one), so otherwise the Building GameObject can still be deleted normally.
            else if ((e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand)
                     && (e.commandName == "SoftDelete" || e.commandName == "Delete"))
            {
                bool justDeleted = EditorApplication.timeSinceStartup - lastKeyDeleteTime < DeleteSwallowSeconds;
                if (HasSelection(b, f))
                {
                    if (e.type == EventType.ExecuteCommand) DeleteSelection(b, data, f, fi);
                    e.Use();
                }
                else if (justDeleted) e.Use();
            }
        }

        static bool HasSelection(Building b, FloorData f) =>
            WallSelection.Floor(b) == f && (WallSelection.Node(b) != null || WallSelection.Segment(b) != null);

        bool DeleteSelection(Building b, BuildingData data, FloorData f, int fi)
        {
            if (!HasSelection(b, f)) return false;
            var node = WallSelection.Node(b);
            var seg = WallSelection.Segment(b);
            if (node != null)
                BuildingEditorUtil.Mutate(b, "Eliminar nodo", fi, () => data.RemoveNode(f, node.id));
            else
                BuildingEditorUtil.Mutate(b, "Eliminar pared", fi, () => data.RemoveSegment(f, seg.id));
            WallSelection.Clear();
            session.Finish();
            return true;
        }

        static void DrawSegments(Building b, FloorData f)
        {
            if (Event.current.type != EventType.Repaint) return;
            var selected = WallSelection.Segment(b);
            foreach (var s in f.segments)
            {
                var na = f.GetNode(s.startNodeId);
                var nb = f.GetNode(s.endNodeId);
                if (na == null || nb == null) continue;
                Handles.color = s == selected ? SelectedColor : WallColor;
                Handles.DrawAAPolyLine(s == selected ? 6f : 3f,
                    BuildingEditorUtil.PlanToWorld(b, na.position, f.elevation),
                    BuildingEditorUtil.PlanToWorld(b, nb.position, f.elevation));
            }
        }

        void DoNodeHandles(Building b, BuildingData data, FloorData f, int fi, Event e)
        {
            var linked = BuildingEditorUtil.LinkedNodeIds(data, f);
            var selectedNode = WallSelection.Node(b);
            // Iterate a copy: handles must keep a stable control-id order even if a mutation edits the list.
            foreach (var n in f.nodes.ToArray())
            {
                bool isLinked = linked.Contains(n.id);
                var world = BuildingEditorUtil.PlanToWorld(b, n.position, f.elevation);
                float size = HandleUtility.GetHandleSize(world) * (isLinked ? 0.09f : 0.07f);
                Handles.color = n == selectedNode ? SelectedColor : isLinked ? LinkedColor : WallColor;
                Handles.CapFunction cap = isLinked ? Handles.CubeHandleCap : Handles.SphereHandleCap;

                int handleId = GUIUtility.GetControlID(NodeHint, FocusType.Passive);
                var type = e.type;
                bool down = type == EventType.MouseDown && e.button == 0 && HandleUtility.nearestControl == handleId;
                bool up = type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl == handleId;
                Vector2 mouse = e.mousePosition;

                EditorGUI.BeginChangeCheck();
                Handles.FreeMoveHandle(handleId, world, size, Vector3.zero, cap);
                bool changed = EditorGUI.EndChangeCheck();

                if (down && GUIUtility.hotControl == handleId)
                {
                    WallSelection.SelectNode(b, f, n.id);
                    pressedNodeId = n.id;
                    pressMouse = mouse;
                    pressDragged = false;
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName("Mover nodo");
                    pressUndoGroup = Undo.GetCurrentGroup();
                }

                if (changed && pressedNodeId == n.id)
                {
                    if ((mouse - pressMouse).magnitude > DragPixels) pressDragged = true;
                    if (pressDragged && BuildingEditorUtil.GuiToPlan(b, mouse, f.elevation, out var p))
                    {
                        if (!(e.control || e.command)) p = GridSnap.Snap(p, Grid);
                        if ((p - n.position).sqrMagnitude > 1e-8f)
                            BuildingEditorUtil.Mutate(b, "Mover nodo", isLinked ? -1 : fi, () => data.MoveNode(f, n.id, p));
                    }
                }

                if (up && pressedNodeId == n.id)
                {
                    if (pressDragged) Undo.CollapseUndoOperations(pressUndoGroup); // whole drag = one Undo step
                    else session.Click(n.position); // click without drag: start a wall at this node
                    pressedNodeId = null;
                    pressUndoGroup = -1;
                }
            }
        }

        void DrawPreview(Building b, FloorData f, Vector2 cursor)
        {
            var c = BuildingEditorUtil.PlanToWorld(b, cursor, f.elevation);
            float size = HandleUtility.GetHandleSize(c) * 0.05f;
            bool closing = session.WouldClose(cursor);
            Handles.color = closing ? SelectedColor : PreviewColor;
            Handles.DrawSolidDisc(c, b.transform.up, size);
            if (!session.IsDrawing) return;
            var last = session.LastPoint.Value;
            var a = BuildingEditorUtil.PlanToWorld(b, last, f.elevation);
            Handles.DrawAAPolyLine(4f, a, c);
            Handles.Label((a + c) * 0.5f, (cursor - last).magnitude.ToString("0.00") + " m");
        }
    }
}
