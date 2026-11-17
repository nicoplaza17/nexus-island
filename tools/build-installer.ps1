<#
.SYNOPSIS
  Publica Nexus Island con .NET incluido y arma el instalador (artifacts\installer\NexusIsland-Setup-X.Y.Z.exe).

.NOTES
  Requiere .NET 10 SDK e Inno Setup 6 (winget install JRSoftware.InnoSetup).
  La versión se toma de Directory.Build.props.
#>
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$publish = Join-Path $root "artifacts\publish"

[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = $props.Project.PropertyGroup.Version
Write-Host "Nexus Island $version"

# 1) Publicar: autocontenido (incluye .NET), x64, precompilado (ReadyToRun: arranca más rápido y usa menos RAM privada).
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root "src\NexusIsland.App\NexusIsland.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -p:GenerateDocumentationFile=false `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló" }

$sizeMb = [math]::Round(((Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "Publicado en $publish ($sizeMb MB)"

# 2) Compilar el instalador
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "No se encontró Inno Setup 6. Instalalo con: winget install JRSoftware.InnoSetup" }

& $iscc "/DAppVersion=$version" /Q (Join-Path $root "installer\NexusIsland.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup falló" }

$setup = Join-Path $root "artifacts\installer\NexusIsland-Setup-$version.exe"
Write-Host ("Instalador: {0} ({1} MB)" -f $setup, [math]::Round((Get-Item $setup).Length / 1MB, 1))
