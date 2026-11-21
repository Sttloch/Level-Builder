using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Scene view panel shown while a Building is selected: active floor, add/remove floor, height, wall, opening and stair tools.</summary>
    [Overlay(typeof(SceneView), "levelbuilder-building", "Level Builder")]
    public class BuildingOverlay : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => Selected() != null;

        static Building Selected()
        {
            var go = Selection.activeGameObject;
            return go != null ? go.GetComponent<Building>() : null;
        }

        public override void OnGUI()
        {
            var b = Selected();
            if (b == null || b.data == null) return;
            var data = b.data;

            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(200)))
            {
                int fi = BuildingEditorUtil.ActiveFloorIndex(b);
                if (fi >= 0)
                {
                    var names = new string[data.floors.Count];
                    for (int i = 0; i < names.Length; i++) names[i] = i + ": " + data.floors[i].name;
                    int sel = EditorGUILayout.Popup("Planta activa", fi, names);
                    if (sel != fi) SetActiveFloor(b, sel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("+ Planta")) AddFloor(b);
                    using (new EditorGUI.DisabledScope(data.floors.Count <= 1))
                        if (GUILayout.Button("− Planta")) RemoveTopFloor(b);
                }

                fi = BuildingEditorUtil.ActiveFloorIndex(b);
                if (fi >= 0)
                {
                    EditorGUI.BeginChangeCheck();
                    float h = EditorGUILayout.DelayedFloatField("Altura planta", data.floors[fi].height);
                    if (EditorGUI.EndChangeCheck())
                        SetFloorHeight(b, fi, h);
                }

                bool active = ToolManager.activeToolType == typeof(WallTool);
                bool want = GUILayout.Toggle(active, "Dibujar paredes", "Button");
                if (want != active)
                {
                    if (want) ToolManager.SetActiveTool<WallTool>();
                    else ToolManager.RestorePreviousPersistentTool();
                }
                if (active)
                    EditorGUILayout.HelpBox(
                        "Clic: punto. Doble clic / Esc / Enter: terminar. Retroceso: quitar último punto.\n" +
                        "Clic en nodo: seleccionar; otro clic en el nodo seleccionado: empezar pared desde él.\n" +
                        "Arrastrar nodo: mover (al soltar se une a nodos / divide paredes).\n" +
                        "Doble clic en pared: dividir.\n" +
                        "Shift+clic: seleccionar pared · Supr o 'Quitar pared': quitarla (une las habitaciones).\n" +
                        "Ctrl: sin rejilla. Shift: ángulos de 15°.", MessageType.None);

                DrawPaintRow();

                DrawOpeningRow();
                DrawStairRow(b);
            }
        }

        static void DrawPaintRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool active = ToolManager.activeToolType == typeof(PaintTool);
                bool want = GUILayout.Toggle(active, "Pintar", "Button");
                if (want != active)
                {
                    if (want) ToolManager.SetActiveTool<PaintTool>();
                    else ToolManager.RestorePreviousPersistentTool();
                }
                if (GUILayout.Button("Paleta")) PaletteWindow.Open();
            }
            EditorGUI.BeginChangeCheck();
            var m = (Material)EditorGUILayout.ObjectField("Material activo", PaletteState.Active, typeof(Material), false);
            if (EditorGUI.EndChangeCheck()) PaletteState.Active = m;
            if (ToolManager.activeToolType != typeof(PaintTool)) return;
            EditorGUILayout.HelpBox(PaletteState.Active == null ? "Elige un material en la Paleta." :
                "Clic: pintar una cara. Shift+clic: la pieza entera.\n" +
                "Alt+clic: cuentagotas (toma el material). Esc: salir.", MessageType.None);
        }

        static void DrawStairRow(Building b)
        {
            bool active = ToolManager.activeToolType == typeof(StairTool);
            bool want = GUILayout.Toggle(active, "Escalera", "Button");
            if (want != active)
            {
                if (want) ToolManager.SetActiveTool<StairTool>();
                else ToolManager.RestorePreviousPersistentTool();
            }
            if (!active) return;
            int fi = BuildingEditorUtil.ActiveFloorIndex(b);
            if (fi < 0 || fi + 1 >= b.data.floors.Count)
                EditorGUILayout.HelpBox(StairIssueText.NoFloorAboveHelp, MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "Clic: punto (esquina). Shift+clic: punto de curva. Retroceso: quitar último.\n" +
                    "Enter / doble clic: terminar. Esc: cancelar. Ctrl: sin rejilla.\n" +
                    "Clic en escalera: seleccionar. Arrastrar punto: mover. Punto amarillo lateral: ancho.\n" +
                    "Supr: quitar el punto seleccionado o, sin punto, borrar la escalera. Rojo: problema (motivo en la etiqueta).",
                    MessageType.None);
        }

        static readonly (OpeningType type, string label)[] OpeningModes =
        {
            (OpeningType.Door, "Puerta"), (OpeningType.Window, "Ventana"), (OpeningType.Empty, "Hueco")
        };

        static void DrawOpeningRow()
        {
            bool toolOn = ToolManager.activeToolType == typeof(OpeningTool);
            EditorGUILayout.LabelField("Abrir hueco", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var (type, label) in OpeningModes)
                {
                    bool on = toolOn && OpeningTool.Type == type;
                    bool want = GUILayout.Toggle(on, label, "Button");
                    if (want == on) continue;
                    if (want) OpeningTool.Activate(type);
                    else ToolManager.RestorePreviousPersistentTool();
                }
            }
            if (!toolOn) return;

            var presets = OpeningPresets.For(OpeningTool.Type);
            var names = new string[presets.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = presets[i].name + " (" + presets[i].width.ToString("0.##") + " × " + presets[i].height.ToString("0.##") + ")";
            OpeningTool.PresetIndex = EditorGUILayout.Popup("Preset", OpeningTool.PresetIndex, names);
            EditorGUILayout.HelpBox(
                "Clic en pared: colocar (ajuste a 0.1 m, Ctrl: sin ajuste). Clic en abertura: seleccionar.\n" +
                "Arrastrar abertura: mover por la pared o a otra. Puntos amarillos: ancho, alto y alféizar.\n" +
                "Supr: borrar abertura. Esc: deseleccionar. Rojo: no cabe (motivo en la etiqueta).", MessageType.None);
        }

        // View state: no Undo entry (it would spam the Undo history). SetDirty so it is still serialized; prefab
        // instances need the explicit override record that Undo.RecordObject would otherwise have made.
        internal static void SetActiveFloor(Building b, int index)
        {
            b.activeFloor = Mathf.Clamp(index, 0, b.data.floors.Count - 1);
            EditorUtility.SetDirty(b);
            if (PrefabUtility.IsPartOfPrefabInstance(b)) PrefabUtility.RecordPrefabInstancePropertyModifications(b);
            WallSelection.Clear();
            OpeningSelection.Clear();
            StairSelection.Clear();
            SceneView.RepaintAll();
        }

        internal static void AddFloor(Building b)
        {
            BuildingEditorUtil.Mutate(b, "Añadir planta", b.data.floors.Count, () =>
            {
                b.data.AddFloor();
                b.activeFloor = b.data.floors.Count - 1;
            });
            WallSelection.Clear();
        }

        internal static void RemoveTopFloor(Building b)
        {
            if (b.data.floors.Count <= 1) return;
            BuildingEditorUtil.Mutate(b, "Eliminar planta", b.data.floors.Count - 2, () =>
            {
                b.data.RemoveTopFloor();
                b.activeFloor = Mathf.Clamp(b.activeFloor, 0, b.data.floors.Count - 1);
            });
            WallSelection.Clear();
            OpeningSelection.Clear();
            StairSelection.Validate();
        }

        internal static void SetFloorHeight(Building b, int index, float height)
        {
            height = Mathf.Max(0.5f, height);
            BuildingEditorUtil.Mutate(b, "Altura de planta", -1, () => b.data.SetFloorHeight(index, height));
        }
    }
}
