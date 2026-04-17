$ErrorActionPreference = "Stop"

$charSet = [char[]]((48..57) + (65..90) + (97..122))
$pwd = -join ($charSet | Get-Random -Count 32)
$securePwd = ConvertTo-SecureString -String $pwd -Force -AsPlainText

$cert = New-SelfSignedCertificate `
    -Subject "CN=MathCursor" `
    -Type CodeSigningCert `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears(5) `
    -FriendlyName "MathCursor Code Signing" `
    -CertStoreLocation "Cert:\CurrentUser\My"

$root = "D:\Software\DocMath"
$pfxPath = Join-Path $root "mathcursor.pfx"
$cerPath = Join-Path $root "docs\mathcursor.cer"
$b64Path = Join-Path $root "pfx-base64.txt"

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePwd | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

$bytes = [IO.File]::ReadAllBytes($pfxPath)
$b64 = [Convert]::ToBase64String($bytes)
[IO.File]::WriteAllText($b64Path, $b64)

Write-Output ""
Write-Output "=========================================="
Write-Output " CERTIFICAT GENERE"
Write-Output "=========================================="
Write-Output ""
Write-Output "Thumbprint : $($cert.Thumbprint)"
Write-Output "Expiration : $($cert.NotAfter.ToString('yyyy-MM-dd'))"
Write-Output "Fichier .cer (public) : $cerPath"
Write-Output "Fichier .pfx (prive)  : $pfxPath     <-- A supprimer apres"
Write-Output "Fichier base64         : $b64Path    <-- A supprimer apres"
Write-Output ""
Write-Output "=========================================="
Write-Output " GITHUB SECRETS A CREER"
Write-Output "=========================================="
Write-Output ""
Write-Output "1. Ouvre : https://github.com/come/MathCursor/settings/secrets/actions"
Write-Output ""
Write-Output "2. New repository secret -> name: PFX_PASSWORD"
Write-Output "   Value: $pwd"
Write-Output ""
Write-Output "3. New repository secret -> name: PFX_BASE64"
Write-Output "   Value: copie tout le contenu de $b64Path"
Write-Output "         (ouvre le fichier avec notepad, Ctrl+A, Ctrl+C)"
Write-Output ""
Write-Output "4. New repository secret -> name: PFX_THUMBPRINT"
Write-Output "   Value: $($cert.Thumbprint)"
Write-Output ""
