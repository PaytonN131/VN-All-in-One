@echo off
where winget >nul 2>&1
if errorlevel 1 (
    echo No se encontro WinGet. Descarga la edicion Full en https://www.gyan.dev/ffmpeg/builds/
    pause
    exit /b 1
)
echo Se instalara FFmpeg Full mediante WinGet.
choice /C SN /N /M "Continuar [S/N]? "
if errorlevel 2 exit /b 0
winget install --id Gyan.FFmpeg --exact --source winget
if errorlevel 1 (
    echo Consulta el mensaje de WinGet. Tambien puedes usar la descarga manual del LEEME.
) else (
    echo Cierra y vuelve a abrir Nitidez VN.
)
pause
