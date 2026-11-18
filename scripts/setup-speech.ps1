param([ValidateSet('tiny','base','small','medium','large-v3-turbo')][string]$Model = 'small', [switch]$Translation)
. (Join-Path $PSScriptRoot 'setup-common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $python = Initialize-LocaptioEnvironment -Root $projectRoot -Name '.venv'
    uv pip install --python $python -r inference/requirements.txt
    if ($LASTEXITCODE) { throw '音声認識の依存関係をインストールできませんでした。ネット接続と空き容量を確認してください。' }
    # Installing large packages can take minutes; security software may scan or remove files meanwhile.
    if (-not (Test-LocaptioPython $python)) { $python = Initialize-LocaptioEnvironment -Root $projectRoot -Name '.venv' }
    & $python inference/setup_speech.py --model $Model
    if ($LASTEXITCODE) { throw '音声認識モデルを準備できませんでした' }
    if ($Translation -and -not (Test-Path 'models/translation/model.bin')) {
        & $python inference/setup_models.py
        if ($LASTEXITCODE) { throw 'Translation preparation failed' }
    }
    Set-Content -LiteralPath '.venv/.locaptio-speech-v2' -Value 'ready' -Encoding utf8
} finally { Pop-Location }
