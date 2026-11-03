param(
    [switch]$PrepareModel, [string]$WaveFile, [switch]$NoBuild,
    [switch]$PreloadModelLibraries,
    [ValidateSet('2.5.4-experimental', '2.4.1-experimental')]
    [string]$SdkVersion = '2.5.4-experimental'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$legacy = $SdkVersion -eq '2.4.1-experimental'
if ($PreloadModelLibraries -and -not $legacy) { throw '-PreloadModelLibraries はSDK 2.4.1-experimental用の検証回避策です。' }
$folder = if ($legacy) { 'speech-api-probe-24' } else { 'speech-api-probe' }
$packageName = if ($legacy) { 'Locaptio.SpeechApiProbe.Legacy24' } else { 'Locaptio.SpeechApiProbe' }
$probeRoot = Join-Path $projectRoot "artifacts/$folder"
$reportFile = Join-Path $projectRoot "artifacts/$folder-packaged.json"
Push-Location $projectRoot
try {
    if ($WaveFile -and -not (Test-Path -LiteralPath $WaveFile -PathType Leaf)) { throw '指定した音声ファイルがありません。マイク録音への切替は行いません。' }
    if (-not $NoBuild) {
        dotnet build src/SpeechApiProbe/Locaptio.SpeechApiProbe.csproj -c Release -p:Platform=x64 "-p:ProbeSdkVersion=$SdkVersion" "-p:OutDir=$probeRoot/"
        if ($LASTEXITCODE) { throw 'Speech API検証アプリのビルドに失敗しました' }
    }
    $null = New-Item -ItemType Directory -Path (Join-Path $probeRoot 'Assets') -Force
    [xml]$manifest = Get-Content -LiteralPath src/SpeechApiProbe/AppxManifest.xml -Raw
    $manifest.Package.Identity.Name = $packageName
    $manifest.Save((Join-Path $probeRoot 'AppxManifest.xml'))
    Copy-Item -LiteralPath src/WinUI/Assets/Square150x150Logo.scale-200.png -Destination (Join-Path $probeRoot 'Assets/Square150x150Logo.png') -Force
    Copy-Item -LiteralPath src/WinUI/Assets/Square44x44Logo.scale-200.png -Destination (Join-Path $probeRoot 'Assets/Square44x44Logo.png') -Force
    $package = Get-AppxPackage -Name $packageName
    if (-not $package) {
        # This deliberately does not enable Developer Mode, install certificates,
        # alter policy or trust unsigned packages by bypassing Windows checks.
        Add-AppxPackage -Register (Join-Path $probeRoot 'AppxManifest.xml')
        $package = Get-AppxPackage -Name $packageName
    }
    if (-not $package) { throw '検証パッケージを登録できませんでした。Windowsの開発者モード／サイドローディング要件を確認してください。' }
    if ([IO.Path]::GetFullPath($package.InstallLocation).TrimEnd('\') -ne [IO.Path]::GetFullPath($probeRoot).TrimEnd('\')) {
        throw "別の場所に同名の検証パッケージが登録されています: $($package.InstallLocation)"
    }
    $parameters = '--output "' + $reportFile + '"'
    if (-not $PrepareModel -and -not $WaveFile) { $parameters += ' --status-only' }
    if ($PrepareModel) { $parameters += ' --prepare' }
    if ($PreloadModelLibraries) {
        # Resolve the installed Microsoft model package; never copy its binaries
        # into our app or modify WindowsApps permissions.
        $modelPackage = Get-AppxPackage -Name Microsoft.Windows.AI.Speech.Preview.CPU
        if (-not $modelPackage) { throw 'CPU用Speechモデルがありません。まず -PrepareModel で準備してください。' }
        $nativeDir = Join-Path $modelPackage.InstallLocation 'winml'
        foreach ($library in @('asrmodelapi.dll', 'cacheapi.dll', 'asrapi.dll', 'vadapi.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $nativeDir $library) -PathType Leaf)) { throw "モデルのDLLがありません: $library" }
        }
        $parameters += ' --model-library-directory "' + $nativeDir + '"'
    }
    if ($WaveFile) { $parameters += ' --wave "' + [IO.Path]::GetFullPath($WaveFile) + '"' }
    $started = [DateTimeOffset]::UtcNow
    Invoke-CommandInDesktopPackage -PackageFamilyName $package.PackageFamilyName -AppId 'Probe' -Command (Join-Path $probeRoot 'Locaptio.SpeechApiProbe.exe') -Args $parameters -PreventBreakaway
    for ($i = 0; $i -lt 120; $i++) {
        if (Test-Path -LiteralPath $reportFile) {
            try {
                $report = Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
                if ([DateTimeOffset]$report.startedUtc -ge $started.AddSeconds(-1) -and $report.finishedUtc) { $report | ConvertTo-Json -Depth 8; return }
            } catch { } # The writer may be replacing its status file.
        }
        Start-Sleep -Milliseconds 500
    }
    Write-Output "検証はまだ終了していません。現在の状態: $reportFile"
} finally { Pop-Location }
