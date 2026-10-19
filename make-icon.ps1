# Draws icon.ico (blue rounded square with an up and a down arrow) at the sizes Windows uses.
# Run once after changing the design: powershell -ExecutionPolicy Bypass -File make-icon.ps1
Add-Type -AssemblyName System.Drawing

function Draw-Icon([int]$n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'

    # rounded square with a blue -> cyan gradient
    $m = [Math]::Max(0.5, $n * 0.03); $size = $n - 2 * $m; $r = $size * 0.24
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($m + $size - 2 * $r, $m, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($m + $size - 2 * $r, $m + $size - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($m, $m + $size - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $n, $n), ([System.Drawing.Color]::FromArgb(255, 0, 103, 214)), ([System.Drawing.Color]::FromArgb(255, 0, 188, 212))
    $g.FillPath($grad, $path)

    # up arrow (left) and down arrow (right)
    $w = [Math]::Max(1.6, $n * 0.095)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $w
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $top = $n * 0.24; $bottom = $n * 0.76; $head = $n * 0.13
    foreach ($a in @(@{ x = $n * 0.35; up = $true }, @{ x = $n * 0.65; up = $false })) {
        $x = $a.x
        $tip = if ($a.up) { $top } else { $bottom }
        $tail = if ($a.up) { $bottom } else { $top }
        $back = if ($a.up) { $head } else { -$head }
        $g.DrawLine($pen, [single]$x, [single]$tail, [single]$x, [single]$tip)
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ($x - $head), ($tip + $back)),
            (New-Object System.Drawing.PointF $x, $tip),
            (New-Object System.Drawing.PointF ($x + $head), ($tip + $back)))
        $g.DrawLines($pen, $pts)
    }
    $g.Dispose()
    return $bmp
}

# Classic ICO image: BITMAPINFOHEADER + bottom-up BGRA pixels + 1-bit AND mask (all zero; alpha does the work).
function To-Dib([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $maskRow = [int]([Math]::Ceiling($n / 32.0) * 4)
    $w.Write([uint32]40); $w.Write([int32]$n); $w.Write([int32]($n * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($n * $n * 4 + $maskRow * $n)); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $w.Write((New-Object byte[] ($maskRow * $n)))
    return , $ms.ToArray()
}

# ICO file: header + directory + images (PNG only for 256 px, as Windows expects)
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($n in $sizes) {
    $bmp = Draw-Icon $n
    if ($n -eq 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
    } else { $bytes = To-Dib $bmp }
    $bmp.Dispose()
    , $bytes
}
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($n % 256)); $bw.Write([byte]($n % 256))   # 256 is stored as 0
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icon.ico'), $out.ToArray())
"icon.ico written ($($out.Length) bytes)"
