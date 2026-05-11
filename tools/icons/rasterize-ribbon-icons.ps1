# Rasterise tous les SVG de tools/icons/ribbon/ en PNG 16/32/48 pour le
# ruban VSTO (Office consomme du PNG, pas du SVG natif).
#
# Sorties : adapter-vsto/src/MathCursor/Resources/ribbon-<name>{16,32,48}.png
#
# Usage :
#   powershell -Sta -ExecutionPolicy Bypass -File tools/icons/rasterize-ribbon-icons.ps1
#
# Le -Sta est requis (WPF refuse de tourner en MTA).
#
# Mini-renderer SVG → WPF DrawingContext : gère uniquement les primitives
# utilisées par notre set d'icônes (rect, line, circle, ellipse, polygon,
# polyline, path, text). Pas de dépendance externe (Inkscape/ImageMagick).
# Les défauts stroke/fill/stroke-width sont hérités du <svg> root.

$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path "$PSScriptRoot\..\.."
$SvgDir   = Join-Path $PSScriptRoot 'ribbon'
$ResDir   = Join-Path $RepoRoot 'adapter-vsto\src\MathCursor\Resources'
$Sizes    = @(16, 32, 48)

Write-Host "SVG source : $SvgDir"
Write-Host "PNG output : $ResDir"
Write-Host ""

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

function Parse-Color {
    param([string]$s, $fallback)
    if ([string]::IsNullOrWhiteSpace($s) -or $s -eq 'none') { return $null }
    if ($s -match '^#[0-9A-Fa-f]{3}$') {
        # #abc → #aabbcc
        $r = [Convert]::ToByte($s[1] + $s[1], 16)
        $g = [Convert]::ToByte($s[2] + $s[2], 16)
        $b = [Convert]::ToByte($s[3] + $s[3], 16)
        return [System.Windows.Media.Color]::FromRgb($r, $g, $b)
    }
    if ($s -match '^#([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})$') {
        return [System.Windows.Media.Color]::FromRgb(
            [Convert]::ToByte($matches[1], 16),
            [Convert]::ToByte($matches[2], 16),
            [Convert]::ToByte($matches[3], 16))
    }
    if ($s -eq 'white') { return [System.Windows.Media.Colors]::White }
    if ($s -eq 'black') { return [System.Windows.Media.Colors]::Black }
    return $fallback
}

function Get-Attr {
    param($el, [string]$name, $defaultVal = $null)
    $a = $el.Attributes[$name]
    if ($a) { return $a.Value } else { return $defaultVal }
}

function Make-Pen {
    param($strokeColor, [double]$width, [string]$linecap, [string]$linejoin)
    if (-not $strokeColor) { return $null }
    $brush = New-Object System.Windows.Media.SolidColorBrush($strokeColor)
    $pen = New-Object System.Windows.Media.Pen($brush, $width)
    switch ($linecap) {
        'round'  { $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round' }
        'square' { $pen.StartLineCap = 'Square'; $pen.EndLineCap = 'Square' }
        default  { $pen.StartLineCap = 'Flat'; $pen.EndLineCap = 'Flat' }
    }
    switch ($linejoin) {
        'round' { $pen.LineJoin = 'Round' }
        'bevel' { $pen.LineJoin = 'Bevel' }
        default { $pen.LineJoin = 'Miter' }
    }
    return $pen
}

function Make-Brush {
    param($fillColor)
    if (-not $fillColor) { return $null }
    return New-Object System.Windows.Media.SolidColorBrush($fillColor)
}

function Points-To-PointList {
    param([string]$s)
    $pts = New-Object System.Collections.Generic.List[System.Windows.Point]
    # "x1,y1 x2,y2 x3,y3" ou "x1 y1 x2 y2 ..." — splitter sur espace/virgule.
    $tokens = [regex]::Split($s.Trim(), '[\s,]+') | Where-Object { $_ -ne '' }
    for ($i = 0; $i -lt $tokens.Count - 1; $i += 2) {
        $x = [double]::Parse($tokens[$i], [System.Globalization.CultureInfo]::InvariantCulture)
        $y = [double]::Parse($tokens[$i + 1], [System.Globalization.CultureInfo]::InvariantCulture)
        $pts.Add((New-Object System.Windows.Point($x, $y)))
    }
    return $pts
}

function Render-Element {
    param($el, $dc, $defaults)

    # Inherit vs override : distinguer "absent" (utilise default) de
    # "présent = none" (vraiment pas de stroke/fill).
    $strokeAttr = if ($el.Attributes['stroke']) { $el.Attributes['stroke'].Value } else { $null }
    if ($null -eq $strokeAttr)    { $stroke = $defaults.Stroke }
    elseif ($strokeAttr -eq 'none') { $stroke = $null }
    else                            { $stroke = Parse-Color $strokeAttr $defaults.Stroke }

    $fillAttr = if ($el.Attributes['fill']) { $el.Attributes['fill'].Value } else { $null }
    if ($null -eq $fillAttr)        { $fill = $defaults.Fill }
    elseif ($fillAttr -eq 'none')   { $fill = $null }
    else                            { $fill = Parse-Color $fillAttr $defaults.Fill }

    $swAttr = if ($el.Attributes['stroke-width']) { $el.Attributes['stroke-width'].Value } else { $null }
    $strokeW = if ($null -eq $swAttr) { $defaults.StrokeWidth } else {
        [double]::Parse($swAttr, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    $linecap  = if ($el.Attributes['stroke-linecap'])  { $el.Attributes['stroke-linecap'].Value }  else { $defaults.Linecap }
    $linejoin = if ($el.Attributes['stroke-linejoin']) { $el.Attributes['stroke-linejoin'].Value } else { $defaults.Linejoin }

    $pen = Make-Pen $stroke $strokeW $linecap $linejoin
    $brush = Make-Brush $fill

    $ci = [System.Globalization.CultureInfo]::InvariantCulture

    switch ($el.LocalName) {
        'rect' {
            $x = [double]::Parse((Get-Attr $el 'x' '0'), $ci)
            $y = [double]::Parse((Get-Attr $el 'y' '0'), $ci)
            $w = [double]::Parse((Get-Attr $el 'width' '0'), $ci)
            $h = [double]::Parse((Get-Attr $el 'height' '0'), $ci)
            $rx = [double]::Parse((Get-Attr $el 'rx' '0'), $ci)
            $ry = [double]::Parse((Get-Attr $el 'ry' "$rx"), $ci)
            $rect = New-Object System.Windows.Rect($x, $y, $w, $h)
            $dc.DrawRoundedRectangle($brush, $pen, $rect, $rx, $ry)
        }
        'line' {
            $x1 = [double]::Parse((Get-Attr $el 'x1' '0'), $ci)
            $y1 = [double]::Parse((Get-Attr $el 'y1' '0'), $ci)
            $x2 = [double]::Parse((Get-Attr $el 'x2' '0'), $ci)
            $y2 = [double]::Parse((Get-Attr $el 'y2' '0'), $ci)
            $dc.DrawLine($pen, (New-Object System.Windows.Point($x1, $y1)), (New-Object System.Windows.Point($x2, $y2)))
        }
        'circle' {
            $cx = [double]::Parse((Get-Attr $el 'cx' '0'), $ci)
            $cy = [double]::Parse((Get-Attr $el 'cy' '0'), $ci)
            $r  = [double]::Parse((Get-Attr $el 'r'  '0'), $ci)
            $dc.DrawEllipse($brush, $pen, (New-Object System.Windows.Point($cx, $cy)), $r, $r)
        }
        'ellipse' {
            $cx = [double]::Parse((Get-Attr $el 'cx' '0'), $ci)
            $cy = [double]::Parse((Get-Attr $el 'cy' '0'), $ci)
            $rx = [double]::Parse((Get-Attr $el 'rx' '0'), $ci)
            $ry = [double]::Parse((Get-Attr $el 'ry' '0'), $ci)
            $dc.DrawEllipse($brush, $pen, (New-Object System.Windows.Point($cx, $cy)), $rx, $ry)
        }
        'polygon' {
            $pts = Points-To-PointList (Get-Attr $el 'points')
            if ($pts.Count -ge 2) {
                $sg = New-Object System.Windows.Media.StreamGeometry
                $ctx = $sg.Open()
                try {
                    $ctx.BeginFigure($pts[0], ($brush -ne $null), $true)
                    $tail = New-Object System.Collections.Generic.List[System.Windows.Point]
                    for ($i = 1; $i -lt $pts.Count; $i++) { $tail.Add($pts[$i]) }
                    $ctx.PolyLineTo($tail, ($pen -ne $null), $true)
                } finally { $ctx.Close() }
                $sg.Freeze()
                $dc.DrawGeometry($brush, $pen, $sg)
            }
        }
        'polyline' {
            $pts = Points-To-PointList (Get-Attr $el 'points')
            if ($pts.Count -ge 2) {
                $sg = New-Object System.Windows.Media.StreamGeometry
                $ctx = $sg.Open()
                try {
                    $ctx.BeginFigure($pts[0], $false, $false)
                    $tail = New-Object System.Collections.Generic.List[System.Windows.Point]
                    for ($i = 1; $i -lt $pts.Count; $i++) { $tail.Add($pts[$i]) }
                    $ctx.PolyLineTo($tail, ($pen -ne $null), $true)
                } finally { $ctx.Close() }
                $sg.Freeze()
                $dc.DrawGeometry($null, $pen, $sg)
            }
        }
        'path' {
            $d = Get-Attr $el 'd'
            if ($d) {
                $geom = [System.Windows.Media.Geometry]::Parse($d)
                $dc.DrawGeometry($brush, $pen, $geom)
            }
        }
        'text' {
            $x = [double]::Parse((Get-Attr $el 'x' '0'), $ci)
            $y = [double]::Parse((Get-Attr $el 'y' '0'), $ci)
            $fontSize   = [double]::Parse((Get-Attr $el 'font-size' '10'), $ci)
            $fontFamilyName = Get-Attr $el 'font-family' 'Segoe UI'
            $fontStyleName  = Get-Attr $el 'font-style'  'normal'
            $fontWeightStr  = Get-Attr $el 'font-weight' '400'
            $textFill = if ($fill) { $fill } else { (Make-Brush $stroke).Color }
            $fontFamily = New-Object System.Windows.Media.FontFamily($fontFamilyName)
            $fontStyle = if ($fontStyleName -eq 'italic') {
                [System.Windows.FontStyles]::Italic
            } else { [System.Windows.FontStyles]::Normal }
            $fontWeight = if ($fontWeightStr -in @('bold','700','600','800','900')) {
                [System.Windows.FontWeights]::SemiBold
            } else { [System.Windows.FontWeights]::Normal }
            $typeface = New-Object System.Windows.Media.Typeface(
                $fontFamily, $fontStyle, $fontWeight, [System.Windows.FontStretches]::Normal)
            $ft = New-Object System.Windows.Media.FormattedText(
                $el.InnerText,
                $ci,
                [System.Windows.FlowDirection]::LeftToRight,
                $typeface,
                $fontSize,
                (New-Object System.Windows.Media.SolidColorBrush($textFill)),
                $null,
                [System.Windows.Media.TextFormattingMode]::Ideal,
                1.0)
            # SVG <text> y = baseline. WPF FormattedText origin = top-left.
            # Offset Y par baseline (= ascent).
            $origin = New-Object System.Windows.Point($x, ($y - $ft.Baseline))
            $dc.DrawText($ft, $origin)
        }
        default {
            # ignore : <title>, <desc>, <defs>, <g> (pas utilisé dans nos icônes)
        }
    }
}

function Render-Svg {
    param([string]$SvgPath, [int]$Size, [string]$OutPath)

    [xml]$svg = Get-Content $SvgPath -Raw
    $root = $svg.svg

    # viewBox "x y w h" — par défaut on suppose 0 0 24 24
    $vbStr = $root.viewBox
    $vb = if ($vbStr) {
        $parts = $vbStr.Trim() -split '[\s,]+'
        @{ X = [double]$parts[0]; Y = [double]$parts[1]; W = [double]$parts[2]; H = [double]$parts[3] }
    } else {
        @{ X = 0.0; Y = 0.0; W = 24.0; H = 24.0 }
    }

    # Defaults presentation depuis <svg>. NB : xmlns="http://www.w3.org/2000/svg"
    # casse l'accès direct $root.stroke en PowerShell XML — on lit par
    # Attributes collection qui ignore le namespace pour les attrs sans préfixe.
    $rootStroke    = if ($root.Attributes['stroke'])         { $root.Attributes['stroke'].Value }         else { '#333' }
    $rootFill      = if ($root.Attributes['fill'])           { $root.Attributes['fill'].Value }           else { 'none' }
    $rootStrokeW   = if ($root.Attributes['stroke-width'])   { $root.Attributes['stroke-width'].Value }   else { '1' }
    $rootLinecap   = if ($root.Attributes['stroke-linecap']) { $root.Attributes['stroke-linecap'].Value } else { 'butt' }
    $rootLinejoin  = if ($root.Attributes['stroke-linejoin']){ $root.Attributes['stroke-linejoin'].Value }else { 'miter' }

    $defaults = @{
        Stroke      = (Parse-Color $rootStroke ([System.Windows.Media.Color]::FromRgb(51,51,51)))
        Fill        = if ($rootFill -eq 'none') { $null } else { Parse-Color $rootFill $null }
        StrokeWidth = [double]::Parse($rootStrokeW, [System.Globalization.CultureInfo]::InvariantCulture)
        Linecap     = $rootLinecap
        Linejoin    = $rootLinejoin
    }

    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    try {
        $scale = $Size / $vb.W
        $tg = New-Object System.Windows.Media.TransformGroup
        $tg.Children.Add((New-Object System.Windows.Media.TranslateTransform(-$vb.X, -$vb.Y)))
        $tg.Children.Add((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
        $dc.PushTransform($tg)
        try {
            foreach ($node in $root.ChildNodes) {
                if ($node.NodeType -ne 'Element') { continue }
                Render-Element $node $dc $defaults
            }
        } finally { $dc.Pop() }
    } finally { $dc.Close() }

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $frame   = [System.Windows.Media.Imaging.BitmapFrame]::Create($rtb)
    $encoder.Frames.Add($frame)

    $fs = [System.IO.File]::Create($OutPath)
    try { $encoder.Save($fs) } finally { $fs.Close() }
}

if (-not (Test-Path $ResDir)) {
    New-Item -ItemType Directory -Path $ResDir | Out-Null
}

$svgs = Get-ChildItem -Path $SvgDir -Filter '*.svg' | Sort-Object Name
if ($svgs.Count -eq 0) {
    Write-Warning "Aucun .svg trouvé dans $SvgDir"
    exit 1
}

$total = 0
foreach ($svg in $svgs) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($svg.Name)
    foreach ($size in $Sizes) {
        $outName = "ribbon-{0}-{1}.png" -f $stem, $size
        $outPath = Join-Path $ResDir $outName
        try {
            Render-Svg -SvgPath $svg.FullName -Size $size -OutPath $outPath
            $bytes = (Get-Item $outPath).Length
            Write-Host ("  OK   {0,-40} {1}x{1}  {2,5} bytes" -f $outName, $size, $bytes)
            $total++
        } catch {
            Write-Warning ("  FAIL {0,-40}  {1}" -f $outName, $_.Exception.Message)
        }
    }
}

Write-Host ""
Write-Host "Done. $total PNG générés dans $ResDir"
