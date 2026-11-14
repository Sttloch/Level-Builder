using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Scene view panel shown while a Building is selected: active floor, add/remove floor, height, wall tool.</summary>
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
                        "Clic: punto (clic en un nodo empieza desde él). Doble clic / Esc / Enter: terminar.\n" +
                        "Doble clic en pared: dividir. Arrastrar nodo: mover. Shift+clic: seleccionar pared.\n" +
                        "Ctrl: sin rejilla. Shift: ángulos de 15°. Supr: borrar selección.", MessageType.None);
            }
        }

        internal static void SetActiveFloor(Building b, int index)
        {
            Undo.RecordObject(b, "Planta activa");
            b.activeFloor = Mathf.Clamp(index, 0, b.data.floors.Count - 1);
            EditorUtility.SetDirty(b);
            WallSelection.Clear();
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
        }

        internal static void SetFloorHeight(Building b, int index, float height)
        {
            height = Mathf.Max(0.5f, height);
            BuildingEditorUtil.Mutate(b, "Altura de planta", -1, () => b.data.SetFloorHeight(index, height));
        }
    }
}
