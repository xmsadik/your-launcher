<#
.SYNOPSIS
  Regenerate every PNG in packaging/Assets from src/Launcher.App/app.ico.

.DESCRIPTION
  Reads the largest frame in app.ico (256x256, stored as an embedded PNG - GDI+'s Icon class
  in .NET only round-trips the small BMP-based frames reliably, so the ICO directory is parsed
  by hand and the 256x256 entry decoded directly as a PNG), then scales it with high-quality
  bicubic interpolation onto a transparent canvas, centered, for every tile Microsoft Store
  packaging needs. Both the scale-100 base filenames (which Store validation expects) and
  scale-200 variants are written, matching the set produced by the DesktopTiler reference app's
  make-icons.py.

  Run from anywhere: pwsh scripts/make-icons.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root   = Resolve-Path (Join-Path $PSScriptRoot '..')
$icoPath = Join-Path $root 'src\Launcher.App\app.ico'
$assets = Join-Path $root 'packaging\Assets'

if (-not (Test-Path $icoPath)) { throw "Icon not found: $icoPath" }

# --- Extract the largest frame from the .ico as a Bitmap -------------------------------------
function Get-LargestIconFrame([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    $best = $null
    $off = 6
    for ($i = 0; $i -lt $count; $i++) {
        $w = $bytes[$off];     if ($w -eq 0) { $w = 256 }
        $h = $bytes[$off + 1]; if ($h -eq 0) { $h = 256 }
        $size   = [BitConverter]::ToUInt32($bytes, $off + 8)
        $imgOff = [BitConverter]::ToUInt32($bytes, $off + 12)
        if (-not $best -or $w -gt $best.Width) {
            $best = [pscustomobject]@{ Width = $w; Height = $h; Size = $size; Offset = $imgOff }
        }
        $off += 16
    }
    if (-not $best) { throw "No frames found in $path" }

    $frame = New-Object byte[] $best.Size
    [Array]::Copy($bytes, $best.Offset, $frame, 0, $best.Size)

    if ($frame.Length -ge 8 -and $frame[0] -eq 0x89 -and $frame[1] -eq 0x50 -and $frame[2] -eq 0x4E -and $frame[3] -eq 0x47) {
        # Embedded PNG (used for large, e.g. 256x256, frames).
        $ms = New-Object System.IO.MemoryStream(, $frame)
        return [System.Drawing.Bitmap]::FromStream($ms)
    }

    # Fall back to GDI+'s own frame selection for classic BMP-based frames.
    $icon = New-Object System.Drawing.Icon($path, (New-Object System.Drawing.Size($best.Width, $best.Height)))
    return $icon.ToBitmap()
}

$source = Get-LargestIconFrame $icoPath
Write-Host "Source frame: $($source.Width)x$($source.Height)"

# --- Compose the icon, scaled to fit and centered, onto a transparent canvas ------------------
function New-Tile([System.Drawing.Bitmap]$src, [int]$width, [int]$height, [double]$fraction) {
    $canvas = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $g.CompositingMode    = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $side = [Math]::Round([Math]::Min($width, $height) * $fraction)
        $x = [int](($width - $side) / 2)
        $y = [int](($height - $side) / 2)
        $g.DrawImage($src, $x, $y, $side, $side)
    } finally {
        $g.Dispose()
    }
    return $canvas
}

if (Test-Path $assets) {
    Get-ChildItem $assets -Filter '*.png' | Remove-Item -Force
} else {
    New-Item -ItemType Directory -Force $assets | Out-Null
}

# name -> (width, height, icon fraction) at scale-100
$tiles = [ordered]@{
    'Square44x44Logo'   = @(44, 44, 1.0)
    'SmallTile'         = @(71, 71, 0.66)
    'Square150x150Logo' = @(150, 150, 0.66)
    'LargeTile'         = @(310, 310, 0.66)
    'Wide310x150Logo'   = @(310, 150, 0.66)
    'SplashScreen'      = @(620, 300, 0.5)
}

foreach ($name in $tiles.Keys) {
    $w, $h, $frac = $tiles[$name]
    (New-Tile $source $w $h $frac).Save((Join-Path $assets "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    (New-Tile $source ($w * 2) ($h * 2) $frac).Save((Join-Path $assets "$name.scale-200.png"), [System.Drawing.Imaging.ImageFormat]::Png)
}

# StoreLogo: the bare icon itself (no extra plate padding), at 50/100.
(New-Tile $source 50 50 1.0).Save((Join-Path $assets 'StoreLogo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
(New-Tile $source 100 100 1.0).Save((Join-Path $assets 'StoreLogo.scale-200.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# Taskbar / Start list use the unplated target sizes: full-bleed icon, no padding.
foreach ($size in 16, 24, 32, 48, 256) {
    (New-Tile $source $size $size 1.0).Save(
        (Join-Path $assets "Square44x44Logo.targetsize-${size}_altform-unplated.png"),
        [System.Drawing.Imaging.ImageFormat]::Png)
}

Get-ChildItem $assets -Filter '*.png' | Sort-Object Name | ForEach-Object {
    $img = [System.Drawing.Image]::FromFile($_.FullName)
    Write-Host "$($_.Name) $($img.Width)x$($img.Height)"
    $img.Dispose()
}
