# Level Builder — Plan 4: Materiales, pintado y texturas propias Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pintar con un clic cualquier cara (lado de pared, suelo, techo, escalón, jamba) o la pieza entera con materiales de una biblioteca, con texturas a escala real que no se estiran, y añadir materiales y texturas propios arrastrándolos a una paleta. Los materiales se conservan al editar (partir, unir, mover paredes, cambiar alturas).

**Architecture:** El material se guarda **en el dato dueño de la cara** (`WallSegment`, `WallOpening`, `StairData`) para heredar gratis partir/unir/invertir; los suelos/techos se pintan por **punto semilla** (la región que contiene el punto), así la pintura sobrevive a cambios de la clave de región. `BuildingRenderer` resuelve clave de cara → material, construye una submalla por material y escala las UV (metros / tamaño real del material). Un componente `FaceKeyMap` en cada objeto generado traduce triángulo → clave para pintar con clic. `MaterialLibrary` (ScriptableObject) + ventana **Paleta** + herramienta **Pintar**.

**Tech Stack:** igual que planes anteriores (Unity 6000.0.47f1 desarrollo, paquete 2022.3, NUnit EditMode). **RUN_TESTS** igual.

**Spec:** `docs/superpowers/specs/2026-11-14-level-builder-design.md` §6.1 (materiales por cara, identidad estable), §6.2 (UV a escala real), §6.3 punto 5 (Pintar), §6.4 (biblioteca, paleta, agregar propios, biblioteca por defecto). Notas heredadas: z-fighting forjado/techo (plan 1), identidad de región inestable (plan 1), L/R ↔ interior/exterior (plan 1), cristal rosa al cambiar el material por defecto (plan 2).

## Global Constraints

- Todas las de planes anteriores (2022.3, Built-in/URP/HDRP sin referenciar paquetes de pipeline, Runtime sin `UnityEditor` fuera de `#if UNITY_EDITOR`, `DontSave`, solo se destruyen mallas `DontSave`, Undo en toda edición, UI en español, nunca `GetInstanceID()` directo).
- Una cara sin material asignado usa el `defaultMaterial` del renderer (comportamiento actual).
- Escala real: UV final = UV en metros / `tamaño` del material (Vector2, metros que cubre la textura). Tamaño por defecto **1 × 1 m** si el material no está en la biblioteca.
- Pintar: **clic = una cara**; **Shift+clic = pieza entera** (pared: ambos lados, cantos y tapa; región: suelo, techo y cantos del forjado; escalera: todas sus caras; abertura: jambas, alféizar y umbral); **Alt+clic = cuentagotas** (toma el material de la cara). Un paso de Undo por clic.
- Claves de cara existentes (no se renombran): `{seg}/L|R|start|end|top`, `node:{id}/top|side`, `region:{key}/floor|floor/under|floor/edge|ceiling|roof|roof/edge`, `{opening}/jamb|sill|threshold`, `stair:{id}/tread|riser|side|landing|rail`.
- Las caras de nodo (`node:*`) usan el material de la tapa (`/top`) del primer segmento del nodo con material asignado; si ninguno, el por defecto.
- Materiales creados desde texturas: shader del pipeline activo (`Standard`, `Universal Render Pipeline/Lit`, `HDRP/Lit`), guardados en la carpeta de la biblioteca (por defecto `Assets/LevelBuilder/Materiales`). Reconocimiento por sufijo de nombre (sin distinguir mayúsculas): color `_Albedo|_BaseColor|_Diffuse|_Color|_Col` o sin sufijo; normal `_Normal|_Nrm|_Nor|_N`; rugosidad `_Rough|_Roughness`; metal `_Metal|_Metallic`; AO `_AO|_Occlusion`; altura `_Height|_Disp`.

## Review Focus

1. **Partir una pared pintada** (doble clic o pared que la cruza) → las dos mitades conservan los materiales de cada lado; unir con "Quitar pared" conserva el del tramo superviviente; invertir un tramo intercambia L↔R. (Task 1.)
2. **Mover paredes de una habitación pintada** → su suelo y techo siguen pintados mientras la semilla quede dentro; si la habitación se parte en dos, la mitad con la semilla conserva el material. (Tasks 1–2.)
3. **Cambiar el tamaño de una pared** → la textura se repite, no se estira (UV en metros / tamaño). (Task 2.)
4. **Suelo de planta N y techo de N−1 coplanares** → sin parpadeo con materiales distintos. (Task 2.)
5. **Material borrado del proyecto** (referencia nula) → la cara vuelve al material por defecto, sin errores. (Task 2.)

---

### Task 1: Datos de materiales por cara y biblioteca

**Files:**
- Create: `Runtime/Data/FaceMaterials.cs`, `Runtime/Materials/MaterialLibrary.cs`, `Tests/Editor/FaceMaterialDataTests.cs`
- Modify: `Runtime/Data/WallSegment.cs`, `Runtime/Data/WallOpening.cs`, `Runtime/Data/StairData.cs`, `Runtime/Data/FloorData.cs`, `Runtime/Data/BuildingData.cs`, `Runtime/Building.cs`

**Interfaces:**
- Produces:
  - `[Serializable] class WallFaceMaterials { Material left, right, caps, top; }` en `WallSegment.materials`; `[Serializable] class OpeningFaceMaterials { Material jamb; }` en `WallOpening.materials`; `[Serializable] class StairFaceMaterials { Material tread, riser, side, landing, rail; }` en `StairData.materials`.
  - `[Serializable] class RegionPaint { Vector2 seed; Material floor, ceiling, edge; }` y `FloorData.regionPaints : List<RegionPaint>`.
  - `Building.library : MaterialLibrary`.
  - `class MaterialLibrary : ScriptableObject { [Serializable] class Entry { string name; string category; Material material; Vector2 size = Vector2.one; } List<Entry> entries; string saveFolder = "Assets/LevelBuilder/Materiales"; Vector2 SizeOf(Material m) /*1×1 si no está*/; Entry Find(Material m); Entry Add(Material m, string name = null); }` (Runtime; creación de assets en el Editor).
  - `static class FaceMaterialRules { Material Get(BuildingData d, FloorData f, string faceKey, Func<FloorData, List<Region>> regions); void Set(BuildingData d, FloorData f, string faceKey, Material m, Func<...> regions, Vector2? regionSeed = null); IEnumerable<string> PieceKeys(...) }` — traduce claves a campos: `/L`→left, `/R`→right, `/start|/end`→caps, `/top`→top; `region:*/floor|floor/under`→floor, `/ceiling|/roof`→ceiling, `/floor/edge|/roof/edge`→edge (busca el `RegionPaint` cuya semilla cae en la región; `Set` crea uno con la semilla dada o el punto interior de la región); `{opening}/*`→jamb; `stair:{id}/x`→campo x.
  - Mantenimiento: `SplitSegmentAt` copia `materials` a la mitad nueva; `ReverseSegment` intercambia left↔right; `CopyExteriorFrom` copia los materiales de los segmentos copiados; `JoinSplit`/`RemoveWall` conservan los del superviviente; `StairAutoFix.Clone` copia `materials`.

- [ ] **Step 1: Write the failing tests:** `Split_CopiesWallMaterials`, `Reverse_SwapsLeftRight`, `Join_KeepsSurvivorMaterials`, `NewFloor_CopiesExteriorWallMaterials`, `RegionPaint_FollowsRoomWhenWallsMove` (pinta suelo, mueve un nodo dentro de tolerancia → `Get` devuelve el material), `RegionPaint_SplitRoom_SeedSideKeepsIt`, `FaceKeys_MapToFields` (cada familia de clave), `Library_SizeOfUnknownIsOne`, `JsonRoundTrip_KeepsMaterialReferences` (con un material en memoria, `EditorJsonUtility`).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** RUN_TESTS → PASS.
- [ ] **Step 5: Commit** `feat: per-face material data and material library`

---

### Task 2: Submallas por material, escala real y z-fighting

**Files:**
- Modify: `Runtime/Geometry/MeshData.cs`, `Runtime/Geometry/SlabMeshBuilder.cs`, `Runtime/BuildingRenderer.cs`
- Create: `Runtime/FaceKeyMap.cs`, `Tests/Editor/MaterialRenderTests.cs`

**Interfaces:**
- Produces:
  - `Mesh MeshData.ToMesh(Func<string, Material> materialOf, Func<Material, Vector2> sizeOf, out Material[] materials, out List<(string key, int submesh, int firstTri, int triCount)> map)` — una submalla por material distinto (orden estable), UV divididas por el tamaño del material de cada grupo (vértices duplicados por grupo ya lo están por cara). La sobrecarga actual `ToMesh()` se mantiene.
  - `class FaceKeyMap : MonoBehaviour { List<Entry> entries /*key, submesh, firstTri, triCount*/; string KeyAt(int submesh, int triangleIndexInSubmesh); }` añadido (oculto, `DontSave`) a cada objeto de malla generado (paredes, suelo, techo, escalones, barandillas, jambas no — van en el muro).
  - `BuildingRenderer`: resuelve materiales con `FaceMaterialRules.Get` (falta/null → `defaultMaterial`) y tamaños con `building.library.SizeOf`; cambiar `defaultMaterial` o `library` → regeneración completa (corrige el cristal de otras plantas).
  - `SlabMeshBuilder.BuildFloor(f, holes, below)`: omite `/floor/under` de una región cuyo punto interior cae en una región **cerrada** (no `open`) de la planta de abajo (el techo de abajo ya la cubre); se mantiene en voladizos y en planta 0.

- [ ] **Step 1: Write the failing tests:** `PaintedWall_UsesItsMaterialAsSubmesh` (dos materiales → 2 submallas, `sharedMaterials` en orden), `UvScaleFollowsMaterialSize` (material de 2×2 m en pared de 6 m → u máximo 3), `LongerWall_RepeatsTexture` (alargar la pared aumenta u, no lo estira), `MissingMaterial_FallsBackToDefault`, `FaceKeyMap_ReturnsKeyForTriangle`, `FloorUnder_OmittedOverClosedRoomBelow`, `FloorUnder_KeptOnOverhang`, `ChangingDefaultMaterial_RegeneratesAllFloorsGlass`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** RUN_TESTS → PASS.
- [ ] **Step 5: Commit** `feat: per-material submeshes, real-scale UVs and no slab z-fighting`

---

### Task 3: Materiales desde texturas y biblioteca por defecto (Editor)

**Files:**
- Create: `Editor/Materials/TextureMaterialFactory.cs`, `Editor/Materials/DefaultLibrary.cs`, `Tests/Editor/TextureMaterialFactoryTests.cs`

**Interfaces:**
- Produces:
  - `static class TextureMaterialFactory { enum MapKind { Albedo, Normal, Roughness, Metallic, Occlusion, Height, Unknown } MapKind Classify(string textureName); string BaseName(string textureName); Dictionary<string, Dictionary<MapKind, Texture2D>> Group(IEnumerable<Texture2D> textures) /*por nombre base*/; Material Create(string baseName, Dictionary<MapKind, Texture2D> maps, string folder) /*shader del pipeline; marca las normales como NormalMap en su importer; guarda el .mat; devuelve el asset*/; Shader PipelineLitShader(); }`.
  - Asignación por pipeline: Standard `_MainTex`, `_BumpMap` (+`_NORMALMAP`), `_OcclusionMap`, `_ParallaxMap`, `_MetallicGlossMap`; URP Lit `_BaseMap`, `_BumpMap`, `_OcclusionMap`, `_MetallicGlossMap`; HDRP Lit `_BaseColorMap`, `_NormalMap`. Rugosidad: se guarda como textura sin conversión (nota en el Inspector: Unity usa suavidad = 1 − rugosidad).
  - `static class DefaultLibrary { MaterialLibrary CreateOrGet(string folder = "Assets/LevelBuilder") }` — crea `LevelBuilder Biblioteca.asset` con 4 materiales greybox (Gris claro, Gris oscuro, Suelo, Acento) sobre una textura de rejilla de 1 m generada por código (PNG guardado), tamaño 1×1 m; si ya existe, la devuelve.

- [ ] **Step 1: Write the failing tests:** `Classify_RecognisesSuffixes` (tabla de nombres: `Brick_Normal`, `brick_n`, `Brick_Rough`, `Brick_AO`, `Brick_BaseColor`, `Brick` → Albedo, `Brick_Foo` → Unknown), `Group_ByBaseName`, `Create_UsesPipelineShaderAndAssignsMaps` (Built-in en el proyecto de desarrollo: `_MainTex` y `_BumpMap` asignados, keyword `_NORMALMAP`, asset en la carpeta; limpia la carpeta temporal en TearDown), `DefaultLibrary_CreatesFourEntriesOnce`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** RUN_TESTS → PASS.
- [ ] **Step 5: Commit** `feat: materials from textures and default greybox library`

---

### Task 4: Selección de cara bajo el ratón

**Files:**
- Create: `Editor/Materials/FacePicker.cs`, `Tests/Editor/FacePickerTests.cs`

**Interfaces:**
- Produces: `static class FacePicker { bool Pick(Building b, Ray worldRay, out string faceKey, out FloorData floor, out Vector3 hitPoint); }` — recorre los objetos generados con `FaceKeyMap` de las plantas visibles, intersecta el rayo con sus triángulos en espacio local (Möller–Trumbore, el más cercano), y traduce (submalla, triángulo) a clave con `FaceKeyMap.KeyAt`.

- [ ] **Step 1: Write the failing tests:** `Pick_WallSide_ReturnsLeftOrRight` (rayo horizontal contra la pared sur desde dentro → `/L` o `/R` según el lado, comprobado con la normal), `Pick_Floor_ReturnsRegionFloorKey`, `Pick_NearestHitWins`, `Pick_Miss_ReturnsFalse`, `Pick_IgnoresHiddenFloors`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** RUN_TESTS → PASS.
- [ ] **Step 5: Commit** `feat: pick the generated face under the mouse`

---

### Task 5: Ventana Paleta

**Files:**
- Create: `Editor/Materials/PaletteWindow.cs`, `Editor/Materials/PaletteState.cs`, `Tests/Editor/PaletteStateTests.cs`

**Interfaces:**
- Produces:
  - `static class PaletteState { Material active; MaterialLibrary library; event Action Changed; }`.
  - `class PaletteWindow : EditorWindow` (menú `Window > Level Builder > Paleta` y botón en el overlay): selector de biblioteca (por defecto la del edificio seleccionado; botón "Crear biblioteca por defecto"), búsqueda por nombre, filtro por categoría, rejilla de miniaturas (`AssetPreview`), clic = material activo, menú contextual (renombrar, categoría, tamaño real, quitar de la biblioteca). **Arrastrar** a la ventana: `Material` → `library.Add`; texturas → `TextureMaterialFactory.Group` + `Create` por grupo y se añaden (con Undo sobre la biblioteca). Campo "Carpeta de guardado".
  - `static class PaletteOps { Entry AddDropped(MaterialLibrary lib, UnityEngine.Object[] objects, out int created); IEnumerable<Entry> Filter(MaterialLibrary lib, string search, string category); }` (lógica testeable).

- [ ] **Step 1: Write the failing tests:** `AddDropped_Material_AddsEntryOnce`, `AddDropped_Textures_CreatesOneMaterialPerBaseName`, `Filter_BySearchAndCategory`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** RUN_TESTS → PASS.
- [ ] **Step 5: Commit** `feat: material palette window with drag and drop`

---

### Task 6: Herramienta Pintar, overlay e inspector

**Files:**
- Create: `Editor/PaintTool.cs`, `Tests/Editor/PaintOpsTests.cs`
- Modify: `Editor/BuildingOverlay.cs`, `Editor/BuildingInspector.cs`
- Create (docs): `docs/superpowers/plans/2026-11-21-level-builder-4-verificacion-manual.md`

**Interfaces:**
- Produces:
  - `static class PaintOps { void PaintFace(Building b, FloorData f, string key, Material m, Vector3 hit); void PaintPiece(Building b, FloorData f, string key, Material m, Vector3 hit); Material Sample(Building b, FloorData f, string key); }` — usan `FaceMaterialRules`, `BuildingEditorUtil.Mutate` (Undo "Pintar"), `MarkDirty`; para regiones la semilla es el punto de impacto en planta.
  - `class PaintTool : EditorTool`: resalta la cara bajo el ratón (contorno de sus triángulos) y su nombre legible en español ("Pared – lado izquierdo", "Suelo", "Escalón – huella"…); clic / Shift+clic / Alt+clic según Global Constraints; sin material activo muestra "Elige un material en la Paleta".
  - Overlay: botón toggle "Pintar" (excluyente con las otras herramientas) + botón "Paleta"; muestra el material activo.
  - Inspector del edificio: campo "Biblioteca" y, con una pared/escalera/abertura seleccionada, sus materiales por cara editables (ObjectField) con Undo.

- [ ] **Step 1: Write the failing tests:** `PaintFace_SetsOnlyThatSide`, `PaintPiece_Wall_SetsAllWallFaces`, `PaintPiece_Region_SetsFloorCeilingEdge`, `Sample_ReturnsFaceMaterial`, `Paint_IsOneUndoStep`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** RUN_TESTS → PASS.
- [ ] **Step 5: Write** la verificación manual: crear biblioteca por defecto; arrastrar un material y un juego de texturas (`Brick_BaseColor`, `Brick_Normal`, `Brick_Rough`) a la Paleta; pintar un lado de pared, la pared entera, un suelo, un techo, una escalera, una jamba; cuentagotas; partir/quitar/mover paredes pintadas; alargar una pared (la textura se repite); cambiar el tamaño real en la Paleta; plantas apiladas sin parpadeo; borrar un material del proyecto; Undo; probar en Built-in y, si puedes, en URP/HDRP.
- [ ] **Step 6: Commit** `feat: paint tool, overlay and inspector materials`

## Decisiones tomadas en este plan (para revisar)

- Los materiales viven en cada pared/escalera/abertura (no en una tabla aparte): así partir, unir o copiar a otra planta los conserva sin trabajo extra.
- Suelos y techos se pintan por un punto (semilla): la habitación que contiene ese punto lleva el material; si mueves paredes, sigue funcionando mientras el punto quede dentro.
- Shift+clic pinta la pieza entera (ambos lados de la pared); Alt+clic toma el material (cuentagotas).
- La biblioteca por defecto se **crea** en tu proyecto con un botón (no viene como asset del paquete), porque un material fijo saldría rosa en otro render pipeline.
- La rugosidad se asigna tal cual; Unity trabaja con suavidad, así que algunos materiales pueden verse más brillantes de lo esperado (se avisa en el Inspector).
- Las jambas de puertas/ventanas tienen un material propio; las caras de las uniones entre paredes toman el de la tapa de la pared.
