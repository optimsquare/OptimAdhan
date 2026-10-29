# Draws OptimAdhan's TEMPORARY icon (gold crescent and star on the navy brand colour)
# and writes a multi-resolution .ico. Replace with the final branded icon when ready.
# Usage: powershell -ExecutionPolicy Bypass -File tools/make-temp-icon.ps1 <output.ico>
param([Parameter(Mandatory)] [string] $OutPath)

Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$navy = [System.Drawing.Color]::FromArgb(255, 0x16, 0x20, 0x38)
$gold = [System.Drawing.Color]::FromArgb(255, 0xC4, 0x9A, 0x50)

function New-IconPng([int] $s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded-square background
    $r = [single]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = [single]($s - 1)
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($w - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($w - $r * 2, $w - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $w - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $navy), $path)

    # Crescent: a gold disc minus an offset disc
    $outer = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($s * 0.62); $x = [single]($s * 0.14); $y = [single](($s - $d) / 2)
    $outer.AddEllipse($x, $y, $d, $d)
    $region = New-Object System.Drawing.Region $outer
    $inner = New-Object System.Drawing.Drawing2D.GraphicsPath
    $inner.AddEllipse([single]($x + $d * 0.28), [single]($y - $d * 0.06), [single]($d * 0.92), [single]($d * 0.92))
    $region.Exclude($inner)
    $g.FillRegion((New-Object System.Drawing.SolidBrush $gold), $region)

    # Five-pointed star
    $cx = $s * 0.70; $cy = $s * 0.44; $ro = $s * 0.13; $ri = $ro * 0.42
    $pts = New-Object 'System.Drawing.PointF[]' 10
    for ($i = 0; $i -lt 10; $i++) {
        $rad = if ($i % 2 -eq 0) { $ro } else { $ri }
        $a = -[Math]::PI / 2 + $i * [Math]::PI / 5
        $pts[$i] = New-Object System.Drawing.PointF ([single]($cx + $rad * [Math]::Cos($a))), ([single]($cy + $rad * [Math]::Sin($a)))
    }
    $g.FillPolygon((New-Object System.Drawing.SolidBrush $gold), $pts)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        # Large frame: PNG-compressed (standard for 256 px).
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Small frames: classic 32-bit DIB, readable by every Windows icon API.
        $w = New-Object System.IO.BinaryWriter $ms
        $w.Write([uint32]40); $w.Write([int32]$s); $w.Write([int32]($s * 2))
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
        $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
        $w.Write([uint32]($s * $s * 4 + $maskStride * $s))
        $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
        for ($yy = $s - 1; $yy -ge 0; $yy--) {
            for ($xx = 0; $xx -lt $s; $xx++) {
                $c = $bmp.GetPixel($xx, $yy)
                $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
            }
        }
        $w.Write((New-Object byte[] ($maskStride * $s)))
        $w.Flush()
    }
    $bmp.Dispose()
    return , $ms.ToArray()
}

$images = foreach ($s in $sizes) { , (New-IconPng $s) }
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
New-Item -ItemType Directory -Force (Split-Path $OutPath) | Out-Null
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $OutPath)).Path + '\' + (Split-Path $OutPath -Leaf), $out.ToArray())
Write-Output "Wrote $OutPath ($($sizes -join ', ') px)"
