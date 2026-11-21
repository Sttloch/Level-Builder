# Level Builder 4 – Materiales: verificación manual

Abre una escena con un Building que tenga al menos dos plantas, varias habitaciones, una puerta, una ventana y una escalera.

1. **Biblioteca por defecto**: en la Paleta pulsa el botón de crear biblioteca. Comprueba que aparece en `Assets/LevelBuilder/Materiales` y que los materiales no salen rosas. Asígnala en el Inspector del edificio (campo "Biblioteca"); Ctrl+Z la quita.
2. **Arrastrar a la Paleta**: arrastra un material existente; aparece en la cuadrícula. Arrastra a la vez `Brick_BaseColor`, `Brick_Normal` y `Brick_Rough`: se crea un único material "Brick" con color, normal y rugosidad asignados.
3. **Herramienta Pintar**: en el overlay pulsa "Pintar" (las otras herramientas se apagan). Sin material activo, junto al ratón se lee "Elige un material en la Paleta". Elige uno: el overlay lo muestra en "Material activo".
4. **Resaltado**: al mover el ratón, la cara bajo él se perfila en amarillo y su nombre aparece ("Pared – lado izquierdo", "Suelo", "Escalón – huella"…).
5. **Pintar**: clic en un lado de una pared → solo ese lado cambia. Shift+clic → toda la pared (ambos lados, cantos y tapa). Clic en un suelo → ese suelo; en un techo → ese techo. Shift+clic en un suelo → suelo, techo y cantos del forjado de esa habitación. Clic en un escalón → la huella; Shift+clic → toda la escalera. Clic en una jamba → las jambas de esa abertura.
6. **Cuentagotas**: Alt+clic (sin arrastrar) sobre una cara pintada → pasa a ser el material activo. Alt+arrastrar sigue orbitando la cámara sin tomar nada.
7. **Edición de paredes pintadas**: parte una pared pintada (doble clic), quita una pared entre dos habitaciones pintadas y mueve un nodo: los materiales se conservan y los suelos siguen pintados mientras su punto quede dentro.
8. **Escala real**: alarga una pared: la textura se repite (no se estira). Cambia el tamaño real del material en la Paleta: el edificio se actualiza.
9. **Plantas apiladas**: con dos plantas pintadas, suelo y techo coincidentes no parpadean.
10. **Inspector**: con una pared, abertura o escalera seleccionadas aparecen sus campos de material por cara; cambiarlos actualiza el edificio y Ctrl+Z lo deshace.
11. **Material borrado**: borra un material usado del proyecto: esas caras vuelven al material por defecto sin errores en la consola.
12. **Undo**: cada clic de pintura es un único paso de Ctrl+Z ("Pintar"), también Shift+clic.
13. **Pipelines**: repite los pasos 1, 2 y 5 en Built-in y, si puedes, en URP y HDRP.
