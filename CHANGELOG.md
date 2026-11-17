# Cambios

## 1.1.1

- La revisión Android busca el marcador de inicio por la hora del teléfono aunque Logcat reciba miles de mensajes durante el arranque.
- El informe indica cuándo la edición elegida de WO no está instalada.

## 1.1.0

- Revisión de APK e instalaciones de WO para Android desde el panel de Windows y la CLI.
- Arranque, captura, registros de Godot, cierres, ANR y protocolo de la escena QA interna.
- Las revisiones de escritorio indican que quedaron incompletas cuando un escenario se bloquea.

## 1.0.0 · 5 de noviembre de 2026

Primera versión de WO Tester para Windows.

- Aplicación de escritorio independiente con panel de pruebas, configuración e historial.
- Descubrimiento de catálogos y escenas de WO Definitive.
- Validación JSON y de recursos; simulación de mapas, disparos y una partida prolongada.
- Aceleración por lotes de pasos originales y pruebas nativas a velocidad normal.
- Ejecución con gráficos de los escenarios que necesitan verificar píxeles o puntero.
- Preparación de las carpetas de salida que requieren las pruebas nativas.
- Perfiles por escenario, cancelación, límites de tiempo y registros con reducción de exposición de tokens.
- Informes HTML, JSON, CSV y Markdown; comparación de contenido entre revisiones.
- Compilación reproducible sin descargas de dependencias y 40 comprobaciones internas.

La integración inicial se verificó con el proyecto editable WO Definitive versión 112 y Godot 4.7.2. Los resultados del juego se conservan en el historial local del usuario; no se incluyen datos personales en el repositorio.
