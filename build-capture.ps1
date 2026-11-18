param([string]$OutputDirectory = $PSScriptRoot, [switch]$TestHooks)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vc = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vc) { throw 'Se requieren Visual Studio Build Tools C++ y Windows SDK para compilar WindowCapture.dll.' }
$out = (Resolve-Path -LiteralPath $OutputDirectory).Path
$envScript = Join-Path $vc 'VC\Auxiliary\Build\vcvars64.bat'
$cmd = Join-Path $out 'compilar-captura.cmd'
$defines = if ($TestHooks) { '/DNITIDEZ_CAPTURE_TESTS' } else { '' }
@"
@echo off
call "$envScript" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /O2 /MT /LD $defines "$PSScriptRoot\WindowCapture.cpp" /Fo:"$out\WindowCapture.obj" /link /OUT:"$out\WindowCapture.dll" /IMPLIB:"$out\WindowCapture.lib" d3d11.lib d3dcompiler.lib dwmapi.lib runtimeobject.lib ole32.lib oleaut32.lib shell32.lib user32.lib
exit /b %errorlevel%
"@ | Set-Content -LiteralPath $cmd -Encoding Ascii
& cmd.exe /d /c $cmd
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar WindowCapture.dll.' }
