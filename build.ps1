param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (!(Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory | Out-Null }
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\WindowsFormsIntegration.dll') | ForEach-Object { '/r:' + (Join-Path $fx $_) }
& "$root\build-capture.ps1" -OutputDirectory $OutputDirectory
$sources = @("$root\NitidezVN.cs","$root\GameAudio.cs","$root\AudioClips.cs","$root\SceneFiles.cs","$root\SceneControls.cs","$root\WindowDisplay.cs","$root\PreparedScene.cs","$root\Shortcuts.cs","$root\CaptureFiles.cs")
& (Join-Path $fx 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ "/resource:$root\Interfaz.xaml,Interfaz.xaml" /win32icon:"$root\assets\NitidezVN.ico" /win32manifest:"$root\app.manifest" /out:"$OutputDirectory\NitidezVN.exe" $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar.' }
& (Join-Path $fx 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /win32manifest:"$root\app.manifest" /out:"$OutputDirectory\PruebaVN.exe" $refs $sources
if ($LASTEXITCODE -ne 0) { Write-Warning 'No se recompiló PruebaVN.exe. Cierra la prueba y compila de nuevo.' }
Write-Host 'Compilado: NitidezVN.exe'
