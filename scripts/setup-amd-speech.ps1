. (Join-Path $PSScriptRoot 'setup-common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $python = Initialize-LocaptioEnvironment -Root $projectRoot -Name '.venv-winml'
    uv pip install --python $python -r inference/requirements-windowsml.txt
    if ($LASTEXITCODE) { throw 'Windows ML環境の準備に失敗しました' }
    & $python inference/stage_amd_ep.py
    if ($LASTEXITCODE) { throw 'AMD EPの準備に失敗しました' }
    & (Join-Path $projectRoot '.venv\Scripts\python.exe') inference/setup_amd_speech.py
    if ($LASTEXITCODE) { throw 'AMD音声モデルの準備に失敗しました' }
    foreach ($part in @('encoder','decoder')) {
        Invoke-WebRequest -Uri "https://raw.githubusercontent.com/amd/RyzenAI-SW/main/Demos/ASR/Whisper/config/vitisai_config_whisper_$part.json" -OutFile "models/speech-amd-npu/vitisai_config_whisper_$part.json"
    }
    & $python -u inference/amd_speech_worker.py --models models --language ja --prepare-only
    if ($LASTEXITCODE) { throw 'AMD NPUの初期コンパイルに失敗しました。ログとWindows ML EPを確認してください。' }
} finally { Pop-Location }
