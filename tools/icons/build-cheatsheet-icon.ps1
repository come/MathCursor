# Génère les PNG d'icône du bouton Cheatsheet du ruban à partir du SVG
# `mortarboard-fill` de Bootstrap Icons (MIT, github.com/twbs/icons).
#
# Sorties : adapter-vsto/src/MathCursor/Resources/cheatsheet{16,32}.png
# Source SVG sauvegardée à côté pour référence + régénération.
#
# Usage : powershell -Sta -ExecutionPolicy Bypass -File tools/icons/build-cheatsheet-icon.ps1
#
# Le -Sta est requis car WPF (RenderTargetBitmap, Path) refuse de tourner en MTA.

$ErrorActionPreference = 'Stop'

$RepoRoot   = Resolve-Path "$PSScriptRoot\..\.."
$ResDir     = Join-Path $RepoRoot 'adapter-vsto\src\MathCursor\Resources'
$SvgPath    = Join-Path $ResDir   'mortarboard-fill.svg'
$SvgUrl     = 'https://raw.githubusercontent.com/twbs/icons/main/icons/mortarboard-fill.svg'

Write-Host "Resources dir : $ResDir"

# 1) Télécharge le SVG (idempotent — garde la version actuelle si déjà là)
if (-not (Test-Path $SvgPath)) {
    Write-Host "Téléchargement du SVG depuis $SvgUrl..."
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $SvgUrl -OutFile $SvgPath -UseBasicParsing
} else {
    Write-Host "SVG déjà présent : $SvgPath"
}

$svg = Get-Content -Path $SvgPath -Raw
# Extract path d=
if ($svg -notmatch 'd="([^"]+)"') {
    throw "Aucun attribut d= trouvé dans le SVG : $SvgPath"
}
$pathData = $matches[1]
Write-Host "Path data : $($pathData.Substring(0, [Math]::Min(60, $pathData.Length)))..."

# Bootstrap Icons utilisent viewBox 0 0 16 16
$svgSize = 16.0

# 2) Charge WPF
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

function Render-Png {
    param(
        [string]$PathData,
        [int]$Size,
        [string]$OutFile
    )

    $geom = [System.Windows.Media.Geometry]::Parse($PathData)

    # Wrap dans un DrawingVisual : permet de scaler le viewBox 16×16 du SVG
    # vers la taille cible $Size.
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    try {
        $scale = $Size / $svgSize
        $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
        $dc.DrawGeometry([System.Windows.Media.Brushes]::Black, $null, $geom)
        $dc.Pop()
    } finally {
        $dc.Close()
    }

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $frame   = [System.Windows.Media.Imaging.BitmapFrame]::Create($rtb)
    $encoder.Frames.Add($frame)

    $fs = [System.IO.File]::Create($OutFile)
    try { $encoder.Save($fs) } finally { $fs.Close() }

    $bytes = (Get-Item $OutFile).Length
    Write-Host ("  OK {0,-30} {1}x{1}  {2} bytes" -f (Split-Path $OutFile -Leaf), $Size, $bytes)
}

# 3) Génère les deux PNG
foreach ($size in @(16, 32)) {
    $out = Join-Path $ResDir ("cheatsheet{0}.png" -f $size)
    Render-Png -PathData $pathData -Size $size -OutFile $out
}

Write-Host "Done."
