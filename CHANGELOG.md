# Nitidez VN 4.3

- Vídeo pasa a llamarse Clips.
- Grabación con filtro y codificación RGB sin pérdida; resolución original y 60 FPS de forma inicial.
- Se eliminó el límite de 30 MB y la compresión automática que reducía resolución o calidad.
- Recortes sin pérdida, conservando resolución y fotogramas por segundo.
- Cada captura de imagen usa una sesión nueva para no reutilizar fotogramas anteriores al diálogo. La sesión preparada de grabación se mantiene al día mientras espera.
- Cursor de Windows excluido en las sesiones de captura. Un cursor dibujado por el juego forma parte de su imagen y puede aparecer.
- Vista previa temporal compatible en el editor, separada del original; se elimina al cerrar y se cancela su preparación si se cierra antes.

Verificado en este PC: 60 fotogramas de prueba RGB idénticos tras codificar y decodificar; 45 fotogramas recortados idénticos al tramo original; clip a 1906 × 1066 y 60 FPS con filtro; tres cambios de diálogo tras pausas sin capturas antiguas; editor y guardado funcionando.
