# Level Builder — Plan 1: Base (paquete, paredes, pisos, suelos/techos, dibujo) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un paquete UPM instalable con el que, en la vista de escena, se dibujan paredes punto a punto por plantas, los pisos superiores copian y enlazan las paredes exteriores, y se generan paredes con esquinas resueltas, suelos y techos, todo regenerado desde datos al editar.

**Architecture:** Datos puros en C# serializables (`BuildingData`) con operaciones de edición testeables sin escena; geometría pura (`LevelBuilder.Geometry`) que convierte datos → `MeshData`; un `Building` (MonoBehaviour) que guarda los datos y un `BuildingRenderer` que crea mallas no guardadas (`HideFlags.DontSave`) al cargar y al cambiar. Herramientas de editor (Overlay + EditorTool) en un ensamblado Editor aparte.

**Tech Stack:** Unity 6000.0.47f1 para desarrollo (paquete declara `"unity": "2022.3"`), C# 9, Unity Test Framework (NUnit) en EditMode, git.

**Spec:** `docs/superpowers/specs/2026-11-14-level-builder-design.md`

**Planes siguientes (fuera de este):** 2) Aberturas: puertas (runtime `Door`) y ventanas · 3) Escaleras + validación/auto-ajuste (§6.8) · 4) Biblioteca de materiales, pintado y escala real de UV · 5) Convertir a ProBuilder, hornear mallas, muestra y documentación.

## Global Constraints

- Versión mínima: **Unity 2022.3** (`package.json` → `"unity": "2022.3"`). No usar APIs exclusivas de Unity 6 (p. ej. `FindObjectsByType` está bien en 2022.3; `Awaitable` no).
- Pipelines: Built-in, URP y HDRP — no referenciar paquetes de pipeline; material por defecto vía `GraphicsSettings.currentRenderPipeline?.defaultMaterial`, si es null `new Material(Shader.Find("Standard"))`.
- Nombre de paquete: `com.levelbuilder.core`; namespaces `LevelBuilder`, `LevelBuilder.Geometry`, `LevelBuilder.Editor`.
- Ensamblados: `LevelBuilder.Runtime` (sin `UnityEditor`), `LevelBuilder.Editor` (solo plataforma Editor), `LevelBuilder.Tests.Editor`.
- ProBuilder **no** es dependencia en este plan.
- Mallas generadas: `HideFlags.DontSave`, regeneradas en `OnEnable` (editor y runtime) — el generador vive en Runtime.
- Undo/Redo de Unity en toda operación de edición desde el editor (`Undo.RecordObject(building, ...)` antes de mutar).
- Tolerancia de fusión de nodos: **0.01 m**. Defaults: altura de planta **3.0 m**, grosor de pared **0.2 m**, forjado **0.2 m**, rejilla **0.5 m**.
- Elevación: planta 0 a 0 m; planta N a `elevation(N-1) + height(N-1)`. Forjado de planta N ocupa `[elevation_N − slab, elevation_N]`; el techo de N−1 es la cara inferior de ese forjado. La planta superior lleva techo con forjado `[top, top + slab]`.

## Review Focus

1. **Paredes que se cruzan sin nodo común** → `AddSegment` las parte en la intersección y crea el nodo; las regiones se detectan igual. (Test en Task 2.)
2. **Clic sobre un nodo existente o segmento de longitud ~0** → se reutiliza el nodo (tolerancia 0.01 m); nunca se crea un segmento degenerado ni duplicado. (Test en Task 2.)
3. **Paredes que no cierran** → no hay suelo/techo en ese tramo y no se lanza ninguna excepción. (Tests en Task 3 y Task 6.)
4. **Ángulos muy agudos (<15°) en esquinas** → la esquina no genera picos: límite de inglete (3 × grosor) con corte perpendicular. (Test en Task 5.)
5. **Borrar un nodo enlazado en la planta de abajo** → el nodo de arriba queda desenlazado (no referencia colgante) y sigue funcionando; Undo restaura datos y mallas coherentes. (Tests en Task 4 y Task 7.)

---

## Estructura de archivos

```
<repo>/                                   proyecto Unity de desarrollo (raíz del repo git)
  Assets/Dev/                             escenas de prueba manual
  Packages/manifest.json                  + "testables": ["com.levelbuilder.core"]
  Packages/com.levelbuilder.core/         paquete embebido
    package.json
    Runtime/LevelBuilder.Runtime.asmdef
    Runtime/Data/IdUtil.cs                ids estables
    Runtime/Data/WallNode.cs, WallSegment.cs, FloorData.cs
    Runtime/Data/BuildingData.cs          operaciones de edición (pisos, nodos, segmentos, enlaces)
    Runtime/Geometry/Seg2D.cs             intersección de segmentos
    Runtime/Geometry/PlanarFaces.cs       regiones cerradas + borde exterior
    Runtime/Geometry/Triangulator.cs      ear clipping con agujeros
    Runtime/Geometry/MeshData.cs          buffer de malla con grupos por clave de cara
    Runtime/Geometry/WallJoins.cs         esquinas (ingletes) por nodo
    Runtime/Geometry/WallMeshBuilder.cs
    Runtime/Geometry/SlabMeshBuilder.cs   suelos y techos
    Runtime/Building.cs                   MonoBehaviour con BuildingData
    Runtime/BuildingRenderer.cs           genera GameObjects/mallas DontSave
    Editor/LevelBuilder.Editor.asmdef
    Editor/BuildingMenu.cs                GameObject > Level Builder > Building
    Editor/GridSnap.cs
    Editor/WallDrawSession.cs             lógica pura del dibujo de paredes
    Editor/WallTool.cs                    EditorTool (escena)
    Editor/BuildingOverlay.cs             Overlay: modo, planta activa
    Editor/BuildingInspector.cs
    Tests/Editor/LevelBuilder.Tests.Editor.asmdef
    Tests/Editor/*.cs
```

**Comando de tests** (Unity cerrado con este proyecto), desde la raíz del repo:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.0.47f1/Editor/Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults/editmode.xml -logFile TestResults/editmode.log
```
Éxito: código de salida 0 y en `TestResults/editmode.xml` `result="Passed"`. Para filtrar: añadir `-testFilter <NombreDeClase>`. En adelante se cita como **RUN_TESTS**.

---

### Task 1: Proyecto de desarrollo y esqueleto del paquete

**Files:**
- Create: proyecto Unity en la raíz del repo; `Packages/com.levelbuilder.core/package.json`; los tres `.asmdef`; `Runtime/Data/IdUtil.cs`; `Tests/Editor/IdUtilTests.cs`; `.gitignore` (estándar de Unity + `TestResults/`)
- Modify: `Packages/manifest.json`

**Interfaces:**
- Produces: `static class LevelBuilder.IdUtil { static string New(); }` → GUID `"N"` (32 hex).

- [ ] **Step 1:** `git init`; crear proyecto: `"/c/Program Files/Unity/Hub/Editor/6000.0.47f1/Editor/Unity.exe" -batchmode -createProject . -quit` (si la carpeta no está vacía, crear en `./_tmp` y mover el contenido). Esperado: existen `Assets/`, `Packages/manifest.json`, `ProjectSettings/`.
- [ ] **Step 2:** Crear `package.json`: `name: "com.levelbuilder.core"`, `version: "0.1.0"`, `displayName: "Level Builder"`, `unity: "2022.3"`, `description` en una línea. Asmdefs: Runtime (sin referencias), Editor (`includePlatforms: ["Editor"]`, referencia Runtime), Tests (`includePlatforms: ["Editor"]`, referencias Runtime + Editor, `optionalUnityReferences`/`overrideReferences` con `nunit.framework.dll`, `defineConstraints: ["UNITY_INCLUDE_TESTS"]`, referencias `UnityEngine.TestRunner`, `UnityEditor.TestRunner`). Añadir `"testables": ["com.levelbuilder.core"]` al manifest.
- [ ] **Step 3: Test que falla**
```csharp
[Test] public void New_ReturnsDistinct32CharIds() {
    var a = IdUtil.New(); var b = IdUtil.New();
    Assert.AreEqual(32, a.Length); Assert.AreNotEqual(a, b);
}
```
- [ ] **Step 4:** RUN_TESTS → FAIL (no compila: `IdUtil` no existe).
- [ ] **Step 5:** Implementar `IdUtil.New()` con `Guid.NewGuid().ToString("N")`.
- [ ] **Step 6:** RUN_TESTS → PASS.
- [ ] **Step 7:** Commit `chore: scaffold dev project and com.levelbuilder.core package`.

---

### Task 2: Modelo de datos y edición de paredes

**Files:**
- Create: `Runtime/Data/WallNode.cs`, `WallSegment.cs`, `FloorData.cs`, `BuildingData.cs`, `Runtime/Geometry/Seg2D.cs`
- Test: `Tests/Editor/BuildingDataWallTests.cs`, `Tests/Editor/Seg2DTests.cs`

**Interfaces:**
- Produces (todos `[Serializable]`, campos públicos para serialización de Unity):
  - `class WallNode { string id; Vector2 position; string linkedBelowId; }` (`linkedBelowId` null/"" = sin enlace)
  - `class WallSegment { string id; string startNodeId; string endNodeId; float thickness = 0.2f; float heightOverride = -1f; /* < 0 → altura de la planta */ float Height(FloorData f); }`
  - `class FloorData { string id; string name; float elevation; float height = 3f; float slabThickness = 0.2f; List<WallNode> nodes; List<WallSegment> segments; WallNode GetNode(string id); WallSegment GetSegment(string id); }`
  - `class BuildingData { List<FloorData> floors; const float MergeTolerance = 0.01f; }` con:
    - `FloorData AddFloor()` (en esta task solo crea; la copia/enlace llega en Task 4) — nombre `"Planta {index}"`, elevación según Global Constraints.
    - `WallNode GetOrAddNode(FloorData f, Vector2 p)` — reutiliza nodo a ≤ 0.01 m; si `p` cae a ≤ 0.01 m de un segmento, lo parte ahí.
    - `List<WallSegment> AddSegment(FloorData f, Vector2 a, Vector2 b)` — devuelve los segmentos creados (varios si cruzó otros); ignora longitud < 0.01 m y duplicados (mismo par de nodos en cualquier orden).
    - `WallNode SplitSegment(FloorData f, string segmentId, Vector2 p)`
    - `void MoveNode(FloorData f, string nodeId, Vector2 p)` (en esta task solo ese nodo; columna enlazada en Task 4)
    - `void RemoveSegment(FloorData f, string segmentId)` — borra nodos que queden sin segmentos.
    - `void RemoveNode(FloorData f, string nodeId)` — borra sus segmentos.
  - `static class Seg2D { static bool Intersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 p, out float ta, out float tb); static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b, out float t); }` — `Intersect` solo intersecciones propias (ta, tb en (ε, 1−ε)), paralelos → false.

- [ ] **Step 1: Tests que fallan** (`BuildingDataWallTests`):
```csharp
[Test] public void AddSegment_CreatesTwoNodesAndOneSegment()        // nodes=2, segments=1
[Test] public void AddSegment_ReusesNodeWithinTolerance()           // (0,0)-(4,0) luego (4.005,0)-(4,3) → nodes=3
[Test] public void AddSegment_IgnoresZeroLength()                   // (1,1)-(1.004,1) → segments=0, nodes=0
[Test] public void AddSegment_IgnoresDuplicateReversed()            // (0,0)-(4,0) y (4,0)-(0,0) → segments=1
[Test] public void AddSegment_CrossingSplitsBoth()                  // (0,0)-(4,0) y (2,-2)-(2,2) → segments=4, nodes=5, existe nodo en (2,0)
[Test] public void AddSegment_EndOnSegmentSplitsIt()                // (0,0)-(4,0) y (2,0)-(2,3) → segments=3
[Test] public void RemoveSegment_RemovesOrphanNodes()               // único segmento → nodes=0
[Test] public void AddFloor_StacksElevation()                       // dos plantas: elevation 0 y 3
```
`Seg2DTests`: cruce en X → true y p=(2,0); paralelos → false; tocar en extremo → false.
- [ ] **Step 2:** RUN_TESTS → FAIL (tipos inexistentes).
- [ ] **Step 3:** Implementar `Seg2D` y luego tipos de datos y `BuildingData` según las firmas. `AddSegment`: obtener nodos con `GetOrAddNode`, buscar cruces con todos los segmentos de la planta (`Seg2D.Intersect`), partir cada segmento cruzado en el punto y encadenar sub-segmentos ordenados por `t`.
- [ ] **Step 4:** RUN_TESTS → PASS.
- [ ] **Step 5:** Commit `feat: wall graph data model with merge and auto-split`.

---

### Task 3: Regiones cerradas y borde exterior (PlanarFaces)

**Files:**
- Create: `Runtime/Geometry/PlanarFaces.cs`
- Test: `Tests/Editor/PlanarFacesTests.cs`

**Interfaces:**
- Consumes: `FloorData`, `WallNode`, `WallSegment` (Task 2).
- Produces:
  - `class Region { List<string> nodeIds; List<Vector2> polygon; /* CCW */ List<string> segmentIds; string key; }` — `key` = ids de segmentos ordenados y unidos con `"|"` (estable mientras no cambie la topología).
  - `static class PlanarFaces { static PlanarResult Compute(FloorData f); }`
  - `class PlanarResult { List<Region> regions; HashSet<string> exteriorSegmentIds; }` — exterior = segmentos que bordean la cara no acotada (outer face) de cada componente conexo.

Algoritmo (no lo determina la firma): grafo planar → para cada arista dirigida, siguiente = en el nodo destino, la arista saliente inmediatamente en sentido horario respecto a la de vuelta (ordenar salientes por ángulo). Recorrer caras; cara con área con signo > 0 (CCW) = región interior; caras con área ≤ 0 = cara exterior de su componente → sus aristas a `exteriorSegmentIds`. Antes, podar iterativamente nodos de grado 1 (paredes colgantes no forman región, pero sus segmentos tampoco son exteriores).

- [ ] **Step 1: Tests que fallan:**
```csharp
[Test] public void Square_OneRegion_AllFourExterior()               // regions=1, área=16 (4×4), exterior=4
[Test] public void SquareWithMiddleWall_TwoRegions_MiddleNotExterior()  // regions=2, exterior=6 (partido en 6 segs), medio no exterior
[Test] public void OpenL_NoRegions_NoThrow()                        // regions=0
[Test] public void SquareWithDanglingWall_OneRegion()               // cola de grado 1 → regions=1, cola no exterior
[Test] public void TwoSeparateSquares_TwoRegions()
[Test] public void RegionPolygon_IsCounterClockwise()               // área con signo > 0
```
- [ ] **Step 2:** RUN_TESTS → FAIL.
- [ ] **Step 3:** Implementar `PlanarFaces.Compute` con el algoritmo descrito.
- [ ] **Step 4:** RUN_TESTS → PASS.
- [ ] **Step 5:** Commit `feat: planar region and exterior edge detection`.

---

### Task 4: Pisos enlazados (copia de exteriores, columna enlazada, desenlazar)

**Files:**
- Modify: `Runtime/Data/BuildingData.cs`
- Test: `Tests/Editor/FloorLinkTests.cs`

**Interfaces:**
- Consumes: `PlanarFaces.Compute` (Task 3).
- Produces (en `BuildingData`):
  - `AddFloor()` ahora: si hay planta inferior, copia sus segmentos de `exteriorSegmentIds` con sus nodos; cada nodo nuevo tiene `linkedBelowId` = id del nodo de abajo; misma `height`, `thickness` por segmento.
  - `MoveNode(...)` mueve toda la columna: nodos enlazados hacia abajo (por `linkedBelowId`) y hacia arriba (nodos cuyo `linkedBelowId` apunta al actual), transitivamente.
  - `void UnlinkNode(FloorData f, string nodeId)` — rompe el enlace del nodo con el de abajo **y** el de los nodos de arriba que lo referencian.
  - `IEnumerable<(FloorData floor, WallNode node)> GetLinkedColumn(string nodeId)`
  - `RemoveNode` / `RemoveSegment` que borren un nodo limpian `linkedBelowId` de los nodos de arriba que lo referencian.

- [ ] **Step 1: Tests que fallan:**
```csharp
[Test] public void AddFloor_CopiesOnlyExteriorWalls()              // cuadrado + muro medio → planta 1 tiene 6 segmentos, no el medio
[Test] public void AddFloor_CopiedNodesAreLinked()                 // todos los nodos de planta 1 con linkedBelowId válido
[Test] public void MoveNode_OnGroundMovesUpperToo()                 // mover esquina en planta 0 → misma posición en planta 1 y 2
[Test] public void MoveNode_OnUpperMovesGroundToo()
[Test] public void UnlinkNode_ThenMove_OnlyThatFloorMoves()
[Test] public void RemoveNode_Below_ClearsUpperLink()               // nodo de arriba sigue existiendo, linkedBelowId vacío
```
- [ ] **Step 2:** RUN_TESTS → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** RUN_TESTS → PASS.
- [ ] **Step 5:** Commit `feat: floor copy with linked exterior wall columns`.

---

### Task 5: Triangulador, MeshData, esquinas y malla de paredes

**Files:**
- Create: `Runtime/Geometry/Triangulator.cs`, `MeshData.cs`, `WallJoins.cs`, `WallMeshBuilder.cs`
- Test: `Tests/Editor/TriangulatorTests.cs`, `Tests/Editor/WallMeshBuilderTests.cs`

**Interfaces:**
- Produces:
  - `static class Triangulator { static List<int> Triangulate(IList<Vector2> outer, IList<IList<Vector2>> holes, out List<Vector2> verts); }` — ear clipping; agujeros unidos al contorno por puente al vértice visible más a la derecha. Acepta contorno en cualquier orientación (normaliza a CCW, agujeros a CW). Los agujeros los usará el Plan 2 (aberturas) y Task 6 (huecos de escalera en Plan 3).
  - `class MeshData { List<Vector3> vertices; List<Vector3> normals; List<Vector2> uvs; List<FaceGroup> groups; Mesh ToMesh(); }`, `class FaceGroup { string key; List<int> triangles; }` — `ToMesh()` crea una submalla por grupo **en este plan se combinan en una sola submalla** (el Plan 4 agrupa por material); `IndexFormat.UInt32` si > 65535 vértices.
  - Claves de cara: `"{segmentId}/L"`, `"{segmentId}/R"` (lados izquierdo/derecho mirando de start→end), `"{segmentId}/start"`, `"{segmentId}/end"`, `"{segmentId}/top"`.
  - `static class WallJoins { static Dictionary<(string segmentId, bool atStart), (Vector2 left, Vector2 right)> Compute(FloorData f); }`
  - `static class WallMeshBuilder { static MeshData Build(FloorData f, float baseY); }` — cada pared de `baseY` a `baseY + segment.Height(f)`.

Algoritmo de esquinas: en cada nodo, ordenar segmentos incidentes por ángulo saliente. Para cada par consecutivo (s_i, s_{i+1}) en sentido antihorario, la esquina compartida es la intersección de la línea desplazada izquierda de s_i con la derecha de s_{i+1} (desplazamiento = grosor/2 de cada uno). Grado 1: tapa perpendicular. Si la esquina queda a > 3 × max(grosor) del nodo, o las líneas son casi paralelas (|cross| < 1e-4), usar los puntos perpendiculares (límite de inglete). Cada cara lateral se construye como polígono 2D en coordenadas de pared (u = metros a lo largo de la pared desde el nodo inicial proyectado; v = altura) y se triangula con `Triangulator` (sin agujeros en este plan). UV = (u, v) en metros; suelos/techo en xz.

- [ ] **Step 1: Tests que fallan:**
```csharp
// Triangulator
[Test] public void Square_TwoTriangles_AreaPreserved()              // suma de áreas = 16
[Test] public void SquareWithHole_AreaIsOuterMinusHole()            // 4×4 con agujero 1×1 → 15
[Test] public void Clockwise_Input_StillWorks()
// WallMeshBuilder
[Test] public void StraightWall_BoundsMatchLengthThicknessHeight()   // (0,0)-(4,0), t=0.2, h=3 → bounds x[0,4], z[-0.1,0.1], y[0,3]
[Test] public void StraightWall_HasFiveFaceGroups()                  // L, R, start, end, top
[Test] public void LCorner_OuterCornerAtMiter()                      // (0,0)-(4,0)-(4,3): existe vértice en (4.1, y, -0.1)
[Test] public void AcuteAngle_NoVertexBeyondMiterLimit()             // ángulo de 10°: ningún vértice a > 0.6 m del nodo de la esquina
[Test] public void SideFace_UVsInMetres()                            // max u de "/L" ≈ 4 (±0.11), max v = 3
[Test] public void HeightOverride_UsedInsteadOfFloorHeight()         // heightOverride = 1.2 → bounds.max.y = 1.2 (murete)
```
- [ ] **Step 2:** RUN_TESTS → FAIL.
- [ ] **Step 3:** Implementar `Triangulator`, `MeshData`, `WallJoins`, `WallMeshBuilder` en ese orden.
- [ ] **Step 4:** RUN_TESTS → PASS.
- [ ] **Step 5:** Commit `feat: wall mesh generation with miter joins`.

---

### Task 6: Suelos y techos

**Files:**
- Create: `Runtime/Geometry/SlabMeshBuilder.cs`
- Test: `Tests/Editor/SlabMeshBuilderTests.cs`

**Interfaces:**
- Consumes: `PlanarFaces`, `Triangulator`, `MeshData`.
- Produces: `static class SlabMeshBuilder { static MeshData BuildFloor(FloorData f); static MeshData BuildCeiling(FloorData f, bool isTopFloor); }`
  - Suelo: cada región, losa de `elevation − slab` a `elevation` (cara superior, inferior y cantos), polígono = región (eje de las paredes; las paredes lo tapan). Clave `"region:{region.key}/floor"`.
  - Techo: cara inferior a `elevation + height − (isTopFloor ? 0 : slab)` mirando hacia abajo. Si `isTopFloor`, además losa de cubierta `[elevation + height, elevation + height + slab]`. Clave `"region:{key}/ceiling"`.
  - El hueco de escalera (agujeros) se añade en el Plan 3: dejar `IList<IList<Vector2>> holes` como parámetro opcional `= null` en ambas funciones.

- [ ] **Step 1: Tests que fallan:**
```csharp
[Test] public void Square_FloorTopFaceAtElevation_AreaMatches()     // planta 0: caras con normal +y en y=0, área total 16
[Test] public void OpenWalls_NoFloor_NoThrow()                       // L abierta → vertices.Count == 0
[Test] public void Ceiling_FacesDown_AtUnderside()                   // planta 0 de 2: normal −y en y = 3 − 0.2
[Test] public void TopFloor_HasRoofSlab()                            // planta superior: existe cara +y en y = elevation + 3 + 0.2
```
- [ ] **Step 2:** RUN_TESTS → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** RUN_TESTS → PASS.
- [ ] **Step 5:** Commit `feat: floor and ceiling slab generation`.

---

### Task 7: Building y BuildingRenderer (regeneración y almacenamiento)

**Files:**
- Create: `Runtime/Building.cs`, `Runtime/BuildingRenderer.cs`
- Test: `Tests/Editor/BuildingRendererTests.cs`

**Interfaces:**
- Consumes: `BuildingData`, `WallMeshBuilder`, `SlabMeshBuilder`.
- Produces:
  - `[ExecuteAlways] class Building : MonoBehaviour { BuildingData data; int activeFloor; void MarkDirty(int floorIndex = -1); event Action Changed; }` — `MarkDirty(-1)` = todas las plantas. `OnValidate` → `MarkDirty()` (cubre Undo/Redo, que repone datos serializados y dispara `OnValidate`).
  - `[ExecuteAlways, RequireComponent(typeof(Building))] class BuildingRenderer : MonoBehaviour { void Regenerate(int floorIndex = -1); Material defaultMaterial; }`
    - Jerarquía generada: `Building` → `Floor {i}` → `Walls`, `Floor`, `Ceiling` (cada uno `MeshFilter` + `MeshRenderer` + `MeshCollider`).
    - Todos los GameObjects y `Mesh` generados con `HideFlags.DontSave` (y `NotEditable`); destruir y recrear al regenerar una planta (destruir mallas previas para no filtrar memoria).
    - `OnEnable` → `Regenerate()`; suscrito a `Building.Changed`; regeneración diferida al siguiente `EditorApplication.update` en editor (`#if UNITY_EDITOR`) para agrupar cambios del mismo frame; en Play/build, inmediata.
    - Regenerar planta N regenera también N−1 (su techo depende de la losa de N) y N+1 si existe.
    - Granularidad: por planta en este plan (el spec pide por pieza; se optimiza si el perfilado lo pide).

- [ ] **Step 1: Tests que fallan:**
```csharp
[Test] public void Regenerate_CreatesFloorHierarchyWithColliders()   // cuadrado → "Floor 0/Walls" con MeshFilter.sharedMesh no nulo y MeshCollider
[Test] public void GeneratedObjects_AreDontSave()                    // hideFlags & DontSave != 0 en GameObjects y meshes
[Test] public void MoveNode_ThenRegenerate_UpdatesBounds()           // mover esquina (4,0)→(6,0) → bounds.max.x ≈ 6.1
[Test] public void Regenerate_Twice_DoesNotDuplicateChildren()       // un solo "Floor 0"
[Test] public void RemoveLinkedNode_Regenerate_NoThrow()             // 2 plantas, borrar nodo en planta 0 → regenerar sin excepción
```
Tests crean `new GameObject().AddComponent<BuildingRenderer>()`, llaman `Regenerate()` directamente y destruyen todo en `[TearDown]` con `Object.DestroyImmediate`.
- [ ] **Step 2:** RUN_TESTS → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** RUN_TESTS → PASS.
- [ ] **Step 5:** Commit `feat: building component and non-saved mesh regeneration`.

---

### Task 8: Herramientas de editor — crear edificio, planta activa, dibujar y editar paredes

**Files:**
- Create: `Editor/BuildingMenu.cs`, `Editor/GridSnap.cs`, `Editor/WallDrawSession.cs`, `Editor/WallTool.cs`, `Editor/BuildingOverlay.cs`, `Editor/BuildingInspector.cs`, `Assets/Dev/Sandbox.unity`
- Test: `Tests/Editor/GridSnapTests.cs`, `Tests/Editor/WallDrawSessionTests.cs`

**Interfaces:**
- Consumes: `Building`, `BuildingData` (todas las operaciones), `BuildingRenderer`.
- Produces:
  - Menú `GameObject/Level Builder/Building` → crea GameObject con `Building` + `BuildingRenderer` y una planta, registrado en Undo.
  - `static class GridSnap { static Vector2 Snap(Vector2 p, float grid); static Vector2 SnapAngle(Vector2 from, Vector2 to, float stepDegrees); }`
  - `class WallDrawSession { WallDrawSession(BuildingData data, int floorIndex); Vector2? LastPoint; void Click(Vector2 p); void Finish(); bool IsDrawing; }` — cada `Click` tras el primero llama `AddSegment(last, p)`; clic sobre el primer nodo de la cadena cierra y termina; `Finish()` (doble clic / Esc / Enter) termina sin cerrar.
  - `WallTool : EditorTool` (`[EditorTool("Paredes", typeof(Building))]`): raycast del ratón al plano y = elevación de la planta activa; previsualización con `Handles` del segmento en curso; rejilla 0.5 m (Ctrl desactiva snap; Shift fuerza ángulos de 15°); con la sesión inactiva, `Handles.FreeMoveHandle` por nodo → `MoveNode` (columna enlazada, se dibuja distinto si el nodo está enlazado); clic con Alt en un segmento → `SplitSegment`; Supr con nodo seleccionado → `RemoveNode`. Toda mutación: `Undo.RecordObject(building, "...")`, luego `building.MarkDirty(floor)`.
  - `BuildingOverlay : Overlay` (`[Overlay(typeof(SceneView), "Level Builder")]`, visible con un `Building` seleccionado): selector de planta activa, botones "+ Planta" (llama `AddFloor`) y "− Planta" (solo la superior), campo de altura de planta, botón que activa `WallTool`. Plantas no activas se dibujan atenuadas: `MeshRenderer` de plantas superiores a la activa desactivados en editor.
  - `BuildingInspector : UnityEditor.Editor` para `Building`: lista de plantas (nombre, altura, forjado), grosor del segmento seleccionado, botón "Desenlazar nodo" para el nodo seleccionado.

- [ ] **Step 1: Tests que fallan:**
```csharp
[Test] public void Snap_RoundsToGrid()                               // (1.26, -0.24) grid 0.5 → (1.5, 0)
[Test] public void SnapAngle_RoundsTo15Degrees()                     // from (0,0) to (1, 0.2) → ángulo 15° conservando longitud
[Test] public void Session_ThreeClicks_TwoSegments()
[Test] public void Session_ClickOnFirstPoint_ClosesAndFinishes()     // 4 esquinas + clic en la primera → 4 segmentos, IsDrawing=false, 1 región
[Test] public void Session_FinishWithOnePoint_CreatesNothing()
```
- [ ] **Step 2:** RUN_TESTS → FAIL.
- [ ] **Step 3:** Implementar `GridSnap`, `WallDrawSession`, luego `BuildingMenu`, `WallTool`, `BuildingOverlay`, `BuildingInspector`.
- [ ] **Step 4:** RUN_TESTS → PASS.
- [ ] **Step 5: Verificación manual** en `Assets/Dev/Sandbox.unity` (abrir el proyecto en Unity 6000.0.47f1):
  1. `GameObject > Level Builder > Building` → aparece en la jerarquía.
  2. Herramienta Paredes: dibujar un rectángulo 8×6 con un muro interior → paredes con esquinas limpias, suelo y techo en las dos habitaciones.
  3. "+ Planta" → planta 1 con las paredes exteriores (sin el muro interior), a 3 m.
  4. Arrastrar una esquina en planta 0 → se mueve también en planta 1; Ctrl+Z la devuelve y las mallas coinciden.
  5. Desenlazar esa esquina en planta 1 y moverla → solo cambia planta 1; la planta 1 tiene suelo propio (voladizo).
  6. Guardar escena, cerrar y reabrir → el edificio se regenera; el `.unity` no contiene mallas (buscar `m_Mesh` de las piezas generadas: ausente).
  7. Entrar en Play → el edificio existe con colisiones.
- [ ] **Step 6:** RUN_TESTS completo → PASS. Commit `feat: editor tools for drawing walls and managing floors`.
