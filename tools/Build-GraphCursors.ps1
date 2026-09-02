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

$referencePath = Join-Path $PSScriptRoot 'assets\ship-reference.png'
$original = [System.Drawing.Bitmap]::FromFile($referencePath)
$reference = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$referenceGraphics = [System.Drawing.Graphics]::FromImage($reference)
$referenceGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$referenceGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$referenceGraphics.DrawImage($original, 0, 0, 256, 256)
$referenceGraphics.Dispose()
$original.Dispose()

$minX = $reference.Width
$minY = $reference.Height
$maxX = 0
$maxY = 0
$flameLeft = New-Object int[] $reference.Height
$flameRight = New-Object int[] $reference.Height
for ($y = 0; $y -lt $reference.Height; $y++) {
    $flameLeft[$y] = $reference.Width
    $flameRight[$y] = -1
}
for ($y = 0; $y -lt $reference.Height; $y++) {
    for ($x = 0; $x -lt $reference.Width; $x++) {
        $pixel = $reference.GetPixel($x, $y)
        $luminance = ($pixel.R + $pixel.G + $pixel.B) / 3
        $maximumChannel = [Math]::Max($pixel.R, [Math]::Max($pixel.G, $pixel.B))
        $minimumChannel = [Math]::Min($pixel.R, [Math]::Min($pixel.G, $pixel.B))
        if ($luminance -lt 205 -and $maximumChannel - $minimumChannel -lt 48) {
            $minX = [Math]::Min($minX, $x)
            $minY = [Math]::Min($minY, $y)
            $maxX = [Math]::Max($maxX, $x)
            $maxY = [Math]::Max($maxY, $y)
        }
        if ($pixel.R -gt 170 -and $pixel.G -gt 90 -and $pixel.R - $pixel.B -gt 35 -and $pixel.G - $pixel.B -gt 15) {
            $flameLeft[$y] = [Math]::Min($flameLeft[$y], $x)
            $flameRight[$y] = [Math]::Max($flameRight[$y], $x)
        }
    }
}

$sourceWidth = $maxX - $minX + 1
$sourceHeight = $maxY - $minY + 1
$bodyCenterX = ($minX + $maxX) / 2
$bodyCenterY = ($minY + $maxY) / 2
$sourceScale = 30 / [Math]::Max($sourceWidth, $sourceHeight)
$idleSource = New-Object System.Drawing.Bitmap $reference.Width, $reference.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$movingSource = New-Object System.Drawing.Bitmap $reference.Width, $reference.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($y = 0; $y -lt $reference.Height; $y++) {
    for ($x = 0; $x -lt $reference.Width; $x++) {
        $pixel = $reference.GetPixel($x, $y)
        $luminance = ($pixel.R + $pixel.G + $pixel.B) / 3
        $maximumChannel = [Math]::Max($pixel.R, [Math]::Max($pixel.G, $pixel.B))
        $minimumChannel = [Math]::Min($pixel.R, [Math]::Min($pixel.G, $pixel.B))
        if ($luminance -lt 220 -and $maximumChannel - $minimumChannel -lt 52) {
            $alpha = [Math]::Clamp([int]((235 - $luminance) * 2.2), 0, 255)
            $bodyColor = [System.Drawing.Color]::FromArgb($alpha, 240, 242, 244)
            $idleSource.SetPixel($x, $y, $bodyColor)
            $movingSource.SetPixel($x, $y, $bodyColor)
        }
        elseif ($flameRight[$y] -ge 0 -and $x -ge $flameLeft[$y] -and $x -le $flameRight[$y]) {
            $flameColor = if ($pixel.R -gt 245 -and $pixel.G -gt 245 -and $pixel.B -gt 245) {
                [System.Drawing.Color]::FromArgb(255, 255, 249, 214)
            }
            else {
                [System.Drawing.Color]::FromArgb(255, $pixel.R, $pixel.G, $pixel.B)
            }
            $movingSource.SetPixel($x, $y, $flameColor)
        }
    }
}

$motionStates = [ordered]@{
    ''        = $false
    '-moving' = $true
}

foreach ($entry in $directions.GetEnumerator()) {
  foreach ($motion in $motionStates.GetEnumerator()) {
    $large = New-Object System.Drawing.Bitmap 64, 64, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($large)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.TranslateTransform(32, 32)
    $graphics.RotateTransform([single](90 - $entry.Value))
    $graphics.ScaleTransform($sourceScale, $sourceScale)
    $graphics.TranslateTransform(-$bodyCenterX, -$bodyCenterY)
    $graphics.DrawImageUnscaled($(if ($motion.Value) { $movingSource } else { $idleSource }), 0, 0)

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
    $path = Join-Path $OutputDirectory "ship-$($entry.Key)$($motion.Key).cur"
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
    $graphics.Dispose()
    $large.Dispose()
  }
}

$movingSource.Dispose()
$idleSource.Dispose()
$reference.Dispose()
