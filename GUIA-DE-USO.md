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

La opción **Iniciar automáticamente al abrir** aplica a las revisiones de escritorio. Android se inicia desde el panel. Para abrir sin iniciar, usá el argumento `--no-autorun`.

## Revisión Android

Conectá un teléfono con depuración USB autorizada o iniciá un emulador. En **Configuración → Android** elegí `adb.exe` si el SDK no aparece automáticamente. Si hay varios dispositivos, copiá la serie desde `adb devices -l`. Mantené la pantalla desbloqueada durante la prueba.

En **APK jugable de WO con pruebas integradas**, seleccioná el archivo de la edición de pruebas. Se requiere `aapt.exe` de **Android SDK Build-Tools** en el mismo SDK que `adb.exe`.

En el panel elegí **Revisión Android** y pulsá **Iniciar revisión**. WO Tester valida el APK, lo instala o actualiza y comprueba el arranque normal. Luego ejecuta datos, catálogo, mapas, disparos, estabilidad, menús, controles táctiles y las escenas nativas incluidas. El informe enlaza la captura de arranque si pudo obtenerla y registra los casos que requieren una adaptación específica. Los escenarios de QA guardan sus datos en un perfil propio del teléfono.

Para generar ese APK desde el proyecto editable de WO, ejecutá el exportador Android desde `VERSION-ACTUAL` apuntando al adaptador de WO Tester.

```powershell
python tools/android_online/build.py --tester-adapter "C:\ruta\a\WO-Tester\assets\probe.gd"
```

El resultado se guarda como `exports/android-online/WO-Definitive-Edition-Android-Pruebas.apk`. El mismo ícono abre el juego de forma normal.

## Leer resultados
Seleccioná una fila para ver la evidencia y qué conviene revisar. Podés copiarla o abrir su registro. **Abrir informe** abre un HTML local con búsqueda y filtros.

El historial conserva ejecuciones anteriores y sus archivos JSON, CSV, Markdown y registros. En el informe se listan los cambios de contenido desde la revisión anterior.

## Capturas
Activá **Mostrar los mapas y guardar capturas** en Configuración. Esta opción abre el juego para los escenarios de mapas; guardará PNG en la carpeta del informe. Las imágenes necesitan revisión humana.

## Límites
Las revisiones de escritorio requieren el proyecto editable y Godot. Online público, login real, nube, EAC, calidad visual/audio y rendimiento requieren verificaciones específicas.

Una mecánica nueva necesita una prueba que explique su comportamiento esperado. Las escenas nuevas tests/*_test.tscn se incorporan automáticamente.

Los datos del jugador se mantienen en perfiles separados. Algunas escenas nativas guardan fixtures dentro de .godot del proyecto. No se realizan correcciones automáticas.
