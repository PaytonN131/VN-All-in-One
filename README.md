# VN-All-in-One

**Nitidez VN 4.4**

Ajusta la imagen de tus novelas visuales, guarda sus voces y captura tus escenas favoritas.

[Descargar Nitidez VN 4.4](https://github.com/PaytonN131/VN-All-in-One/releases/tag/v4.4.0)

![Nitidez VN](docs/inicio.png)

## Cómo usarlo

Extrae toda la carpeta del RAR y abre **NitidezVN.exe**. Abre tu novela en ventana o sin bordes, selecciónala en **Inicio** y pulsa **Conectar juego**.

- **Imagen:** ajusta nitidez, brillo y colores. El botón **+** enciende o apaga el filtro.
- **Audio:** después de escuchar una frase, pulsa la tecla de audio para conservar los últimos segundos. Abre **Editar clips** para elegir el tramo y guardarlo como MP3.
- **Clips:** pulsa la tecla o el botón para empezar a capturar y vuelve a detenerlo cuando termine la escena. El clip se guarda con el filtro aplicado, sin límite de peso.
- **Capturas:** guarda una imagen nueva de la novela, con tus ajustes de imagen.
- **Teclas:** elige tus propias teclas o combinaciones.

Atajos iniciales: **.** para audio, **{** para empezar un clip, **}** para terminarlo y **<** para una captura. Funcionan con la novela al frente. **Ctrl + Alt + F8** alterna el filtro y **Ctrl + Alt + F10** cierra el programa.

## Editar audio y clips

Los dos editores están organizados en tres pasos:

1. **Elige y revisa.** Selecciona el audio o busca el momento del clip. Usa **▶** para escuchar o ver la selección.
2. **Ajusta el tramo.** Cambia el inicio y el final. En audio puedes arrastrar las marcas de la onda; en clips puedes marcar la posición que estás viendo.
3. **Ponle nombre y guarda.** Escribe el nombre en el panel de la derecha y pulsa Guardar. El MP3 se guarda directamente; guardar un recorte de clip crea otro archivo y conserva el original.

Los botones para descartar audio o enviar un clip a la Papelera están separados de Guardar. **Abrir carpeta** te lleva directamente a tus archivos.

## Grabación

Esta versión recupera el formato MP4 compatible y los ajustes de grabación de la 3.0. La captura ya está preparada cuando pulsas: conserva los fotogramas mientras el archivo empieza a escribirse, sin descartar un segundo al principio.

Usa **60 FPS** para escenas con movimiento. El archivo conserva el filtro aplicado. No se vuelve a comprimir para hacerlo más pequeño ni se impone un límite de MB. El tamaño depende de la duración y los ajustes que elijas.

## Tus archivos

Se guardan en **D:\Archivos VN All in One**, separados en **Capturas**, **Grabaciones** y **Videos**, con una subcarpeta por juego. Si no tienes unidad D:, se usa Documentos. Las carpetas se crean solas.

En **Inicio** puedes corregir el nombre del juego. Las capturas llevan nombres como **Wagamama_01.png**. Si un nombre de audio o recorte ya existe al guardar, se añade un número para conservar ambos.

Los archivos de versiones anteriores no se mueven ni se borran. El cursor de Windows queda fuera de las capturas y los clips; uno dibujado dentro del propio juego puede aparecer.

## Antes de empezar

Para Windows 10 22H2 o Windows 11. Grabar clips requiere una NVIDIA compatible. Para guardar audio y clips necesitas FFmpeg; si falta, abre **Preparar FFmpeg.cmd** y sigue sus pasos. El filtro y los PNG funcionan sin él.

La grabación está limitada a 30 minutos por clip y se detiene si queda poco espacio. La fluidez y el tiempo de respuesta dependen del juego y del equipo. Usa ventana o sin bordes; la pantalla completa exclusiva puede impedir que se vea el filtro.
