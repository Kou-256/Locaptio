param([Parameter(Mandatory)][ValidateSet('ja-en','multi')][string]$Pack)
. (Join-Path $PSScriptRoot 'setup-common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $env:PATH = $projectRoot + [IO.Path]::PathSeparator + $env:PATH
    # Translation uses the speech environment (ctranslate2, transformers, sentencepiece).
    $python = Initialize-LocaptioEnvironment -Root $projectRoot -Name '.venv'
    uv pip install --python $python -r inference/requirements.txt
    if ($LASTEXITCODE) { throw '翻訳の依存関係をインストールできませんでした。ネット接続と空き容量を確認してください。' }
    if (-not (Test-LocaptioPython $python)) { $python = Initialize-LocaptioEnvironment -Root $projectRoot -Name '.venv' }
    & $python -u inference/setup_translation.py --pack $Pack
    if ($LASTEXITCODE) { throw '翻訳モデルを準備できませんでした' }
} finally { Pop-Location }
