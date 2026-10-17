$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (!(Test-Path '.venv-winml/Scripts/python.exe')) {
        uv venv --python 3.12 .venv-winml
        if ($LASTEXITCODE) { throw 'NPU用Python環境の作成に失敗しました' }
    }
    uv pip install --python .venv-winml/Scripts/python.exe -r inference/requirements-windowsml.txt
    if ($LASTEXITCODE) { throw 'Windows ML依存関係の準備に失敗しました' }
    & .venv-winml/Scripts/python.exe inference/prepare_npu_model.py
    if ($LASTEXITCODE) { throw 'NPU用モデルの準備に失敗しました。setup-directml.ps1でONNXモデルを先に準備してください。' }
    # This explicit setup may download compatible NPU execution providers.
    & .venv-winml/Scripts/python.exe inference/setup_npu.py
    if ($LASTEXITCODE) { throw 'NPU EPを準備できませんでした。ドライバー・Windows App Runtimeを確認してください。' }
} finally { Pop-Location }
