# install-cert.ps1
#
# Importe le certificat MathCursor dans les trust stores du user courant
# pour que Word accepte d'installer le complément.
#
# Usage (une seule fois, par machine testeur) :
#   iwr -UseBasicParsing https://come.github.io/MathCursor/install-cert.ps1 | iex
#
# Ou, en local, si vous avez téléchargé le fichier :
#   powershell -ExecutionPolicy Bypass -File install-cert.ps1

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=============================================="
Write-Host " Installation du certificat MathCursor"
Write-Host "=============================================="
Write-Host ""

$certUrl  = "https://come.github.io/MathCursor/mathcursor.cer"
$certPath = Join-Path $env:TEMP "mathcursor.cer"

try {
    Write-Host "1/3 Telechargement du certificat..."
    Invoke-WebRequest -Uri $certUrl -OutFile $certPath -UseBasicParsing

    Write-Host "2/3 Import dans TrustedPublisher..."
    Import-Certificate -FilePath $certPath `
        -CertStoreLocation Cert:\CurrentUser\TrustedPublisher | Out-Null

    Write-Host "3/3 Import dans Root (chaine auto-signee)..."
    Import-Certificate -FilePath $certPath `
        -CertStoreLocation Cert:\CurrentUser\Root | Out-Null

    Write-Host ""
    Write-Host "[OK] Certificat installe."
    Write-Host ""
    Write-Host "Vous pouvez maintenant installer MathCursor :"
    Write-Host "  https://come.github.io/MathCursor/release/setup.exe"
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Host "[ERREUR] Echec de l'installation du certificat :" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Contact : come2percin@gmail.com"
    exit 1
}
finally {
    if (Test-Path $certPath) { Remove-Item -Force $certPath }
}
