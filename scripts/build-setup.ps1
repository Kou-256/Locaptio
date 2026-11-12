param([Parameter(Mandatory)][string]$AppSource, [string]$Destination = 'artifacts/setup', [string]$Compiler = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$source = (Resolve-Path -LiteralPath $AppSource).Path
if (-not (Test-Path -LiteralPath (Join-Path $source 'Locaptio.exe'))) { throw 'Prepared distribution is missing Locaptio.exe' }
if (!$Compiler) {
    $Compiler = @((Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'), (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$Compiler) { throw 'Install Inno Setup 6 from https://jrsoftware.org/isdl.php or pass -Compiler.' }
if (![IO.Path]::IsPathRooted($Destination)) { $Destination = Join-Path $projectRoot $Destination }
$null = New-Item -ItemType Directory -Path $Destination -Force
[xml]$project = Get-Content (Join-Path $projectRoot 'src/WinUI/Locaptio.WinUI.csproj') -Raw
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
& $Compiler "/DAppSource=$source" "/DAppVersion=$version" "/O$Destination" (Join-Path $projectRoot 'installer/Locaptio.iss')
if ($LASTEXITCODE) { throw 'Setup compilation failed' }
$setup = Join-Path $Destination 'Locaptio-Setup-x64.exe'
"$((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant())  Locaptio-Setup-x64.exe" | Set-Content (Join-Path $Destination 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Setup: $setup"
