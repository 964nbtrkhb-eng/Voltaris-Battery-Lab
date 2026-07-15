param([Parameter(Mandatory)][string]$OutputPath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$path = [System.Drawing.Drawing2D.GraphicsPath]::new()
$path.AddArc(12, 12, 48, 48, 180, 90)
$path.AddArc(196, 12, 48, 48, 270, 90)
$path.AddArc(196, 196, 48, 48, 0, 90)
$path.AddArc(12, 196, 48, 48, 90, 90)
$path.CloseFigure()
$background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.Point]::new(24, 24), [System.Drawing.Point]::new(232, 232),
    [System.Drawing.ColorTranslator]::FromHtml('#162C49'),
    [System.Drawing.ColorTranslator]::FromHtml('#111329'))
$graphics.FillPath($background, $path)
$graphics.DrawPath([System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#56DFF8'), 5), $path)

$ringPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#63E5FF'), 14)
$ringPen.StartCap = $ringPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawArc($ringPen, 50, 50, 156, 156, -78, 292)
$graphics.DrawArc([System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#2B4867'), 14), 50, 50, 156, 156, 222, 54)

$bolt = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(139, 62),
    [System.Drawing.PointF]::new(91, 137),
    [System.Drawing.PointF]::new(122, 137),
    [System.Drawing.PointF]::new(109, 194),
    [System.Drawing.PointF]::new(169, 111),
    [System.Drawing.PointF]::new(137, 111))
$graphics.FillPolygon([System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#E9FCFF')), $bolt)

$stream = [System.IO.MemoryStream]::new()
$bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $stream.ToArray()
$directory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$file = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
$writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22)
$writer.Write($png)
$writer.Dispose(); $file.Dispose(); $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
Write-Host "Created $OutputPath"
