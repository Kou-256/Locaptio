. (Join-Path $PSScriptRoot 'setup-common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $python = Initialize-LocaptioEnvironment -Root $projectRoot -Name '.venv-speakers'
    uv pip install --python $python sherpa-onnx==2.0.1 sherpa-onnx-core==2.0.1 numpy==2.2.6
    if ($LASTEXITCODE) { throw '話者識別の依存関係を準備できませんでした' }
    & $python inference/setup_speakers.py
    if ($LASTEXITCODE) { throw '話者識別モデルを準備できませんでした' }
} finally { Pop-Location }
