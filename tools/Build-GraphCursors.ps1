param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\Assets\Cursors'))

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$directions = [ordered]@{
    e   = 0
    ene = 22.5
    ne  = 45
    nne = 67.5
    n   = 90
    nnw = 112.5
    nw  = 135
    wnw = 157.5
    w   = 180
    wsw = 202.5
    sw  = 225
    ssw = 247.5
    s   = 270
    sse = 292.5
    se  = 315
    ese = 337.5
}

foreach ($entry in $directions.GetEnumerator()) {
    $large = New-Object System.Drawing.Bitmap 64, 64, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($large)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.TranslateTransform(32, 32)
    $graphics.RotateTransform(-[single]$entry.Value)
    $graphics.TranslateTransform(-32, -32)

    $flameBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 205, 232, 255))
    $graphics.FillPolygon($flameBrush, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(11, 29), [System.Drawing.PointF]::new(0, 32), [System.Drawing.PointF]::new(11, 35)
    ))

    $ship = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ship.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(61, 32),
        [System.Drawing.PointF]::new(43, 27),
        [System.Drawing.PointF]::new(32, 20),
        [System.Drawing.PointF]::new(28, 21),
        [System.Drawing.PointF]::new(30, 29),
        [System.Drawing.PointF]::new(12, 29),
        [System.Drawing.PointF]::new(7, 31),
        [System.Drawing.PointF]::new(7, 33),
        [System.Drawing.PointF]::new(12, 35),
        [System.Drawing.PointF]::new(30, 35),
        [System.Drawing.PointF]::new(28, 43),
        [System.Drawing.PointF]::new(32, 44),
        [System.Drawing.PointF]::new(43, 37)
    ))
    $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(225, 20, 23, 28)), 3
    $outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $body = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 248, 249, 250))
    $graphics.FillPath($body, $ship)
    $graphics.DrawPath($outline, $ship)

    $detailPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(160, 95, 105, 118)), 1.7
    $graphics.DrawLine($detailPen, 18, 32, 56, 32)
    $cockpit = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 38, 47, 58))
    $graphics.FillEllipse($cockpit, 39, 29, 9, 6)
    $glass = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(210, 186, 219, 240))
    $graphics.FillEllipse($glass, 41, 30, 5, 2.5)

    $small = New-Object System.Drawing.Bitmap 32, 32, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $downsample = [System.Drawing.Graphics]::FromImage($small)
    $downsample.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $downsample.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $downsample.DrawImage($large, 0, 0, 32, 32)

    $dib = New-Object System.IO.MemoryStream
    $dibWriter = New-Object System.IO.BinaryWriter $dib
    $dibWriter.Write([uint32]40)
    $dibWriter.Write([int32]32)
    $dibWriter.Write([int32]64)
    $dibWriter.Write([uint16]1)
    $dibWriter.Write([uint16]32)
    $dibWriter.Write([uint32]0)
    $dibWriter.Write([uint32]4096)
    $dibWriter.Write([int32]0)
    $dibWriter.Write([int32]0)
    $dibWriter.Write([uint32]0)
    $dibWriter.Write([uint32]0)
    for ($y = 31; $y -ge 0; $y--) {
        for ($x = 0; $x -lt 32; $x++) {
            $pixel = $small.GetPixel($x, $y)
            $dibWriter.Write([byte]$pixel.B)
            $dibWriter.Write([byte]$pixel.G)
            $dibWriter.Write([byte]$pixel.R)
            $dibWriter.Write([byte]$pixel.A)
        }
    }
    $dibWriter.Write((New-Object byte[] 128))
    $image = $dib.ToArray()
    $path = Join-Path $OutputDirectory "ship-$($entry.Key).cur"
    $stream = [System.IO.File]::Create($path)
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([uint16]0)
    $writer.Write([uint16]2)
    $writer.Write([uint16]1)
    $writer.Write([byte]32)
    $writer.Write([byte]32)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]16)
    $writer.Write([uint16]16)
    $writer.Write([uint32]$image.Length)
    $writer.Write([uint32]22)
    $writer.Write($image)

    $writer.Dispose()
    $dibWriter.Dispose()
    $dib.Dispose()
    $downsample.Dispose()
    $small.Dispose()
    $glass.Dispose()
    $cockpit.Dispose()
    $detailPen.Dispose()
    $body.Dispose()
    $outline.Dispose()
    $ship.Dispose()
    $flameBrush.Dispose()
    $graphics.Dispose()
    $large.Dispose()
}
