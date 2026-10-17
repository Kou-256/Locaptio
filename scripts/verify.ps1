$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    dotnet build
    if ($LASTEXITCODE) { throw 'Build failed' }
    dotnet run --project src/Tests --no-build
    if ($LASTEXITCODE) { throw 'Failure recovery tests failed' }
    dotnet run --project src/Tests --no-build -- --storage
    if ($LASTEXITCODE) { throw 'Text export/recording tests failed' }
    & powershell -ExecutionPolicy Bypass -File scripts/make-fixture.ps1
    if ($LASTEXITCODE) { throw 'Fixture failed' }
    & .venv/Scripts/python.exe inference/smoke.py speech --wav artifacts/test.wav
    if ($LASTEXITCODE) { throw 'STT failed' }
    & .venv/Scripts/python.exe inference/smoke.py translation
    if ($LASTEXITCODE) { throw 'Translation failed' }
    dotnet run --project src/Probe --no-build -- 15 --pipeline --cpu --fixture
    if ($LASTEXITCODE) { throw 'Pipeline failed' }
    dotnet run --project src/App --no-build -- --verify --verify-storage
    if ($LASTEXITCODE) { throw 'Overlay failed' }
} finally { Pop-Location }
