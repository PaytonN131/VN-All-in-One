# VN-All-in-One

**Nitidez VN 4.3**

Ajusta la imagen de tus novelas visuales, guarda sus voces y captura tus escenas favoritas.

[Descargar Nitidez VN 4.3](https://github.com/PaytonN131/VN-All-in-One/releases/tag/v4.3.0)

![Nitidez VN](docs/inicio.png)

## Cómo usarlo

Extrae toda la carpeta del RAR y abre **NitidezVN.exe**. Abre tu novela en ventana o sin bordes, selecciónala en **Inicio** y pulsa **Conectar juego**.

- **Imagen:** ajusta nitidez, brillo y colores. El botón **+** enciende o apaga el filtro.
- **Audio:** después de escuchar una frase, pulsa la tecla de audio para conservar los últimos segundos. En **Editar clips**, recorta, escribe el nombre y pulsa **Guardar MP3**. Se guarda directamente, sin preguntarte dónde.
- **Clips:** inicia y termina la grabación con sus teclas o con el botón. En el editor puedes recortar la escena y escribir el nombre del vídeo antes de guardarlo.
- **Capturas:** guarda una imagen de la novela con el filtro aplicado.
- **Teclas:** pulsa un campo y elige tu propia tecla o combinación.

Atajos iniciales: **.** para audio, **{** para empezar un clip, **}** para terminarlo y **<** para una captura. Funcionan con la novela al frente. **Ctrl + Alt + F8** alterna el filtro y **Ctrl + Alt + F10** cierra el programa.

## Tus archivos

Se guardan en **D:\Archivos VN All in One**, dentro de **Capturas**, **Grabaciones** y **Videos**, y separados por nombre de juego. Si no tienes unidad D:, se usa Documentos. Las carpetas se crean solas.

En cada panel tienes **Abrir carpeta** para ir directamente a tus archivos. En Inicio puedes corregir el nombre del juego si la ventana muestra algo de más.

Las capturas llevan nombres como **Wagamama_01.png**. El nombre del audio y del recorte del clip se escribe en su editor. También puedes renombrar archivos ya guardados desde sus paneles. Si un nombre ya existe al guardar, se añade un número para conservar ambos.

Los clips se guardan **con el filtro aplicado, sin reducir su calidad para cumplir un tamaño**. Elige **Original** y **60 FPS** para conservar el detalle y el movimiento. Puedes reducir estos ajustes si tu equipo lo necesita.

Se prefieren archivos pequeños, pero **30 MB no es un límite**: un clip puede pesar más si conservar la calidad lo requiere. El peso depende de la duración y del movimiento de la escena. Recortar no reduce la resolución ni los fotogramas por segundo. Los archivos anteriores se conservan.

Las capturas toman una imagen nueva al pulsar, sin reutilizar una de antes del diálogo. No se promete un instante exacto de cero milisegundos: dependen de cuándo el juego entrega su siguiente imagen. El cursor de Windows queda fuera de clips y capturas; si el juego dibuja su propio cursor dentro de la imagen, puede aparecer.

Los clips sin pérdida pueden necesitar un reproductor compatible. El editor prepara una vista previa ligera para poder abrirlos; el archivo que guardas sale del original y conserva su calidad.

## Antes de empezar

Para Windows 10 22H2 o Windows 11. La grabación de clips sin pérdida requiere una tarjeta NVIDIA compatible.

Para guardar audio y vídeo necesitas FFmpeg. Si el programa indica que falta, abre **Preparar FFmpeg.cmd** y sigue sus pasos. El filtro y las capturas pueden usarse sin él.

El resultado depende del juego y del equipo. Usa ventana o sin bordes; la pantalla completa exclusiva puede impedir que se vea el filtro.
