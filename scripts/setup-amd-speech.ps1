$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (!(Test-Path '.venv-winml/Scripts/python.exe')) {
        uv venv --python 3.12 .venv-winml
        if ($LASTEXITCODE) { throw 'NPU Python環境の作成に失敗しました' }
    }
    uv pip install --python .venv-winml/Scripts/python.exe -r inference/requirements-windowsml.txt
    if ($LASTEXITCODE) { throw 'Windows ML環境の準備に失敗しました' }
    & .venv-winml/Scripts/python.exe inference/stage_amd_ep.py
    if ($LASTEXITCODE) { throw 'AMD EPの準備に失敗しました' }
    & .venv/Scripts/python.exe inference/setup_amd_speech.py
    if ($LASTEXITCODE) { throw 'AMD音声モデルの準備に失敗しました' }
    foreach ($part in @('encoder','decoder')) {
        Invoke-WebRequest -Uri "https://raw.githubusercontent.com/amd/RyzenAI-SW/main/Demos/ASR/Whisper/config/vitisai_config_whisper_$part.json" -OutFile "models/speech-amd-npu/vitisai_config_whisper_$part.json"
    }
    & .venv-winml/Scripts/python.exe -u inference/amd_speech_worker.py --models models --language ja --prepare-only
    if ($LASTEXITCODE) { throw 'AMD NPUの初期コンパイルに失敗しました。ログとWindows ML EPを確認してください。' }
} finally { Pop-Location }
