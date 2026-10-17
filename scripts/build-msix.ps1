param([switch]$Sign, [string]$CertificateThumbprint, [switch]$NoBuild, [string]$BuildOutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$buildTools = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools/10.0.28000.2705/bin/10.0.28000.0/x64'
$artifacts = Join-Path $projectRoot 'artifacts'
$stage = Join-Path $artifacts ('msix-staging-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$null = New-Item -ItemType Directory -Path $stage -Force
Push-Location $projectRoot
try {
    $publishArguments = @('publish','src/WinUI/Locaptio.WinUI.csproj','-c','Release','-p:Platform=x64','-o',$stage)
    if ($NoBuild) { $publishArguments += '--no-build' }
    if ($BuildOutputDirectory) { $publishArguments += ('-p:OutDir=' + [System.IO.Path]::GetFullPath($BuildOutputDirectory).TrimEnd([char[]]'\/') + '/') }
    dotnet @publishArguments
    if ($LASTEXITCODE) { throw 'WinUI publish failed' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src/WinUI/Assets') -Destination $stage -Recurse -Force
    foreach ($logo in @('Square150x150Logo','Square44x44Logo','Wide310x150Logo','SplashScreen')) {
        Copy-Item -LiteralPath (Join-Path $stage "Assets/$logo.scale-200.png") -Destination (Join-Path $stage "Assets/$logo.png") -Force
    }
    [xml]$manifest = Get-Content src/WinUI/Package.appxmanifest -Raw
    $manifest.Package.Identity.Name = 'Locaptio'
    $manifest.Package.Properties.DisplayName = 'Locaptio'
    $manifest.Package.Resources.Resource.Language = 'en-us'
    $application = $manifest.Package.Applications.Application
    $application.Executable = 'Locaptio.exe'
    $application.EntryPoint = 'Windows.FullTrustApplication'
    $application.VisualElements.DisplayName = 'Locaptio'
    $application.VisualElements.Description = 'Offline English-Japanese captions and PC audio recording'
    $manifest.Package.Identity.SetAttribute('ProcessorArchitecture', 'x64')
    $manifest.Save((Join-Path $stage 'AppxManifest.xml'))
    $package = Join-Path $artifacts 'Locaptio-x64.msix'
    & (Join-Path $buildTools 'makeappx.exe') pack /d $stage /p $package /o > (Join-Path $artifacts 'makeappx.log') 2>&1
    if ($LASTEXITCODE) { Get-Content (Join-Path $artifacts 'makeappx.log') -Tail 15; throw 'MSIX packaging failed' }
    if ($Sign) {
        if (!$CertificateThumbprint) { throw 'Specify a code-signing certificate thumbprint with publisher CN=Locaptio' }
        & (Join-Path $buildTools 'signtool.exe') sign /fd SHA256 /sha1 $CertificateThumbprint $package
        if ($LASTEXITCODE) { throw 'MSIX signing failed' }
    }
    Write-Output "MSIX: $package"
    Write-Output "Registration directory: $stage"
} finally { Pop-Location }
