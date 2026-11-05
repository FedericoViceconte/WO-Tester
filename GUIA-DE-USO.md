# WO Tester · guía rápida

Abrí **WO Tester.exe**. La aplicación se mantiene y descarga por separado de WO Definitive.

## Primera configuración
En Configuración, seleccioná **project.godot** dentro de VERSION-ACTUAL y el **Godot de consola** de la misma versión. Si el juego todavía no fue importado, abrilo primero en Godot y esperá a que termine.

Guardá la configuración. El panel muestra los mapas, definiciones de armas, mascotas y escenas detectadas.

## Revisar
- **Completa:** datos, catálogo, mapas, disparos, estabilidad y todas las escenas nativas.
- **Rápida:** datos, catálogo, mapas y escenarios críticos.
- **Repetir fallos anteriores:** repite las escenas nativas con errores o bloqueos y las comprobaciones comunes.

Elegí 1×, 2×, 5× o 10×. La aceleración es del adaptador; las escenas nativas se prueban a 1×. Podés detener la revisión: se cierra el proceso activo y se guarda lo obtenido.

La opción **Iniciar automáticamente al abrir** inicia la revisión seleccionada en las próximas aperturas. Para abrir sin iniciar, usá el argumento --no-autorun.

## Leer resultados
Seleccioná una fila para ver la evidencia y qué conviene revisar. Podés copiarla o abrir su registro. **Abrir informe** abre un HTML local con búsqueda y filtros.

El historial conserva ejecuciones anteriores y sus archivos JSON, CSV, Markdown y registros. En el informe se listan los cambios de contenido desde la revisión anterior.

## Capturas
Activá **Mostrar los mapas y guardar capturas** en Configuración. Esta opción abre el juego para los escenarios de mapas; guardará PNG en la carpeta del informe. Las imágenes necesitan revisión humana.

## Límites
Esta versión requiere el proyecto editable y Godot. No certifica el online público, la nube, EAC, calidad visual/audio ni el rendimiento de otros equipos. Los resultados se refieren a los escenarios ejecutados.

Una mecánica nueva necesita una prueba que explique su comportamiento esperado. Las escenas nuevas tests/*_test.tscn se incorporan automáticamente.

Los datos del jugador se mantienen en perfiles separados. Algunas escenas nativas guardan fixtures dentro de .godot del proyecto. No se realizan correcciones automáticas.
