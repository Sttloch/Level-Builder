using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Material palette: pick the active material, drop materials/textures to add them to the library.</summary>
    public class PaletteWindow : EditorWindow
    {
        string search = "";
        string category = "";
        Vector2 scroll;
        float thumb = 72f;

        [MenuItem("Window/Level Builder/Paleta")]
        public static void Open() => GetWindow<PaletteWindow>("Paleta");

        void OnEnable() { PaletteState.Changed += Repaint; Undo.undoRedoPerformed += OnUndoRedo; }
        void OnDisable() { PaletteState.Changed -= Repaint; Undo.undoRedoPerformed -= OnUndoRedo; }

        void OnUndoRedo()
        {
            PaletteOps.MarkAllBuildingsDirty();
            Repaint();
        }
        bool dropHint;

        void OnSelectionChange() => Repaint();

        static Building SelectedBuilding()
        {
            var go = Selection.activeGameObject;
            return go != null ? go.GetComponent<Building>() : null;
        }

        void OnGUI()
        {
            var b = SelectedBuilding();
            var lib = (b != null && b.library != null) ? b.library : PaletteState.Library;

            EditorGUI.BeginChangeCheck();
            var picked = (MaterialLibrary)EditorGUILayout.ObjectField("Biblioteca", lib, typeof(MaterialLibrary), false);
            if (EditorGUI.EndChangeCheck())
            {
                PaletteState.Library = picked;
                if (b != null && picked != null) AssignLibrary(b, picked);
                lib = picked;
            }
            else if (lib != null && PaletteState.Library != lib) PaletteState.Library = lib;

            if (lib != null) dropHint = false;
            if (lib == null)
            {
                EditorGUILayout.HelpBox("No hay biblioteca de materiales.", MessageType.Info);
                if (dropHint)
                    EditorGUILayout.HelpBox("Crea o elige una biblioteca para añadir materiales", MessageType.Warning);
                var dropEv = Event.current;
                if ((dropEv.type == EventType.DragUpdated || dropEv.type == EventType.DragPerform)
                    && DragAndDrop.objectReferences.Any(o => o is Material || o is Texture2D))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                    if (dropEv.type == EventType.DragPerform)
                        dropHint = true;
                    dropEv.Use();
                }
                if (GUILayout.Button("Crear biblioteca por defecto"))
                {
                    var created = DefaultLibrary.CreateOrGet();
                    PaletteState.Library = created;
                    if (b != null) AssignLibrary(b, created);
                }
                return;
            }

            EditorGUI.BeginChangeCheck();
            var folder = EditorGUILayout.TextField("Carpeta de guardado", lib.saveFolder);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(lib, "Carpeta de guardado");
                lib.saveFolder = folder;
                EditorUtility.SetDirty(lib);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                search = EditorGUILayout.TextField("Buscar", search);
                var cats = new[] { "(todas)" }.Concat(PaletteOps.Categories(lib)).ToArray();
                int ci = System.Array.IndexOf(cats, category);
                if (ci < 0) ci = 0;
                int ni = EditorGUILayout.Popup(ci, cats, GUILayout.Width(110));
                category = ni == 0 ? "" : cats[ni];
            }

            var active = PaletteState.Active;
            EditorGUILayout.LabelField("Material activo: " + (active != null ? active.name : "ninguno"), EditorStyles.miniBoldLabel);

            var gridRect = DrawGrid(lib, active);
            HandleDrop(lib, gridRect);
        }

        static void AssignLibrary(Building b, MaterialLibrary lib)
        {
            Undo.RecordObject(b, "Asignar biblioteca");
            b.library = lib;
            EditorUtility.SetDirty(b);
            b.MarkDirty();
        }

        Rect DrawGrid(MaterialLibrary lib, Material active)
        {
            var entries = PaletteOps.Filter(lib, search, category).ToList();
            var area = GUILayoutUtility.GetRect(0, 100000, 0, 100000, GUILayout.ExpandHeight(true));
            GUI.Box(area, GUIContent.none);
            float cell = thumb + 8f, label = 16f;
            float viewW = area.width - 16f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((viewW - 4) / cell));
            int rows = Mathf.CeilToInt(entries.Count / (float)cols);
            var view = new Rect(0, 0, viewW, rows * (cell + label) + 4);
            var visibleRect = new Rect(scroll.x, scroll.y, viewW, area.height);
            scroll = GUI.BeginScrollView(area, scroll, view);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                int cx = i % cols, cy = i / cols;
                var r = new Rect(2 + cx * cell, 2 + cy * (cell + label), thumb + 4, thumb + label);
                var tr = new Rect(r.x + 2, r.y + 2, thumb, thumb);
                bool isActive = e.material != null && e.material == active;
                if (isActive) EditorGUI.DrawRect(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(0.2f, 0.5f, 1f, 0.6f));
                var tex = (e.material != null && Event.current.type == EventType.Repaint && visibleRect.Overlaps(r))
                    ? AssetPreview.GetAssetPreview(e.material) : null;
                if (tex != null) GUI.DrawTexture(tr, tex, ScaleMode.ScaleToFit);
                else EditorGUI.DrawRect(tr, new Color(0.3f, 0.3f, 0.3f));
                GUI.Label(new Rect(r.x, r.y + thumb + 2, r.width, label), e.name, EditorStyles.miniLabel);

                var ev = Event.current;
                if (visibleRect.Contains(ev.mousePosition) && r.Contains(ev.mousePosition))
                {
                    if (ev.type == EventType.MouseDown && ev.button == 0)
                    {
                        if (e.material != null) PaletteState.Active = e.material;
                        ev.Use();
                    }
                    else if (ev.type == EventType.ContextClick)
                    {
                        ShowContextMenu(lib, e);
                        ev.Use();
                    }
                }
            }
            GUI.EndScrollView();
            if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
            return area;
        }

        void ShowContextMenu(MaterialLibrary lib, MaterialLibrary.Entry e)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Renombrar"), false, () =>
                TextPrompt.Show("Renombrar", e.name, v => Edit(lib, "Renombrar material", false, () => e.name = v)));
            menu.AddItem(new GUIContent("Categoría"), false, () =>
                TextPrompt.Show("Categoría", e.category, v => Edit(lib, "Categoría de material", false, () => e.category = v)));
            menu.AddItem(new GUIContent("Tamaño real"), false, () =>
                TextPrompt.Show("Tamaño (m): ancho x alto", PaletteOps.FormatSize(e.size), v =>
                {
                    if (!PaletteOps.TryParseSize(v, out var sz)) return;
                    Edit(lib, "Tamaño real de material", true, () => e.size = sz);
                }));
            menu.AddItem(new GUIContent("Quitar de la biblioteca"), false, () =>
                Edit(lib, "Quitar material", true, () => lib.entries.Remove(e)));
            menu.ShowAsContext();
        }

        void Edit(MaterialLibrary lib, string undoName, bool affectsBuildings, System.Action change)
        {
            Undo.RecordObject(lib, undoName);
            change();
            EditorUtility.SetDirty(lib);
            if (affectsBuildings) PaletteOps.MarkBuildingsDirty(lib);
            Repaint();
        }

        void HandleDrop(MaterialLibrary lib, Rect area)
        {
            var ev = Event.current;
            if (ev.type != EventType.DragUpdated && ev.type != EventType.DragPerform) return;
            if (!area.Contains(ev.mousePosition)) return;
            var objs = DragAndDrop.objectReferences;
            if (!objs.Any(o => o is Material || o is Texture2D)) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (ev.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                Undo.RecordObject(lib, "Añadir materiales");
                var first = PaletteOps.AddDropped(lib, objs, out int created);
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
                if (created > 0) PaletteOps.MarkBuildingsDirty(lib);
                if (first != null && first.material != null) PaletteState.Active = first.material;
            }
            ev.Use();
        }

        /// <summary>Minimal text prompt (IMGUI utility window).</summary>
        class TextPrompt : EditorWindow
        {
            string value, label;
            System.Action<string> onOk;
            bool focused;

            public static void Show(string label, string initial, System.Action<string> onOk)
            {
                var w = CreateInstance<TextPrompt>();
                w.titleContent = new GUIContent(label);
                w.label = label; w.value = initial ?? ""; w.onOk = onOk;
                w.minSize = w.maxSize = new Vector2(300, 70);
                w.ShowUtility();
            }

            void OnGUI()
            {
                GUI.SetNextControlName("f");
                value = EditorGUILayout.TextField(label, value);
                if (!focused) { EditorGUI.FocusTextInControl("f"); focused = true; }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Cancelar")) Close();
                    if (GUILayout.Button("Aceptar") || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return))
                    {
                        var cb = onOk; var v = value;
                        Close();
                        cb?.Invoke(v);
                    }
                }
            }
        }
    }
}
