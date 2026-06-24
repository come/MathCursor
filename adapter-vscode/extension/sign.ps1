# Signe les binaires Rust embarqués dans le VSIX (analyze, mc-ner, mc-popup).
# Le hook clavier global (mc-popup) + des exes non signés font râler Defender /
# SmartScreen → signature recommandée AVANT `vsce package`.
#
# Prérequis : un certificat code-signing (TON cert — non fourni). Usage :
#   .\sign.ps1 -Thumbprint <empreinte du cert installé>
#   .\sign.ps1 -PfxPath cert.pfx -PfxPassword (Read-Host -AsSecureString)
#
# Puis re-packager : npx @vscode/vsce package --target win32-x64
[CmdletBinding(DefaultParameterSetName = 'Thumbprint')]
param(
    [Parameter(ParameterSetName = 'Thumbprint', Mandatory = $true)]
    [string]$Thumbprint,
    [Parameter(ParameterSetName = 'Pfx', Mandatory = $true)]
    [string]$PfxPath,
    [Parameter(ParameterSetName = 'Pfx', Mandatory = $true)]
    [securestring]$PfxPassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exes = @(
    "$root\out\engine\analyze.exe",
    "$root\out\ner\mc-ner.exe",
    "$root\out\popup\mc-popup.exe"
)

foreach ($exe in $exes) {
    if (-not (Test-Path $exe)) { throw "Binaire absent (lance build.mjs d'abord) : $exe" }
}

$common = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256')
if ($PSCmdlet.ParameterSetName -eq 'Pfx') {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword))
    $args = $common + @('/f', $PfxPath, '/p', $plain)
} else {
    $args = $common + @('/sha1', $Thumbprint)
}

foreach ($exe in $exes) {
    Write-Host "Signature : $exe"
    & signtool @args $exe
    if ($LASTEXITCODE -ne 0) { throw "signtool a échoué sur $exe" }
}
Write-Host "OK — 3 binaires signés. Re-packager : npx @vscode/vsce package --target win32-x64"
