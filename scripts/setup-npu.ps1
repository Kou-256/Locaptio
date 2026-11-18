. (Join-Path $PSScriptRoot 'setup-common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $python = Initialize-LocaptioEnvironment -Root $projectRoot -Name '.venv-winml'
    uv pip install --python $python -r inference/requirements-windowsml.txt
    if ($LASTEXITCODE) { throw 'Windows ML依存関係の準備に失敗しました' }
    & $python inference/prepare_npu_model.py
    if ($LASTEXITCODE) { throw 'NPU用モデルの準備に失敗しました。setup-directml.ps1でONNXモデルを先に準備してください。' }
    # This explicit setup may download compatible NPU execution providers.
    & $python inference/setup_npu.py
    if ($LASTEXITCODE) { throw 'NPU EPを準備できませんでした。ドライバー・Windows App Runtimeを確認してください。' }
} finally { Pop-Location }
