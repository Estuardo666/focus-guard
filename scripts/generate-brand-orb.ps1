$ErrorActionPreference = 'Stop'
# Renders the FocusGuard orb brand icon to assets/branding/focusguard-icon.png.
# Run scripts/generate-extension-icons.ps1 afterwards to derive extension PNGs and the .ico.
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputPath = Join-Path $repositoryRoot 'assets\branding\focusguard-icon.png'
Add-Type -AssemblyName System.Drawing

$size = 1024
$bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($bitmap)
function New-Ellipse([float]$x, [float]$y, [float]$w, [float]$h) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new(); $path.AddEllipse($x, $y, $w, $h); $path
}
function New-RadialBrush($path, [Drawing.PointF]$center, [Drawing.Color[]]$colors, [single[]]$positions) {
    $brush = [Drawing.Drawing2D.PathGradientBrush]::new($path)
    $brush.CenterPoint = $center
    $blend = [Drawing.Drawing2D.ColorBlend]::new($colors.Length)
    $blend.Colors = $colors
    $blend.Positions = $positions
    $brush.InterpolationColors = $blend
    $brush
}
$rgba = { param($a, $r, $gr, $b) [Drawing.Color]::FromArgb($a, $r, $gr, $b) }
try {
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([Drawing.Color]::Transparent)

    $sphere = New-Ellipse 64 64 896 896
    # Colour stops run from the outer edge (position 0) to the centre point (position 1).
    $body = New-RadialBrush $sphere ([Drawing.PointF]::new(420, 380)) `
        @((& $rgba 255 150 120 225), (& $rgba 255 186 164 246), (& $rgba 255 214 200 255), (& $rgba 255 240 234 255)) `
        @(0.0, 0.28, 0.62, 1.0)
    $g.FillPath($body, $sphere)

    $g.SetClip($sphere)
    # Warm pink bounce light in the lower right.
    $bouncePath = New-Ellipse 430 470 620 560
    $bounce = New-RadialBrush $bouncePath ([Drawing.PointF]::new(760, 780)) `
        @((& $rgba 0 255 170 225), (& $rgba 95 255 175 228)) @(0.0, 1.0)
    $g.FillPath($bounce, $bouncePath)

    # Soft edge falloff so the silhouette reads as a sphere.
    $edgePath = New-Ellipse 64 64 896 896
    $edge = New-RadialBrush $edgePath ([Drawing.PointF]::new(512, 512)) `
        @((& $rgba 90 110 80 200), (& $rgba 0 110 80 200), (& $rgba 0 110 80 200)) @(0.0, 0.12, 1.0)
    $g.FillPath($edge, $edgePath)

    # Broad white highlight in the upper left.
    $shinePath = New-Ellipse 150 130 520 470
    $shine = New-RadialBrush $shinePath ([Drawing.PointF]::new(380, 330)) `
        @((& $rgba 0 255 255 255), (& $rgba 120 255 255 255), (& $rgba 245 255 255 255)) @(0.0, 0.55, 1.0)
    $g.FillPath($shine, $shinePath)
    $g.ResetClip()

    $bitmap.Save($outputPath, [Drawing.Imaging.ImageFormat]::Png)
} finally { $g.Dispose(); $bitmap.Dispose() }
Write-Output "Generated orb brand icon at $outputPath"
