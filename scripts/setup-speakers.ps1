. (Join-Path $PSScriptRoot 'setup-common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (!(Test-Path '.venv-speakers/Scripts/python.exe')) {
        uv venv --python (Get-LocaptioPython -Root $projectRoot) .venv-speakers
        if ($LASTEXITCODE) { throw '話者識別環境の作成に失敗しました' }
    }
    uv pip install --python .venv-speakers/Scripts/python.exe sherpa-onnx==2.0.1 sherpa-onnx-core==2.0.1 numpy==2.2.6
    if ($LASTEXITCODE) { throw '話者識別の依存関係を準備できませんでした' }
    & .venv-speakers/Scripts/python.exe inference/setup_speakers.py
    if ($LASTEXITCODE) { throw '話者識別モデルを準備できませんでした' }
} finally { Pop-Location }
