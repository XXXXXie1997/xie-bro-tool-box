$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot = Split-Path $PSScriptRoot -Parent
# Keep the supplied logo unchanged; derive Windows icon sizes from the project asset.
$taskSource = [Drawing.Image]::FromFile((Join-Path $taskRoot 'Resources/logo.png'))
$taskFrames = @()
try {
    foreach ($taskSize in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
        $taskBitmap = [Drawing.Bitmap]::new($taskSize, $taskSize, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
        $taskAttributes = [Drawing.Imaging.ImageAttributes]::new()
        $taskStream = [IO.MemoryStream]::new()
        try {
            $taskGraphics.Clear([Drawing.Color]::Transparent)
            $taskGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $taskGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $taskGraphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $taskAttributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $taskScale = [Math]::Min($taskSize / $taskSource.Width, $taskSize / $taskSource.Height)
            $taskWidth = [Math]::Max(1, [int][Math]::Round($taskSource.Width * $taskScale))
            $taskHeight = [Math]::Max(1, [int][Math]::Round($taskSource.Height * $taskScale))
            $taskBounds = [Drawing.Rectangle]::new([int](($taskSize - $taskWidth) / 2), [int](($taskSize - $taskHeight) / 2), $taskWidth, $taskHeight)
            $taskGraphics.DrawImage($taskSource, $taskBounds, 0, 0, $taskSource.Width, $taskSource.Height, [Drawing.GraphicsUnit]::Pixel, $taskAttributes)
            $taskBitmap.Save($taskStream, [Drawing.Imaging.ImageFormat]::Png)
            $taskFrames += ,@($taskSize, $taskStream.ToArray())
        } finally {
            $taskStream.Dispose(); $taskAttributes.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose()
        }
    }
} finally { $taskSource.Dispose() }
$taskFile = [IO.File]::Create((Join-Path $taskRoot 'Resources/app.ico'))
$taskWriter = [IO.BinaryWriter]::new($taskFile)
try {
    $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskFrames.Count)
    $taskOffset = 6 + 16 * $taskFrames.Count
    foreach ($taskFrame in $taskFrames) {
        $taskDimension = if ($taskFrame[0] -eq 256) { 0 } else { $taskFrame[0] }
        $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]$taskDimension)
        $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
        $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
        $taskWriter.Write([uint32]$taskFrame[1].Length); $taskWriter.Write([uint32]$taskOffset)
        $taskOffset += $taskFrame[1].Length
    }
    foreach ($taskFrame in $taskFrames) { $taskWriter.Write([byte[]]$taskFrame[1]) }
} finally { $taskWriter.Dispose(); $taskFile.Dispose() }
