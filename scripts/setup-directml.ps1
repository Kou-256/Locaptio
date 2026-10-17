$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (-not (Test-Path models/fugumt-source/pytorch_model.bin)) { throw '先にscripts/setup.ps1でモデルを準備してください' }
    uv venv --python 3.12 .venv-dml
    if ($LASTEXITCODE) { throw 'Python environment failed' }
    uv pip install --python .venv-dml/Scripts/python.exe -r inference/requirements-directml.txt
    if ($LASTEXITCODE) { throw 'Dependencies failed' }
    & .venv-dml/Scripts/python.exe inference/export_onnx.py
    if ($LASTEXITCODE) { throw 'ONNX export failed' }
    & .venv-dml/Scripts/python.exe inference/smoke_directml.py
    if ($LASTEXITCODE) { throw 'DirectML verification failed' }
} finally { Pop-Location }
