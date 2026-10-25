#requires -version 7.0
<#
    Generates src/SnapOCR/app.ico — a multi-resolution icon built from PNG frames.

    Design: rounded square with a blue gradient, four white corner brackets
    (a "capture frame") and three text lines inside.

    Usage: pwsh -File scripts/make-icon.ps1
#>
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\src\SnapOCR\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256

function New-RoundedPath {
    param([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R)
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $R * 2
    $p.AddArc($X, $Y, $d, $d, 180, 90)
    $p.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $p.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $p.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng {
    param([int]$Size)

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [single]($Size * 0.06)
    $body = [single]($Size - 2 * $pad)
    $radius = [single]($Size * 0.22)

    $path = New-RoundedPath -X $pad -Y $pad -W $body -H $body -R $radius
    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new($pad, $pad),
        [System.Drawing.PointF]::new($pad + $body, $pad + $body),
        [System.Drawing.Color]::FromArgb(255, 41, 121, 255),
        [System.Drawing.Color]::FromArgb(255, 17, 60, 168))
    $g.FillPath($brush, $path)

    # Inner "capture frame" corners.
    $ink = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
    $penW = [single][Math]::Max(1.0, $Size * 0.075)
    $pen = [System.Drawing.Pen]::new($ink, $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $inset = [single]($Size * 0.26)
    $arm = [single]($Size * 0.13)
    $l = $inset
    $t = $inset
    $r = [single]($Size - $inset)
    $b = [single]($Size - $inset)

    $g.DrawLine($pen, $l, $t, $l + $arm, $t)   # top-left
    $g.DrawLine($pen, $l, $t, $l, $t + $arm)
    $g.DrawLine($pen, $r, $t, $r - $arm, $t)   # top-right
    $g.DrawLine($pen, $r, $t, $r, $t + $arm)
    $g.DrawLine($pen, $l, $b, $l + $arm, $b)   # bottom-left
    $g.DrawLine($pen, $l, $b, $l, $b - $arm)
    $g.DrawLine($pen, $r, $b, $r - $arm, $b)   # bottom-right
    $g.DrawLine($pen, $r, $b, $r, $b - $arm)

    # Three text lines in the middle (only readable at >= 32px).
    if ($Size -ge 32) {
        $linePen = [System.Drawing.Pen]::new($ink, [single][Math]::Max(1.0, $Size * 0.048))
        $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $cx = $Size / 2.0
        $w1 = $Size * 0.24
        $w2 = $Size * 0.18
        $w3 = $Size * 0.10
        $g.DrawLine($linePen, [single]($cx - $w1 / 2), [single]($Size * 0.44), [single]($cx + $w1 / 2), [single]($Size * 0.44))
        $g.DrawLine($linePen, [single]($cx - $w1 / 2), [single]($Size * 0.53), [single]($cx - $w1 / 2 + $w2), [single]($Size * 0.53))
        $g.DrawLine($linePen, [single]($cx - $w1 / 2), [single]($Size * 0.62), [single]($cx - $w1 / 2 + $w3), [single]($Size * 0.62))
        $linePen.Dispose()
    }

    $pen.Dispose()
    $brush.Dispose()
    $path.Dispose()

    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    $g.Dispose()
    $bmp.Dispose()
    # `,` keeps the byte[] intact instead of unrolling it into the pipeline.
    return , $bytes
}

$frames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($s in $sizes) { $frames.Add((New-IconPng -Size $s)) }

$outPath = [System.IO.Path]::GetFullPath($Out)
$dir = [System.IO.Path]::GetDirectoryName($outPath)
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$fs = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($fs)
$bw.Write([uint16]0)                 # reserved
$bw.Write([uint16]1)                 # type: icon
$bw.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $len = $frames[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # width
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $bw.Write([byte]0)               # palette count
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # planes
    $bw.Write([uint16]32)            # bpp
    $bw.Write([uint32]$len)
    $bw.Write([uint32]$offset)
    $offset += $len
}
$bw.Flush()

foreach ($f in $frames) { $fs.Write($f, 0, $f.Length) }

[System.IO.File]::WriteAllBytes($outPath, $fs.ToArray())
$bw.Dispose(); $fs.Dispose()
Write-Host "wrote $outPath ($([Math]::Round((Get-Item $outPath).Length / 1kb, 1)) KB, $($sizes.Count) frames)"
