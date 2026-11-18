# Compilar Nitidez VN

Se necesitan Visual Studio Build Tools con C++ de escritorio y Windows SDK. Probado con SDK 10.0.26100 y .NET Framework 4.8.

Desde PowerShell, en la carpeta del código:

```powershell
.\build.ps1 -OutputDirectory .\dist
Copy-Item .\assets .\dist -Recurse -Force
```

Abre dist\NitidezVN.exe. Mantén WindowCapture.dll y assets junto al ejecutable. El programa usa FFmpeg completo para MP3 y MP4; grabar MP4 requiere h264_nvenc. La compilación incluye también PruebaVN.exe, una ventana sintética para verificar el filtro sin abrir un juego.

Los programas en pruebas se compilan con las mismas referencias y archivos que build.ps1, seleccionando su clase como punto de entrada. UiWorkflowProbe verifica la interfaz y la captura por juego; EditorSaveProbe comprueba el guardado de audio y vídeo con nombres elegidos; StorageProbe comprueba nombres y carpetas.

El icono aportado al proyecto se describe en assets/CREDITOS.md.
