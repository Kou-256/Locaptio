param([string]$BuildOutputDirectory = 'artifacts/portable-build', [string]$Destination = 'artifacts/distribution', [switch]$NoBuild)
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
    $archive = Join-Path $distribution "Locaptio-$version-win-x64.zip"
    Compress-Archive -LiteralPath $app -DestinationPath $archive -CompressionLevel Optimal -Force
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($archive))" | Set-Content (Join-Path $distribution 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Portable folder: $app"
    Write-Output "Release archive: $archive"
    Write-Output "SHA256: $hash"
} finally { Pop-Location }
