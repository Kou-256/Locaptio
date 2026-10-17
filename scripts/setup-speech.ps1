param([ValidateSet('tiny','base','small','medium','large-v3-turbo')][string]$Model = 'small')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    & .venv/Scripts/python.exe inference/setup_speech.py --model $Model
    if ($LASTEXITCODE) { throw '音声認識モデルを準備できませんでした' }
} finally { Pop-Location }
