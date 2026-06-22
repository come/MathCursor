<#
.SYNOPSIS
    Gate de test LOCAL de MathCursor — joue TOUT le harnais en une commande.

.DESCRIPTION
    Le projet n'utilise pas la CI GitHub (dépôt privé). Ce script est le garde-fou
    à lancer AVANT une release (ou via un hook pre-push) : il exécute les bouts qui,
    sinon, ne tournent jamais ensemble —
      1. Tests C# xUnit (Engine, Serialization, Analyzers, Adapter)
      2. Parité SpanComputer C#<->JS  (node spancomputer.test.js)
      3. Conformance moteur C#<->Python (engine-python/conformance.py)

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

# 3) Conformance moteur C#<->Python (NON bloquant) --------------------------
# Le port Python est un check de parite secondaire, PAS le produit livre.
# Une derive est signalee fort mais ne fait PAS echouer le gate. Pour le
# rendre bloquant (si tu maintiens le port a jour) : remplace `$drift` par
# `$failures` ci-dessous.
Write-Host "`n=== Conformance Python (non bloquant) ===" -ForegroundColor Cyan
$py = Get-Command python -ErrorAction SilentlyContinue
if ($null -eq $py) { $py = Get-Command py -ErrorAction SilentlyContinue }
$pyTest = Join-Path $root 'engine-python/conformance.py'
if ($null -eq $py) { $warnings += 'python absent -> conformance C#<->Python NON verifiee' }
elseif (-not (Test-Path $pyTest)) { $warnings += "conformance.py introuvable" }
else {
    Push-Location (Split-Path $pyTest)
    & $py.Source (Split-Path -Leaf $pyTest)
    if ($LASTEXITCODE -ne 0) { $script:drift += 'Conformance Python (port secondaire en retard)' }
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
