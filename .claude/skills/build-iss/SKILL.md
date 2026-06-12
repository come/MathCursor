---
name: build-iss
description: Build l'installer MathCursor (.iss) prêt pour /deploy-prod. Joue les tests xUnit (core + adapter), lance build.ps1 + ISCC, puis vérifie le payload — feedback.url embarqué, natifs ONNX x86 + x64 présents avec la bonne arch (PE header check), VC++ Redists x86 + x64 bundlés, modèle NER, MathCursor.dll/.vsto. À utiliser avant /deploy-prod ou quand l'utilisateur dit "build iss", "build installer", "rebuild setup".
user-invocable: true
allowed-tools:
  - Read
  - Bash
  - AskUserQuestion
---

# /build-iss — Build & verify de l'installer MathCursor

Pipeline qui prépare l'EXE prêt à être uploadé sur R2 par `/deploy-prod`. Ne touche RIEN en prod : juste tests + build local + checks de cohérence du payload.

Working dir : `D:/Software/MathCursor`. Tous les chemins sont relatifs à cette racine.

## Sortie attendue

`adapter-vsto/installer/output/MathCursor-Setup-<VERSION>.exe` produit, avec :
- `feedback.url` non vide pointant vers `https://`
- `onnxruntime-x86/onnxruntime.dll` (PE Machine = 0x014c)
- `onnxruntime-x64/onnxruntime.dll` (PE Machine = 0x8664)
- `vc_redist.x86.exe` + `vc_redist.x64.exe`
- Modèle NER `distilmult-v6` complet
- Tous les binaires .NET requis
- `MathCursor-Tutoriel-fr.docx` (généré par `tools/TutorialBuilder/` ; EN optionnel, spec non portée)

Si **n'importe quel** check échoue → stop, montre l'erreur, ne lance pas la suite. Pas de "best-effort" sur l'installer : un payload incomplet finit en crash chez les users.

---

## Étape 1 — Tests xUnit

Deux projets de tests (les deux sont SDK-style, donc `dotnet test` marche) :

```powershell
dotnet test engine/tests/MathCursor.Engine.Tests/MathCursor.Engine.Tests.csproj --nologo --verbosity minimal
dotnet test serialization/tests/MathCursor.Serialization.Tests/MathCursor.Serialization.Tests.csproj --nologo --verbosity minimal
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj --nologo --verbosity minimal
```

(beta-clean 2026-06-10 : moteur forest = engine + serialization, l'ex
core-csharp/LatticeEngine n'existe plus ; le verrou tuto↔moteur vit dans
`engine/tests` — `TutorialSpecTests`.)

**Règle** (cf. memory `feedback_show_failing_tests`) : si des tests sont rouges, **lister les noms + cause** dans la sortie, même s'ils étaient déjà rouges avant. Puis demander via AskUserQuestion : *"Continuer le build malgré N tests rouges ?"*. Si non → stop.

Si tout est vert → continue.

---

## Étape 1.5 — (retirée en beta-clean)

L'ex-check de couverture tuto vs `data-v2/concepts/*.yml` n'a plus d'objet :
le contrat du moteur forest = `engine/tests/fixtures.json`, et le verrou
tuto↔moteur est testé par `TutorialSpecTests` (étape 1). Cf. ADR
`2026-06-10-Feat-tutorial-fixtures-driven`.

---

## Étape 2 — Build payload + ISCC

```powershell
powershell -ExecutionPolicy Bypass -File adapter-vsto/installer/build.ps1
```

Ce script :
1. MSBuild Release du `MathCursor.csproj` VSTO
2. Copie les binaires dans `adapter-vsto/installer/payload/`
3. Copie les natifs ONNX dans `payload/onnxruntime-x86/` et `payload/onnxruntime-x64/`
4. Bundle `vc_redist.x86.exe` + `vc_redist.x64.exe` (téléchargés depuis aka.ms si absents du cache)
5. Copie le modèle NER `distilmult-v6`
6. Lance ISCC pour produire l'EXE final dans `adapter-vsto/installer/output/`

Si exit code ≠ 0 → stop, montre l'erreur.

Si ISCC est absent (pas dans `Program Files\Inno Setup 6\`), le script s'arrête après la préparation du payload sans produire l'EXE. Dans ce cas → stop avec message clair (l'utilisateur doit installer Inno Setup 6 ou compiler manuellement).

---

## Étape 3 — Vérifications post-build

Lis la version cible dans `adapter-vsto/installer/MathCursor.iss` (ligne `#define MyAppVersion "X.Y.Z"`). Stocke-la dans `$VERSION`.

Lance ce bloc PowerShell **complet** dans un seul `Bash` PowerShell — il vérifie tout et retourne un récap final. La fonction `Get-PeMachine` lit l'octet COFF Machine du PE header (offset `e_lfanew + 4`).

```powershell
$ErrorActionPreference = 'Stop'
$Root    = 'D:/Software/MathCursor'
$Payload = Join-Path $Root 'adapter-vsto/installer/payload'
$Output  = Join-Path $Root 'adapter-vsto/installer/output'
$Iss     = Join-Path $Root 'adapter-vsto/installer/MathCursor.iss'
$FbUrl   = Join-Path $Root 'adapter-vsto/installer/feedback.url'

$version = (Select-String -Path $Iss -Pattern '#define MyAppVersion "([^"]+)"').Matches[0].Groups[1].Value
$exe = Join-Path $Output ("MathCursor-Setup-$version.exe")

function Get-PeMachine($path) {
    $fs = [System.IO.File]::OpenRead($path)
    try {
        $br = New-Object System.IO.BinaryReader($fs)
        [void]$br.BaseStream.Seek(0x3C, 'Begin')
        $peOffset = $br.ReadInt32()
        [void]$br.BaseStream.Seek($peOffset + 4, 'Begin')
        return $br.ReadUInt16()
    } finally { $fs.Dispose() }
}

$problems = @()

# 1) feedback.url source non vide + https
if (-not (Test-Path $FbUrl)) {
    $problems += "feedback.url MANQUANT : $FbUrl"
} else {
    $url = (Get-Content $FbUrl -Raw).Trim()
    if (-not $url) { $problems += "feedback.url VIDE" }
    elseif (-not $url.StartsWith('https://')) { $problems += "feedback.url ne pointe pas vers https:// (got: $url)" }
    else { Write-Host "  OK feedback.url -> $url" -ForegroundColor Green }
}

# 2) Natifs ONNX par arch
$archChecks = @(
    @{ Arch = 'x86'; Machine = 0x014c },
    @{ Arch = 'x64'; Machine = 0x8664 }
)
foreach ($c in $archChecks) {
    foreach ($dll in @('onnxruntime.dll', 'onnxruntime_providers_shared.dll')) {
        $p = Join-Path $Payload ("onnxruntime-{0}/{1}" -f $c.Arch, $dll)
        if (-not (Test-Path $p)) {
            $problems += "MANQUANT : $p"
        } else {
            $m = Get-PeMachine $p
            if ($m -ne $c.Machine) {
                $problems += ("ARCH WRONG : {0} -> Machine 0x{1:x4} (attendu 0x{2:x4})" -f $p, $m, $c.Machine)
            } else {
                Write-Host ("  OK {0} ({1})" -f $dll, $c.Arch) -ForegroundColor Green
            }
        }
    }
}

# 3) VC++ Redists
foreach ($f in @('vc_redist.x86.exe', 'vc_redist.x64.exe')) {
    $p = Join-Path $Payload $f
    if (-not (Test-Path $p)) { $problems += "MANQUANT : $p" }
    else { Write-Host "  OK $f" -ForegroundColor Green }
}

# 4) Binaires VSTO essentiels
foreach ($f in @('MathCursor.dll', 'MathCursor.vsto', 'MathCursor.dll.manifest', 'MathCursor.Engine.dll', 'MathCursor.Serialization.dll', 'MathCursor.HostContract.dll', 'Microsoft.ML.OnnxRuntime.dll')) {
    $p = Join-Path $Payload $f
    if (-not (Test-Path $p)) { $problems += "MANQUANT : $p" }
    else { Write-Host "  OK $f" -ForegroundColor Green }
}

# 5bis) Tutoriels FR + EN .docx (ADR 2026-05-22-Feat-tutorial-docx-generated-onboarding)
foreach ($lang in @('fr')) {  # EN : spec non portée en beta-clean (skipifsourcedoesntexist dans l'iss)
    $tuto = Join-Path $Payload "MathCursor-Tutoriel-$lang.docx"
    if (-not (Test-Path $tuto)) {
        $problems += "MANQUANT : $tuto (généré par tools/TutorialBuilder/)"
    } else {
        $sizeKB = [math]::Round((Get-Item $tuto).Length / 1KB, 1)
        Write-Host "  OK MathCursor-Tutoriel-$lang.docx ($sizeKB Ko)" -ForegroundColor Green
    }
}

# 5) Modèle NER
foreach ($f in @('models/distilmult-v5/model_quantized.onnx', 'models/distilmult-v5/vocab.txt')) {
    $p = Join-Path $Payload $f
    if (-not (Test-Path $p)) { $problems += "MANQUANT : $p" }
    else {
        $sizeMB = [math]::Round((Get-Item $p).Length / 1MB, 1)
        Write-Host "  OK $f ($sizeMB Mo)" -ForegroundColor Green
    }
}

# 6) EXE final
if (-not (Test-Path $exe)) {
    $problems += "EXE installer MANQUANT : $exe"
} else {
    $sizeMB = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "  OK MathCursor-Setup-$version.exe ($sizeMB Mo)" -ForegroundColor Green
}

if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Host "BUILD INVALIDE :" -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  X $_" -ForegroundColor Red }
    exit 1
}

Write-Host ""
Write-Host "OK Installer pret pour /deploy-prod" -ForegroundColor Green
Write-Host "   Version : $version"
Write-Host "   Fichier : $exe"
```

Si la sortie contient `BUILD INVALIDE` → stop, **ne pas** prétendre que c'est OK pour le deploy. Liste exactement ce qui manque dans la réponse à l'utilisateur.

---

## Rapport final

Format court, pas de blabla :

```
✓ Tests : engine (N OK) + serialization (M OK) + adapter (P OK)
✓ Build : payload OK + ISCC OK
✓ Payload : feedback.url, onnxruntime x86+x64, vc_redist x86+x64, modèle NER, MathCursor.{dll,vsto,manifest} + Engine + Serialization, MathCursor-Tutoriel-fr.docx
✓ Installer : adapter-vsto/installer/output/MathCursor-Setup-<VERSION>.exe (XX Mo)

→ Prêt pour /deploy-prod <VERSION>
```

Si une étape a échoué : ✗ + raison exacte, et **pas** de message "→ Prêt pour /deploy-prod".

---

## Garde-fous

- **Ne jamais** committer/pusher quoi que ce soit (cf. memory `feedback_commits`).
- **Ne pas** continuer après un test rouge sans confirmation explicite.
- **Ne pas** modifier les fichiers source pour faire passer le build (sauf bug évident découvert pendant les checks → demander à l'utilisateur).
- Si le cache NuGet ne contient pas `microsoft.ml.onnxruntime/1.16.3/runtimes/win-x86/native/` → message clair pour lancer `dotnet restore` ou un build complet d'abord.

Arguments passés : `$ARGUMENTS` (ignorés actuellement, la version est lue depuis le `.iss`).
