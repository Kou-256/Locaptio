param([ValidateSet('vulkan','cpu')][string]$Backend = 'vulkan')
. (Join-Path $PSScriptRoot 'setup-common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (Test-Path 'tools/uv.exe') { $env:PATH = (Join-Path $projectRoot 'tools') + [IO.Path]::PathSeparator + $env:PATH }
    if (-not (Get-Command uv -ErrorAction SilentlyContinue)) { throw 'uvが見つかりません。アプリからAI補正をONにすると自動準備できます。' }
    if (!(Test-Path '.venv-llm/Scripts/python.exe')) { uv venv --python (Get-LocaptioPython -Root $projectRoot) .venv-llm; if ($LASTEXITCODE) { throw 'LLM環境作成に失敗しました' } }
    uv pip install --python .venv-llm/Scripts/python.exe --index "https://abetlen.github.io/llama-cpp-python/whl/$Backend" --index-strategy unsafe-best-match --only-binary llama-cpp-python --reinstall-package llama-cpp-python llama-cpp-python==0.3.36 huggingface-hub==0.36.0
    if ($LASTEXITCODE) { throw 'llama.cpp依存関係の準備に失敗しました' }
    Write-Output 'LOCAPTIO_MODEL_DOWNLOAD'
    & .venv-llm/Scripts/python.exe -u inference/setup_llm.py
    if ($LASTEXITCODE) { throw 'LLMモデルの準備に失敗しました' }
} finally { Pop-Location }
