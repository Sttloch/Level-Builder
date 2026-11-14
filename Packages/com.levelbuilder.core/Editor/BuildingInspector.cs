using UnityEditor;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Inspector for Building: floor list, selected segment thickness and node unlinking.</summary>
    [CustomEditor(typeof(Building))]
    public class BuildingInspector : UnityEditor.Editor
    {
        bool showRaw;

        void OnEnable() => WallSelection.Changed += Repaint;
        void OnDisable() => WallSelection.Changed -= Repaint;

        public override void OnInspectorGUI()
        {
            var b = (Building)target;
            if (b.data == null) return;
            var data = b.data;

            EditorGUILayout.LabelField("Plantas", EditorStyles.boldLabel);
            int active = BuildingEditorUtil.ActiveFloorIndex(b);
            for (int i = 0; i < data.floors.Count; i++)
            {
                var f = data.floors[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool isActive = i == active;
                        if (GUILayout.Toggle(isActive, GUIContent.none, EditorStyles.radioButton, GUILayout.Width(16)) && !isActive)
                            BuildingOverlay.SetActiveFloor(b, i);
                        EditorGUI.BeginChangeCheck();
                        string n = EditorGUILayout.DelayedTextField(f.name);
                        if (EditorGUI.EndChangeCheck())
                            BuildingEditorUtil.Mutate(b, "Nombre de planta", i, () => f.name = n);
                        EditorGUILayout.LabelField("y = " + f.elevation.ToString("0.##") + " m", GUILayout.Width(80));
                    }

                    EditorGUI.BeginChangeCheck();
                    float h = EditorGUILayout.DelayedFloatField("Altura", f.height);
                    if (EditorGUI.EndChangeCheck()) BuildingOverlay.SetFloorHeight(b, i, h);

                    EditorGUI.BeginChangeCheck();
                    float slab = EditorGUILayout.DelayedFloatField("Forjado", f.slabThickness);
                    if (EditorGUI.EndChangeCheck())
                        BuildingEditorUtil.Mutate(b, "Grosor de forjado", -1, () => f.slabThickness = Mathf.Max(0.01f, slab));
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Planta")) BuildingOverlay.AddFloor(b);
                using (new EditorGUI.DisabledScope(data.floors.Count <= 1))
                    if (GUILayout.Button("− Planta")) BuildingOverlay.RemoveTopFloor(b);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selección (herramienta Paredes)", EditorStyles.boldLabel);
            var selFloor = WallSelection.Floor(b);
            int selIndex = selFloor != null ? data.floors.IndexOf(selFloor) : -1;
            var seg = WallSelection.Segment(b);
            var node = WallSelection.Node(b);

            if (seg != null)
            {
                EditorGUI.BeginChangeCheck();
                float t = EditorGUILayout.DelayedFloatField("Grosor de pared", seg.thickness);
                if (EditorGUI.EndChangeCheck())
                    BuildingEditorUtil.Mutate(b, "Grosor de pared", selIndex, () => seg.thickness = Mathf.Max(0.01f, t));
            }
            else if (node != null)
            {
                EditorGUILayout.LabelField("Nodo", node.position.ToString("0.00"));
                bool linked = BuildingEditorUtil.IsLinked(data, node.id);
                using (new EditorGUI.DisabledScope(!linked))
                    if (GUILayout.Button("Desenlazar nodo"))
                        BuildingEditorUtil.Mutate(b, "Desenlazar nodo", -1, () => data.UnlinkNode(selFloor, node.id));
                if (!linked) EditorGUILayout.HelpBox("El nodo no está enlazado con otras plantas.", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("Selecciona un nodo (clic) o una pared (Shift+clic) con la herramienta Paredes.", MessageType.None);
            }

            EditorGUILayout.Space();
            showRaw = EditorGUILayout.Foldout(showRaw, "Datos (avanzado)");
            if (showRaw) DrawDefaultInspector();
        }
    }
}
