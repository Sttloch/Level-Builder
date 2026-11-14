using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>
    /// Scene view tool to draw and edit the walls of the active floor.
    /// Click: add point (double click / Esc / Enter finishes, click on the first point closes).
    /// Ctrl: no grid snap. Shift while drawing: 15 degree angles. Shift+click (not drawing): select segment.
    /// Alt+click on a segment: split it. Not drawing: drag node handles to move them (linked columns move together);
    /// Delete removes the selected node / segment.
    /// </summary>
    [EditorTool("Paredes", typeof(Building))]
    public class WallTool : EditorTool
    {
        public const float Grid = 0.5f;
        public const float AngleStep = 15f;
        const float PickPixels = 10f;

        static readonly int ToolHint = "LevelBuilder.WallTool".GetHashCode();
        static readonly Color WallColor = new Color(1f, 1f, 1f, 0.9f);
        static readonly Color SelectedColor = new Color(1f, 0.85f, 0.1f, 1f);
        static readonly Color LinkedColor = new Color(0.3f, 0.8f, 1f, 1f);
        static readonly Color PreviewColor = new Color(0.2f, 1f, 0.4f, 1f);

        WallDrawSession session;
        Building sessionBuilding;

        public override GUIContent toolbarIcon =>
            new GUIContent(EditorGUIUtility.IconContent("EditCollider").image, "Paredes (Level Builder)");

        public override void OnActivated()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            session = null;
        }

        void OnUndoRedo()
        {
            session?.Finish();
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

            var e = Event.current;
            int id = GUIUtility.GetControlID(ToolHint, FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            bool hasCursor = TryCursor(b, f, e, out Vector2 cursor);

            DrawSegments(b, f);
            if (!session.IsDrawing) DoNodeHandles(b, data, f, fi, e);

            HandleKeys(b, data, f, fi, e);

            if (e.type == EventType.MouseDown && e.button == 0 && hasCursor && HandleUtility.nearestControl == id)
            {
                if (e.alt) TrySplit(b, data, f, fi, e.mousePosition, cursor);
                else if (e.clickCount >= 2 && session.IsDrawing) session.Finish();
                else if (e.shift && !session.IsDrawing && PickSegment(b, f, e.mousePosition, out var seg))
                    WallSelection.SelectSegment(b, f, seg.id);
                else
                {
                    WallSelection.Clear();
                    BuildingEditorUtil.Mutate(b, "Dibujar pared", fi, () => session.Click(cursor));
                }
                e.Use();
            }

            if (hasCursor && e.type == EventType.Repaint) DrawPreview(b, f, cursor);
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint();
        }

        bool TryCursor(Building b, FloorData f, Event e, out Vector2 p)
        {
            if (!BuildingEditorUtil.GuiToPlan(b, e.mousePosition, f.elevation, out p)) return false;
            // Existing nodes win over the grid (also with Ctrl) so chains can start/close on off-grid nodes.
            if (NearNode(b, f, e.mousePosition, out var node)) p = node.position;
            else if (!(e.control || e.command)) p = GridSnap.Snap(p, Grid);
            if (e.shift && session.IsDrawing && !e.alt)
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

        void TrySplit(Building b, BuildingData data, FloorData f, int fi, Vector2 mouse, Vector2 cursor)
        {
            if (!PickSegment(b, f, mouse, out var seg)) return;
            WallNode created = null;
            BuildingEditorUtil.Mutate(b, "Dividir pared", fi, () => created = data.SplitSegment(f, seg.id, cursor));
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
                else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
                {
                    if (DeleteSelection(b, data, f, fi)) e.Use();
                }
            }
            // Delete also arrives as a command; swallow it while this tool is active so it never deletes the
            // Building GameObject from the Scene view (a KeyDown may already have removed the selection).
            else if ((e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand)
                     && (e.commandName == "SoftDelete" || e.commandName == "Delete"))
            {
                if (e.type == EventType.ExecuteCommand) DeleteSelection(b, data, f, fi);
                e.Use();
            }
        }

        static bool DeleteSelection(Building b, BuildingData data, FloorData f, int fi)
        {
            if (WallSelection.Floor(b) != f) return false;
            var node = WallSelection.Node(b);
            var seg = WallSelection.Segment(b);
            if (node != null)
                BuildingEditorUtil.Mutate(b, "Eliminar nodo", fi, () => data.RemoveNode(f, node.id));
            else if (seg != null)
                BuildingEditorUtil.Mutate(b, "Eliminar pared", fi, () => data.RemoveSegment(f, seg.id));
            else return false;
            WallSelection.Clear();
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

        static void DoNodeHandles(Building b, BuildingData data, FloorData f, int fi, Event e)
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

                int handleId = GUIUtility.GetControlID(FocusType.Passive);
                bool down = e.type == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == handleId;
                EditorGUI.BeginChangeCheck();
                Handles.FreeMoveHandle(handleId, world, size, Vector3.zero, cap);
                if (down && GUIUtility.hotControl == handleId) WallSelection.SelectNode(b, f, n.id);
                if (EditorGUI.EndChangeCheck()
                    && BuildingEditorUtil.GuiToPlan(b, e.mousePosition, f.elevation, out var p))
                {
                    if (!(e.control || e.command)) p = GridSnap.Snap(p, Grid);
                    if ((p - n.position).sqrMagnitude > 1e-8f)
                        BuildingEditorUtil.Mutate(b, "Mover nodo", isLinked ? -1 : fi, () => data.MoveNode(f, n.id, p));
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
