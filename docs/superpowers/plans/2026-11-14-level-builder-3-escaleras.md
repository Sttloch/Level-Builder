# Level Builder — Plan 3: Escaleras que siguen una ruta y se ajustan solas Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dibujar en la planta activa la ruta de una escalera (rectas, L, U o curvas) que sube a la planta de arriba; la herramienta genera escalones cómodos, descansillos, barandillas, rampa de colisión invisible y el hueco en el forjado superior, valida que el jugador no acabe frente a una pared ni atrapado y lo corrige solo cuando puede.

**Architecture:** Las escaleras se guardan en `BuildingData.stairs` (abarcan dos plantas, referenciadas por id). Una pasada pura `StairLayout` convierte ruta + alturas en piezas (escalones/descansillos con su polígono en planta y altura), zonas de salida/llegada y polígono de hueco. `StairValidator` comprueba las reglas de §6.8 y `StairAutoFix` propone una ruta corregida. `StairMeshBuilder` genera mallas; `BuildingRenderer` crea `Floor N/Stairs/{id}` y pasa los huecos a `SlabMeshBuilder`. Herramienta de editor `StairTool`.

**Tech Stack:** Unity 6000.0.47f1 para desarrollo (paquete `"unity": "2022.3"`), C# 9, Unity Test Framework (EditMode), git. Mismo **RUN_TESTS** que los planes 1 y 2.

**Spec:** `docs/superpowers/specs/2026-11-14-level-builder-design.md` (§6.1 Escaleras, §6.2 Escaleras y suelos con agujeros, §6.3 punto 4, §6.8). Notas heredadas del plan 1: el parámetro `holes` de los forjados no tiene test; los huecos de escalera no deben cortar la losa de cubierta.

## Global Constraints

- Todas las de los planes 1 y 2 siguen vigentes (2022.3, sin APIs exclusivas de Unity 6, Built-in/URP/HDRP, Runtime sin `UnityEditor` fuera de `#if UNITY_EDITOR`, `HideFlags.DontSave` para lo generado y solo se destruyen mallas `DontSave`, Undo en toda edición, nunca `GetInstanceID()` directo).
- Una escalera conecta la planta **N con la N+1** (v1). Sin planta encima no se puede crear.
- Defaults: ancho **1.0 m** (mín. 0.6, máx. 3.0); contrahuella objetivo **0.18 m** (rango 0.12–0.20); huella **0.28 m** (rango 0.25–0.32, nunca se estira); altura libre **2.1 m**; descansillo de profundidad = ancho; zona de llegada/salida de profundidad **max(ancho, 1.0)**; barandilla de **0.9 m**; rampa de colisión activada.
- Número de contrahuellas `n = max(1, round(H / contrahuellaObjetivo))`, contrahuella real `H / n` (H = altura de la planta N). Hay n contrahuellas y n−1 piezas pisables; la última contrahuella sube al suelo de la planta N+1.
- Esquinas de la ruta: un punto `Corner` con giro > **30°** genera descansillo; con giro ≤ 30° se trata como `Smooth`. Los puntos `Smooth` generan curva (Catmull-Rom centrípeta muestreada) con escalones en abanico, sin descansillo. Caracol de vuelta completa: fuera de v1.
- La ruta marca dirección: si es más corta que lo necesario, el último tramo se prolonga en su dirección; si es más larga, la escalera termina antes. La previsualización muestra la longitud real.
- Coordenadas: plano (x, y) → local del edificio (x, altura, y). "Izquierda" de una dirección d es (−d.y, d.x).
- Claves de cara: `stair:{id}/tread`, `/riser`, `/side`, `/landing`, `/rail`. La rampa no tiene caras visibles (solo `MeshCollider`).
- Los huecos de escalera cortan **solo** el forjado de la planta N+1 (cara superior, inferior y cantos de `BuildFloor`) y el techo de la planta N (`BuildCeiling(N)`); **nunca** la losa de cubierta.
- Una escalera inválida se genera igualmente (para verla) y se dibuja en rojo con sus motivos; nunca se borra sola.

## Review Focus

1. **Ruta que termina contra una pared en la planta de arriba** → `ArrivalBlocked`; "Arreglar automáticamente" gira el último tramo o añade un descansillo hacia el lado libre; si no hay solución, queda roja con el motivo. (Tests en Task 3.)
2. **Mover una pared después de colocar la escalera** → se revalida en la siguiente regeneración (aparece `Collision`/`ArrivalBlocked`), la escalera sigue generándose y no hay excepciones. (Test en Task 5.)
3. **Altura libre** → el hueco del forjado superior cubre todas las piezas con menos de 2.1 m hasta el forjado; al bajar la altura de planta, el hueco crece. (Tests en Task 2 y Task 5.)
4. **Quitar la planta de arriba o cambiar alturas** → las escaleras que la usan se borran con ella (Undo las restaura); al cambiar la altura se recalculan escalones y hueco. (Tests en Task 1 y Task 5.)
5. **Rutas degeneradas** (puntos repetidos, tramo de longitud 0, giro de 180°, ruta que se cruza a sí misma) → sin excepciones; la escalera queda inválida con motivo (`RouteInvalid`). (Test en Task 2.)

---

## Estructura de archivos

```
Packages/com.levelbuilder.core/
  Runtime/Data/StairData.cs            StairPointKind, StairPoint, StairData
  Runtime/Data/BuildingData.cs         + stairs y sus operaciones
  Runtime/Geometry/StairLayout.cs      ruta → piezas, zonas, hueco
  Runtime/Geometry/StairValidator.cs   reglas §6.8
  Runtime/Geometry/StairAutoFix.cs     corrección de ruta
  Runtime/Geometry/StairMeshBuilder.cs escalones, descansillos, barandillas, rampa
  Runtime/BuildingRenderer.cs          + Floor N/Stairs/{id}; huecos a los forjados
  Editor/StairTool.cs                  EditorTool
  Editor/StairDrawSession.cs           lógica pura del dibujo de la ruta
  Editor/BuildingOverlay.cs, BuildingInspector.cs, BuildingEditorUtil.cs (+ StairSelection)
  Tests/Editor/StairDataTests.cs, StairLayoutTests.cs, StairValidatorTests.cs,
               StairMeshTests.cs, StairRendererTests.cs, StairDrawSessionTests.cs
```

---

### Task 1: Datos de escaleras

**Files:**
- Create: `Runtime/Data/StairData.cs`, `Tests/Editor/StairDataTests.cs`
- Modify: `Runtime/Data/BuildingData.cs`

**Interfaces:**
- Produces:
  - `enum StairPointKind { Corner, Smooth }`, `[Serializable] struct StairPoint { Vector2 position; StairPointKind kind; }`
  - `[Serializable] class StairData { string id; string fromFloorId; string toFloorId; List<StairPoint> route = new(); float width = 1f; float riserTarget = 0.18f; float tread = 0.28f; bool railLeft = true; bool railRight = true; bool collisionRamp = true; }`
  - `BuildingData.stairs : List<StairData>` (inicializada).
  - `StairData AddStair(int fromFloorIndex, IList<StairPoint> route)` → null si no existe la planta `fromFloorIndex + 1` o la ruta tiene < 2 puntos; asigna id y floor ids; valores fuera de rango se acotan a los de Global Constraints.
  - `bool RemoveStair(string id)`, `StairData FindStair(string id)`, `int FloorIndex(string floorId)` (−1 si no existe).
  - `bool MoveStairPoint(string id, int index, Vector2 p)`, `bool InsertStairPoint(string id, int index, StairPoint p)`, `bool RemoveStairPoint(string id, int index)` (nunca deja < 2 puntos → false), `bool SetStairPointKind(string id, int index, StairPointKind k)`.
  - `static void ClampStair(StairData s)` (aplica rangos; la usan `AddStair` y el inspector).
  - `RemoveTopFloor` borra las escaleras cuyo `fromFloorId` o `toFloorId` ya no existen.

- [ ] **Step 1: Write the failing tests** en `StairDataTests`:
  - `AddStair_RequiresFloorAbove` → una sola planta → null; con dos → escalera con `fromFloorId == floors[0].id`, `toFloorId == floors[1].id`, id no vacío.
  - `AddStair_RejectsShortRoute` → 1 punto → null.
  - `AddStair_ClampsValues` → ancho 0.2 → 0.6; contrahuella 0.3 → 0.20; huella 0.4 → 0.32.
  - `RemoveTopFloor_RemovesStairsUsingIt` → escalera 0→1, quitar planta 1 → `stairs.Count == 0`.
  - `RemoveStairPoint_NeverBelowTwo`.
  - `MoveInsertSetKind_EditRoute` → valores esperados en `route`.
  - `StairData_SurvivesJsonRoundTrip` → `JsonUtility.ToJson/FromJson` de `BuildingData` conserva ruta, tipos de punto e ids (serializable por Unity, como necesita Undo).
- [ ] **Step 2: Run** RUN_TESTS `-testFilter StairDataTests` → FAIL (tipos no definidos).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** RUN_TESTS completo → PASS.
- [ ] **Step 5: Commit** `feat: stair data`

---

### Task 2: Disposición de la escalera (`StairLayout`)

**Files:**
- Create: `Runtime/Geometry/StairLayout.cs`, `Tests/Editor/StairLayoutTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `enum StairPieceKind { Tread, Landing }`
  - `class StairPiece { StairPieceKind kind; List<Vector2> polygon /*CCW, planta*/; float height /*sobre la planta N*/; Vector2 frontA, frontB /*arista de la contrahuella que sube a esta pieza, izquierda→derecha mirando en el sentido de subida*/; float clearance /*hasta la cara inferior del forjado superior*/; }`
  - `class StairLayoutResult { bool ok; string error; int risers; float riser; float runLength; List<Vector2> centerline; List<StairPiece> pieces; Vector2 bottomA, bottomB /*arista de la primera contrahuella*/; Vector2 topA, topB /*arista de la última contrahuella (llega a N+1)*/; Vector2 startDir, endDir; List<Vector2> departureZone, arrivalZone /*rectángulos CCW*/; List<Vector2> hole /*null si no hace falta*/; }`
  - `static class StairLayout { StairLayoutResult Compute(StairData s, FloorData from, FloorData to); const float Headroom = 2.1f; const float CornerAngle = 30f; }`

Algoritmo (lo que la firma no determina):
1. Limpia la ruta: quita puntos a menos de 0.01 m del anterior; si quedan < 2 o hay un giro ≥ 179° → `ok = false`, `error = "RouteInvalid"` (sin excepción).
2. Línea central: tramos rectos entre puntos; en cada `Smooth` (o `Corner` con giro ≤ 30°) se sustituye la vecindad por Catmull-Rom centrípeta muestreada cada 0.05 m.
3. Recorrido: desde `route[0]` (arista inferior de la primera contrahuella, perpendicular a la dirección inicial) se colocan n−1 piezas pisables de profundidad `tread` medida sobre la línea central; al alcanzar un `Corner` > 30° la pieza que cae ahí es un **descansillo** cuadrado (lado = ancho) centrado en el vértice, que consume la longitud de la línea central que cubre. En curvas, cada pieza es el cuadrilátero entre dos secciones perpendiculares a la línea central (abanico). Si la línea central se acaba antes, se prolonga el último tramo en su dirección.
4. Altura de la pieza k (0-based) = (k+1) × contrahuella; `clearance = (to.elevation − to.slabThickness) − (from.elevation + height)`.
5. Zonas: `departureZone` = rectángulo ancho × max(ancho,1) detrás de la arista inferior (en sentido contrario a `startDir`); `arrivalZone` = rectángulo ancho × max(ancho,1) delante de la arista superior (en `endDir`).
6. Hueco: contorno (lado izquierdo hacia delante + lado derecho hacia atrás, descansillos incluidos) de la franja que va desde la primera pieza con `clearance < Headroom` hasta la arista superior; null si ninguna pieza lo necesita.

- [ ] **Step 1: Write the failing tests** (plantas de 3.0 m, forjado 0.2, salvo indicación):
  - `Straight_RiserCountAndTread` → ruta (0,0)→(0,10): risers = 17 (round(3/0.18)), riser ≈ 0.17647, 16 piezas Tread de 0.28, runLength ≈ 4.48; pieza 0 a altura 0.17647 entre y=0 y y=0.28.
  - `Straight_ShortRoute_IsExtended` → ruta de 1 m: mismas 16 piezas; topA/topB en y ≈ 4.48.
  - `LShape_HasOneLandingAtCorner` → (0,0)→(0,2)→(3,2) Corner: exactamente un `Landing` cuadrado 1×1 centrado en (0,2); las piezas siguientes avanzan en +x; endDir = (1,0).
  - `SmallAngleCorner_IsSmooth` → giro de 20° → ningún Landing.
  - `Curve_FanSteps_TreadOnCenterline` → ruta con punto `Smooth`: ningún Landing; distancia entre centros de piezas consecutivas sobre la línea central ≈ 0.28 (±0.02).
  - `Hole_CoversPiecesWithLowClearance` → recta: hueco no nulo; todas las piezas con clearance < 2.1 están dentro del hueco (centroide); la pieza 0 (clearance 2.62) fuera.
  - `LowerFloorHeight_GrowsHole` → planta de 2.6 m: el hueco tiene más área que con 3.0 m.
  - `Zones_AreBehindStartAndBeyondEnd` → recta: departureZone en y ∈ [−1, 0]; arrivalZone en y ∈ [4.48, 5.48].
  - `DegenerateRoutes_NoThrow` → puntos repetidos, giro de 180°, ruta que se cruza a sí misma → `ok == false` o resultado sin excepción; el giro de 180° da `error == "RouteInvalid"`.
- [ ] **Step 2: Run** `-testFilter StairLayoutTests` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** RUN_TESTS completo → PASS.
- [ ] **Step 5: Commit** `feat: stair layout`

---

### Task 3: Validación y arreglo automático

**Files:**
- Create: `Runtime/Geometry/StairValidator.cs`, `Runtime/Geometry/StairAutoFix.cs`, `Tests/Editor/StairValidatorTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2; `PlanarFaces.Compute(f).regions` y `PlanarFaces.PointInPolygon` (internos, mismo ensamblado).
- Produces:
  - `enum StairIssue { RouteInvalid, NoFloorAbove, DepartureBlocked, ArrivalBlocked, Collision, HoleCrossesWall }`
  - `class StairValidation { StairLayoutResult layout; List<StairIssue> issues; bool Valid => issues.Count == 0; }`
  - `static class StairValidator { StairValidation Validate(BuildingData data, StairData s); }`
    - `NoFloorAbove`: la planta destino no existe o no es la siguiente.
    - `DepartureBlocked`: la zona de salida corta la huella de alguna pared de la planta N (rectángulo del segmento ± grosor/2) o no está dentro de una región de suelo de N.
    - `ArrivalBlocked`: lo mismo con la zona de llegada en la planta N+1.
    - `Collision`: alguna pieza corta la huella de una pared de la planta N.
    - `HoleCrossesWall`: el hueco no queda dentro de una sola región de la planta N+1 o corta la huella de una pared de N+1.
    - Intersección de polígonos convexos por ejes separadores; huecos/zonas no convexos se prueban por sus triángulos.
  - `static class StairAutoFix { bool TryFix(BuildingData data, StairData s, out List<StairPoint> fixedRoute); }` — no modifica `s`. Para `ArrivalBlocked` prueba, en orden: (a) girar el último tramo ±90° alrededor de su punto inicial (cambiando el último punto); (b) añadir un punto `Corner` donde termina la escalera ahora y continuar ±90°. Para `DepartureBlocked`, lo simétrico con el primer tramo. Entre los dos giros prefiere el que deja más distancia libre a las paredes en la zona nueva. Acepta el primer candidato cuya validación no tenga `ArrivalBlocked`/`DepartureBlocked` ni issues nuevos; si ninguno, devuelve false.

- [ ] **Step 1: Write the failing tests** (planta 0 y planta 1 con un rectángulo exterior 8×6, salvo indicación):
  - `FreeStair_IsValid` → recta desde (2,1) hacia +y dentro del rectángulo con espacio → `Valid`.
  - `ArrivalAgainstWall_IsBlocked` → planta 1 con una pared interior justo delante de la arista superior → `ArrivalBlocked`.
  - `DepartureOutsideBuilding_IsBlocked` → la zona de salida cae fuera del rectángulo → `DepartureBlocked`.
  - `StepsThroughWall_Collision` → una pared interior de la planta 0 cruza la ruta → `Collision`.
  - `HoleAcrossUpperWall` → pared en la planta 1 que atraviesa el hueco → `HoleCrossesWall`.
  - `NoFloorAbove` → tras quitar la planta 1 manualmente de `floors` (sin `RemoveTopFloor`) → `NoFloorAbove`.
  - `AutoFix_TurnsLastFlightTowardFreeSide` → caso `ArrivalBlocked` con espacio libre a la derecha → `TryFix` true; la ruta corregida valida sin issues y su `endDir` es perpendicular a la original y apunta al lado libre.
  - `AutoFix_NoSolution_ReturnsFalse` → escalera encajonada (paredes delante y a ambos lados) → false y `s.route` intacta.
  - `AutoFix_DepartureSymmetric`.
- [ ] **Step 2: Run** `-testFilter StairValidatorTests` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** RUN_TESTS completo → PASS.
- [ ] **Step 5: Commit** `feat: stair validation and auto-fix`

---

### Task 4: Mallas de escalera y huecos en forjados

**Files:**
- Create: `Runtime/Geometry/StairMeshBuilder.cs`, `Tests/Editor/StairMeshTests.cs`
- Modify: `Tests/Editor/SlabMeshBuilderTests.cs` (cubre el parámetro `holes`)

**Interfaces:**
- Consumes: Task 2 (`StairLayoutResult`), `MeshData.AddFace`.
- Produces:
  - `static class StairMeshBuilder { MeshData Steps(StairData s, StairLayoutResult l, float baseY); MeshData Ramp(StairLayoutResult l, float baseY, float thickness = 0.05f); MeshData Rails(StairData s, StairLayoutResult l, float baseY, float height = 0.9f, float bar = 0.05f); }`
    - `Steps`: cada pieza es un prisma macizo desde `baseY` hasta `baseY + height`: cara superior `/tread` o `/landing`; la cara de su arista `front` solo en la franja [altura de la pieza anterior, altura de la pieza] con clave `/riser`; el resto de aristas, caras verticales completas `/side`. Más la última contrahuella sobre `topA–topB` hasta `H`. UV en metros (horizontales = xz; verticales = (metros a lo largo de la arista, altura)).
    - `Ramp`: por cada tramo entre descansillos (y por cada descansillo, plano), una losa de grosor `thickness` cuya cara superior pasa por las aristas delanteras de los escalones (de la arista inferior a la superior). Sin claves de material especiales (no se renderiza).
    - `Rails`: por cada lado activado, pasamanos continuo a `height` sobre las piezas, a `ancho/2 − bar/2` de la línea central, más postes en la primera y la última pieza y en cada descansillo; nunca más allá de las aristas inferior y superior (no invade las zonas de salida/llegada). Clave `/rail`.

- [ ] **Step 1: Write the failing tests:**
  - `SlabMeshBuilderTests.FloorHole_CutsTopUnderAndEdges` → región 6×6 con un agujero 1×2 → área de `/floor` y `/floor/under` = 36 − 2; aparecen caras de canto alrededor del agujero (4 quads, área total 6 × grosor). (Deuda del plan 1.)
  - `SlabMeshBuilderTests.CeilingHole_DoesNotCutRoof` → `BuildCeiling(f, true, holes)`: el techo tiene el agujero, la cubierta `/roof` no.
  - `Steps_TopsAtPieceHeights` → recta de 17 contrahuellas: la cara `/tread` de la pieza k está a `baseY + (k+1)·riser`; la última contrahuella llega a `baseY + 3.0`.
  - `Steps_RiserAreas` → suma de áreas `/riser` = ancho × H (±1e-3).
  - `Steps_WindingMatchesNormals` → reutiliza `WallMeshBuilderTests.AssertWindingMatchesNormals`.
  - `Ramp_PassesThroughNosings` → cada punto medio de arista delantera queda a ≤ 0.01 m de la cara superior de la rampa.
  - `Rails_StayWithinStair` → ningún vértice de `/rail` cae dentro de `departureZone` ni `arrivalZone`; sin barandillas (`railLeft = railRight = false`) → malla vacía.
- [ ] **Step 2: Run** `-testFilter "StairMeshTests|SlabMeshBuilderTests"` → FAIL (los de Stair; los de Slab pueden pasar ya: si pasan, se dejan como regresión).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** RUN_TESTS completo → PASS.
- [ ] **Step 5: Commit** `feat: stair meshes and slab hole coverage`

---

### Task 5: Escaleras en el renderer

**Files:**
- Modify: `Runtime/BuildingRenderer.cs`
- Create: `Tests/Editor/StairRendererTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - En `BuildFloorObject(i)`: `Floor i/Stairs/{id}` para cada escalera con `from == floors[i]`: hijo `Steps` (malla, `MeshCollider` solo si `collisionRamp == false`), hijo `Ramp` (solo `MeshCollider`, sin `MeshRenderer`, si `collisionRamp`), hijo `Rails` (malla + `MeshCollider`) si hay algún lado. Se generan aunque la validación falle.
  - Huecos: `SlabMeshBuilder.BuildFloor(floors[i], holes)` con los huecos de las escaleras cuyo `to == floors[i]`, y `BuildCeiling(floors[i], top, holes)` con los de las escaleras cuyo `from == floors[i]`; solo huecos de escaleras sin `HoleCrossesWall` y con layout `ok`.
  - Regenerar la planta i ya regenera i±1 (plan 1), así que cambiar una escalera de i→i+1 marca sucia la planta i.
  - `public IReadOnlyDictionary<string, StairValidation> StairValidations` en `BuildingRenderer`, actualizado en cada regeneración (lo usa el editor para pintar en rojo sin recalcular).

- [ ] **Step 1: Write the failing tests:**
  - `Stair_CreatesStepsRampAndRails` → dos plantas, escalera válida → existen `Floor 0/Stairs/{id}/Steps`, `/Ramp` con `MeshCollider` y sin `MeshRenderer`, `/Rails`; `Steps` sin `MeshCollider`.
  - `RampOff_StepsGetCollider`.
  - `Stair_CutsUpperFloorAndLowerCeiling_NotRoof` → con 2 plantas: el área de la cara `/floor` de la planta 1 y la de `/ceiling` de la planta 0 se reducen en el área del hueco; la cubierta (`/roof` de la planta 1) no cambia.
  - `MovingWallIntoStair_RevalidatesWithoutThrowing` → tras `MoveNode` que mete una pared en la escalera y `Regenerate()`, `StairValidations[id]` contiene `Collision`, la escalera sigue generada, sin excepciones en el log (`LogAssert.NoUnexpectedReceived`).
  - `ChangingFloorHeight_RecomputesSteps` → `SetFloorHeight(0, 3.6)` + regenerar → contrahuellas = round(3.6/0.18) = 20 y 19 piezas pisables; el hueco se recalcula.
  - `RemoveTopFloor_RemovesStairObjects_UndoRestores` → patrón de Undo de los tests del plan 1: quitar la planta, la escalera desaparece; `Undo.PerformUndo()` + regenerar → vuelve.
  - `Regenerate_NoLeakedMeshes` → mallas `DontSave` iguales tras 3 regeneraciones con escalera.
- [ ] **Step 2: Run** `-testFilter StairRendererTests` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** RUN_TESTS completo → PASS.
- [ ] **Step 5: Commit** `feat: generate stairs and cut slab holes`

---

### Task 6: Herramienta de escaleras en el editor

**Files:**
- Create: `Editor/StairDrawSession.cs`, `Editor/StairTool.cs`, `Tests/Editor/StairDrawSessionTests.cs`
- Modify: `Editor/BuildingOverlay.cs`, `Editor/BuildingInspector.cs`, `Editor/BuildingEditorUtil.cs`
- Create (docs): `docs/superpowers/plans/2026-11-14-level-builder-3-verificacion-manual.md`

**Interfaces:**
- Consumes: Tasks 1–5; `BuildingEditorUtil.Mutate/GuiToPlan/PlanToWorld/ActiveFloorIndex`, `GridSnap.Snap` con `WallTool.Grid` (la rejilla no cambia), patrón de `WallTool`/`OpeningTool`.
- Produces:
  - `class StairDrawSession { StairDrawSession(int fromFloorIndex); IReadOnlyList<StairPoint> Points; void Click(Vector2 p, bool smooth); bool RemoveLastPoint(); bool CanFinish /*≥ 2 puntos*/; StairData Preview(BuildingData data) /*sin añadirla a data*/; StairData Finish(BuildingData data) /*AddStair; null si no se puede*/; }` — un clic a menos de 0.01 m del punto anterior se ignora.
  - `static class StairSelection { Building building; string stairId; int pointIndex = -1; event Action Changed; }`
  - `class StairTool : EditorTool`:
    - Sin planta encima de la activa: no dibuja y muestra en el overlay "Añade una planta encima para crear una escalera".
    - Dibujo: clic = punto `Corner`, Shift+clic = punto `Smooth`, Retroceso = quitar último, Enter / doble clic = terminar (un paso de Undo "Crear escalera"), Esc = cancelar. Previsualización en vivo de piezas (verde si la validación no tiene issues, roja si los tiene, con el texto del motivo) y la longitud real.
    - Edición: clic sobre una escalera la selecciona; manipuladores en cada punto (arrastrar = mover, un paso de Undo por arrastre), clic en un punto lo selecciona (Supr lo quita si quedan ≥ 2), Supr sin punto seleccionado borra la escalera; manipulador lateral de ancho. Nunca borra el edificio.
    - Escaleras de la planta activa dibujadas en contorno; rojas si `StairValidations` tiene issues.
  - Overlay: botón toggle "Escalera" (excluyente con paredes y aberturas).
  - Inspector (escalera seleccionada): ancho, contrahuella objetivo, huella, barandilla izquierda/derecha, rampa de colisión; datos calculados (escalones, contrahuella real, longitud); tipo del punto seleccionado (Esquina/Curva); lista de problemas en español y botón **"Arreglar automáticamente"** (llama a `StairAutoFix.TryFix`; si devuelve true aplica la ruta con un paso de Undo; si no, muestra "No se encontró un arreglo automático"); botón "Borrar escalera".

- [ ] **Step 1: Write the failing tests:**
  - `Click_AddsCornerOrSmoothPoints`.
  - `Click_NearPreviousPoint_Ignored`.
  - `RemoveLastPoint_ThenFinishNeedsTwo` → `CanFinish` false con 1 punto.
  - `Preview_DoesNotTouchData` → `data.stairs.Count` sin cambios.
  - `Finish_AddsStair_OneUndoStep` → con `BuildingEditorUtil.Mutate`; `Undo.PerformUndo()` → sin escaleras; `PerformRedo()` → una.
  - `Finish_WithoutFloorAbove_ReturnsNull`.
- [ ] **Step 2: Run** `-testFilter StairDrawSessionTests` → FAIL.
- [ ] **Step 3: Implement** `StairDrawSession`, luego `StairTool`, overlay e inspector.
- [ ] **Step 4: Run** RUN_TESTS completo → PASS, 0 warnings.
- [ ] **Step 5: Write** la verificación manual (pasos en Unity): escalera recta, en L, en U (dos esquinas) y curva; ruta corta (se prolonga) y larga (termina antes); llegada contra una pared → rojo → "Arreglar automáticamente"; caso sin arreglo; mover una pared encima de la escalera → rojo; cambiar la altura de planta → escalones y hueco cambian; quitar la planta de arriba y Ctrl+Z; Play con un `CharacterController`: sube y baja sin engancharse (rampa), no queda atrapado arriba, barandillas no bloquean; la cubierta no tiene agujero; Undo de cada edición; abrir en 2022.3 y en 6.4+.
- [ ] **Step 6: Commit** `feat: stair editor tool, overlay and inspector`

---

## Decisiones tomadas en este plan (para revisar)

- Una escalera solo une plantas consecutivas (N→N+1). Para subir dos plantas se ponen dos escaleras.
- Curvas con Catmull-Rom centrípeta (pasa por tus puntos; equivale a tramos Bézier con tangentes automáticas), en lugar de Bézier con tiradores: menos manipuladores que ajustar.
- La escalera es maciza por debajo (cada pieza es un bloque hasta el suelo); sin hueco bajo la escalera en v1.
- Con la rampa activada, los escalones no tienen colisión propia: el jugador pisa la rampa invisible.
- Una escalera inválida se genera igual y se marca en rojo (al contrario que las aberturas, que no se generan), para que se vea qué está mal al mover paredes.
- El arreglo automático no se aplica solo: se ofrece con el botón y es un paso de Undo, para no mover tu ruta sin que lo veas.
- El hueco se corta en el forjado de arriba y en el techo de abajo solo si cae entero dentro de una habitación de la planta de arriba; si cruza una pared, la escalera queda roja (`HoleCrossesWall`) y no se corta nada.
