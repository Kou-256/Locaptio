param([ValidateSet('tiny','base','small','medium','large-v3-turbo')][string]$Model = 'small', [switch]$Translation)
. (Join-Path $PSScriptRoot 'setup-common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (-not (Test-Path '.venv/Scripts/python.exe')) {
        uv venv --python (Get-LocaptioPython -Root $projectRoot) .venv
        if ($LASTEXITCODE) { throw 'Python environment creation failed' }
    }
    uv pip install --python .venv/Scripts/python.exe -r inference/requirements.txt
    if ($LASTEXITCODE) { throw 'Speech dependencies installation failed' }
    & .venv/Scripts/python.exe inference/setup_speech.py --model $Model
    if ($LASTEXITCODE) { throw '音声認識モデルを準備できませんでした' }
    if ($Translation -and -not (Test-Path 'models/translation/model.bin')) {
        & .venv/Scripts/python.exe inference/setup_models.py
        if ($LASTEXITCODE) { throw 'Translation preparation failed' }
    }
} finally { Pop-Location }
