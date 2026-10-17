param([ValidateSet('tiny.en','base.en','small.en')][string]$Speech = 'tiny.en', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (-not (Get-Command uv -ErrorAction SilentlyContinue)) { throw 'uv is required: https://docs.astral.sh/uv/getting-started/installation/' }
    if (-not (Test-Path '.venv/Scripts/python.exe')) {
        uv venv --python 3.12 .venv
        if ($LASTEXITCODE) { throw 'Python環境作成に失敗しました' }
    }
    uv pip install --python .venv/Scripts/python.exe -r inference/requirements.txt
    if ($LASTEXITCODE) { throw '依存関係インストールに失敗しました' }
    & .venv/Scripts/python.exe inference/setup_models.py --speech $Speech
    if ($LASTEXITCODE) { throw 'モデル準備に失敗しました' }
    & .venv/Scripts/python.exe inference/setup_speech.py --model small
    if ($LASTEXITCODE) { throw '日本語対応モデルの準備に失敗しました' }
    & .venv/Scripts/python.exe inference/setup_speech.py --model base
    if ($LASTEXITCODE) { throw '高速文字起こしモデルの準備に失敗しました' }
    if (-not $SkipBuild) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Building requires .NET SDK. Portable ZIP users can rerun with -SkipBuild.' }
    dotnet build src/App/Locaptio.Legacy.csproj -c Release
    if ($LASTEXITCODE) { throw 'ビルドに失敗しました' }
    dotnet build src/WinUI/Locaptio.WinUI.csproj -c Release -p:Platform=x64
    if ($LASTEXITCODE) { throw 'WinUI 3ビルドに失敗しました' }
    }
} finally { Pop-Location }
