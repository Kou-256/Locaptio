param([ValidateSet('vulkan','cpu')][string]$Backend = 'vulkan')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (!(Test-Path '.venv-llm/Scripts/python.exe')) { uv venv --python 3.12 .venv-llm; if ($LASTEXITCODE) { throw 'LLM環境作成に失敗しました' } }
    uv pip install --python .venv-llm/Scripts/python.exe --index "https://abetlen.github.io/llama-cpp-python/whl/$Backend" --index-strategy unsafe-best-match --only-binary llama-cpp-python --reinstall-package llama-cpp-python llama-cpp-python==0.3.36 huggingface-hub==0.36.0
    if ($LASTEXITCODE) { throw 'llama.cpp依存関係の準備に失敗しました' }
    & .venv-llm/Scripts/python.exe inference/setup_llm.py
    if ($LASTEXITCODE) { throw 'LLMモデルの準備に失敗しました' }
} finally { Pop-Location }
