# Level Builder — Documento de diseño

Fecha: 2026-11-14
Estado: borrador, pendiente de aprobación

## 1. Intención

Una herramienta de editor para Unity que permita armar escenarios jugables (interiores y edificios) **rápido y sin montar sistemas complejos**. El usuario define el recorrido —dónde van las paredes, por dónde sube una escalera, dónde hay una puerta— y la herramienta construye la geometría, los huecos, los suelos y techos, y aplica materiales.

Inspiración: *tsitski ProBuilder Tools* (Asset Store). No es una copia: se toma como referencia de categoría (herramienta de level design sobre el editor de Unity), no de funciones concretas.

**Por qué:** hoy, con ProBuilder a secas, hacer una escalera que siga una ruta, abrir el hueco de una puerta o cambiar una pared ya colocada obliga a rehacer geometría a mano. El usuario quiere iterar niveles cómodamente en sus propios proyectos (p. ej. VR_Escape, HookGame, CraftGame).

**Éxito:** en un proyecto vacío, instalar el paquete y en pocos minutos tener un edificio de dos plantas con paredes, puertas que abren en Play, ventanas, una escalera que conecta las plantas y materiales propios aplicados; y poder mover una pared después y que todo se reajuste.

## 2. Objetivos, no-objetivos y anti-objetivos

### Objetivos
- **Instalable en cualquier proyecto** como paquete UPM.
- **Todo sigue siendo editable** (paramétrico): mover un punto, cambiar una altura o desplazar una puerta regenera la geometría afectada conservando materiales.
- **Trabajo por plantas**: cada planta tiene altura, paredes, suelo y techo generados.
- **Paredes** dibujadas punto a punto sobre la planta.
- **Escaleras** que siguen una ruta definida por el usuario y conectan dos plantas (abriendo el hueco en el suelo de la planta superior).
- **Puertas funcionales** en Play y **ventanas**, colocadas sobre paredes, que abren su hueco automáticamente.
- **Materiales cómodos**: paleta con pintado por cara/pieza, texturas a escala real, y posibilidad de **agregar materiales y texturas propios**.

### No-objetivos (v1)
- Tejados (inclinados o a dos aguas). El techo de la última planta es plano.
- Terreno exterior, vegetación, iluminación automática.
- Generación procedural/aleatoria de niveles.
- Edición en tiempo de ejecución (todo es en el editor; solo las puertas tienen comportamiento en runtime).
- Booleanas/CSG genéricas sobre mallas arbitrarias.
- Muebles o props.

### Anti-objetivos (sería un fracaso aunque funcione)
- Que usarla requiera configurar muchas cosas antes de poder dibujar la primera pared.
- Que editar algo ya colocado rompa o pierda materiales, huecos o conexiones.
- Que las texturas se estiren al cambiar el tamaño de una pieza.
- Que el paquete meta código de editor en las builds del juego.

## 3. Restricciones

- **Unity 2022.3 LTS o superior** (mi decisión: misma base que la referencia; cubre Unity 6).
- **Render pipelines:** Built-in, URP y HDRP.
- **Distribución:** paquete UPM con `package.json`; instalable por Git URL o desde disco. Opcionalmente exportable como `.unitypackage`.
- **Dos ensamblados:** `Runtime` (datos del edificio, componente de puerta, generador de malla para que exista en builds si hace falta) y `Editor` (herramientas, ventanas, gizmos). El código de editor no entra en builds.
- **ProBuilder es dependencia opcional**: solo la usa el botón "Convertir a ProBuilder" (compilación condicional con `versionDefines`). Sin ProBuilder instalado, todo lo demás funciona.
- **Input de las puertas:** compatible con Input Manager clásico y con el Input System nuevo (detección por `versionDefines` / símbolos de compilación).
- **Undo/Redo** de Unity debe funcionar en todas las operaciones.

## 4. Enfoques considerados

| Enfoque | Qué es | Ventajas | Inconvenientes |
|---|---|---|---|
| **A) Generador propio desde datos** ✅ elegido | Cada edificio guarda una descripción (plantas, puntos, aberturas, materiales por cara). Se regenera la malla en cada cambio. Huecos calculados al generar. | Edición paramétrica fiable; sin depender de ProBuilder; control total de UV a escala real. | Hay que escribir la geometría (paredes con huecos, triangulación, UV). |
| B) Generar vía ProBuilder | Misma descripción, pero la salida es `ProBuilderMesh`. | Herramientas de caras de ProBuilder disponibles. | Las ediciones manuales se pierden al regenerar; las booleanas de ProBuilder son experimentales; los huecos habría que calcularlos igual. |
| C) Kit modular de prefabs | Piezas que encajan en rejilla. | Simplísimo de programar. | Escaleras no siguen rutas libres; necesita arte por pieza; rígido. Va contra la intención. |

**Elegido: A**, por el usuario, porque es el único que cumple "todo sigue siendo editable" de forma fiable. Se añade un **"Convertir a ProBuilder"** como salida de emergencia para retoques finales (la pieza convertida deja de ser paramétrica).

## 5. Lo que decidió el usuario

- Distribución como herramienta instalable en cualquier proyecto (paquete UPM).
- Enfoque A (generador propio a partir de datos).
- Todo sigue siendo editable tras colocarlo.
- Trabajo por plantas.
- Paleta de materiales con pintado por clic, y **poder subir/agregar materiales y texturas propios**.
- **Paredes entre pisos: copia + enlace.** Un piso nuevo arranca con copia de las paredes exteriores del de abajo, enlazadas (mover una mueve la otra); se pueden desenlazar para voladizos/retranqueos. Las interiores son independientes por piso.
- **Escaleras que no queden mal puestas:** el jugador no debe terminar mirando a una pared ni quedar atrapado al subir; la escalera se ajusta sola para evitarlo (ver §6.1 Escaleras y §6.8).
- Escalera de caracol de vuelta completa: no pedida explícitamente → fuera de v1.

## 6. Diseño

### 6.1 Modelo de datos (componente `Building`)

Un GameObject con el componente `Building` es la raíz de un edificio. Contiene (serializado en la escena):

- **Plantas** (`Floor`): índice, elevación, altura libre, grosor de forjado.
- **Paredes** por planta: un grafo de **nodos** (puntos 2D en el plano de la planta) y **segmentos** que los unen. Cada segmento: grosor, altura (por defecto la de la planta), y material por cara (interior / exterior / cantos / tapa superior).
- **Enlace entre pisos:** al crear un piso, sus paredes exteriores se copian del piso inferior y cada nodo copiado guarda un enlace al nodo de abajo. Mover un nodo enlazado mueve toda la columna de nodos enlazados (en cualquier piso). Acción "Desenlazar" por nodo/segmento. El suelo de un piso superior se genera de las regiones cerradas de **su propio** grafo de paredes (así un voladizo tiene suelo).
- **Aberturas** (`Opening`) sobre un segmento: tipo (puerta / ventana / hueco vacío), posición **en metros desde el nodo inicial del segmento** (mi decisión: al alargar/acortar la pared la puerta no se desliza; si deja de caber, se marca en rojo como inválida y no se borra), ancho, alto, altura desde el suelo (ventanas), y referencia al prefab de la hoja de puerta / cristal.
- **Suelos y techos**: generados automáticamente a partir de las regiones cerradas que forman las paredes de cada planta. Material por región. Si las paredes no cierran una región, no se genera suelo ahí (y la herramienta lo indica visualmente).
- **Escaleras** (`Stair`): planta de origen y planta de destino, ruta como secuencia de puntos de control con tramos rectos o curvos (Bézier; mi decisión), ancho, barandilla opcional por lado, material de huella/contrahuella/laterales. Cubre rectas, en L, en U y curvas; **caracol de vuelta completa queda fuera de v1**.
  - **Reglas de forma (mis decisiones):** número de escalones = diferencia de altura / contrahuella objetivo (~0.18 m, ajustable). La **huella se mantiene en un rango cómodo** (~0.25–0.32 m) y no se estira para llenar la ruta: la ruta marca dirección; la longitud real resultante se muestra en la previsualización. Descansillos automáticos solo en esquinas marcadas de la ruta (cambio de dirección > ~30°), de profundidad = ancho de la escalera; los tramos curvos tienen escalones en abanico sin descansillo.
  - **Ajuste automático para que no quede mal puesta** (petición del usuario): ver §6.8.
  - La escalera recorta su hueco en el suelo de la planta de destino, dimensionado para dejar altura libre de paso sobre toda la escalera.
- **Materiales por cara**: cada cara generada guarda una clave estable (p. ej. `segmento#id/interior`) → material. Así, al regenerar, se conserva el material aunque cambie la geometría.

**Identidad estable:** nodos, segmentos, aberturas y escaleras tienen IDs persistentes; los materiales y aberturas se anclan a IDs, no a índices, para que editar no los desordene. Si se borra un segmento, sus aberturas se borran con él; si se divide un segmento (insertando un nodo), las aberturas se reasignan a la mitad donde caen.

### 6.2 Generación de geometría

- Regeneración **por pieza sucia**: cambiar un segmento regenera ese segmento, sus vecinos (uniones) y las regiones de suelo/techo afectadas; no todo el edificio.
- **Paredes:** cada segmento se extruye con su grosor; las uniones entre segmentos se resuelven en ingletes (esquinas en L, uniones en T y en cruz). Los huecos de las aberturas se integran al construir cada cara (polígono de la cara con agujeros → triangulación), no con booleanas posteriores. Los huecos generan sus caras de "jamba".
- **Suelos/techos:** detección de ciclos cerrados en el grafo de paredes de cada planta → polígonos → triangulación (con agujeros para huecos de escalera).
- **Escaleras:** escalones generados a lo largo de la ruta; en tramos curvos, escalones en abanico; descansillos automáticos en los vértices de la ruta (mi decisión, ajustable).
- **UV a escala real:** proyección por cara en espacio mundo usando el tamaño en metros del material (definido en la biblioteca). Cambiar la longitud de una pared repite la textura, no la estira.
- **Salida:** un `MeshFilter`/`MeshRenderer` por pieza lógica (o por planta + submallas por material, a decidir por el constructor por rendimiento) y `MeshCollider` generado. Las mallas generadas se marcan como no editables a mano y se regeneran desde los datos.
- **Almacenamiento (mi decisión):** las mallas **no se guardan en la escena** (`HideFlags.DontSave`); se regeneran desde los datos al cargar (en el editor y en builds, en `Awake`), por eso el generador vive en `Runtime` y no puede usar APIs de editor. Ventajas: escenas ligeras, diffs limpios y edificios que funcionan como **prefabs**. Botón **"Hornear mallas"** que guarda las mallas como assets (carpeta configurable) y desactiva la regeneración en runtime, para quien prefiera no generar al cargar.
- **Organización en jerarquía:** `Building` → `Floor N` → `Walls`, `Floor`, `Ceiling`, `Stairs`, `Doors`, `Windows`.

### 6.3 Herramientas de editor

Barra de herramientas en la vista de escena (Overlay de Unity) con modos:

1. **Planta:** elegir planta activa, crear/borrar plantas, ajustar altura. Las otras plantas se muestran atenuadas o se ocultan.
2. **Pared:** clic para poner nodos y encadenar segmentos; doble clic o Esc para terminar; clic sobre un nodo existente para cerrar o enlazar. Snap a rejilla configurable y a ángulos (15°/45°/90°) con modificador. Arrastrar nodos para editarlos; clic en un segmento para insertar un nodo.
3. **Puerta / Ventana:** elegir tipo y preset; pasar el ratón sobre una pared muestra la previsualización; clic para colocar; arrastrar para moverla a lo largo del segmento (o a otro segmento). Manipuladores para ancho y alto.
4. **Escalera:** elegir planta de origen y destino; clic para definir los puntos de la ruta; manipuladores para mover puntos y ajustar ancho. La previsualización muestra los escalones en tiempo real.
5. **Pintar:** con la paleta abierta, clic en una cara pinta esa cara; con modificador, pinta la pieza entera (toda la pared, todo el suelo de la región).

El Inspector del `Building` muestra y permite editar numéricamente lo seleccionado (altura, grosor, ancho, etc.).

### 6.4 Biblioteca de materiales

- Un asset `MaterialLibrary` (ScriptableObject) con entradas: material + tamaño real de la textura en metros + nombre/categoría. Puede haber varias bibliotecas por proyecto; el `Building` referencia una.
- Ventana **Paleta**: rejilla de miniaturas, búsqueda por nombre, selección del material activo para pintar.
- **Agregar propios:**
  - Arrastrar un `Material` existente a la paleta → se añade como entrada.
  - Arrastrar una o varias **texturas** → la herramienta crea un material nuevo con el shader por defecto del pipeline activo (Standard / URP Lit / HDRP Lit), asignando color, normal y, si se reconocen por nombre (`_Normal`, `_Rough`, `_Metal`, `_AO`…), los demás mapas. El material se guarda en una carpeta configurable del proyecto.
- El paquete trae una pequeña **biblioteca por defecto** (unos pocos materiales neutros tipo greybox con rejilla de medida) para poder empezar sin nada.

### 6.5 Puertas en runtime (componente `Door`)

- Tipos: **batiente** (gira sobre bisagra, hacia un lado o ambos) y **corredera**.
- Activación por puerta: **por proximidad** (trigger) o **por interacción** (tecla configurable; en el Input System nuevo, una acción). Expone también métodos públicos `Open()`, `Close()`, `Toggle()` y eventos `UnityEvent` (OnOpened, OnClosed) para que otros sistemas (VR, puzles) la controlen.
- **Bloqueo opcional**: `Locked` con `Unlock()` público.
- Hoja de puerta y marco: el paquete trae prefabs simples por defecto; el usuario puede asignar los suyos en el preset (el hueco se ajusta al tamaño de la abertura, el prefab se escala o se elige según tamaño — ver §8).
- Animación por código (sin Animator), con velocidad configurable.

### 6.6 Ventanas

- Hueco + marco + cristal (malla simple con material transparente por defecto, reemplazable). Presets de tamaño. Sin comportamiento en runtime.

### 6.7 Convertir a ProBuilder

- Disponible solo si ProBuilder está instalado. Convierte una pieza o una planta entera en `ProBuilderMesh`, conservando materiales y UV. La pieza convertida sale del control del `Building` (deja de regenerarse) y se avisa al usuario antes.

### 6.8 Escaleras que se ajustan solas (validación y auto-ajuste)

Objetivo del usuario: que el jugador **no termine mirando a una pared ni quede atrapado** al subir, y que la escalera se ajuste sola para no quedar mal puesta. Mientras se dibuja o edita una escalera, la herramienta comprueba y corrige:

1. **Llegada libre:** al final de la escalera, en la planta de destino, debe haber una zona de llegada despejada (profundidad ≥ ancho de la escalera, mín. ~1 m) sin paredes delante. Si la ruta acaba contra una pared, la herramienta **gira el último tramo o añade un descansillo** hacia el lado libre más cercano; si no es posible, marca la escalera en rojo indicando el problema.
2. **Salida libre:** lo mismo al pie de la escalera en la planta de origen.
3. **Altura libre de cabeza:** sobre cada escalón debe haber ≥ ~2.1 m hasta el techo/forjado; el hueco del forjado superior se agranda automáticamente para cumplirlo.
4. **Sin choques:** los escalones y descansillos no pueden atravesar paredes; si una pared se cruza, el tramo se marca en rojo (y al mover una pared después, la escalera se revalida).
5. **Transitable:** contrahuella y huella siempre dentro de rangos que un `CharacterController` estándar puede subir (contrahuella ≤ step offset por defecto de 0.3 m; se mantiene ~0.18 m). Opcionalmente, una **rampa invisible de colisión** sobre los escalones para que el jugador suba suave sin engancharse (activada por defecto; mi decisión).
6. **Barandillas** no bloquean la llegada ni la salida.

Los avisos se ven en la escena (resaltado en rojo + mensaje) y en el Inspector, con botón "Arreglar automáticamente" cuando hay corrección posible.

## 7. Estructura del paquete

```
com.levelbuilder.core/          (nombre provisional — mi decisión)
  package.json
  Runtime/   LevelBuilder.Runtime.asmdef   (datos, generador, Door)
  Editor/    LevelBuilder.Editor.asmdef    (overlay, herramientas, paleta, inspector)
  Editor/ProBuilder/  (asmdef con defineConstraints para la conversión)
  Samples~/  (escena de ejemplo con un edificio de dos plantas)
  Resources o Assets por defecto: materiales greybox, prefabs de puerta/ventana
  Tests/     Editor + Runtime (tests de geometría y de datos)
  Documentation~/
```

## 8. Lo que queda al constructor

- Nombre definitivo del paquete y del menú.
- Estrategia de mallas: una malla por pieza vs. combinada por planta (equilibrio rendimiento/regeneración).
- Algoritmo de triangulación con agujeros (ear clipping propio o librería con licencia compatible) y de resolución de uniones de muros.
- Cómo se ajusta la hoja de puerta personalizada al hueco (escalar vs. ajustar el hueco al prefab).
- Valores por defecto (alturas, grosores, contrahuella, tamaños de rejilla).
- Atajos de teclado concretos.
- Detalles visuales de gizmos y previsualizaciones.
- Formato de serialización interno (siempre que IDs sean estables y funcione Undo).
