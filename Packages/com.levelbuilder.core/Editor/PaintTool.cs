using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Undoable paint operations on face keys (one Undo step "Pintar" per call).</summary>
    public static class PaintOps
    {
        public const string UndoName = "Pintar";

        /// <summary>Paints one face. hit = world point of the hit; for regions its plan point is the seed.</summary>
        public static void PaintFace(Building b, FloorData f, string key, Material m, Vector3 hit) =>
            Paint(b, f, new[] { key }, m, hit);

        /// <summary>Paints every face of the piece the face belongs to (FaceMaterialRules.PieceKeys).</summary>
        public static void PaintPiece(Building b, FloorData f, string key, Material m, Vector3 hit)
        {
            if (b == null || b.data == null || f == null || string.IsNullOrEmpty(key)) return;
            Paint(b, f, new List<string>(FaceMaterialRules.PieceKeys(b.data, f, key)), m, hit);
        }

        /// <summary>The material assigned to the face (null = default material).</summary>
        public static Material Sample(Building b, FloorData f, string key) =>
            b == null ? null : FaceMaterialRules.Get(b.data, f, key);

        static void Paint(Building b, FloorData f, IList<string> keys, Material m, Vector3 hit)
        {
            if (b == null || b.data == null || f == null || keys.Count == 0) return;
            bool any = false;
            foreach (var k in keys) if (FaceMaterialRules.IsWritable(f, k)) { any = true; break; }
            if (!any) return;
            var data = b.data;
            int fi = data.floors.IndexOf(f);
            var local = b.transform.InverseTransformPoint(hit);
            var seed = new Vector2(local.x, local.z);
            int stairFrom = -1;
            const string stairPrefix = "stair:";
            if (keys[0].StartsWith(stairPrefix, StringComparison.Ordinal))
            {
                int slash = keys[0].LastIndexOf('/');
                var st = slash > stairPrefix.Length ? data.FindStair(keys[0].Substring(stairPrefix.Length, slash - stairPrefix.Length)) : null;
                if (st != null) stairFrom = data.FloorIndex(st.fromFloorId);
            }
            BuildingEditorUtil.Mutate(b, UndoName, fi, () =>
            {
                var regions = LevelBuilder.Geometry.PlanarFaces.Compute(f).regions;
                foreach (var k in keys) FaceMaterialRules.Set(data, f, k, m, regions, seed);
                if (stairFrom >= 0 && stairFrom != fi) b.MarkDirty(stairFrom);
            });
        }
    }

    /// <summary>Readable Spanish names of face keys.</summary>
    public static class FaceNames
    {
        static readonly (string suffix, string name)[] Region =
        {
            ("/floor/under", "Suelo – cara inferior"), ("/floor/edge", "Canto del forjado"),
            ("/roof/edge", "Canto de cubierta"), ("/floor", "Suelo"), ("/ceiling", "Techo"), ("/roof", "Cubierta")
        };

        public static string Of(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (key.StartsWith("region:", StringComparison.Ordinal))
            {
                foreach (var (suffix, name) in Region)
                    if (key.EndsWith(suffix, StringComparison.Ordinal)) return name;
                return "Suelo";
            }
            if (key.StartsWith("node:", StringComparison.Ordinal)) return "Unión";
            int i = key.LastIndexOf('/');
            string part = i >= 0 ? key.Substring(i + 1) : key;
            if (key.StartsWith("stair:", StringComparison.Ordinal))
            {
                switch (part)
                {
                    case "tread": return "Escalón – huella";
                    case "riser": return "Escalón – contrahuella";
                    case "side": return "Escalón – lateral";
                    case "landing": return "Descansillo";
                    case "rail": return "Barandilla";
                    default: return "Escalera";
                }
            }
            switch (part)
            {
                case "L": return "Pared – lado izquierdo";
                case "R": return "Pared – lado derecho";
                case "start": case "end": return "Pared – canto";
                case "top": return "Pared – tapa";
                case "jamb": return "Jamba";
                case "sill": return "Alféizar";
                case "threshold": return "Umbral";
                default: return part;
            }
        }
    }

    /// <summary>
    /// Scene view tool to paint faces with the palette's active material. Hover: outline and name of the face.
    /// Click: that face. Shift+click: the whole piece. Alt+click (no drag): eyedropper; Alt+drag keeps orbiting.
    /// </summary>
    [EditorTool("Pintar", typeof(Building))]
    public class PaintTool : EditorTool
    {
        const float DragPixels = 4f;
        static readonly int ToolHint = "LevelBuilder.PaintTool".GetHashCode();
        static readonly Color HoverColor = new Color(1f, 0.85f, 0.1f, 1f);

        string hoverKey;
        FloorData hoverFloor;
        Vector2 mouse;

        bool altPress;
        Vector2 altPressPos;
        bool swallowDeleteCommand;

        public override GUIContent toolbarIcon =>
            new GUIContent(EditorGUIUtility.IconContent("Grid.PaintTool").image,
                "Pintar (Level Builder): clic una cara, Shift+clic la pieza, Alt+clic cuentagotas");

        public override void OnActivated()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            PaletteState.Changed += SceneView.RepaintAll;
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            PaletteState.Changed -= SceneView.RepaintAll;
            hoverKey = null; hoverFloor = null; altPress = false;
        }

        void OnUndoRedo() { hoverKey = null; hoverFloor = null; SceneView.RepaintAll(); }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView view)) return;
            var b = target as Building;
            if (b == null || b.data == null) return;
            var e = Event.current;

            int id = GUIUtility.GetControlID(ToolHint, FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            HandleKeys(e);

            // Eyedropper: Alt + press without drag. Alt events are never used, so Alt+drag still orbits; the
            // release is read through rawType because the camera may already have used it.
            if (e.rawType == EventType.MouseDown && e.button == 0 && e.alt)
            {
                altPress = true;
                altPressPos = e.mousePosition;
            }
            else if (e.rawType == EventType.MouseDrag && altPress && (e.mousePosition - altPressPos).magnitude >= DragPixels)
                altPress = false;
            else if (e.rawType == EventType.MouseUp && e.button == 0 && altPress)
            {
                altPress = false;
                if ((e.mousePosition - altPressPos).magnitude < DragPixels) Eyedrop(b, e.mousePosition);
            }

            switch (e.type)
            {
                case EventType.MouseMove:
                    UpdateHover(b, e.mousePosition);
                    view.Repaint();
                    break;
                case EventType.MouseDown:
                    if (!e.alt) altPress = false;
                    if (e.button == 0 && !e.alt && HandleUtility.nearestControl == id)
                    {
                        GUIUtility.hotControl = id;
                        UpdateHover(b, e.mousePosition);
                        Paint(b, e);
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
                    Draw(b);
                    break;
            }
        }

        void UpdateHover(Building b, Vector2 guiPoint)
        {
            mouse = guiPoint;
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            if (!FacePicker.Pick(b, ray, out hoverKey, out hoverFloor, out _)) { hoverKey = null; hoverFloor = null; }
        }

        static void Paint(Building b, Event e)
        {
            var m = PaletteState.Active;
            if (m == null) return;
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!FacePicker.Pick(b, ray, out var key, out var floor, out var hit)) return;
            if (e.shift) PaintOps.PaintPiece(b, floor, key, m, hit);
            else PaintOps.PaintFace(b, floor, key, m, hit);
        }

        static void Eyedrop(Building b, Vector2 guiPoint)
        {
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            if (!FacePicker.Pick(b, ray, out var key, out var floor, out _)) return;
            var m = PaintOps.Sample(b, floor, key);
            if (m != null) PaletteState.Active = m;
        }

        void HandleKeys(Event e)
        {
            switch (e.type)
            {
                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
                    {
                        // Never let Delete reach Unity while painting: it would delete the Building.
                        swallowDeleteCommand = true;
                        e.Use();
                    }
                    else
                    {
                        swallowDeleteCommand = false;
                        if (e.keyCode == KeyCode.Escape)
                        {
                            ToolManager.RestorePreviousPersistentTool();
                            e.Use();
                        }
                    }
                    break;
                case EventType.ValidateCommand:
                case EventType.ExecuteCommand:
                    if ((e.commandName == "SoftDelete" || e.commandName == "Delete") && swallowDeleteCommand)
                    {
                        e.Use();
                        if (e.type == EventType.ExecuteCommand) swallowDeleteCommand = false;
                    }
                    break;
            }
        }

        void Draw(Building b)
        {
            if (hoverKey != null && hoverFloor != null) DrawOutline(b, hoverFloor, hoverKey);

            string text = PaletteState.Active == null ? "Elige un material en la Paleta"
                : hoverKey != null ? FaceNames.Of(hoverKey) : null;
            if (text == null) return;
            Handles.BeginGUI();
            var content = new GUIContent(text);
            var size = EditorStyles.helpBox.CalcSize(content);
            GUI.Label(new Rect(mouse.x + 16f, mouse.y + 8f, size.x + 8f, size.y + 4f), content, EditorStyles.helpBox);
            Handles.EndGUI();
        }

        static readonly List<Vector3> Lines = new List<Vector3>();
        static readonly List<Vector3> Verts = new List<Vector3>();

        static void DrawOutline(Building b, FloorData f, string key)
        {
            int fi = b.data.floors.IndexOf(f);
            Lines.Clear();
            var root = b.transform;
            for (int c = 0; c < root.childCount; c++)
            {
                var child = root.GetChild(c);
                if (!BuildingRenderer.IsGeneratedFloor(child, out int index) || index != fi) continue;
                foreach (var map in child.GetComponentsInChildren<FaceKeyMap>(false))
                {
                    var mf = map.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var mesh = mf.sharedMesh;
                    bool gotVerts = false;
                    foreach (var en in map.entries)
                    {
                        if (en.key != key || en.submesh >= mesh.subMeshCount) continue;
                        if (!gotVerts) { mesh.GetVertices(Verts); gotVerts = true; }
                        var tris = mesh.GetTriangles(en.submesh);
                        var tr = map.transform;
                        int end = Mathf.Min((en.firstTri + en.triCount) * 3, tris.Length);
                        for (int t = en.firstTri * 3; t + 2 < end; t += 3)
                        {
                            var a = tr.TransformPoint(Verts[tris[t]]);
                            var bb = tr.TransformPoint(Verts[tris[t + 1]]);
                            var cc = tr.TransformPoint(Verts[tris[t + 2]]);
                            Lines.Add(a); Lines.Add(bb); Lines.Add(bb); Lines.Add(cc); Lines.Add(cc); Lines.Add(a);
                        }
                    }
                }
            }
            if (Lines.Count == 0) return;
            var prevColor = Handles.color;
            Handles.color = HoverColor;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.DrawLines(Lines.ToArray());
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = prevColor;
        }
    }
}
