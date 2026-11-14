# Level Builder — Plan 2: Aberturas (puertas funcionales, ventanas, huecos) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Colocar sobre cualquier pared puertas (que abren en Play), ventanas (marco + cristal) y huecos vacíos; la pared abre su hueco con jambas al generarse, y todo sigue siendo editable: mover, redimensionar, alargar o partir la pared conserva las aberturas.

**Architecture:** Las aberturas viven en los datos (`WallSegment.openings`), así que partir, unir, fusionar o borrar paredes las mantiene coherentes dentro de `BuildingData` sin suscripciones a eventos (sustituye la nota del plan 1 sobre `SegmentSplit`). Una pasada pura `OpeningLayout` decide qué aberturas caben (las que no, quedan inválidas: se marcan en rojo, no cortan la pared y no se borran). `WallMeshBuilder` recorta huecos/muescas y genera jambas; `BuildingRenderer` crea las hojas de puerta (`Door`, Runtime) y las ventanas como objetos generados. Herramienta de editor `OpeningTool` para colocar/mover/redimensionar.

**Tech Stack:** Unity 6000.0.47f1 para desarrollo (paquete `"unity": "2022.3"`), C# 9, Unity Test Framework (EditMode), git. Mismo **RUN_TESTS** que el plan 1.

**Spec:** `docs/superpowers/specs/2026-11-14-level-builder-design.md` (§6.1 Aberturas, §6.2, §6.3 punto 3, §6.5, §6.6). Notas heredadas: `docs/superpowers/plans/2026-11-14-level-builder-1-verificacion-manual.md` § "Notas para los planes siguientes".

## Global Constraints

- Todas las del plan 1 siguen vigentes (2022.3, sin APIs exclusivas de Unity 6, Built-in/URP/HDRP sin referenciar paquetes de pipeline, Runtime sin `UnityEditor` fuera de `#if UNITY_EDITOR`, `HideFlags.DontSave` para lo generado, Undo en toda edición, tolerancia 0.01 m).
- Identidad de objeto: nunca `GetInstanceID()` directo (error en Unity 6.4+); usar `GeneratedFloorMarker.KeyOf`.
- Posición de una abertura = **centro, en metros desde el nodo inicial** del segmento (`center`). Alargar o acortar la pared no la desliza.
- Una abertura que no cabe es **inválida**: no se borra, no corta la pared, no genera hoja/ventana y se dibuja en rojo con el motivo.
- Defaults (presets): Puerta 0.9 × 2.1 m (alféizar 0); Puerta doble 1.6 × 2.1; Ventana 1.2 × 1.2 a 0.9 m; Ventana alta 0.6 × 0.6 a 1.6 m; Hueco 0.9 × 2.1 a 0. Ancho mínimo 0.3 m, alto mínimo 0.3 m.
- Márgenes de validez: ≥ 0.05 m de pared entre la abertura y la zona de unión de cada extremo, entre dos aberturas, y entre la parte superior de la abertura y la parte superior de la pared.
- Claves de cara nuevas: `{openingId}/jamb` (laterales y dintel del hueco), `{openingId}/sill` (cara superior del antepecho, solo si alféizar > 0), `{openingId}/threshold` (suelo del paso, solo en la mitad del grosor donde no hay región de suelo).
- Puertas: animación por código (sin Animator), métodos públicos `Open()`, `Open(Vector3 from)`, `Close()`, `Toggle()`, `Unlock()`, propiedad `Locked`, eventos `UnityEvent` `onOpened`/`onClosed`. Input clásico y Input System nuevo vía `versionDefines` (`com.unity.inputsystem` → `LEVELBUILDER_INPUT_SYSTEM`) + `ENABLE_LEGACY_INPUT_MANAGER` / `ENABLE_INPUT_SYSTEM`; nunca llamar a `UnityEngine.Input` si `ENABLE_LEGACY_INPUT_MANAGER` no está definido.
- Mallas generadas que se destruyen al regenerar: **solo las que tienen `HideFlags.DontSave`** (las de un prefab de usuario son assets y no se tocan).

## Review Focus

1. **Acortar una pared hasta que la puerta no cabe** → la puerta queda inválida (sin hueco, sin hoja, roja), y al alargarla vuelve sola con la misma posición. (Tests en Task 2 y Task 3.)
2. **Partir una pared con aberturas** (doble clic, o dibujar una pared que la cruza) → cada abertura pasa a la mitad donde está su centro con `center` recalculado; si la partición cae dentro de la abertura, se queda en la mitad de su centro y probablemente inválida, nunca se pierde. (Test en Task 1.)
3. **Abertura junto a una esquina o unión en T** → el hueco nunca corta la geometría de la unión (validez calculada con las distancias de corte reales de las uniones). (Test en Task 2.)
4. **Undo/Redo tras colocar, mover o borrar una abertura** → datos, huecos y hojas coherentes; ningún objeto generado guarda referencias a objetos de datos (busca por id). (Test en Task 5.)
5. **Prefab de hoja de puerta propio** → se instancia escalado al hueco y al regenerar se destruye la instancia pero nunca su malla/material (assets). (Test en Task 5.)

---

## Estructura de archivos

```
Packages/com.levelbuilder.core/
  Runtime/Data/WallOpening.cs          OpeningType, DoorKind, DoorSettings, WallOpening
  Runtime/Data/OpeningPresets.cs       presets por tipo
  Runtime/Data/WallSegment.cs          + List<WallOpening> openings
  Runtime/Data/BuildingData.cs         + operaciones de aberturas y su mantenimiento en split/join/merge
  Runtime/Geometry/OpeningLayout.cs    validez y rectángulos de hueco por segmento
  Runtime/Geometry/WallMeshBuilder.cs  + huecos, muescas, jambas, umbral; expone cortes de unión
  Runtime/Doors/DoorMotion.cs          estado/animación pura
  Runtime/Doors/DoorInput.cs           abstracción de input (clásico / nuevo)
  Runtime/Doors/Door.cs                MonoBehaviour
  Runtime/Openings/OpeningBuilder.cs   hoja por defecto, marco y cristal (MeshData) + encaje de prefab
  Runtime/Openings/GlassMaterial.cs    material transparente por pipeline
  Runtime/BuildingRenderer.cs          + objetos de aberturas; destrucción solo de mallas DontSave
  Runtime/LevelBuilder.Runtime.asmdef  + referencia Unity.InputSystem + versionDefines
  Editor/OpeningPlacement.cs           lógica pura: punto del plano → segmento + centro válido
  Editor/OpeningTool.cs                EditorTool
  Editor/BuildingOverlay.cs            + botones Puerta/Ventana/Hueco y preset
  Editor/BuildingInspector.cs          + abertura seleccionada
  Editor/BuildingEditorUtil.cs         + OpeningSelection
  Tests/Editor/OpeningDataTests.cs, OpeningLayoutTests.cs, WallOpeningMeshTests.cs,
               DoorTests.cs, OpeningRendererTests.cs, OpeningPlacementTests.cs
```

---

### Task 1: Datos de aberturas y su mantenimiento al editar paredes

**Files:**
- Create: `Runtime/Data/WallOpening.cs`, `Runtime/Data/OpeningPresets.cs`, `Tests/Editor/OpeningDataTests.cs`
- Modify: `Runtime/Data/WallSegment.cs`, `Runtime/Data/BuildingData.cs`

**Interfaces:**
- Produces:
  - `enum OpeningType { Door, Window, Empty }`, `enum DoorKind { Swing, Sliding }`, `enum DoorActivation { Proximity, Interact, Script }`
  - `[Serializable] class DoorSettings { DoorKind kind = Swing; bool hingeOnRight; bool bothWays = true; float openAngle = 95f; float speed = 2f /*vueltas de 0→1 por segundo*/; DoorActivation activation = Proximity; KeyCode key = KeyCode.E; string playerTag = "Player"; bool locked; GameObject leafPrefab; UnityEvent onOpened = new(); UnityEvent onClosed = new(); }`
  - `[Serializable] class WallOpening { string id; OpeningType type; float center; float width; float height; float sill; DoorSettings door = new(); GameObject windowPrefab; WallOpening Clone(); }` (`Clone` copia todo con id nuevo; los `UnityEvent` se copian vacíos)
  - `WallSegment.openings : List<WallOpening>` (inicializada)
  - `static class OpeningPresets { struct Preset { string name; OpeningType type; float width, height, sill; } IReadOnlyList<Preset> For(OpeningType t); WallOpening Create(Preset p, float center); }` con los valores de Global Constraints.
  - En `BuildingData`: `WallOpening AddOpening(FloorData f, string segmentId, WallOpening o)` (asigna id si falta; devuelve `o`), `bool RemoveOpening(FloorData f, string openingId)`, `(WallSegment seg, WallOpening o) FindOpening(FloorData f, string openingId)`, `bool MoveOpening(FloorData f, string openingId, string toSegmentId, float center)`, `static float Length(FloorData f, WallSegment s)`.

- [ ] **Step 1: Write the failing tests** en `OpeningDataTests` (pared 0,0→6,0 salvo indicación):
  - `AddOpening_AssignsIdAndStoresOnSegment` → id no vacío, `seg.openings.Count == 1`.
  - `SplitSegment_MovesOpeningsToHalfContainingCenter` → puertas con center 1.0 y 4.5; `SplitSegment` en x=3 → original conserva la de 1.0 (center 1.0); la mitad nueva tiene la de 4.5 con center 1.5.
  - `SplitThroughOpening_KeepsItOnCenterHalf` → ventana center 3.2 ancho 1.2; split en x=3 → en la mitad nueva con center 0.2 (sin borrarla).
  - `AddSegmentCrossing_SplitsAndKeepsOpenings` → pared vertical (3,-2)→(3,2) cruzando: la abertura de center 4.5 sigue existiendo con center 1.5 en la mitad este.
  - `JoinSplit_RestoresOpeningOffsets` → tras split y `JoinSplit`, center vuelve a 4.5 en el segmento original.
  - `RemoveSegment_RemovesItsOpenings` y `RemoveNode_RemovesOpeningsOfIncidentWalls` → `FindOpening` devuelve null.
  - `ResolveNode_DuplicateWallKeepsOpenings` → dos paredes entre los mismos nodos (una invertida, creada arrastrando un nodo encima de otro); la abertura de la que se elimina pasa a la superviviente con center `len − center` si iba en sentido opuesto.
  - `MoveOpening_ToOtherSegment` → sale de una pared y entra en otra con el center dado.
  - `AddFloor_CopiesWindowsOfExteriorWallsOnly` → planta 0 con ventana y puerta en paredes exteriores y una ventana en una interior → la planta 1 solo tiene la ventana exterior (id nuevo, mismo center/tamaño), ninguna puerta.
  - `OpeningPresets_DefaultsMatchSpec` → Puerta 0.9/2.1/0; Ventana 1.2/1.2/0.9; Hueco 0.9/2.1/0.

- [ ] **Step 2: Run** RUN_TESTS `-testFilter OpeningDataTests` → FAIL (tipos no definidos).

- [ ] **Step 3: Implement.** `SplitSegmentAt` reparte `openings` por center (`center >= splitLen` → mitad nueva con `center - splitLen`), antes de invocar `SegmentSplit`. `JoinSplit` devuelve las de la mitad añadida con `center + len(original)`. `RemoveDegenerateSegments` pasa a ser de instancia y traslada las aberturas del duplicado eliminado al superviviente (invirtiendo center si el sentido es opuesto). `CopyExteriorFrom` clona solo `OpeningType.Window`. Las demás operaciones (`RemoveSegment`, `RemoveNode`) no necesitan código: las aberturas van con el segmento.

- [ ] **Step 4: Run** RUN_TESTS completo → PASS (los 115 anteriores incluidos).

- [ ] **Step 5: Commit** `feat: wall openings data kept through split/join/merge`

---

### Task 2: Validez y disposición de aberturas (`OpeningLayout`)

**Files:**
- Create: `Runtime/Geometry/OpeningLayout.cs`, `Tests/Editor/OpeningLayoutTests.cs`
- Modify: `Runtime/Geometry/WallMeshBuilder.cs` (exponer cortes)

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `WallMeshBuilder.ComputeCuts(FloorData f) : Dictionary<string segmentId, (float start, float end)>` — la misma `EndInfo.cut` que ya usa `Build` (refactor sin cambio de geometría: `Build` llama a la misma ruta).
  - `enum OpeningIssue { None, TooSmall, OutsideWall, IntoJoint, TooTall, Overlap }`
  - `struct OpeningRect { WallOpening opening; float u0, u1, v0, v1; bool valid; OpeningIssue issue; }` (u: metros desde el nodo inicial; v: altura sobre el suelo de la planta)
  - `static class OpeningLayout { Dictionary<string segmentId, List<OpeningRect>> Compute(FloorData f); }` — ordenadas por `u0`.
  - Constantes `OpeningLayout.Margin = 0.05f`, `MinSize = 0.3f`.

- [ ] **Step 1: Write the failing tests:**
  - `CenteredDoor_IsValid` → pared 6 m aislada, puerta 0.9×2.1 center 3 → valid, u0 2.55, u1 3.45, v0 0, v1 2.1.
  - `ShorteningWall_MakesDoorInvalid_LengtheningRestores` → `MoveNode` del extremo a x=3.2 → issue `IntoJoint` u `OutsideWall`, valid false; volver a x=6 → valid.
  - `DoorNextToCorner_IntoJoint` → rectángulo 6×4; puerta center 0.3 en la pared sur → `IntoJoint`; center 0.65 → valid (corte de esquina = 0.1 + margen 0.05 ≤ 0.2).
  - `DoorNextToTJunction_UsesJointCut` → pared interior que llega en T a x=3 de la pared sur (la pared sur queda partida en x=3); puerta 0.9 en la mitad este con center 0.5 → `IntoJoint`; center 0.7 → valid (corte de la T = 0.1, medio grosor del montante).
  - `TooTall_WhenTopWithinMarginOfWallTop` → ventana sill 1.0 alto 1.98 en pared de 3 m → `TooTall`; alto 1.9 → valid.
  - `Overlap_SecondInListIsInvalid` → dos puertas solapadas: la primera de la lista valid, la segunda `Overlap`.
  - `TooSmall` → ancho 0.2 → `TooSmall`.

- [ ] **Step 2: Run** `-testFilter OpeningLayoutTests` → FAIL.
- [ ] **Step 3: Implement.** Rango útil de un segmento `[startCut + Margin, len − endCut − Margin]`; altura útil `[0, H − Margin]` con `H = seg.Height(f)`. Validar en orden de lista (el solape lo pierde la que aparece después; margen entre aberturas `Margin`).
- [ ] **Step 4: Run** RUN_TESTS completo → PASS (geometría de paredes intacta: los tests de `WallMeshBuilderTests` siguen verdes).
- [ ] **Step 5: Commit** `feat: opening layout and validity`

---

### Task 3: Huecos, muescas, jambas y umbral en las paredes

**Files:**
- Modify: `Runtime/Geometry/WallMeshBuilder.cs`
- Create: `Tests/Editor/WallOpeningMeshTests.cs`

**Interfaces:**
- Consumes: `OpeningLayout.Compute` (Task 2), `PlanarFaces.Compute(f).regions` (plan 1) para el umbral.
- Produces: `WallMeshBuilder.Build(FloorData f, float baseY)` sin cambio de firma; ahora con huecos para aberturas válidas y claves `{openingId}/jamb`, `/sill`, `/threshold`.

- [ ] **Step 1: Write the failing tests** (reutiliza `WallMeshBuilderTests.AssertWatertight` y la comprobación de winding):
  - `Window_CutsHoleInBothSides` → ventana 1.2×1.2 a 0.9 en pared 6 m: ningún triángulo de `/L` ni `/R` tiene su centroide dentro del rectángulo del hueco; área de `/L` = 6×3 − 1.44 (±1e-3) cuando la pared está aislada (corrige por los extremos: pared aislada = rectángulo).
  - `Door_IsNotchNotHole` → puerta a nivel del suelo: el contorno de `/L` llega a v=0 a ambos lados de la puerta y no hay triángulos con centroide en el paso; área = 18 − 0.9×2.1.
  - `Opening_HasJambFacesThroughThickness` → `{id}/jamb` existe; sus normales apuntan hacia el interior del hueco (±d en los laterales, −up en el dintel); área de `/jamb` = (1.2 + 1.2 + 1.2) × 0.2 = 0.72 (dos laterales + dintel) y `{id}/sill` de 1.2 × 0.2 a y = 0.9 con normal +up.
  - `Openings_KeepWallWatertight` → `AssertWatertight` pasa con una ventana y una puerta (la puerta abre el borde inferior: el chequeo admite aristas libres en y = baseY, que es donde queda el paso).
  - `InvalidOpening_DoesNotCut` → puerta que no cabe → malla idéntica (mismo número de vértices) a la pared sin aberturas.
  - `Threshold_OnlyWhereNoFloorRegion` → rectángulo cerrado con puerta en pared exterior → `{id}/threshold` existe y cubre solo la mitad exterior (área 0.9 × 0.1); puerta en pared interior entre dos habitaciones → no hay `/threshold` (los dos suelos llegan al eje).
  - `Opening_UvsInMetres` → en `/R`, el UV del vértice de la esquina inferior izquierda del hueco = (u0, sill).

- [ ] **Step 2: Run** `-testFilter WallOpeningMeshTests` → FAIL.
- [ ] **Step 3: Implement.** En `BuildSegment`: ventanas/huecos con `sill > 0` → agujeros para `Triangulator.Triangulate(outline, holes, …)`; aberturas con `sill == 0` → se insertan en el borde inferior del contorno (`(u0,0),(u0,v1),(u1,v1),(u1,0)` en orden de u creciente). En la cara izquierda la coordenada u es la misma (el espejo de UV se aplica después, como hoy). Jambas: quads verticales entre las dos caras (`±n·half`) en u0 y u1, dintel horizontal en v1 (normal −up), antepecho en v0 si sill > 0 (normal +up). Umbral: por cada mitad del grosor (izquierda/derecha), si el punto `A + d·center ± n·half/2` no está dentro de ninguna región (polígono exterior y fuera de sus agujeros), quad horizontal en y = baseY con normal +up.
- [ ] **Step 4: Run** RUN_TESTS completo → PASS.
- [ ] **Step 5: Commit** `feat: cut openings into walls with jambs and thresholds`

---

### Task 4: Componente `Door` (runtime)

**Files:**
- Create: `Runtime/Doors/DoorMotion.cs`, `Runtime/Doors/DoorInput.cs`, `Runtime/Doors/Door.cs`, `Tests/Editor/DoorTests.cs`
- Modify: `Runtime/LevelBuilder.Runtime.asmdef` (`"references": ["Unity.InputSystem"]`, `versionDefines: [{ "name": "com.unity.inputsystem", "expression": "1.0.0", "define": "LEVELBUILDER_INPUT_SYSTEM" }]`)

**Interfaces:**
- Produces:
  - `class DoorMotion { float Progress {get;} /*0 cerrada .. 1 abierta*/ int Direction {get;} /*+1 / −1 (batiente a ambos lados)*/ bool IsOpen {get;} bool Locked; void Open(int direction); void Close(); bool Step(float dt, float speed, out bool justOpened, out bool justClosed); }` — `Open` con `Locked` no hace nada; cambiar de dirección solo con `Progress == 0`.
  - `static class DoorInput { bool InteractPressed(KeyCode key); }` — Input System (si `LEVELBUILDER_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM`): `Keyboard.current` con la tecla equivalente (mapeo de `KeyCode` A–Z, 0–9, Space, Return, E por defecto) o gamepad `buttonWest`; si no, `ENABLE_LEGACY_INPUT_MANAGER` → `Input.GetKeyDown`; si ninguno, false.
  - `[DisallowMultipleComponent] class Door : MonoBehaviour { DoorSettings settings; Transform leaf; Vector3 leafSize; bool IsOpen; bool Locked {get;set;} void Open(); void Open(Vector3 from); void Close(); void Toggle(); void Unlock(); UnityEvent onOpened, onClosed; void Tick(float dt); }` — `Update` llama a `Tick(Time.deltaTime)`. Batiente: rota `leaf` alrededor de la bisagra (eje Y local; bisagra en el borde según `hingeOnRight`) hasta `openAngle × Direction`; con `bothWays`, `Open(from)` elige la dirección que se aleja de `from`. Corredera: desplaza `leaf` en X local `leafSize.x × 0.95`.
  - Activación: `Proximity` → trigger (BoxCollider `isTrigger`, tamaño ancho × alto × 3 m centrado) abre al entrar un collider cuyo `tag == settings.playerTag` (o cualquiera si está vacío) y cierra al salir el último; `Interact` → dentro del trigger, `DoorInput.InteractPressed(settings.key)` hace `Toggle`; `Script` → solo métodos públicos.

- [ ] **Step 1: Write the failing tests:**
  - `DoorMotion_OpensOverTime` → `Open(1)`, `Step(0.25f, 2f)` → Progress 0.5; otro `Step(0.5f, 2f)` → 1, `justOpened` true una sola vez.
  - `DoorMotion_Locked_DoesNotOpen` y `Unlock_ThenOpens`.
  - `DoorMotion_DirectionOnlyChangesWhenClosed`.
  - `Door_Swing_RotatesLeafAroundHinge` → GameObject con `Door` y hoja de 0.9 m; `Open()` + `Tick(1f)` → `leaf.localRotation` ≈ 95° en Y y la bisagra (borde) no se mueve (±1e-4).
  - `Door_BothWays_OpensAwayFromPosition` → `Open(from)` con from delante → ángulo negativo; detrás → positivo.
  - `Door_Sliding_MovesLeaf` → `localPosition.x` desplazado 0.855 tras abrir del todo.
  - `Door_EventsFire` → `onOpened` y `onClosed` se invocan una vez cada uno.
- [ ] **Step 2: Run** `-testFilter DoorTests` → FAIL.
- [ ] **Step 3: Implement.** Comprobar que el paquete compila sin Input System instalado (el proyecto de desarrollo no lo tiene): la referencia sin resolver se ignora y el código queda tras `#if`.
- [ ] **Step 4: Run** RUN_TESTS completo → PASS, 0 warnings.
- [ ] **Step 5: Commit** `feat: runtime Door component`

---

### Task 5: Objetos generados de puertas y ventanas

**Files:**
- Create: `Runtime/Openings/OpeningBuilder.cs`, `Runtime/Openings/GlassMaterial.cs`, `Tests/Editor/OpeningRendererTests.cs`
- Modify: `Runtime/BuildingRenderer.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - `static class OpeningBuilder { MeshData DefaultLeaf(float width, float height, float thickness = 0.04f); MeshData WindowFrame(float width, float height, float bar = 0.05f, float depth = 0.08f); MeshData Glass(float width, float height); void FitToBox(GameObject instance, Vector3 size); }` — `FitToBox` escala la instancia para que los bounds de sus renderers ocupen `size` (x ancho, y alto; z se conserva) con el pivote abajo en el centro.
  - `static class GlassMaterial { Material Create(Material basedOn); }` — clona `basedOn` y lo configura transparente según el shader (`Standard`: modo Transparent; `Universal Render Pipeline/Lit`: `_Surface = 1`, keyword `_SURFACE_TYPE_TRANSPARENT`; `HDRP/Lit`: `_SurfaceType = 1`, keywords `_SURFACE_TYPE_TRANSPARENT` y `_BLENDMODE_ALPHA`), color (0.6, 0.8, 0.9, 0.3), `renderQueue = 3000`, `HideFlags.DontSave`.
  - `BuildingRenderer`: nuevos campos `public Material glassMaterial;` (null → `GlassMaterial.Create(defaultMaterial)` cacheado), jerarquía `Floor N/Doors/{openingId}` y `Floor N/Windows/{openingId}` (§6.2) para cada abertura **válida** de tipo Door o Window. Puerta: objeto en la base del hueco orientado con la pared (forward = normal de la cara `/R`), con `Door` (settings copiados, `onOpened/onClosed` reenvían a los eventos de la abertura **buscándola por id** en `building.data` en el momento de invocar), hoja = `leafPrefab` instanciado y encajado o `DefaultLeaf` (hueco − 0.01 m por lado), `BoxCollider` sólido en la hoja. Ventana: marco + cristal (o `windowPrefab` encajado), centrados en el grosor.
  - `DestroyFloorObject`: destruye solo mallas con `HideFlags.DontSave`.
  - Todo objeto creado (incluidas instancias de prefab y sus hijos) recibe `GenFlags` recursivamente.

- [ ] **Step 1: Write the failing tests:**
  - `Door_CreatesLeafWithDoorComponent` → cuadrado con puerta válida → existe `Floor 0/Doors/{id}` con `Door`, su hoja con `BoxCollider` y tamaño ≈ (0.88, 2.09, 0.04).
  - `InvalidOpening_CreatesNothing` → puerta que no cabe → no existe el objeto.
  - `Window_CreatesFrameAndGlass` → dos renderers; el del cristal usa un material con `renderQueue >= 3000`.
  - `CustomLeafPrefab_IsFittedAndItsAssetsSurviveRegenerate` → "prefab" = GameObject del test con un cubo 1×1×1 cuya malla tiene `hideFlags = None` (como un asset) → instancia escalada al hueco (bounds ≈ ancho × alto); tras `Regenerate()` dos veces, la malla del prefab sigue viva (`!= null`) y no hay instancias duplicadas.
  - `DoorEvents_ForwardToOpeningById` → listener en `opening.door.onOpened`; se reemplaza `b.data` por una copia (simula Undo) y se regenera; abrir la puerta generada invoca el listener de la abertura de la copia.
  - `Regenerate_NoLeakedMeshes` → contar mallas `DontSave` antes/después de 3 regeneraciones con aberturas: igual.
- [ ] **Step 2: Run** `-testFilter OpeningRendererTests` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** RUN_TESTS completo → PASS.
- [ ] **Step 5: Commit** `feat: generate door leaves and windows`

---

### Task 6: Herramienta de aberturas en el editor

**Files:**
- Create: `Editor/OpeningPlacement.cs`, `Editor/OpeningTool.cs`, `Tests/Editor/OpeningPlacementTests.cs`
- Modify: `Editor/BuildingOverlay.cs`, `Editor/BuildingInspector.cs`, `Editor/BuildingEditorUtil.cs`
- Create (docs): `docs/superpowers/plans/2026-11-14-level-builder-2-verificacion-manual.md`

**Interfaces:**
- Consumes: Tasks 1–5; `BuildingEditorUtil.Mutate`, `GuiToPlan`, `ActiveFloorIndex` (plan 1).
- Produces:
  - `static class OpeningPlacement { bool Pick(FloorData f, Vector2 plan, float maxDistance, out string segmentId, out float center); float SnapCenter(float center, float step = 0.1f); bool Fits(FloorData f, string segmentId, WallOpening probe); string PickOpening(FloorData f, Vector2 plan, float maxDistance); }` — `Pick` elige el segmento más cercano dentro de `maxDistance` (0.5 m) y proyecta; `PickOpening` devuelve la abertura cuyo hueco (en planta, dentro del grosor ± 0.2 m) contiene el punto.
  - `static class OpeningSelection { Building building; string floorId; string openingId; event Action Changed; }` (mismo patrón que `WallSelection`).
  - `class OpeningTool : EditorTool` — estado: tipo y preset activos (`OpeningType`, índice de preset), puestos desde el overlay.
    - Pasar el ratón por una pared: previsualización del hueco en verde (rojo si `Fits` es false).
    - Clic: si hay abertura bajo el ratón → la selecciona; si no → coloca una nueva con el preset (Undo "Colocar puerta/ventana/hueco"), centro ajustado a 0.1 m (Ctrl: sin ajuste).
    - Arrastrar una abertura seleccionada: la mueve a lo largo de su pared o a otra (`MoveOpening`), un único paso de Undo por arrastre.
    - Manipuladores (`Handles.Slider`) en los bordes izquierdo/derecho (ancho, simétrico respecto al centro), superior (alto) y, en ventanas, inferior (alféizar); mínimos de Global Constraints.
    - Supr/Retroceso: borra la abertura seleccionada (nunca el edificio). Esc: deselecciona.
    - Todas las aberturas de la planta activa se dibujan como rectángulo; inválidas en rojo con el motivo (`OpeningIssue`) en etiqueta.
  - Overlay: fila "Abrir hueco" con tres botones de modo (Puerta / Ventana / Hueco) que activan `OpeningTool` con ese tipo, y un desplegable de preset para el tipo activo.
  - Inspector: si hay abertura seleccionada, sección con tipo, ancho, alto, alféizar (ventana), centro, y para puertas `DoorSettings` completo vía `SerializedProperty` (`data.floors[i].segments[j].openings[k].door`, incluye los `UnityEvent` y `leafPrefab`), para ventanas `windowPrefab`; botón "Borrar abertura"; aviso con el motivo si es inválida.

- [ ] **Step 1: Write the failing tests:**
  - `Pick_ProjectsOntoNearestWall` → rectángulo 6×4, punto (2.3, 0.2) → pared sur, center 2.3.
  - `Pick_TooFar_ReturnsFalse` → punto (3, 2) → false.
  - `SnapCenter_RoundsToTenCentimetres` → 2.34 → 2.3.
  - `Fits_FalseNearCorner_TrueInMiddle`.
  - `PickOpening_FindsDoorUnderPoint` → puerta center 3 en la pared sur; punto (3.2, 0.05) → su id; punto (5, 0) → null.
  - `PlaceOpening_IsOneUndoStep` → con `BuildingEditorUtil.Mutate`, colocar y `Undo.PerformUndo()` → la pared no tiene aberturas; `Undo.PerformRedo()` → la tiene (patrón de los tests de Undo del plan 1).
- [ ] **Step 2: Run** `-testFilter OpeningPlacementTests` → FAIL.
- [ ] **Step 3: Implement** `OpeningPlacement`, luego `OpeningTool`, overlay e inspector.
- [ ] **Step 4: Run** RUN_TESTS completo → PASS, 0 warnings.
- [ ] **Step 5: Write** `2026-11-14-level-builder-2-verificacion-manual.md` (pasos en Unity): colocar puerta/ventana/hueco con cada preset; mover a lo largo y a otra pared; manipuladores; acortar la pared hasta invalidar y alargar; partir la pared con doble clic entre dos aberturas; Undo/Redo de cada uno; Play: la puerta abre al acercarse con un `CharacterController` con tag Player, en modo Interact con E, a ambos lados; ventana con cristal visible; prefab de hoja propio; prefab del edificio y recompilación sin errores ni duplicados; abrir en 2022.3 y en una versión 6.4+.
- [ ] **Step 6: Commit** `feat: opening editor tool, overlay and inspector`

---

## Decisiones tomadas en este plan (para revisar)

- Aberturas guardadas dentro de cada `WallSegment` en lugar de una lista aparte con `SegmentSplit`: así partir/unir/borrar las mantiene sin suscripciones que se pierdan con el Undo. El evento `SegmentSplit` se conserva para el plan 4.
- Al crear una planta se copian las **ventanas** de las paredes exteriores, no las puertas.
- Hoja de puerta propia: se **escala** al hueco (no se ajusta el hueco al prefab).
- Uniones en T: la validez usa el corte máximo del extremo (conservador): una abertura no puede acercarse a menos de ~0.15 m del nodo de la T, tampoco por la cara a la que no llega el montante. Una abertura nunca cruza un nodo (cada mitad es otro segmento).
- Cristal por defecto: material transparente creado en ejecución a partir del material por defecto; si el pipeline elimina las variantes transparentes en la build, el usuario asigna su propio `glassMaterial` (anotado para la documentación del plan 5).
