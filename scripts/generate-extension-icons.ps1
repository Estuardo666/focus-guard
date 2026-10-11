$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$iconDirectory = Join-Path $repositoryRoot 'extension\icons'
$sourcePath = Join-Path $repositoryRoot 'assets\branding\focusguard-icon.png'
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Brand icon is missing: $sourcePath" }
New-Item -ItemType Directory -Path $iconDirectory -Force | Out-Null

Add-Type -AssemblyName System.Drawing
$source = [Drawing.Bitmap]::new($sourcePath)
try {
foreach ($size in @(16, 32, 48, 128)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $size, $size))
        $bitmap.Save((Join-Path $iconDirectory "icon-$size.png"), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
    # Multi-resolution .ico with PNG-encoded frames so Windows picks a crisp image at every size.
    $frameSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $frames = foreach ($frameSize in $frameSizes) {
        $frame = [Drawing.Bitmap]::new($frameSize, $frameSize, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $frameGraphics = [Drawing.Graphics]::FromImage($frame)
        try {
            $frameGraphics.Clear([Drawing.Color]::Transparent)
            $frameGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $frameGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $frameGraphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $frameSize, $frameSize))
            $buffer = [IO.MemoryStream]::new()
            $frame.Save($buffer, [Drawing.Imaging.ImageFormat]::Png)
            ,$buffer.ToArray()
        } finally { $frameGraphics.Dispose(); $frame.Dispose() }
    }
    $iconPath = Join-Path $repositoryRoot 'src\FocusGuard.Setup\focusguard.ico'
    $iconStream = [IO.File]::Open($iconPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $writer = [IO.BinaryWriter]::new($iconStream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frameSizes.Count)
        $offset = 6 + 16 * $frameSizes.Count
        for ($index = 0; $index -lt $frameSizes.Count; $index++) {
            $dimension = if ($frameSizes[$index] -ge 256) { 0 } else { $frameSizes[$index] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($bytes in $frames) { $writer.Write($bytes) }
    } finally { $writer.Dispose() }
} finally { $source.Dispose() }

Write-Output "Generated deterministic extension icons in $iconDirectory"
