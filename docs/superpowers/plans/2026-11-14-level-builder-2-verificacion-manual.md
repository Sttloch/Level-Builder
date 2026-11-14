# Plan 2 — Verificación manual en Unity (aberturas)

Pendiente. Hacer en Unity 6000.0.47f1 con `Assets/Dev/Sandbox.unity`. Los tests automáticos no cubren la interacción en la vista de escena ni el modo Play.

Preparación: crea un edificio (`GameObject > Level Builder > Building`) y dibuja un rectángulo de 8×6 con un muro interior.

1. Colocar con cada preset (overlay, fila "Abrir hueco"):
   - Botón **Puerta** → se activa la herramienta Aberturas (el botón queda marcado y "Dibujar paredes" se desmarca). Preset "Puerta" y "Puerta doble".
   - Botón **Ventana** → presets "Ventana" y "Ventana alta". Botón **Hueco** → preset "Hueco".
   - Al pasar el ratón por una pared se ve el hueco en verde; junto a una esquina o sobre otra abertura, en rojo.
   - Clic → se coloca con el centro ajustado a 0.1 m; con Ctrl (Cmd en Mac) sin ajuste. La pared se recorta, con jambas y dintel; la puerta tiene hoja y la ventana marco y cristal.
   - Volver a pulsar el botón marcado → vuelve la herramienta anterior.
2. Mover: clic en una abertura la selecciona (amarilla, sección "Abertura seleccionada" en el Inspector). Arrastrarla → se mueve a lo largo de la pared; llevarla a otra pared → cambia de pared. Un Ctrl+Z deshace el arrastre entero.
3. Manipuladores (puntos amarillos): bordes izquierdo/derecho → ancho simétrico respecto al centro; borde superior → alto; en ventanas y huecos, borde inferior → alféizar (las puertas no tienen) (la parte superior no se mueve). No bajan de 0.3 m. Si el resultado no cabe se muestra en rojo con el motivo, no se bloquea.
4. Acortar la pared (herramienta Paredes, arrastrar un nodo) hasta que la abertura no quepa → se dibuja en rojo con el motivo, la pared deja de recortarse y no hay hoja/ventana. Alargarla de nuevo → vuelve a ser válida en la misma posición (el centro se mide desde el nodo inicial).
5. Partir la pared con doble clic (herramienta Paredes) entre dos aberturas → cada una se queda en su mitad, en el mismo sitio.
6. Undo/Redo (Ctrl+Z / Ctrl+Y) de cada paso anterior: colocar, mover, cada manipulador, borrar, partir. La selección se limpia si la abertura deja de existir.
7. Teclas: Supr/Retroceso borra la abertura seleccionada y nunca el edificio (tampoco sin selección mientras la herramienta está activa). Esc deselecciona.
8. Inspector de una puerta: tipo, ancho, alto, centro y todos los ajustes de puerta (incluidos los eventos "Al abrirse" / "Al cerrarse" y "Prefab de hoja"). **"Bisagra a la derecha"** es vista desde la cara R de la pared: de pie en el lado R mirando a la pared, la bisagra queda a tu derecha; la herramienta marca la bisagra de la puerta seleccionada ("Bisagra"). En una ventana aparecen alféizar y "Prefab de ventana"; en un hueco, alféizar. Cada cambio regenera y se deshace con Ctrl+Z. Una abertura inválida muestra un aviso con el motivo. "Borrar abertura" la quita.
9. Play:
   - Un `CharacterController` con tag `Player` se acerca a una puerta en modo Proximity → se abre; al alejarse se cierra. Probar desde ambos lados.
   - Modo Interact → se abre/cierra con E, desde ambos lados.
   - Ventana con el cristal visible (transparente).
   - Puerta con un prefab de hoja propio → se escala al hueco.
10. Cristal: el material por defecto se crea en ejecución; si en una build (o en HDRP) el cristal no se ve transparente, asigna tu propio material transparente en el campo `glassMaterial` del `BuildingRenderer`.
11. Prefab del edificio: arrastra el edificio a Project, coloca una instancia, añade aberturas y recompila scripts → sin errores, sin hojas/ventanas duplicadas y sin objetos generados guardados en el prefab ni en la escena.
12. Abre el paquete en Unity 2022.3 y en una versión 6.4+ → compila sin errores ni avisos y las aberturas se generan igual.
13. Cambiar el tipo de una abertura en el Inspector: selecciona una ventana (alféizar 0.9) y cambia "Tipo" a Puerta → el alféizar pasa a 0 y desaparece del Inspector, el hueco baja hasta el suelo (sin cara de antepecho) y la hoja queda en el suelo dentro del hueco, sin solaparse con la pared. Ctrl+Z devuelve la ventana con su alféizar. Ventana → Hueco conserva el alféizar (campo y manipulador inferior visibles).
14. Puertas en modo Script: en el Inspector de la puerta, el campo "Id" muestra el identificador; "Copiar id" lo copia al portapapeles. En un script de prueba: `GetComponent<Building>().FindDoor("<id pegado>").Open();` (o `Close()`, `Toggle()`, `Unlock()`) → la puerta responde. Un id desconocido devuelve null. Tras regenerar (cambiar cualquier medida) hay que volver a llamar a `FindDoor`: la puerta anterior se destruye.
15. Modo Interact a ambos lados (puerta con "Abre a ambos lados"): colócate delante y pulsa E → la hoja se abre alejándose de ti; ciérrala, cruza al otro lado y pulsa E → se abre hacia el lado contrario, también alejándose de ti. Repetir con "Bisagra a la derecha" marcada.
16. "Cualquier objeto" (puerta en Proximity o Interact): al marcarlo desaparece "Tag del jugador" y cualquier collider abre la puerta (p. ej. un cubo con Rigidbody empujado hacia ella); al desmarcarlo vuelve el tag `Player`. Con el tag `Player` solo en el objeto del Rigidbody y un collider hijo sin tag, la puerta también se abre.
