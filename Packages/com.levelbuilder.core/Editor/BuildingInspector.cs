using System.Collections.Generic;
using LevelBuilder.Geometry;
using UnityEditor;
using UnityEngine;

namespace LevelBuilder.Editor
{
    /// <summary>Inspector for Building: floor list, selected segment thickness, node unlinking, the selected opening and the selected stair.</summary>
    [CustomEditor(typeof(Building))]
    public class BuildingInspector : UnityEditor.Editor
    {
        bool showRaw;

        // Cached once per Layout pass (OpeningLayout.Compute solves the wall joins).
        Dictionary<string, List<OpeningRect>> openingLayout;
        FloorData openingLayoutFloor;

        // Stair whose "Arreglar automáticamente" found nothing; cleared by any change of selection or data.
        string autoFixFailedStairId;
        StairValidation stairValidation;
        string stairValidationId;

        void OnEnable()
        {
            WallSelection.Changed += Repaint;
            OpeningSelection.Changed += Repaint;
            StairSelection.Changed += OnStairSelectionChanged;
            Undo.undoRedoPerformed += OnStairSelectionChanged;
        }

        void OnDisable()
        {
            WallSelection.Changed -= Repaint;
            OpeningSelection.Changed -= Repaint;
            StairSelection.Changed -= OnStairSelectionChanged;
            Undo.undoRedoPerformed -= OnStairSelectionChanged;
        }

        void OnStairSelectionChanged()
        {
            autoFixFailedStairId = null;
            Repaint();
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
            DrawStairSection(b);

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

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Id",
                    "Identificador de la abertura. Desde código: building.FindDoor(id) devuelve la puerta generada."));
                EditorGUILayout.SelectableLabel(o.id, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Copiar id", GUILayout.Width(72))) EditorGUIUtility.systemCopyBuffer = o.id;
            }

            // Type changes go through OpeningPlacement.SetType: a door always gets sill 0.
            EditorGUI.BeginChangeCheck();
            var newType = (OpeningType)EditorGUILayout.EnumPopup("Tipo", o.type);
            if (EditorGUI.EndChangeCheck() && newType != o.type)
                OpeningPlacement.SetType(b, fi, o.id, newType);

            serializedObject.Update();
            var p = serializedObject.FindProperty(
                $"data.floors.Array.data[{fi}].segments.Array.data[{si}].openings.Array.data[{oi}]");
            if (p == null) return;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(p.FindPropertyRelative("width"), new GUIContent("Ancho"));
            EditorGUILayout.PropertyField(p.FindPropertyRelative("height"), new GUIContent("Alto"));
            var kind = o.type;
            if (OpeningPlacement.HasSill(kind))
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

        void DrawStairSection(Building b)
        {
            if (Event.current.type == EventType.Layout) StairSelection.Validate();
            var s = StairSelection.Find(b);
            if (s == null) return;
            var data = b.data;
            int from = data.FloorIndex(s.fromFloorId);
            string sid = s.id;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Escalera seleccionada", EditorStyles.boldLabel);

            StairFloat(b, from, sid, "Ancho", "Ancho de escalera", s.width, (st, v) => st.width = v, "Entre 0.6 y 3 m.");
            StairFloat(b, from, sid, "Contrahuella objetivo", "Contrahuella de escalera", s.riserTarget, (st, v) => st.riserTarget = v,
                "Altura de escalón deseada (0.12–0.20 m). La real reparte la altura de la planta en escalones iguales.");
            StairFloat(b, from, sid, "Huella", "Huella de escalera", s.tread, (st, v) => st.tread = v, "Fondo de cada escalón (0.25–0.32 m).");
            StairToggle(b, from, sid, "Barandilla izquierda", "Barandilla de escalera", s.railLeft, (st, v) => st.railLeft = v);
            StairToggle(b, from, sid, "Barandilla derecha", "Barandilla de escalera", s.railRight, (st, v) => st.railRight = v);
            StairToggle(b, from, sid, "Rampa de colisión", "Rampa de escalera", s.collisionRamp, (st, v) => st.collisionRamp = v,
                "Una rampa invisible hace de suelo al subir y bajar; los escalones no tienen colisión propia.");

            // Validated here once per Layout pass, so the panel is current right after an edit (the renderer's
            // StairValidations only catch up at its next regeneration).
            if (stairValidation == null || stairValidationId != sid || Event.current.type == EventType.Layout)
            {
                stairValidation = StairValidator.Validate(data, s);
                stairValidationId = sid;
            }
            var v0 = stairValidation;
            var L = v0.layout;
            bool ok = L != null && L.ok;
            EditorGUILayout.LabelField("Escalones", ok ? L.risers.ToString() : "—");
            EditorGUILayout.LabelField("Contrahuella real", ok ? L.riser.ToString("0.000") + " m" : "—");
            EditorGUILayout.LabelField("Longitud", ok ? L.runLength.ToString("0.00") + " m" : "—");

            int pi = StairSelection.pointIndex;
            if (pi >= 0 && pi < s.route.Count)
            {
                EditorGUILayout.LabelField("Punto seleccionado", (pi + 1) + " de " + s.route.Count);
                int kind = s.route[pi].kind == StairPointKind.Smooth ? 1 : 0;
                EditorGUI.BeginChangeCheck();
                int nk = EditorGUILayout.Popup("Tipo de punto", kind, new[] { "Esquina", "Curva" });
                if (EditorGUI.EndChangeCheck() && nk != kind)
                {
                    var k = nk == 1 ? StairPointKind.Smooth : StairPointKind.Corner;
                    BuildingEditorUtil.Mutate(b, "Tipo de punto de escalera", from, () => data.SetStairPointKind(sid, pi, k));
                    autoFixFailedStairId = null;
                }
            }

            if (!v0.Valid)
            {
                EditorGUILayout.HelpBox(StairIssueText.Join(v0.issues), MessageType.Warning);
                if (GUILayout.Button("Arreglar automáticamente"))
                {
                    if (StairAutoFix.TryFix(data, s, out var route))
                    {
                        BuildingEditorUtil.Mutate(b, "Arreglar escalera", from, () =>
                        {
                            var st = data.FindStair(sid);
                            if (st == null) return;
                            st.route.Clear();
                            st.route.AddRange(route);
                        });
                        autoFixFailedStairId = null;
                        StairSelection.Validate();
                    }
                    else autoFixFailedStairId = sid;
                }
                if (autoFixFailedStairId == sid)
                    EditorGUILayout.HelpBox("No se encontró un arreglo automático", MessageType.Info);
            }

            if (GUILayout.Button("Borrar escalera"))
            {
                BuildingEditorUtil.Mutate(b, "Borrar escalera", from, () => data.RemoveStair(sid));
                StairSelection.Clear();
                GUIUtility.ExitGUI();
            }
        }

        void StairFloat(Building b, int from, string sid, string label, string undoName, float value,
            System.Action<StairData, float> set, string tooltip)
        {
            EditorGUI.BeginChangeCheck();
            float v = EditorGUILayout.DelayedFloatField(new GUIContent(label, tooltip), value);
            if (!EditorGUI.EndChangeCheck() || float.IsNaN(v) || float.IsInfinity(v)) return; // NaN would pass ClampStair
            BuildingEditorUtil.Mutate(b, undoName, from, () =>
            {
                var st = b.data.FindStair(sid);
                if (st == null) return;
                set(st, v);
                BuildingData.ClampStair(st);
            });
            autoFixFailedStairId = null;
        }

        void StairToggle(Building b, int from, string sid, string label, string undoName, bool value,
            System.Action<StairData, bool> set, string tooltip = null)
        {
            EditorGUI.BeginChangeCheck();
            bool v = EditorGUILayout.Toggle(new GUIContent(label, tooltip), value);
            if (!EditorGUI.EndChangeCheck()) return;
            BuildingEditorUtil.Mutate(b, undoName, from, () =>
            {
                var st = b.data.FindStair(sid);
                if (st != null) set(st, v);
            });
            autoFixFailedStairId = null;
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
                // Empty playerTag = any collider (other doors' leaves excepted).
                var tag = door.FindPropertyRelative("playerTag");
                bool any = string.IsNullOrEmpty(tag.stringValue);
                bool newAny = EditorGUILayout.Toggle(new GUIContent("Cualquier objeto",
                    "Cualquier collider activa la puerta, sin mirar su tag."), any);
                if (newAny != any) tag.stringValue = newAny ? "" : "Player";
                if (!newAny)
                {
                    string t = EditorGUILayout.TagField("Tag del jugador", tag.stringValue);
                    if (t != tag.stringValue) tag.stringValue = t;
                }
            }
            EditorGUILayout.PropertyField(door.FindPropertyRelative("locked"), new GUIContent("Cerrada con llave"));
            EditorGUILayout.PropertyField(door.FindPropertyRelative("leafPrefab"),
                new GUIContent("Prefab de hoja", "Vacío: hoja por defecto. Se escala al hueco."));
            EditorGUILayout.PropertyField(door.FindPropertyRelative("onOpened"), new GUIContent("Al abrirse"));
            EditorGUILayout.PropertyField(door.FindPropertyRelative("onClosed"), new GUIContent("Al cerrarse"));
        }
    }
}
