<#
Signs Locaptio's own executables with the self-signed "CN=Locaptio" code-signing certificate in the current
user's certificate store (created on first use, valid 3 years, private key not exportable), adds an RFC 3161
timestamp so signatures stay valid after the certificate expires, and exports the public certificate.

A self-signed signature shows who built the files and detects tampering, but Windows does not trust it by
itself: SmartScreen and "Unknown publisher" stay until a PC trusts the certificate (see docs/code-signing.md).
Files that are already signed by someone else (Microsoft's runtime, NuGet packages) are left as they are.
#>
param([Parameter(Mandatory)][string[]]$Path, [switch]$NoTimestamp)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent

$signtool = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools') -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw 'signtool.exe was not found. Build the WinUI project once so the Windows SDK build tools are restored.' }

$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=Locaptio' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) -and
    ($_.EnhancedKeyUsageList | Where-Object ObjectId -eq '1.3.6.1.5.5.7.3.3') } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $certificate) {
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Locaptio' -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(3) -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable
    Write-Output "Created self-signed code-signing certificate CN=Locaptio ($($certificate.Thumbprint))"
}

$files = foreach ($item in $Path) { Get-Item -LiteralPath $item }
$signed = 0
foreach ($file in $files) {
    $current = Get-AuthenticodeSignature -LiteralPath $file.FullName
    # Keep signatures from other publishers (Microsoft runtime, .NET, Windows App SDK).
    if ($current.SignerCertificate -and $current.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -and $current.Status -ne 'NotSigned') { continue }
    $arguments = @('sign', '/fd', 'SHA256', '/sha1', $certificate.Thumbprint, '/d', 'Locaptio')
    if (-not $NoTimestamp) { $arguments += @('/tr', 'http://timestamp.digicert.com', '/td', 'SHA256') }
    & $signtool.FullName @arguments $file.FullName | Out-Null
    if ($LASTEXITCODE) { throw "Signing failed: $($file.FullName)" }
    $signed++
}
$public = Join-Path $projectRoot 'artifacts/Locaptio-codesign.cer'
$null = New-Item -ItemType Directory -Path (Split-Path $public -Parent) -Force
$null = Export-Certificate -Cert $certificate -FilePath $public
Write-Output "Signed $signed file(s) with CN=Locaptio ($($certificate.Thumbprint)); public certificate: $public"
