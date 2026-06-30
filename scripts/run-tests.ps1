<#
.SYNOPSIS
    Gate de test LOCAL de MathCursor — joue TOUT le harnais en une commande.

.DESCRIPTION
    Le projet n'utilise pas la CI GitHub (dépôt privé). Ce script est le garde-fou
    à lancer AVANT une release (ou via un hook pre-push) : il exécute les bouts qui,
    sinon, ne tournent jamais ensemble —
      1. Tests C# xUnit (Engine, Serialization, Analyzers, Adapter)
      2. Parité SpanComputer C#<->JS  (node spancomputer.test.js)
      3. Conformance moteur Rust (rust/mc-engine --bin conformance)

    Sort en code 1 si un test PRÉSENT échoue (gate rouge). Un outil absent
    (node/python non installés) est un AVERTISSEMENT jaune, pas un échec —
    mais signale que la parité/conformance n'a PAS été vérifiée.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\run-tests.ps1
#>

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$root = Split-Path -Parent $PSScriptRoot
$failures = @()   # BLOQUANT (produit) : C# + parite JS
$warnings = @()   # outils absents
$drift    = @()   # NON bloquant : port Python secondaire (anti-drift, pas le produit)

function Invoke-DotnetTest($name, $proj) {
    Write-Host "`n=== $name (dotnet test) ===" -ForegroundColor Cyan
    $full = Join-Path $root $proj
    if (-not (Test-Path $full)) { $script:warnings += "$name : csproj introuvable ($proj)"; return }
    dotnet test $full -v q --nologo
    if ($LASTEXITCODE -ne 0) { $script:failures += $name }
}

# 1) Tests C# xUnit ----------------------------------------------------------
Invoke-DotnetTest 'Engine'        'engine/tests/MathCursor.Engine.Tests/MathCursor.Engine.Tests.csproj'
Invoke-DotnetTest 'Serialization' 'serialization/tests/MathCursor.Serialization.Tests/MathCursor.Serialization.Tests.csproj'
Invoke-DotnetTest 'Analyzers'     'analyzers/MathCursor.Analyzers.Tests/MathCursor.Analyzers.Tests.csproj'
# Build mc-ner AVANT l'adapter : binaire consommé par NerCSharpRustParityTests
# (parité détection NER C#<->Rust). Absent -> le test SKIP (pas d'échec), mais la
# parité n'est alors PAS vérifiée. cargo absent = avertissement.
$cargoEarly = Get-Command cargo -ErrorAction SilentlyContinue
if ($null -eq $cargoEarly) { $warnings += 'cargo absent -> parité NER C#/Rust NON vérifiée (mc-ner non buildé)' }
else {
    Write-Host "`n=== Build mc-ner (parité NER C#/Rust) ===" -ForegroundColor Cyan
    Push-Location (Join-Path $root 'rust')
    & $cargoEarly.Source build -q -p mc-ner --release
    if ($LASTEXITCODE -ne 0) { $script:warnings += 'build mc-ner échoué -> parité NER skippée' }
    Pop-Location
}

# Adapter = net48 (interop Word). Si ton environnement ne build pas net48 en
# headless, commente la ligne suivante — les 3 projets ci-dessus suffisent au
# verrou moteur/sérialisation.
Invoke-DotnetTest 'Adapter'       'adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj'

# 2) Parité SpanComputer C#<->JS --------------------------------------------
Write-Host "`n=== Parité SpanComputer (node) ===" -ForegroundColor Cyan
$node = Get-Command node -ErrorAction SilentlyContinue
$jsTest = Join-Path $root 'web-demo/MathCursor.Demo.WebAssembly/wwwroot/spancomputer.test.js'
if ($null -eq $node) { $warnings += 'node absent -> parite SpanComputer C#<->JS NON verifiee' }
elseif (-not (Test-Path $jsTest)) { $warnings += "spancomputer.test.js introuvable" }
else {
    Push-Location (Split-Path $jsTest)
    node (Split-Path -Leaf $jsTest)
    if ($LASTEXITCODE -ne 0) { $script:failures += 'Parite SpanComputer (JS)' }
    Pop-Location
}

# 3) Conformance moteur RUST (BLOQUANT) -------------------------------------
# mc-engine (port shippe pour VSCode + LibreOffice) doit etre 456/456 vs
# fixtures.json, comme le C#. Un echec casse le gate.
# Conformance moteur RUST : mc-engine doit etre 456/456 vs fixtures.json (meme
# gate que le C#). C'est le port shippe (VSCode + LibreOffice) -> BLOQUANT.
Write-Host "`n=== Conformance Rust (mc-engine) ===" -ForegroundColor Cyan
$cargo = Get-Command cargo -ErrorAction SilentlyContinue
$rustDir = Join-Path $root 'rust'
if ($null -eq $cargo) { $warnings += 'cargo absent -> conformance Rust NON verifiee' }
elseif (-not (Test-Path (Join-Path $rustDir 'mc-engine'))) { $warnings += 'crate mc-engine introuvable' }
else {
    Push-Location $rustDir
    & $cargo.Source run -q -p mc-engine --bin conformance
    if ($LASTEXITCODE -ne 0) { $failures += 'Conformance Rust (mc-engine)' }
    Pop-Location
}

# Verdict --------------------------------------------------------------------
Write-Host "`n========================================" -ForegroundColor White
foreach ($w in $warnings) { Write-Host "AVERTISSEMENT : $w" -ForegroundColor Yellow }
foreach ($d in $drift)    { Write-Host ("DERIVE (non bloquant) : " + $d) -ForegroundColor DarkYellow }
if ($failures.Count -gt 0) {
    Write-Host ("ECHEC : " + ($failures -join ', ')) -ForegroundColor Red
    exit 1
}
Write-Host "OK : C# + parite JS verts (gate produit)." -ForegroundColor Green
exit 0
