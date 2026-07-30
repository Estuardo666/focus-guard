$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$iconDirectory = Join-Path $repositoryRoot 'extension\icons'
$sourcePath = Join-Path $repositoryRoot 'assets\branding\focusguard-icon.png'
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Brand icon is missing: $sourcePath" }
New-Item -ItemType Directory -Path $iconDirectory -Force | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class IconNative {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr handle);
}
'@
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
    $icoBitmap = [Drawing.Bitmap]::new(256, 256, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $icoGraphics = [Drawing.Graphics]::FromImage($icoBitmap)
    try {
        $icoGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $icoGraphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, 256, 256))
        $iconHandle = $icoBitmap.GetHicon()
        try {
            $icon = [Drawing.Icon]::FromHandle($iconHandle)
            $iconPath = Join-Path $repositoryRoot 'src\FocusGuard.Setup\focusguard.ico'
            $iconStream = [IO.File]::Open($iconPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $icon.Save($iconStream) } finally { $iconStream.Dispose(); $icon.Dispose() }
        } finally { [IconNative]::DestroyIcon($iconHandle) | Out-Null }
    } finally { $icoGraphics.Dispose(); $icoBitmap.Dispose() }
} finally { $source.Dispose() }

Write-Output "Generated deterministic extension icons in $iconDirectory"
