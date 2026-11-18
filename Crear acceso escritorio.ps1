$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$target = Join-Path $root 'NitidezVN.exe'
$icon = Join-Path $root 'assets\NitidezVN.ico'
if (-not (Test-Path -LiteralPath $target) -or -not (Test-Path -LiteralPath $icon)) { throw 'Falta el ejecutable o el icono.' }
$desktop = [Environment]::GetFolderPath('DesktopDirectory')
if (-not $desktop -or -not (Test-Path -LiteralPath $desktop)) { throw 'No se encontró el escritorio del usuario.' }
$shell = New-Object -ComObject WScript.Shell
$linkPath = Join-Path $desktop 'Nitidez VN.lnk'
if (Test-Path -LiteralPath $linkPath) {
    $existing = $shell.CreateShortcut($linkPath)
    if ($existing.TargetPath -ne $target) {
        $linkPath = Join-Path $desktop 'Nitidez VN - filtros.lnk'
        if (Test-Path -LiteralPath $linkPath) {
            $other = $shell.CreateShortcut($linkPath)
            if ($other.TargetPath -ne $target) { throw 'Los dos nombres de acceso ya existen y son ajenos al proyecto.' }
        }
    }
}
$link = $shell.CreateShortcut($linkPath)
$link.TargetPath = $target
$link.WorkingDirectory = $root
$link.IconLocation = "$icon,0"
$link.Description = 'Nitidez VN — filtros en tiempo real para ventana y sin bordes'
$link.Save()
$verified = $shell.CreateShortcut($linkPath)
if ($verified.TargetPath -ne $target -or -not (Test-Path -LiteralPath $linkPath)) { throw 'No se pudo verificar el acceso.' }
Write-Output "Acceso creado: $linkPath"
Write-Output "Destino: $($verified.TargetPath)"
Write-Output "Icono: $($verified.IconLocation)"
