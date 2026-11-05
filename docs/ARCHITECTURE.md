# Arquitectura

WO Tester es una aplicación WPF sobre .NET Framework 4.8, con un motor reutilizable por interfaz gráfica y línea de comandos.

## Flujo

1. Scanner valida el proyecto, lee los catálogos, descubre escenas y calcula una huella SHA-256 del código, escenas, datos y pruebas.
2. Runner crea un directorio de ejecución y perfiles por proceso, comprueba la versión de Godot y ejecuta validaciones.
3. Un adaptador GDScript embebido se extrae únicamente al directorio de resultados. Godot lo carga como SceneTree externo usando el proyecto original.
4. El adaptador emite eventos JSON prefijados y un marcador de finalización. Runner separa el protocolo de los errores del motor.
5. Las escenas nativas corren como procesos independientes. Las escenas que usan frame_post_draw o warp_mouse se ejecutan con gráficos a 1280×720 para evitar bloqueos y falsos resultados del modo sin gráficos. Sus requisitos de aislamiento se descubren en su script, incluidos escenarios de mini mascotas que comparten código.
6. Reports guarda resultados parciales y exporta HTML, JSON, CSV y Markdown. La interfaz recibe actualizaciones a través de Dispatcher.

## Aceleración
WO Definitive implementa su física mediante pasos discretos de Physical.DT y un contador de ticks. El adaptador desactiva _physics_process automático y llama al mismo método en lotes. No modifica time_scale. Cada arma usa una partida nueva y la misma semilla; cada mapa usa una semilla derivada estable.

Esto acelera la lógica, pero no equivale a probar todas las carreras temporales o animaciones reales. La evidencia declara el alcance. Los escenarios nativos mantienen su reloj normal.

## Aislamiento y procesos
APPDATA, LOCALAPPDATA, XDG_DATA_HOME y XDG_CONFIG_HOME apuntan al perfil del escenario. No se copian tokens de cuentas reales. El adaptador exige WO_TESTER_ISOLATED=1 y asigna Profile.file_path y Settings.file_path propios.

Cada proyecto admite una ejecución por carpeta de datos del tester. La cancelación y los límites cierran exclusivamente el proceso iniciado por Runner. Las escenas nativas se ejecutan como código de confianza y podrían iniciar sus propios procesos: no se proporciona un aislamiento de seguridad del sistema operativo.

## Evidencia
report.json usa el esquema wo-tester.report/1. Guarda versión del tester/juego/Godot, commit, modificaciones locales, huella, semilla, resultados y diferencias de contenido. La huella cubre código/datos/escenas/pruebas; no es una firma completa de todos los binarios y recursos gráficos.

PASS describe las aserciones ejecutadas, no toda una característica. Los cierres sin resumen verificable quedan en REVIEW. Tiempo agotado y protocolo incompleto quedan en BLOCKED. Los errores de recursos aún en uso al cierre quedan visibles como REVIEW.

## Extensión
Una escena tests/nombre_test.tscn puede informar un resumen N checks, M failures y salir con 0/1. Para requisitos adicionales o nuevas API del juego, ampliar el adaptador y las pruebas del tester. Nunca convertir una incompatibilidad en un resultado aprobado.
