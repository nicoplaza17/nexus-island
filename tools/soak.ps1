<#
.SYNOPSIS
  Prueba de resistencia de Nexus Island: registra memoria, handles y objetos gráficos a lo largo del tiempo
  para detectar fugas (valores que crecen sin parar).

.EXAMPLE
  # Prueba real de 24 h con la isla en uso normal (una muestra cada 5 minutos):
  .\tools\soak.ps1 -Minutes 1440 -IntervalSeconds 300

.EXAMPLE
  # Prueba acelerada: la isla cambia de estado sola cada 150 ms durante 8 minutos:
  .\tools\soak.ps1 -Minutes 8 -IntervalSeconds 30 -Stress

.NOTES
  Si la isla ya está abierta, la usa tal cual (no la reinicia), salvo con -Stress.
  Los resultados quedan en un CSV junto al log de la app (%LocalAppData%\NexusIsland\logs).
#>
param(
    [int]$Minutes = 1440,
    [int]$IntervalSeconds = 300,
    [switch]$Stress,
    [string]$Exe = (Join-Path $PSScriptRoot "..\src\NexusIsland.App\bin\Release\net10.0-windows10.0.19041.0\NexusIsland.exe")
)

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SoakNative { [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr hProcess, uint flags); }
"@

if ($Stress) {
    Get-Process NexusIsland -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    $env:NEXUS_BENCH = "1"; $env:NEXUS_BENCH_MS = "150"
    Start-Process $Exe | Out-Null
    Remove-Item env:NEXUS_BENCH, env:NEXUS_BENCH_MS
    Start-Sleep -Seconds 8
}

$process = Get-Process NexusIsland -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) { Write-Error "Nexus Island no está corriendo."; exit 1 }

$csv = Join-Path $env:LOCALAPPDATA ("NexusIsland\logs\soak-{0:yyyyMMdd-HHmm}.csv" -f (Get-Date))
"Hora,MinutoTranscurrido,PrivadaMB,WorkingSetMB,Handles,Hilos,GDI,USER,CpuSegundos" | Set-Content $csv -Encoding UTF8
Write-Host "Registrando en $csv durante $Minutes min (muestra cada $IntervalSeconds s)..."

$start = Get-Date
$samples = @()
while ($true) {
    $process.Refresh()
    if ($process.HasExited) { Write-Warning "La isla se cerró durante la prueba."; break }
    $sample = [pscustomobject]@{
        Hora = (Get-Date).ToString("HH:mm:ss")
        Minuto = [math]::Round(((Get-Date) - $start).TotalMinutes, 1)
        PrivadaMB = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
        WorkingSetMB = [math]::Round($process.WorkingSet64 / 1MB, 1)
        Handles = $process.HandleCount
        Hilos = $process.Threads.Count
        GDI = [SoakNative]::GetGuiResources($process.Handle, 0)
        USER = [SoakNative]::GetGuiResources($process.Handle, 1)
        Cpu = [math]::Round($process.TotalProcessorTime.TotalSeconds, 1)
    }
    $samples += $sample
    # Formato invariante: con configuración regional en español los decimales llevan coma y romperían el CSV.
    (($sample.PSObject.Properties.Value | ForEach-Object { [System.Convert]::ToString($_, [cultureinfo]::InvariantCulture) }) -join ",") |
        Add-Content $csv -Encoding UTF8
    if (((Get-Date) - $start).TotalMinutes -ge $Minutes) { break }
    Start-Sleep -Seconds $IntervalSeconds
}

if ($Stress) { Get-Process NexusIsland -ErrorAction SilentlyContinue | Stop-Process -Force }

# Resumen: se compara la segunda mitad con la primera (la primera incluye el "calentamiento").
if ($samples.Count -ge 4) {
    $half = [math]::Floor($samples.Count / 2)
    $first = $samples[0..($half - 1)]; $second = $samples[$half..($samples.Count - 1)]
    Write-Host ""
    Write-Host ("{0,-12} {1,10} {2,10} {3,10} {4,10}" -f "Medida", "Inicio", "Final", "Máximo", "Tendencia*")
    foreach ($m in "PrivadaMB", "Handles", "Hilos", "GDI", "USER") {
        $avg1 = ($first | Measure-Object $m -Average).Average
        $avg2 = ($second | Measure-Object $m -Average).Average
        Write-Host ("{0,-12} {1,10} {2,10} {3,10} {4,10:+0.0;-0.0;0}" -f $m, $samples[0].$m, $samples[-1].$m,
            ($samples | Measure-Object $m -Maximum).Maximum, ($avg2 - $avg1))
    }
    Write-Host "* promedio de la segunda mitad menos el de la primera: cerca de 0 = estable; crecimiento sostenido = posible fuga."
}
Write-Host "CSV: $csv"
