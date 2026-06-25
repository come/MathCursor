<#
.SYNOPSIS
  Signe (Authenticode) les binaires cœur Rust — analyze, mc-ner, mc-popup —
  PARTAGÉS par VSCode et LibreOffice. On signe À LA SOURCE
  (rust/target/release/) : relancer ensuite build.mjs (VSCode) et build_oxt.py
  (LibreOffice) recopie les binaires SIGNÉS dans les paquets. Aucun besoin de
  toucher aux scripts de build.

.DESCRIPTION
  Pourquoi : `mc-popup` pose un hook clavier global → un exe NON signé est
  souvent un faux positif antivirus. La signature = « éditeur connu » → bien
  moins de blocages. À faire AVANT une diffusion publique (Marketplace) ; inutile
  en beta. Le VSIX lui-même est signé automatiquement par le Marketplace — seuls
  les exes embarqués ont besoin de cette signature Authenticode.

  Horodatage (/tr) : la signature reste VALIDE À VIE, même après expiration du
  certificat → on peut payer un cert seulement le mois où l'on signe.

  3 modes selon le certificat :
   - Azure Trusted Signing (cloud, recommandé) : -Azure -MetadataPath <json>
       (prérequis : `dotnet tool install --global sign` + `az login`).
   - Cert installé (carte/token/magasin Windows) : -Thumbprint <empreinte>
   - Fichier PFX                                 : -PfxPath <p> -PfxPassword <secure>

.EXAMPLE
  ./scripts/sign-binaries.ps1 -Azure -MetadataPath ./scripts/trusted-signing.json
.EXAMPLE
  ./scripts/sign-binaries.ps1 -Thumbprint 1A2B3C...
#>
[CmdletBinding(DefaultParameterSetName = 'Thumbprint')]
param(
    [Parameter(ParameterSetName = 'Azure', Mandatory = $true)] [switch]$Azure,
    [Parameter(ParameterSetName = 'Azure', Mandatory = $true)] [string]$MetadataPath,
    [Parameter(ParameterSetName = 'Thumbprint', Mandatory = $true)] [string]$Thumbprint,
    [Parameter(ParameterSetName = 'Pfx', Mandatory = $true)] [string]$PfxPath,
    [Parameter(ParameterSetName = 'Pfx', Mandatory = $true)] [securestring]$PfxPassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$rel = Join-Path $root 'rust\target\release'
$bins = @('analyze.exe', 'mc-ner.exe', 'mc-popup.exe') | ForEach-Object { Join-Path $rel $_ }
foreach ($b in $bins) {
    if (-not (Test-Path $b)) { throw "Binaire absent : $b  (lance d'abord : cargo build --release)" }
}

if ($Azure) {
    # Tool Microsoft `sign` + Azure Trusted Signing (auth : az login / variables).
    if (-not (Get-Command sign -ErrorAction SilentlyContinue)) {
        throw "Tool 'sign' absent. Installe-le : dotnet tool install --global sign"
    }
    foreach ($b in $bins) {
        Write-Host "Signature (Azure Trusted Signing) : $b"
        & sign code trusted-signing $b --trusted-signing-metadata $MetadataPath
        if ($LASTEXITCODE -ne 0) { throw "sign a échoué sur $b" }
    }
}
else {
    # signtool (Windows SDK) : cert installé (thumbprint) ou PFX.
    $st = Get-Command signtool.exe -ErrorAction SilentlyContinue
    $signtool = if ($st) { $st.Source } else {
        (Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1).FullName
    }
    if (-not $signtool) { throw "signtool introuvable — installe le SDK Windows 10/11." }

    $common = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256')
    if ($PSCmdlet.ParameterSetName -eq 'Pfx') {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword))
        $signArgs = $common + @('/f', $PfxPath, '/p', $plain)
    }
    else {
        $signArgs = $common + @('/sha1', $Thumbprint)
    }
    foreach ($b in $bins) {
        Write-Host "Signature : $b"
        & $signtool @signArgs $b
        if ($LASTEXITCODE -ne 0) { throw "signtool a échoué sur $b" }
    }
    foreach ($b in $bins) { & $signtool verify /pa $b | Out-Null }
}

Write-Host ""
Write-Host "OK — binaires signés dans rust/target/release/." -ForegroundColor Green
Write-Host "Étape suivante : relancer les builds pour propager les binaires SIGNÉS :"
Write-Host "  - VSCode      : (cd adapter-vscode/extension ; node build.mjs)  puis  vsce package --target <…>"
Write-Host "  - LibreOffice : python libreoffice-ext/build_oxt.py"
