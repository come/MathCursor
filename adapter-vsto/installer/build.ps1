# MathCursor — script de préparation de l'installer Inno Setup.
#
# 1) Build le projet VSTO en Release via MSBuild (Visual Studio).
# 2) Copie les fichiers nécessaires dans installer/payload/.
# 3) Vérifie que le modèle NER est présent dans installer/payload/models/.
# 4) Optionnel : lance le compilateur Inno Setup pour produire l'EXE.
#
# Usage :
#   powershell -ExecutionPolicy Bypass -File adapter-vsto\installer\build.ps1
#
# Prérequis :
#   - Visual Studio 2022 (MSBuild avec charge Office Dev)
#   - Inno Setup 6 installé (pour la production de l'EXE final)
#   - Les fichiers du modèle NER (copier depuis D:\Software\DocMath\models\)

$ErrorActionPreference = 'Stop'
$InstallerDir = $PSScriptRoot
$RepoRoot     = Resolve-Path "$InstallerDir\..\.."
$VstoProj     = Join-Path $RepoRoot 'adapter-vsto\src\MathCursor\MathCursor.csproj'
$BinRelease   = Join-Path $RepoRoot 'adapter-vsto\src\MathCursor\bin\Release'
$BinDebug     = Join-Path $RepoRoot 'adapter-vsto\src\MathCursor\bin\Debug'
$PayloadDir   = Join-Path $InstallerDir 'payload'
$ModelSrcDir  = Join-Path $RepoRoot 'models'
$ModelDstDir  = Join-Path $PayloadDir 'models'
$IssFile      = Join-Path $InstallerDir 'MathCursor.iss'
$OutputDir    = Join-Path $InstallerDir 'output'

Write-Host "== MathCursor installer builder ==" -ForegroundColor Cyan
Write-Host "Repo      : $RepoRoot"
Write-Host "Payload   : $PayloadDir"
Write-Host "Output    : $OutputDir"
Write-Host ""

# 1) Build Release via MSBuild
Write-Host "[1/4] Build VSTO en Release..." -ForegroundColor Yellow
$msbuild = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
if (-not $msbuild) {
    # Fallback : chercher dans Program Files
    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $msbuild = $c; break }
    }
}
if (-not $msbuild) {
    Write-Warning "MSBuild introuvable. Lance manuellement :"
    Write-Warning "  msbuild '$VstoProj' /p:Configuration=Release"
    Write-Warning "Puis relance ce script."
    exit 1
}
& $msbuild $VstoProj /p:Configuration=Release /nologo /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build échoué."
    exit 1
}

# 2) Copie des binaires vers payload/
Write-Host "[2/4] Copie des binaires vers payload/..." -ForegroundColor Yellow
$SrcBin = if (Test-Path $BinRelease) { $BinRelease } else { $BinDebug }
if (-not (Test-Path $SrcBin)) {
    Write-Error "Dossier de build introuvable : $SrcBin"
    exit 1
}
Write-Host "  source : $SrcBin"
if (Test-Path $PayloadDir) { Remove-Item -Recurse -Force $PayloadDir }
New-Item -ItemType Directory -Force -Path $PayloadDir | Out-Null

$FilesToCopy = @(
    'MathCursor.dll',
    'MathCursor.dll.manifest',
    'MathCursor.dll.config',
    'MathCursor.vsto',
    'MathCursor.Core.dll',
    'MathCursor.HostContract.dll',
    'WpfMath.dll',
    'XamlMath.Shared.dll',
    'FuzzySharp.dll',
    'YamlDotNet.dll',
    'Google.Protobuf.dll',
    'Microsoft.ML.OnnxRuntime.dll',
    # Microsoft.ML.OnnxRuntime.dll est managé (AnyCPU) — ok à la racine.
    # Les natifs onnxruntime.dll / onnxruntime_providers_shared.dll sont
    # copiés en arch-séparée dans 2b-bis ci-dessous.
    'Microsoft.Office.Tools.Common.v4.0.Utilities.dll',
    'System.Buffers.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll'
)

foreach ($f in $FilesToCopy) {
    $src = Join-Path $SrcBin $f
    if (Test-Path $src) {
        Copy-Item $src -Destination $PayloadDir -Force
    }
    else {
        if ($f -like 'System.*') {
            # System.* peuvent ne pas être redistribués, pas grave (GAC)
            Write-Host "  (skip optionnel : $f)"
        } else {
            Write-Warning "manquant : $f"
        }
    }
}

# 2b) Certificat à importer au runtime (cf. [Run] dans MathCursor.iss)
$CertSrc = Join-Path $RepoRoot 'docs\mathcursor.cer'
$CertDst = Join-Path $PayloadDir 'mathcursor.cer'
if (Test-Path $CertSrc) {
    Copy-Item $CertSrc -Destination $CertDst -Force
    Write-Host "  certif copié : $CertDst"
} else {
    Write-Warning "Certificat introuvable ($CertSrc) — l'installer ne pourra pas l'importer automatiquement."
}

# 2b-bis) Native ONNX Runtime DLLs (NON copiées par MSBuild).
# Le NuGet Microsoft.ML.OnnxRuntime expose ses natives via .props avec
# une condition `PlatformTarget == x64` (ou AnyCPU+!Prefer32Bit). Le
# csproj VSTO n'a aucun PlatformTarget défini → la condition est fausse
# → MSBuild ne copie PAS les natives dans bin/Release. Sans elles, le
# .cctor de NativeMethods lève une TypeInitializationException au
# démarrage de l'add-in.
#
# IMPORTANT : Word peut être 32 ou 64 bits. Le DLL natif onnxruntime.dll
# DOIT correspondre à la bitness du process WINWORD.EXE — sinon
# BadImageFormatException remonte en TypeInitializationException sur
# NativeMethods..cctor() au premier `new SessionOptions()`. On déploie
# donc les DEUX runtimes dans des sous-dossiers et ThisAddIn_Startup
# choisira le bon via SetDllDirectory(<app>\onnxruntime-x86 ou x64) en
# fonction de IntPtr.Size avant d'instancier MathNerDetector.
$OrtVersion = '1.16.3'
$OrtNativeRoot = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.ml.onnxruntime\$OrtVersion\runtimes"
foreach ($arch in @('x86', 'x64')) {
    $srcDir = Join-Path $OrtNativeRoot "win-$arch\native"
    $dstDir = Join-Path $PayloadDir "onnxruntime-$arch"
    if (Test-Path $srcDir) {
        New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
        foreach ($dll in @('onnxruntime.dll', 'onnxruntime_providers_shared.dll')) {
            $src = Join-Path $srcDir $dll
            if (Test-Path $src) {
                Copy-Item $src -Destination $dstDir -Force
                Write-Host "  ORT native ($arch) : $dll"
            } else {
                Write-Warning "ORT native manquante ($arch) dans cache NuGet : $dll"
            }
        }
    } else {
        Write-Warning "Cache NuGet ORT $arch introuvable : $srcDir"
        Write-Warning "Lance d'abord 'dotnet restore' ou un build complet pour peupler le cache."
    }
}

# 2c) Visual C++ Redistributable x86 + x64 (requis par ONNX Runtime native).
# Word peut être 32 ou 64 bits → on bundle les deux. Le iss lance les deux
# au runtime (idempotent, skippe si version >= déjà présente). Cache au
# niveau dossier installer pour éviter de re-télécharger à chaque build.
foreach ($arch in @('x86', 'x64')) {
    $VcRedistDst   = Join-Path $PayloadDir   "vc_redist.$arch.exe"
    $VcRedistCache = Join-Path $InstallerDir "vc_redist.$arch.exe"
    if (Test-Path $VcRedistCache) {
        Copy-Item $VcRedistCache -Destination $VcRedistDst -Force
        Write-Host "  VC++ Redist $arch depuis cache : $VcRedistCache"
    } elseif (Test-Path $VcRedistDst) {
        Write-Host "  VC++ Redist $arch déjà présent dans payload/"
    } else {
        $url = "https://aka.ms/vs/17/release/vc_redist.$arch.exe"
        Write-Host "  Téléchargement VC++ Redist $arch depuis $url..."
        try {
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $url -OutFile $VcRedistDst -UseBasicParsing
            Copy-Item $VcRedistDst -Destination $VcRedistCache -Force
            Write-Host "  VC++ Redist $arch téléchargé ($([math]::Round((Get-Item $VcRedistDst).Length / 1MB, 1)) Mo)"
        } catch {
            Write-Warning "Téléchargement VC++ Redist $arch échoué : $_"
            Write-Warning "L'installer fonctionnera mais sans bundling VC++ $arch — l'utilisateur devra l'avoir installé."
        }
    }
}

# 3) Modèle NER
# On déploie UNIQUEMENT distilmult-v5 (le NER actif depuis 2026-04-30,
# bumpé depuis distilmult-v4 du 2026-04-27 — F1 0.95 vs 0.80 sur regression_v1_gold).
# L'ancien XLM-R laissé à la racine de models/ en dev est ignoré ici —
# il ferait ~265 Mo de poids mort dans l'installer et ne sert plus
# (FindModelDir priorise distilmult-v5 et tombe sur celui-ci).
Write-Host "[3/4] Modèle NER (distilmult-v5)..." -ForegroundColor Yellow
if (Test-Path $ModelDstDir) { Remove-Item -Recurse -Force $ModelDstDir }
$DistilSrc = Join-Path $ModelSrcDir 'distilmult-v5'
$DistilDst = Join-Path $ModelDstDir 'distilmult-v5'
$modelOk = $false
if (Test-Path (Join-Path $DistilSrc 'model_quantized.onnx')) {
    Write-Host "  copie depuis $DistilSrc → $DistilDst"
    New-Item -ItemType Directory -Force -Path $DistilDst | Out-Null
    Copy-Item -Path "$DistilSrc\*" -Destination $DistilDst -Force -Recurse
    $modelOk = $true
}
else {
    Write-Warning "Modèle distilmult-v5 introuvable. Copier les fichiers dans :"
    Write-Warning "  $DistilSrc"
    Write-Warning "Fichiers requis : model_quantized.onnx, vocab.txt, tokenizer.json, config.json, special_tokens_map.json, tokenizer_config.json, ort_config.json"
}

# 4) Info fichier post-install
$afterInstallFile = Join-Path $InstallerDir 'after-install.txt'
if (-not (Test-Path $afterInstallFile)) {
    @'
Installation terminée.

Pour activer MathCursor :
  1. Ouvrez Microsoft Word.
  2. L'add-in se charge automatiquement (barre d'état : "MathCursor prêt").
  3. Commencez à taper : dès qu'une expression math est détectée, une popup apparaît.
     Naviguez avec Flèche bas, validez avec Entrée.

Si la popup n'apparaît pas :
  - Vérifiez Fichier > Options > Compléments > Compléments COM : "MathCursor" doit être coché.
  - Logs : %AppData%\MathCursor\logs\mathcursor.log

Désinstaller : Panneau de configuration > Programmes > MathCursor > Désinstaller.
'@ | Set-Content -Path $afterInstallFile -Encoding UTF8
    Write-Host "  créé : after-install.txt"
}

# 5) Compiler Inno Setup si disponible
Write-Host "[4/4] Inno Setup..." -ForegroundColor Yellow
$iscc = Get-Command 'iscc.exe' -ErrorAction SilentlyContinue
if (-not $iscc) {
    $isccCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $isccCandidates) {
        if (Test-Path $c) { $iscc = $c; break }
    }
}

if ($modelOk -and $iscc) {
    if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null }
    & $iscc $IssFile
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "OK — installer créé dans $OutputDir" -ForegroundColor Green
        Get-ChildItem $OutputDir -Filter '*.exe' | ForEach-Object { Write-Host "  → $($_.FullName)" }
    } else {
        Write-Error "Compilation Inno Setup échouée (code $LASTEXITCODE)"
    }
}
else {
    if (-not $iscc) {
        Write-Host ""
        Write-Host "Inno Setup introuvable. Installe-le depuis https://jrsoftware.org/isinfo.php"
        Write-Host "Puis compile manuellement : clique-droit sur MathCursor.iss → 'Compile'"
    }
    if (-not $modelOk) {
        Write-Host ""
        Write-Host "Compile à la main après avoir mis le modèle dans payload/models/"
    }
}
