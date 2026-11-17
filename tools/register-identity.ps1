<#
.SYNOPSIS
  Registra (o quita) la identidad de paquete de Nexus Island, necesaria para leer notificaciones de Windows.

.DESCRIPTION
  Registra packaging\identity\AppxManifest.xml como "paquete con ubicación externa" apuntando a la carpeta
  donde está NexusIsland.exe. No copia ni modifica la app: solo le da identidad.

  En desarrollo se registra SIN FIRMAR, lo que requiere el Modo de desarrollador de Windows
  (Configuración → Sistema → Para programadores). Para distribuir a otras PCs hay que firmar el paquete.

.EXAMPLE
  .\tools\register-identity.ps1                        # versión compilada (bin\Release)
  .\tools\register-identity.ps1 -Installed             # versión instalada con el instalador
  .\tools\register-identity.ps1 -Unregister            # quitar la identidad
#>
param(
    [switch]$Installed,
    [string]$ExternalLocation,
    [switch]$Unregister
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$packageName = "NexusIsland"

function Remove-Identity {
    $existing = Get-AppxPackage -Name $packageName
    if ($existing) {
        Remove-AppxPackage -Package $existing.PackageFullName
        Write-Host "Identidad anterior quitada ($($existing.PackageFullName))."
    }
}

if ($Unregister) {
    Remove-Identity
    if (-not (Get-AppxPackage -Name $packageName)) { Write-Host "Nexus Island ya no tiene identidad de paquete." }
    exit 0
}

# 1) Modo de desarrollador (necesario para registrar sin firmar)
$unlock = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -ErrorAction SilentlyContinue
if ($unlock.AllowDevelopmentWithoutDevLicense -ne 1) {
    throw "El Modo de desarrollador está desactivado. Activalo en Configuración → Sistema → Para programadores y volvé a correr este script."
}

# 2) Carpeta del .exe
if (-not $ExternalLocation) {
    $ExternalLocation = if ($Installed) { Join-Path $env:LOCALAPPDATA "Programs\Nexus Island" }
                        else { Join-Path $root "src\NexusIsland.App\bin\Release\net10.0-windows10.0.19041.0" }
}
$ExternalLocation = (Resolve-Path $ExternalLocation).Path
if (-not (Test-Path (Join-Path $ExternalLocation "NexusIsland.exe"))) { throw "No está NexusIsland.exe en $ExternalLocation" }

# 3) Con -ExternalLocation, Windows busca el manifiesto DENTRO de la carpeta del .exe (no en la ruta indicada):
#    se copian AppxManifest.xml y sus logos junto a NexusIsland.exe.
Copy-Item (Join-Path $root "packaging\identity\AppxManifest.xml") $ExternalLocation -Force
Copy-Item (Join-Path $root "packaging\identity\Assets") $ExternalLocation -Recurse -Force

# 4) Registrar (si la app está abierta, se cierra: tiene que reabrirse para tomar la identidad)
Get-Process NexusIsland -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Identity
Add-AppxPackage -Register (Join-Path $ExternalLocation "AppxManifest.xml") -ExternalLocation $ExternalLocation
$pkg = Get-AppxPackage -Name $packageName
Write-Host "Identidad registrada: $($pkg.PackageFullName)"
Write-Host "Ubicación del .exe:   $ExternalLocation"
Write-Host "Abrí Nexus Island de nuevo para que la tome."
