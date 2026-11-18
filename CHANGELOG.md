# Historial de versiones

## 4.5 — Grabación a la resolución del juego

- «Del juego — automática» pasa a ser la opción inicial. El MP4 conserva el ancho y alto del área de imagen del juego, también cuando son impares.
- Se elimina el tope de 1920×1080 en el modo automático. Se mantienen 1080p y 720p como opciones manuales.
- La calidad de codificación se ajusta al número de píxeles cuando la ventana es mayor que 1080p.
- El tamaño actual aparece en el selector y en el estado de grabación.
- Si cambia el tamaño durante un clip, se finaliza el archivo con su resolución original y se avisa para iniciar otro. No se estira el contenido.
- Se descartan texturas antiguas que no correspondan a las dimensiones actuales de la ventana.
- Steam, los editores, los atajos y la organización de archivos se mantienen sin cambios.

Pruebas: MP4 a 640×360, 641×361, 1940×801 y 1600×1100, verificando dimensiones exactas y filtro; cambio de resolución durante la grabación; reproducción y recorte de un clip con dimensiones impares; guardado de audio y captura PNG desde la interfaz.
