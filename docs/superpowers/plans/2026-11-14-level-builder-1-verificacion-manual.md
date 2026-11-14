# Plan 1 — Verificación manual en Unity

Pendiente. Hacer en Unity 6000.0.47f1 con `Assets/Dev/Sandbox.unity`. Los tests automáticos (115) no cubren la interacción en la vista de escena.

1. `GameObject > Level Builder > Building` → aparece en la jerarquía; Ctrl+Z lo quita.
2. Herramienta Paredes (botón "Dibujar paredes" del overlay):
   - Dibuja un rectángulo de 8×6 y ciérralo con clic en la primera esquina.
   - Clic en una esquina existente sin arrastrar → solo se selecciona. Segundo clic → empieza una pared desde ella.
   - Dibuja un muro interior de una pared a la opuesta → esquinas limpias, suelo y techo en las dos habitaciones.
3. "+ Planta" → planta 1 con solo las paredes exteriores, a 3 m, y pasa a ser la activa. Con la planta 0 activa, la 1 se oculta. Al deseleccionar el edificio se ve todo.
4. Arrastra una esquina en planta 0 (cubo cian = enlazada) → se mueve también en planta 1. Un Ctrl+Z deshace el arrastre entero.
5. En planta 1, selecciona esa esquina, pulsa "Desenlazar nodo" en el Inspector y arrástrala → solo cambia la planta 1, que tiene su propio suelo (voladizo).
6. Guarda, cierra y reabre la escena → el edificio se regenera; el `.unity` no contiene mallas generadas (`m_Mesh`).
7. Play → el edificio está, con colisiones y todas las plantas visibles.
8. Gestos:
   - Doble clic en medio de una pared → se parte y el nodo nuevo queda seleccionado; Supr lo borra. Doble clic junto a un extremo → no pasa nada.
   - Shift+clic en una pared → el Inspector muestra su grosor.
   - Esc, Enter o doble clic terminan la cadena.
   - Dibuja, Ctrl+Z, vuelve a dibujar → la pared nueva se queda.
   - Sin nada seleccionado en la herramienta, Supr borra el edificio como siempre.
   - Recompila scripts con el edificio seleccionado y deselecciónalo → todas las plantas visibles.
9. Arreglos de la revisión final:
   - Desactiva y reactiva el edificio (y Ctrl+Z / Ctrl+Y) → sin error "Cannot destroy GameObject while it is being activated" y sin plantas duplicadas.
   - Backspace mientras dibujas → quita el último punto (con uno solo, cancela). Supr/Backspace mientras dibujas nunca borra el edificio.
   - Arrastra un nodo sobre otro → se fusionan. Sobre el centro de una pared → la parte (unión en T). Que su pared cruce otra → aparece un nodo en el cruce. En una esquina enlazada pasa en todas las plantas. Un Ctrl+Z lo deshace todo.
   - Cambiar la planta activa no añade entradas a Edit > Undo History.
   - Clic en una pared generada → selecciona el edificio (las piezas generadas están ocultas en la jerarquía).
   - Edificio nuevo → el Inspector muestra Default Material = Default-Material. En una build del juego las paredes no salen rosas.
   - Prefab: arrastra el edificio a Project, coloca una instancia, dibuja, cambia de planta y desactívala/actívala → sin errores, sin plantas duplicadas y sin objetos generados guardados en el prefab ni en la escena.
10. Abre el paquete una vez en Unity 2022.3 para confirmar que compila (todo se probó en Unity 6).

## Notas para los planes siguientes (de la revisión)

- **Plan 2 (puertas y ventanas):** una puerta que toca el borde inferior de la pared debe ser una muesca del contorno, no un agujero. Hay que limitar el recorte de las paredes muy cortas antes de abrir huecos cerca de las esquinas. Las aberturas pasan a la mitad correcta usando el evento `BuildingData.SegmentSplit`. Hay que suscribirse a través de `Building` y volver a suscribirse después de un Undo, porque Undo reemplaza la instancia de datos. `RemoveDegenerateSegments` borra sin avisar: necesita un evento si las paredes llevan datos.
- **Plan 3 (escaleras):** el parámetro `holes` de los forjados no tiene test. Los huecos de escalera no deben cortar la losa de cubierta.
- **Plan 4 (materiales):** el forjado inferior de la planta N y el techo de la N−1 son caras coplanares, y con materiales distintos parpadearán (z-fighting). La identidad de una región (su clave, la lista de ids de segmento) cambia al partir una pared, así que hace falta una identidad más estable, por solape o con un punto semilla. Las caras L/R de cada pared hay que traducirlas a interior/exterior con `exteriorSegmentIds`.
