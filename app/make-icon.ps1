# Draws the app icon (green tile + white paper plane) and writes app\app.ico with PNG frames.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot 'app.ico'
$sizes = 16, 24, 32, 48, 64, 256
$frames = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $k = $s / 256.0
    function Pts($list) { , ($list | ForEach-Object { New-Object System.Drawing.PointF ($_[0] * $k), ($_[1] * $k) }) }
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 96 * $k
    $tile.AddArc(8 * $k, 8 * $k, $d, $d, 180, 90); $tile.AddArc((248 * $k) - $d, 8 * $k, $d, $d, 270, 90)
    $tile.AddArc((248 * $k) - $d, (248 * $k) - $d, $d, $d, 0, 90); $tile.AddArc(8 * $k, (248 * $k) - $d, $d, $d, 90, 90)
    $tile.CloseFigure()
    $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(30, 142, 62))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $shade = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 228, 208))
    $dark  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 196, 165))
    $g.FillPath($green, $tile)
    $g.FillPolygon($white, (Pts @(@(40, 122), @(212, 50), @(110, 146))))   # top wing
    $g.FillPolygon($shade, (Pts @(@(110, 146), @(212, 50), @(160, 204))))  # bottom wing
    $g.FillPolygon($dark,  (Pts @(@(110, 146), @(124, 200), @(140, 166)))) # fold
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @($s, $ms.ToArray())
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $len = $f[1].Length
    $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $w.Write($f[1]) }
$w.Close()
Write-Host "Wrote $out"
