# make-assets.ps1 - generates the 21 WP8.1 tile/logo/splash PNGs of GGMaps with System.Drawing.
#
#   powershell -ExecutionPolicy Bypass -File tools\make-assets.ps1 [-AssetsDir <dir>]
#
# Design:
#   * Logo / SmallLogo / Square71x71Logo / WideLogo / StoreLogo / BadgeLogo: white map-pin glyph on transparent
#     (the phone paints the tile with the user's accent colour, manifest BackgroundColor="transparent").
#   * SplashScreen: white background, a red map pin with a white dot, app name "GG Maps" below it.
# This file is pure ASCII on purpose: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.
param(
    [string]$AssetsDir = (Join-Path $PSScriptRoot '..\GGMaps\Assets'),
    [string]$FontsDir  = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$AssetsDir = [System.IO.Path]::GetFullPath($AssetsDir)
if (-not $FontsDir) { $FontsDir = Join-Path $AssetsDir 'Fonts' }
New-Item -ItemType Directory -Force $AssetsDir | Out-Null

$Red      = [System.Drawing.Color]::FromArgb(255, 0xEA, 0x43, 0x35)
$DarkRed  = [System.Drawing.Color]::FromArgb(255, 0xB3, 0x1E, 0x12)
$TextGray = [System.Drawing.Color]::FromArgb(255, 0x44, 0x47, 0x46)
$White    = [System.Drawing.Color]::White

$sizes = [ordered]@{
    'Logo'            = @{ 100 = '150x150'; 140 = '210x210';  240 = '360x360' }
    'SmallLogo'       = @{ 100 = '44x44';   140 = '62x62';    240 = '106x106' }
    'Square71x71Logo' = @{ 100 = '71x71';   140 = '99x99';    240 = '170x170' }
    'WideLogo'        = @{ 100 = '310x150'; 140 = '434x210';  240 = '744x360' }
    'StoreLogo'       = @{ 100 = '50x50';   140 = '70x70';    240 = '120x120' }
    'SplashScreen'    = @{ 100 = '480x800'; 140 = '672x1120'; 240 = '1152x1920' }
    'BadgeLogo'       = @{ 100 = '24x24';   140 = '33x33';    240 = '58x58' }
}
# fraction of min(w,h) used by the pin HEIGHT
$glyphFraction = @{ 'Logo' = 0.50; 'SmallLogo' = 0.74; 'Square71x71Logo' = 0.62; 'WideLogo' = 0.50;
                    'StoreLogo' = 0.74; 'BadgeLogo' = 0.96 }

function Get-InstalledFamily([string[]]$names) {
    foreach ($n in $names) {
        try { return New-Object System.Drawing.FontFamily($n) } catch { }
    }
    return [System.Drawing.FontFamily]::GenericSansSerif
}

$pfc = New-Object System.Drawing.Text.PrivateFontCollection
$captionFamily = $null
$captionFont = Join-Path $FontsDir 'AppFont-Medium.ttf'
if (Test-Path $captionFont) {
    $pfc.AddFontFile($captionFont)
    if ($pfc.Families.Count -gt 0) { $captionFamily = $pfc.Families[0] }
}
if (-not $captionFamily) { $captionFamily = Get-InstalledFamily @('Segoe UI Semibold', 'Segoe UI', 'Arial') }

# Pin outline in a 20 x 28 box: circle of radius 10 centred at (10,10), tip at (10,28)
function New-PinPath([single]$cx, [single]$cy, [single]$height) {
    $s = $height / 28.0
    $left = $cx - 10 * $s; $top = $cy - 14 * $s
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    # arc from the lower-left tangent point over the top to the lower-right tangent point
    $p.AddArc([single]$left, [single]$top, [single](20 * $s), [single](20 * $s), [single]145, [single]250)
    $p.AddBezier([single]($left + 18.19 * $s), [single]($top + 15.74 * $s),
                 [single]($left + 15.5 * $s), [single]($top + 20 * $s),
                 [single]($left + 11.5 * $s), [single]($top + 24 * $s),
                 [single]($left + 10 * $s),   [single]($top + 28 * $s))
    $p.AddBezier([single]($left + 10 * $s),  [single]($top + 28 * $s),
                 [single]($left + 8.5 * $s), [single]($top + 24 * $s),
                 [single]($left + 4.5 * $s), [single]($top + 20 * $s),
                 [single]($left + 1.81 * $s), [single]($top + 15.74 * $s))
    $p.CloseFigure()
    return $p
}

function Add-Pin([System.Drawing.Graphics]$g, [single]$cx, [single]$cy, [single]$height, $fill, $dot) {
    $s = $height / 28.0
    $path = New-PinPath $cx $cy $height
    $b = New-Object System.Drawing.SolidBrush($fill)
    $g.FillPath($b, $path); $b.Dispose(); $path.Dispose()
    $r = 3.6 * $s
    $dx = $cx; $dy = $cy - 14 * $s + 10 * $s
    if ($dot) {
        $db = New-Object System.Drawing.SolidBrush($dot)
        $g.FillEllipse($db, [single]($dx - $r), [single]($dy - $r), [single](2 * $r), [single](2 * $r)); $db.Dispose()
    } else {
        # punch a transparent hole
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $hb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Transparent)
        $g.FillEllipse($hb, [single]($dx - $r), [single]($dy - $r), [single](2 * $r), [single](2 * $r)); $hb.Dispose()
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    }
}

function New-FittedTextPath([string]$text, [System.Drawing.FontFamily]$fam, [System.Drawing.RectangleF]$box) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddString($text, $fam, 0, 1000.0, (New-Object System.Drawing.PointF(0, 0)), [System.Drawing.StringFormat]::GenericTypographic)
    $b = $p.GetBounds()
    $sc = [Math]::Min($box.Width / $b.Width, $box.Height / $b.Height)
    $w = $b.Width * $sc; $h = $b.Height * $sc
    $x = $box.X + ($box.Width - $w) / 2; $y = $box.Y + ($box.Height - $h) / 2
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate($x, $y); $m.Scale($sc, $sc); $m.Translate(-$b.X, -$b.Y)
    $p.Transform($m); $m.Dispose()
    return $p
}

$written = 0
foreach ($name in $sizes.Keys) {
    foreach ($scale in 100, 140, 240) {
        $w, $h = $sizes[$name][$scale].Split('x') | ForEach-Object { [int]$_ }
        $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        if ($name -eq 'SplashScreen') {
            $g.Clear($White)
            $ph = [single]($w * 0.30)
            Add-Pin $g ([single]($w / 2)) ([single]($h * 0.42)) $ph $Red $DarkRed
            $capBox = New-Object System.Drawing.RectangleF(0, ($h * 0.42 + $ph * 0.66), $w, ($w * 0.060))
            $cp = New-FittedTextPath 'GG Maps' $captionFamily $capBox
            $tb = New-Object System.Drawing.SolidBrush($TextGray)
            $g.FillPath($tb, $cp); $tb.Dispose(); $cp.Dispose()
        }
        else {
            $g.Clear([System.Drawing.Color]::Transparent)
            Add-Pin $g ([single]($w / 2)) ([single]($h / 2)) ([single]([Math]::Min($w, $h) * $glyphFraction[$name])) $White $null
        }

        $g.Dispose()
        $out = Join-Path $AssetsDir ('{0}.scale-{1}.png' -f $name, $scale)
        $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $written++
        '{0,-30} {1}x{2}' -f (Split-Path $out -Leaf), $w, $h
    }
}
$pfc.Dispose()
Write-Host "$written PNGs written to $AssetsDir"
