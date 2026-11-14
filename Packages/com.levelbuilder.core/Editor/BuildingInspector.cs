using System.Collections.Generic;
using LevelBuilder.Geometry;
using UnityEditor;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Inspector for Building: floor list, selected segment thickness, node unlinking and the selected opening.</summary>
    [CustomEditor(typeof(Building))]
    public class BuildingInspector : UnityEditor.Editor
    {
        bool showRaw;

        // Cached once per Layout pass (OpeningLayout.Compute solves the wall joins).
        Dictionary<string, List<OpeningRect>> openingLayout;
        FloorData openingLayoutFloor;

        void OnEnable()
        {
            WallSelection.Changed += Repaint;
            OpeningSelection.Changed += Repaint;
        }

        void OnDisable()
        {
            WallSelection.Changed -= Repaint;
            OpeningSelection.Changed -= Repaint;
        }

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

            DrawOpeningSection(b);

            EditorGUILayout.Space();
            showRaw = EditorGUILayout.Foldout(showRaw, "Datos (avanzado)");
            if (showRaw) DrawDefaultInspector();
        }

        void DrawOpeningSection(Building b)
        {
            if (Event.current.type == EventType.Layout) OpeningSelection.Validate();
            var f = OpeningSelection.Floor(b);
            var (seg, o) = OpeningSelection.Find(b);
            if (f == null || o == null) return;

            // Indices are found again every time: Undo/Redo replaces the data objects.
            var data = b.data;
            int fi = data.floors.IndexOf(f);
            int si = f.segments.IndexOf(seg);
            int oi = seg.openings.IndexOf(o);
            if (fi < 0 || si < 0 || oi < 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Abertura seleccionada", EditorStyles.boldLabel);

            if (openingLayout == null || openingLayoutFloor != f || Event.current.type == EventType.Layout)
            {
                openingLayout = OpeningLayout.Compute(f);
                openingLayoutFloor = f;
            }
            if (openingLayout.TryGetValue(seg.id, out var rects))
                foreach (var r in rects)
                    if (r.opening == o && !r.valid)
                        EditorGUILayout.HelpBox("No cabe: " + OpeningPlacement.IssueText(r.issue) +
                            ". No se recorta la pared ni se genera hasta que sea válida.", MessageType.Warning);

            serializedObject.Update();
            var p = serializedObject.FindProperty(
                $"data.floors.Array.data[{fi}].segments.Array.data[{si}].openings.Array.data[{oi}]");
            if (p == null) return;

            EditorGUI.BeginChangeCheck();
            var typeProp = p.FindPropertyRelative("type");
            EditorGUILayout.PropertyField(typeProp, new GUIContent("Tipo"));
            EditorGUILayout.PropertyField(p.FindPropertyRelative("width"), new GUIContent("Ancho"));
            EditorGUILayout.PropertyField(p.FindPropertyRelative("height"), new GUIContent("Alto"));
            var kind = (OpeningType)typeProp.enumValueIndex;
            if (kind == OpeningType.Window)
                EditorGUILayout.PropertyField(p.FindPropertyRelative("sill"), new GUIContent("Alféizar"));
            EditorGUILayout.PropertyField(p.FindPropertyRelative("center"),
                new GUIContent("Centro", "Metros desde el nodo inicial de la pared."));

            if (kind == OpeningType.Door) DrawDoor(p.FindPropertyRelative("door"));
            else if (kind == OpeningType.Window)
                EditorGUILayout.PropertyField(p.FindPropertyRelative("windowPrefab"), new GUIContent("Prefab de ventana",
                    "Vacío: marco y cristal por defecto."));

            if (EditorGUI.EndChangeCheck() && serializedObject.ApplyModifiedProperties())
                b.MarkDirty(fi);

            if (GUILayout.Button("Borrar abertura"))
            {
                OpeningPlacement.Remove(b, fi, o.id);
                OpeningSelection.Clear();
                GUIUtility.ExitGUI();
            }
        }

        static void DrawDoor(SerializedProperty door)
        {
            EditorGUILayout.LabelField("Puerta", EditorStyles.miniBoldLabel);
            var kind = door.FindPropertyRelative("kind");
            EditorGUILayout.PropertyField(kind, new GUIContent("Tipo de puerta"));
            EditorGUILayout.PropertyField(door.FindPropertyRelative("hingeOnRight"),
                new GUIContent("Bisagra a la derecha", "Vista desde la cara R de la pared."));
            if (kind.enumValueIndex == (int)DoorKind.Swing)
            {
                EditorGUILayout.PropertyField(door.FindPropertyRelative("bothWays"), new GUIContent("Abre a ambos lados"));
                EditorGUILayout.PropertyField(door.FindPropertyRelative("openAngle"), new GUIContent("Ángulo de apertura"));
            }
            EditorGUILayout.PropertyField(door.FindPropertyRelative("speed"),
                new GUIContent("Velocidad", "Aperturas completas por segundo."));
            var activation = door.FindPropertyRelative("activation");
            EditorGUILayout.PropertyField(activation, new GUIContent("Activación"));
            if (activation.enumValueIndex == (int)DoorActivation.Interact)
                EditorGUILayout.PropertyField(door.FindPropertyRelative("key"), new GUIContent("Tecla"));
            if (activation.enumValueIndex != (int)DoorActivation.Script)
            {
                var tag = door.FindPropertyRelative("playerTag");
                string t = EditorGUILayout.TagField("Tag del jugador", tag.stringValue);
                if (t != tag.stringValue) tag.stringValue = t;
            }
            EditorGUILayout.PropertyField(door.FindPropertyRelative("locked"), new GUIContent("Cerrada con llave"));
            EditorGUILayout.PropertyField(door.FindPropertyRelative("leafPrefab"),
                new GUIContent("Prefab de hoja", "Vacío: hoja por defecto. Se escala al hueco."));
            EditorGUILayout.PropertyField(door.FindPropertyRelative("onOpened"), new GUIContent("Al abrirse"));
            EditorGUILayout.PropertyField(door.FindPropertyRelative("onClosed"), new GUIContent("Al cerrarse"));
        }
    }
}
