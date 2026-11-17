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

| Paquete | Selección en el tester | Prueba |
| --- | --- | --- |
| `com.frrentertainment.wodefinitive` | WO Definitive | Arranque y estabilidad durante 10 segundos. |
| `com.frrentertainment.wodefinitive.test` | WO Definitive (pruebas) | El mismo arranque en el paquete de distribución de pruebas. |
| `com.frrentertainment.wodefinitive.qa` | QA interno (menús y controles) | La escena de WO prueba menús, mapas, gestos y disparos y emite un resumen con fallos y capturas. |

Dejá vacío **APK de WO Definitive** para usar el paquete ya instalado. Para instalar un archivo desde el tester, seleccioná el APK y su paquete. Se requiere `aapt.exe` de **Android SDK Build-Tools** en el mismo SDK que `adb.exe`. El APK interno `WO-Android-QA.apk` se genera con `python tools/android_online/build.py --smoke` desde `VERSION-ACTUAL` con las dependencias de exportación de WO configuradas.

En el panel elegí **Revisión Android** y luego **Iniciar revisión**. El informe incluye los registros de WO de esa ejecución y enlaza la captura de arranque cuando la obtuvo. En QA, el informe incluye el resumen y deja las capturas de la escena en el almacenamiento del paquete. Si el dispositivo es lento, aumentá **Tiempo máximo por prueba** para darle más tiempo a la escena de QA.

## Leer resultados
Seleccioná una fila para ver la evidencia y qué conviene revisar. Podés copiarla o abrir su registro. **Abrir informe** abre un HTML local con búsqueda y filtros.

El historial conserva ejecuciones anteriores y sus archivos JSON, CSV, Markdown y registros. En el informe se listan los cambios de contenido desde la revisión anterior.

## Capturas
Activá **Mostrar los mapas y guardar capturas** en Configuración. Esta opción abre el juego para los escenarios de mapas; guardará PNG en la carpeta del informe. Las imágenes necesitan revisión humana.

## Límites
Las revisiones de escritorio requieren el proyecto editable y Godot. Online público, login real, nube, EAC, calidad visual/audio y rendimiento requieren verificaciones específicas.

Una mecánica nueva necesita una prueba que explique su comportamiento esperado. Las escenas nuevas tests/*_test.tscn se incorporan automáticamente.

Los datos del jugador se mantienen en perfiles separados. Algunas escenas nativas guardan fixtures dentro de .godot del proyecto. No se realizan correcciones automáticas.
