$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$package = Join-Path $projectRoot 'artifacts/Locaptio-x64.msix'
if (!(Test-Path -LiteralPath $package)) { throw 'Run scripts/build-msix.ps1 first.' }
$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=Locaptio' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(7) } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (!$certificate) {
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Locaptio' -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(2) -HashAlgorithm SHA256
}
$tools = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools/10.0.28000.2705/bin/10.0.28000.0/x64'
& (Join-Path $tools 'signtool.exe') sign /fd SHA256 /sha1 $certificate.Thumbprint $package
if ($LASTEXITCODE) { throw 'MSIX signing failed' }
$publicCertificate = Join-Path $projectRoot 'artifacts/Locaptio-development.cer'
$null = Export-Certificate -Cert $certificate -FilePath $publicCertificate
Write-Output "Development-signed MSIX: $package"
Write-Output "Public certificate: $publicCertificate"
Write-Output 'Certificate trust is not modified. Install the public certificate in Local Machine / Trusted People for local testing.'
