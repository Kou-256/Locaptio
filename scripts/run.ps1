$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try { dotnet run --project src/WinUI/Locaptio.WinUI.csproj -c Release -p:Platform=x64 } finally { Pop-Location }
