param([string]$BuildOutputDirectory = 'artifacts/portable-build', [string]$Destination = 'artifacts/distribution', [switch]$NoBuild,
    # Sign Locaptio's own executables with the self-signed CN=Locaptio certificate (scripts/sign-files.ps1).
    [switch]$Sign)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
function FullPath([string]$path) {
    if ([IO.Path]::IsPathRooted($path)) { return [IO.Path]::GetFullPath($path) }
    return [IO.Path]::GetFullPath((Join-Path $projectRoot $path))
}
$build = FullPath $BuildOutputDirectory
$distribution = FullPath $Destination
[xml]$project = Get-Content (Join-Path $projectRoot 'src/WinUI/Locaptio.WinUI.csproj') -Raw -Encoding UTF8
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if (-not $version) { throw 'Project version missing' }
Push-Location $projectRoot
try {
    if (-not $NoBuild) {
        dotnet build src/WinUI/Locaptio.WinUI.csproj -c Release -p:Platform=x64 "-p:OutDir=$build/" -p:ContinuousIntegrationBuild=true "-p:PathMap=$projectRoot=/_/Locaptio"
        if ($LASTEXITCODE) { throw 'Portable build failed' }
    }
    foreach ($required in @('Locaptio.exe','Locaptio.dll','Locaptio.Core.dll','Assets/Locaptio.ico','bridge/Locaptio.Bridge.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $build $required))) { throw "Missing build output: $required" }
    }
    $null = New-Item -ItemType Directory -Path $distribution -Force
    $stage = Join-Path $distribution ('staging-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    $app = Join-Path $stage 'Locaptio'
    $null = New-Item -ItemType Directory -Path $app -Force
    foreach ($file in Get-ChildItem -LiteralPath $build -File -Recurse) {
        if ($file.Extension -eq '.pdb') { continue }
        $relative = $file.FullName.Substring($build.TrimEnd([char[]]'\/').Length).TrimStart([char[]]'\/')
        $target = Join-Path $app $relative
        $null = New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    foreach ($file in @('README.md','README.en.md','LICENSE','THIRD_PARTY_NOTICES.md','CHANGELOG.md','Launch.cmd')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $app
    }
    foreach ($folder in @('inference','docs','scripts')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File) {
            if ($file.Name -eq 'validation.md') { continue }
            if ($folder -eq 'scripts' -and $file.Name -notlike 'setup*.ps1') { continue }
            $target = Join-Path $app "$folder/$($file.Name)"
            $null = New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }
    $iconFolder = Join-Path $app 'src/WinUI/Assets'
    $null = New-Item -ItemType Directory -Path $iconFolder -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src/WinUI/Assets/Locaptio.png') -Destination $iconFolder
    # Include actual license/notice texts from the resolved NuGet packages and runtime packs.
    $packages = @{}
    foreach ($assetsPath in @('src/WinUI/obj/project.assets.json','src/Locaptio.Bridge/obj/project.assets.json')) {
        $assets = Get-Content (Join-Path $projectRoot $assetsPath) -Raw -Encoding UTF8 | ConvertFrom-Json
        $packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
        $paths = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Value.path })
        foreach ($framework in $assets.project.frameworks.PSObject.Properties.Value) {
            foreach ($dependency in $framework.downloadDependencies) {
                $packageVersion = ($dependency.version.Trim([char[]]'[]').Split(',')[0]).Trim()
                $paths += $dependency.name.ToLowerInvariant() + '/' + $packageVersion
            }
        }
        foreach ($path in $paths) {
            foreach ($cache in $packageFolders) {
                $folder = Join-Path $cache $path
                if (Test-Path -LiteralPath $folder) { $packages[$path] = $folder; break }
            }
        }
    }
    foreach ($entry in $packages.GetEnumerator()) {
        $notices = @(Get-ChildItem -LiteralPath $entry.Value -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY|COPYRIGHT)' })
        if ($notices.Count -eq 0) { continue }
        $legalFolder = Join-Path $app ('licenses/' + $entry.Key.Replace('/','-'))
        $null = New-Item -ItemType Directory -Path $legalFolder -Force
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $legalFolder }
    }
    # Screen recording (ScreenRecorderLib, C++/CLI) needs the Visual C++ runtime. Ship it app-local
    # (Microsoft's documented "local deployment") so recording works on PCs without the redistributable.
    $runtime = @('msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll','concrt140.dll','vcomp140.dll')
    # Visual Studio's redist keeps OpenMP (vcomp140) in its own folder next to the CRT.
    $redist = @(Get-ChildItem -Path "${env:ProgramFiles}\Microsoft Visual Studio\*\*\VC\Redist\MSVC\*\x64",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\*\*\VC\Redist\MSVC\*\x64" -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path -Path (Join-Path $_.FullName 'Microsoft.VC14*.CRT') } | Sort-Object FullName -Descending | Select-Object -First 1)
    $folders = @(if ($redist.Count -gt 0) { @(Get-ChildItem -LiteralPath $redist[0].FullName -Directory | Where-Object Name -match '^Microsoft\.VC14\d*\.(CRT|OpenMP)$' | ForEach-Object FullName) })
    $folders += Join-Path $env:SystemRoot 'System32'
    $used = @()
    foreach ($name in $runtime) {
        $source = @($folders | ForEach-Object { Join-Path $_ $name } | Where-Object { Test-Path -LiteralPath $_ })[0]
        if (-not $source) { $source = Join-Path $folders[0] $name }
        if (-not (Test-Path -LiteralPath $source)) { throw "Visual C++ runtime file not found: $source. Install the Visual C++ Redistributable (x64) or Visual Studio Build Tools." }
        $signature = Get-AuthenticodeSignature -LiteralPath $source
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw "Visual C++ runtime file is not signed by Microsoft: $source" }
        if ([version](Get-Item -LiteralPath $source).VersionInfo.FileVersion -lt [version]'14.30') { throw "Visual C++ runtime is too old for ScreenRecorderLib: $source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $app $name) -Force; $used += Split-Path $source -Parent
    }
    Write-Output ("Visual C++ runtime (app-local) from: " + (($used | Select-Object -Unique) -join ", "))
    if ($Sign) {
        $own = @(Get-ChildItem -LiteralPath $app -Recurse -File | Where-Object Name -match '^Locaptio.*\.(exe|dll)$' | ForEach-Object FullName)
        & (Join-Path $PSScriptRoot 'sign-files.ps1') -Path $own
        if (-not $?) { throw 'Signing failed' }
    }
    $archive = Join-Path $distribution "Locaptio-$version-win-x64.zip"
    Compress-Archive -LiteralPath $app -DestinationPath $archive -CompressionLevel Optimal -Force
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($archive))" | Set-Content (Join-Path $distribution 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Portable folder: $app"
    Write-Output "Release archive: $archive"
    Write-Output "SHA256: $hash"
} finally { Pop-Location }
