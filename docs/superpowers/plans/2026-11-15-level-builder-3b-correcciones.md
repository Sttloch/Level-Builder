# Level Builder — Plan 3b: Correcciones antes de materiales Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cinco mejoras pedidas por el usuario tras probar los planes 1–3: imán a paredes existentes al dibujar, quitar paredes de forma visible, bordes sin pared (pasarelas y balcones), tirador de altura de pared en la escena, y que la escalera también se pueda **bajar** con un `CharacterController` estándar.

**Architecture:** Cambios sobre lo existente, sin sistemas nuevos. El imán es lógica pura en `GridSnap`/`WallSnap` usada por `WallTool`. "Borde sin pared" = segmento con `heightOverride = 0`: genera suelo pero no muro; las regiones con algún borde más bajo que la planta son **abiertas** (suelo sin techo ni cubierta). La bajada de la escalera se reproduce primero con un test de `CharacterController` y se corrige donde falle.

**Tech Stack:** Igual que los planes anteriores (Unity 6000.0.47f1, paquete 2022.3, NUnit EditMode). **RUN_TESTS** igual.

**Spec:** `docs/superpowers/specs/2026-11-14-level-builder-design.md` (§2 "todo sigue siendo editable", §6.3 herramienta Pared, §6.8 "el jugador no queda atrapado").

## Decisiones del usuario (2026-11-15)

- Pasarelas/balcones: **bordes sin pared** dibujados con la herramienta de paredes (altura 0), no una herramienta de suelo aparte. Un antepecho de balcón = tramo de pared bajo (p. ej. 1 m).
- Quitar pared intermedia: no sabía que existía (Shift+clic + Supr) → hacerlo visible.
- Altura de paredes: **tirador en la escena**.

## Global Constraints

- Todas las de los planes 1–3 siguen vigentes (2022.3, Built-in/URP/HDRP, Runtime sin `UnityEditor`, `DontSave`, Undo en toda edición, nunca `GetInstanceID()` directo, UI en español, la rejilla de 0.5 m no cambia).
- Imán (solo con rejilla activa, es decir sin Ctrl/Cmd): nodo existente a ≤ **0.3 m** → ese nodo; si no, pared existente a ≤ **0.25 m** → punto sobre su eje, ajustado al cruce de esa pared con la línea de rejilla más cercana si está a ≤ 0.25 m a lo largo de la pared, si no la proyección; si no, rejilla normal. Prioridad: nodo > pared > rejilla.
- Altura de pared: `heightOverride` en [0, altura de planta]; **0 = borde sin pared**. Tirador con paso de **0.1 m** (Ctrl: libre), un paso de Undo por arrastre.
- Región **abierta** = alguno de sus segmentos tiene altura < altura de planta − 0.01. Las regiones abiertas tienen suelo (cara superior, inferior y cantos) pero **no** techo ni cubierta.
- Copia de paredes exteriores al crear planta: el contorno exterior se calcula **solo con paredes de altura completa**; los bordes bajos/sin pared no se copian.
- Paredes de altura 0: sin malla de muro, no cuentan como obstáculo para escaleras ni aberturas (una abertura en ellas es inválida, `TooTall`).

## Review Focus

1. **Imán contra la propia cadena que se está dibujando** → el punto puede pegarse a nodos/paredes ya creados por la misma cadena (para cerrar), nunca a la pared provisional del ratón; sin bucles ni nodos duplicados. (Task 1.)
2. **Quitar una pared que deja un nodo de grado 2 colineal** → los dos tramos restantes se unen en uno (sin nodo sobrante) y las aberturas de ambos se conservan con su posición; con Undo vuelve todo. (Task 2.)
3. **Balcón en planta 1 y luego "+ Planta"** → la planta 2 no copia el balcón y su habitación sigue cerrada (la pared entre habitación y balcón se copia). (Task 3.)
4. **Bajar la altura de una pared exterior enlazada** → solo cambia esa planta (la altura no se propaga por el enlace) y el techo de la habitación de esa planta desaparece porque pasa a ser abierta. (Task 3.)
5. **Bajar la escalera** → un `CharacterController` por defecto que parte de la planta de arriba llega abajo pasando la arista inferior, con H = 2.7 / 3.0 / 3.3 y rampa activada. (Task 5.)

---

### Task 1: Imán a paredes y nodos existentes

**Files:**
- Create: `Editor/WallSnap.cs`, `Tests/Editor/WallSnapTests.cs`
- Modify: `Editor/WallTool.cs` (clic, previsualización y arrastre de nodos usan el imán)

**Interfaces:**
- Produces: `static class WallSnap { const float NodeRadius = 0.3f, WallRadius = 0.25f; enum Kind { None, Node, Wall, Grid } Vector2 Snap(FloorData f, Vector2 p, float grid, out Kind kind, out string targetId, string ignoreNodeId = null); }` — `ignoreNodeId` excluye el nodo que se arrastra y sus segmentos.
- `WallTool`: sin Ctrl/Cmd usa `WallSnap.Snap` (antes `GridSnap.Snap`); con Shift (ángulo de 15°) se aplica primero el ángulo y luego solo el imán a nodo. Indicador visual del destino: anillo en nodo, marca cuadrada sobre pared.

- [ ] **Step 1: Write the failing tests:** `SnapsToNearbyNode`; `SnapsOntoWallAxis_AtGridCrossing` (pared (0,0)→(6,0) grosor 0.2, punto (2.1, 0.18) → (2.0, 0)); `SnapsOntoWallAxis_Projection_WhenNoGridNear` (pared diagonal); `NodeBeatsWall`; `FarPoint_FallsBackToGrid`; `IgnoresDraggedNodeAndItsWalls`.
- [ ] **Step 2: Run** `-testFilter WallSnapTests` → FAIL.
- [ ] **Step 3: Implement** `WallSnap` y conéctalo en `WallTool` (clic, preview, arrastre de nodo con `ignoreNodeId`).
- [ ] **Step 4: Run** RUN_TESTS completo → PASS.
- [ ] **Step 5: Commit** `feat: wall tool snaps to existing walls and nodes`

---

### Task 2: Quitar pared de forma visible

**Files:**
- Modify: `Runtime/Data/BuildingData.cs`, `Editor/BuildingInspector.cs`, `Editor/BuildingOverlay.cs` (texto de ayuda), `Editor/WallTool.cs`
- Test: `Tests/Editor/BuildingDataWallTests.cs`

**Interfaces:**
- Produces: `bool BuildingData.RemoveWall(FloorData f, string segmentId)` — `RemoveSegment` y después, para cada extremo que quede con exactamente dos segmentos colineales (desvío < 0.5°) y sin enlaces de planta, los une con `JoinSplit` (aberturas trasladadas como ya hace `JoinSplit`). Devuelve false si el segmento no existe.
- Inspector: con una pared seleccionada, botón **"Quitar pared"** (Undo "Quitar pared"); `WallTool`: Supr sobre pared seleccionada llama a `RemoveWall`. Ayuda del overlay: "Shift+clic: seleccionar pared · Supr o 'Quitar pared': quitarla (une las habitaciones)".

- [ ] **Step 1: Write the failing tests:** `RemoveWall_MergesTwoRoomsIntoOneRegion` (rectángulo 8×6 con pared interior x=4 → tras quitarla, `PlanarFaces` da 1 región); `RemoveWall_JoinsCollinearLeftovers` (los dos tramos de la pared sur partida en x=4 se unen en uno; sin nodo en (4,0)); `RemoveWall_KeepsOpeningsOnJoinedWall` (puerta en el tramo este conserva su posición absoluta); `RemoveWall_KeepsLinkedNodes` (nodo enlazado con planta 1 no se une).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** RUN_TESTS → PASS.
- [ ] **Step 5: Commit** `feat: visible remove-wall action that merges rooms`

---

### Task 3: Bordes sin pared y regiones abiertas (pasarelas y balcones)

**Files:**
- Modify: `Runtime/Data/BuildingData.cs` (`CopyExteriorFrom`), `Runtime/Geometry/PlanarFaces.cs` (marca `open` por región), `Runtime/Geometry/SlabMeshBuilder.cs`, `Runtime/Geometry/OpeningLayout.cs`, `Runtime/Geometry/StairValidator.cs`
- Test: `Tests/Editor/OpenRegionTests.cs`

**Interfaces:**
- Produces: `Region.open : bool` (calculado en `PlanarFaces.Compute(f)` según Global Constraints); `PlanarFaces.ComputeFullHeight(FloorData f)` (mismo resultado usando solo paredes de altura completa; lo usa `CopyExteriorFrom`).
- `SlabMeshBuilder.BuildCeiling`: omite regiones `open` (sin techo ni cubierta). `BuildFloor` las genera igual.
- `StairValidator`: huellas de pared solo de paredes con altura > 0.01. `OpeningLayout`: segmentos de altura 0 → `TooTall`.

- [ ] **Step 1: Write the failing tests:**
  - `ZeroHeightEdges_MakeFloorWithoutWallMesh` (rectángulo con un lado a 0: región con `/floor`, ningún triángulo del muro de ese segmento).
  - `OpenRegion_HasNoCeilingOrRoof` (planta única con balcón cerrado por antepecho de 1 m: la habitación tiene `/ceiling` y `/roof`; el balcón no).
  - `NewFloor_DoesNotCopyBalcony` (balcón en planta 0, `AddFloor` → la planta 1 tiene la habitación cerrada, sin los bordes del balcón; la pared habitación–balcón sí se copia).
  - `LoweringLinkedExteriorWall_OnlyThatFloor` (bajar `heightOverride` en planta 1 no cambia planta 0; la región de planta 1 pasa a abierta).
  - `ZeroHeightWall_NotAStairObstacle` y `OpeningOnZeroHeightWall_IsInvalid`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** RUN_TESTS → PASS.
- [ ] **Step 5: Commit** `feat: wall-less edges and open regions for walkways and balconies`

---

### Task 4: Tirador de altura de pared en la escena

**Files:**
- Modify: `Editor/WallTool.cs`, `Editor/BuildingInspector.cs`
- Create: `Tests/Editor/WallHeightTests.cs`

**Interfaces:**
- Produces: `static float WallTool.SnapHeight(float h, float floorHeight, bool free)` (paso 0.1, acotado a [0, floorHeight]).
- Con una pared seleccionada (Shift+clic), tirador vertical (`Handles.Slider` en el punto medio del borde superior, en el mundo); arrastrar cambia `heightOverride` (Undo "Altura de pared", un paso por arrastre; valor igual a la altura de planta → `heightOverride = -1`). Etiqueta junto al tirador: "2.40 m" o "Borde sin pared" en 0. Inspector: campo "Altura" (con la misma regla) y nota "0 = borde sin pared (pasarela/balcón)".

- [ ] **Step 1: Write the failing tests:** `SnapHeight_StepsOfTenCm`, `SnapHeight_ClampsToFloor`, `FullHeight_StoresMinusOne` (helper `WallTool.ApplyHeight(seg, h, floorHeight)`), `HeightDrag_IsOneUndoStep` (patrón de Undo de los tests existentes).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** RUN_TESTS → PASS.
- [ ] **Step 5: Commit** `feat: scene handle to adjust wall height`

---

### Task 5: La escalera también se baja

**Files:**
- Modify: `Tests/Editor/StairWalkTests.cs` y lo que el diagnóstico señale (`StairMeshBuilder`, `StairLayout`, `BuildingRenderer`)

**Interfaces:**
- Produces: test `DefaultCharacterController_DescendsDefaultStair(H, ramp)` simétrico al de subida: el CC parte en la planta N+1, centrado en la zona de llegada, se mueve en `-endDir` con gravedad, y debe terminar en la planta N más allá de la arista inferior (`bottomA–bottomB`), a la altura del suelo de N.

- [ ] **Step 1: Write the failing test** para H = 2.7 / 3.0 / 3.3 con rampa activada (y 3.0 / 3.3 sin rampa). Confirmar que falla como describe el usuario (sube pero no baja).
- [ ] **Step 2: Diagnose** con superpowers:systematic-debugging: registrar dónde se para el CC (posición, `collisionFlags`, collider golpeado con `OnControllerColliderHit` en un componente de test). Candidatos: borde del hueco (canto del forjado) por encima de la rampa en la primera pieza, colisionador de barandilla que sobresale del forjado de arriba en la arista superior, `StepsSides` o la última contrahuella atravesando la rampa.
- [ ] **Step 3: Fix** la causa raíz (no un parche en el test). Si es geométrica, añadir además un test de geometría que la fije.
- [ ] **Step 4: Run** RUN_TESTS completo → PASS (subida y bajada).
- [ ] **Step 5: Commit** `fix: stairs can be walked down by a default CharacterController`

---

### Task 6: Verificación manual

- [ ] **Step 1: Write** `docs/superpowers/plans/2026-11-15-level-builder-3b-verificacion-manual.md`: imán (dibujar una pared interior entre dos existentes sin Ctrl, y con Ctrl libre), quitar pared con el botón y con Supr + Undo, balcón con antepecho de 1 m y pasarela con bordes a 0 m (sin techo encima), "+ Planta" con balcón, tirador de altura (paso 0.1, 0 = borde, Undo), Play subiendo y bajando la escalera con un `CharacterController` estándar.
- [ ] **Step 2: Commit** `docs: manual verification for plan 3b`

## Decisiones tomadas en este plan (para revisar)

- La altura de una pared **no** se propaga por el enlace entre plantas (bajar una pared de arriba no toca la de abajo).
- Un balcón o pasarela **no tiene techo**; si quieres cubrirlo, la planta de arriba tendría que llegar hasta ahí con sus propias paredes.
- Quitar una pared une automáticamente los tramos rectos que sobran, para no dejar nodos sueltos.
- El imán solo actúa con la rejilla activa; con Ctrl dibujas libre como hasta ahora.
- La puerta que detecta al jugador sin triggers no está en este plan (quedó pendiente de que revises tu jugador); se puede añadir.
