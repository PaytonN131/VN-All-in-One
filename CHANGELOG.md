# Historial de versiones

## 4.4 — Grabación compatible y editores organizados

- Recupera los ajustes H.264/NVENC y AAC de la versión 3.0, sin límite de tamaño ni compresión posterior para reducir el peso.
- Prepara la captura antes de iniciar y conserva los primeros fotogramas en memoria mientras arranca el codificador. No descarta un segundo inicial.
- Adelanta la preparación de las superficies de captura. La prueba de esta versión conservó el primer fotograma a los 7,4 ms en este equipo; el resultado depende de la carga y del hardware.
- Reorganiza ambos editores en tres pasos: revisión, selección del tramo y guardado con nombre.
- Separa las acciones de descartar o enviar a la Papelera de los controles de reproducción y guardado.
- Abre directamente los MP4 compatibles en la vista previa. Mantiene una vista previa alternativa para archivos antiguos que Windows no pueda reproducir directamente.
- Conserva las capturas nuevas por pulsación, exclusión del cursor de Windows, atajos, guardado directo de MP3 y carpetas por juego.

Verificación: grabación a 60 FPS, filtro visible en PNG y MP4, audio del proceso, recortes, reproducción en el editor, nombres personalizados y conservación de archivos existentes. Pruebas con una ventana animada; no implica compatibilidad universal con todos los juegos.
