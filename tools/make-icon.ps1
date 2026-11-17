<#
.SYNOPSIS
  Genera el ícono de Nexus Island (assets\NexusIsland.ico) en todos los tamaños que usa Windows,
  más una vista previa PNG de 256 px. Diseño: cuadrado redondeado oscuro con la "isla" (cápsula) y su punto verde.
#>
param([string]$OutDir = (Join-Path $PSScriptRoot "..\assets"))

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)

    # Fondo: cuadrado redondeado con degradé sutil (en 16 px se omite el margen para ganar detalle)
    $m = if ($size -le 16) { 0 } else { $size * 0.04 }
    $bg = New-RoundedPath $m $m ($size - 2 * $m) ($size - 2 * $m) ($size * 0.22)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $size),
        ([System.Drawing.Color]::FromArgb(255, 44, 44, 54)), ([System.Drawing.Color]::FromArgb(255, 14, 14, 18))
    $g.FillPath($grad, $bg)
    if ($size -ge 32) {
        $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 255, 255, 255)), ([Math]::Max(1, $size / 128))
        $g.DrawPath($edge, $bg); $edge.Dispose()
    }

    # La isla: cápsula negra con borde claro, arriba al centro (como en pantalla)
    $cw = $size * 0.66; $ch = $size * 0.26
    $cx = ($size - $cw) / 2; $cy = $size * 0.37
    $capsule = New-RoundedPath $cx $cy $cw $ch ($ch / 2)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 0, 0))), $capsule)
    $stroke = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 210, 210, 222)), ([Math]::Max(1, $size / 48))
    $g.DrawPath($stroke, $capsule)

    # Punto verde (acento de la app)
    $dot = $ch * 0.42
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 52, 211, 153))),
        [float]($cx + $ch * 0.38), [float]($cy + ($ch - $dot) / 2), [float]$dot, [float]$dot)
    # "Barras" de reproducción a la derecha (solo en tamaños donde se distinguen)
    if ($size -ge 48) {
        $bw = $size * 0.035; $gap = $bw * 0.8; $bx = $cx + $cw - $ch * 0.45 - 3 * $bw - 2 * $gap
        $barBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 52, 211, 153))
        foreach ($i in 0..2) {
            $bh = $ch * @(0.30, 0.55, 0.40)[$i]
            $g.FillRectangle($barBrush, [float]($bx + $i * ($bw + $gap)), [float]($cy + ($ch - $bh) / 2), [float]$bw, [float]$bh)
        }
    }
    $g.Dispose()
    return $bmp
}

# Tamaños estándar de Windows (barra de tareas, Explorador, menú Inicio, alta escala)
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save((Join-Path $OutDir "icon-256.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Data = $ms.ToArray() }
}

# Formato ICO: cabecera + directorio + imágenes PNG (soportado desde Windows Vista)
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$img.Data.Length); $w.Write([UInt32]$offset)
    $offset += $img.Data.Length
}
foreach ($img in $images) { $w.Write($img.Data) }
$w.Flush()
$ico = Join-Path $OutDir "NexusIsland.ico"
[System.IO.File]::WriteAllBytes($ico, $out.ToArray())
"Ícono generado: $ico ($([math]::Round($out.Length / 1KB, 1)) KB, tamaños: $($sizes -join ', '))"
