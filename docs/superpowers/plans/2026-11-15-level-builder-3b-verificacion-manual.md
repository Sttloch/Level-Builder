# Plan 3b — Verificación manual (correcciones de paredes, balcones, altura y escaleras)

Pendiente. Hacer en Unity 6000.0.47f1 con `Assets/Dev/Sandbox.unity`. Los tests automáticos no cubren la interacción en la vista de escena ni el modo Play.

Preparación: crea un edificio (`GameObject > Level Builder > Building`), dibuja un rectángulo de 10×8 con un muro interior y pulsa **+ Planta** (la planta 1 copia el contorno exterior). Vuelve a la planta 0 como planta activa.

1. Imán al dibujar paredes (herramienta Paredes, rejilla activa):
   - Sin Ctrl, acerca el cursor a un nodo existente (menos de 0.3 m): el punto se pega al nodo y aparece un anillo indicador.
   - Sin Ctrl, acerca el cursor al eje de una pared existente (menos de 0.25 m): el punto se pega al eje, preferiblemente en el cruce con la rejilla, y aparece un cuadrado indicador.
   - Dibuja una pared interior entre dos existentes sin Ctrl: queda enganchada en sus dos extremos, sin huecos.
   - Con Ctrl (Cmd en Mac): la rejilla y el imán a paredes se desactivan y el punto va libre. Los nodos existentes siguen enganchando.

2. Quitar pared:
   - Shift+clic en una pared interior: queda seleccionada.
   - Pulsa **Quitar pared** en el Inspector: las dos habitaciones quedan en un solo suelo y los tramos rectos sobrantes se unen (no quedan nodos sueltos).
   - Repite el paso con **Supr** en lugar del botón: el resultado es el mismo.
   - Ctrl+Z: vuelven la pared y las dos habitaciones.
   - Quita una pared cuyos tramos sobrantes tienen distinto grosor o altura: esos tramos **no** se unen y siguen como antes.

3. Balcón y pasarela:
   - Dibuja un contorno fuera del edificio, adosado a una fachada, con tramos a 1 m (antepecho): se crea el suelo del balcón y no tiene techo encima.
   - Dibuja una pasarela con tramos a 0 m (borde sin pared): se crea el suelo, sin muro ni techo.
   - Con el balcón dibujado, pulsa **+ Planta**: la planta 1 no copia el balcón y la habitación de arriba queda cerrada.
   - Dentro de una habitación, dibuja media pared o una jardinera (un muro que no cierra la habitación): la habitación conserva su techo.

4. Tirador de altura:
   - Shift+clic en una pared: aparece un tirador en su borde superior.
   - Arrastra el tirador: sube y baja en pasos de 0.1 m. Con Ctrl el movimiento es libre.
   - La etiqueta muestra "X.XX m". A 0 m muestra "Borde sin pared".
   - Cambia el campo **Altura** en el Inspector: la pared y el tirador se actualizan.
   - Baja una pared de la planta 1: la pared de la planta 0 que queda debajo no cambia.
   - Ctrl+Z: deshace el arrastre entero, no un paso por cada movimiento.

5. Escaleras y Play con `CharacterController` estándar (radio 0.5, altura 2), rampa de colisión activada:
   - Repite para plantas de 2.7 m, 3.0 m y 3.3 m (cambia la altura de la planta 0 en el overlay o en el Inspector y regenera la escalera).
   - En cada altura, entra en Play y sube por la escalera hasta la planta 1, luego baja. No debe engancharse en los escalones ni quedarse atrapado al llegar arriba.
   - Con 2.7 m, el hueco del forjado se alarga solo por delante de la escalera.

6. Hueco ajustable (sección **Hueco** del Inspector de la escalera):
   - **Altura libre** (1.5–4 m): al cambiarla, la etiqueta "Personaje de hasta X m" se actualiza.
   - **Margen del hueco** (0–1 m): alarga el hueco hacia atrás y ensancha los lados, pero nunca más allá del último escalón.
   - Arrastra el tirador de la escena situado en el borde inicial del hueco: se mueve en pasos de 0.05 m y ajusta el margen en el Inspector.
   - Sube el margen hasta que toque una pared: la escalera se pone roja.
   - Ctrl+Z: deshace cada cambio de altura libre, margen o arrastre.

7. Compatibilidad de versiones:
   - Abre el paquete en Unity 2022.3 y en una versión 6.4+: compila sin errores ni avisos y las escaleras se generan igual.

8. Escaleras existentes (guardadas antes de este cambio):
   - Se regeneran con huecos algo mayores: el hueco ahora siempre deja espacio para el desplazamiento de escalón (step offset) de la cápsula, necesario para poder bajar andando.
   - Las escaleras antiguas, sin altura libre guardada, usan 2.25 m por defecto.
