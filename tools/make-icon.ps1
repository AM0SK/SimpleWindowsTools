# Draws Assets/app.ico: accent-gradient rounded square with a white key outline.
# Run with Windows PowerShell:  powershell -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\SimpleWindowsTools\Assets\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Background squircle
    $pad = [Math]::Max(0.5, $s * 0.03)
    $bg = New-RoundedPath $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ($s * 0.26)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(0x6D, 0x5D, 0xF6)), ([System.Drawing.Color]::FromArgb(0x4C, 0x8D, 0xFF)), 45
    $g.FillPath($grad, $bg)

    # Key outline with a space-bar line
    $stroke = [Math]::Max(1.4, $s * 0.075)
    $kw = $s * 0.56; $kh = $s * 0.42
    $kx = ($s - $kw) / 2; $ky = ($s - $kh) / 2
    $key = New-RoundedPath $kx $ky $kw $kh ($s * 0.1)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $stroke
    $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $key)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $ly = $ky + $kh * 0.66
    $g.DrawLine($pen, [float]($kx + $kw * 0.3), [float]$ly, [float]($kx + $kw * 0.7), [float]$ly)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,@($s, $ms.ToArray())
    $g.Dispose(); $bmp.Dispose()
}

# ICO container with PNG entries
$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $out)).Path + '\app.ico')
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($e in $pngs) {
    $s = $e[0]; $data = $e[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($e in $pngs) { $w.Write([byte[]]$e[1]) }
$w.Close()
Write-Output "Wrote $out"
