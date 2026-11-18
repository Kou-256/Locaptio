. (Join-Path $PSScriptRoot 'setup-common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (-not (Test-Path models/fugumt-source/pytorch_model.bin)) { throw '先にscripts/setup.ps1でモデルを準備してください' }
    $python = Initialize-LocaptioEnvironment -Root $projectRoot -Name '.venv-dml'
    uv pip install --python $python -r inference/requirements-directml.txt
    if ($LASTEXITCODE) { throw 'Dependencies failed' }
    & $python inference/export_onnx.py
    if ($LASTEXITCODE) { throw 'ONNX export failed' }
    & $python inference/smoke_directml.py
    if ($LASTEXITCODE) { throw 'DirectML verification failed' }
} finally { Pop-Location }
