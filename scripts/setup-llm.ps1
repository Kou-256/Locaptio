param([ValidateSet('vulkan','cpu')][string]$Backend = 'vulkan',
    [ValidatePattern('^Qwen/Qwen3-[0-9.]+B-GGUF$')][string]$Repository = 'Qwen/Qwen3-4B-GGUF',
    [ValidatePattern('^Qwen3-[0-9.]+B-Q[0-9A-Z_]+\.gguf$')][string]$File = 'Qwen3-4B-Q4_K_M.gguf',
    [switch]$Reinstall)
. (Join-Path $PSScriptRoot 'setup-common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (Test-Path 'tools/uv.exe') { $env:PATH = (Join-Path $projectRoot 'tools') + [IO.Path]::PathSeparator + $env:PATH }
    if (-not (Get-Command uv -ErrorAction SilentlyContinue)) { throw 'uvが見つかりません。アプリからAI補正をONにすると自動準備できます。' }
    # llama.cpp needs the Visual C++ runtime; Locaptio ships it next to Locaptio.exe for PCs without the redistributable.
    $env:PATH = $projectRoot + [IO.Path]::PathSeparator + $env:PATH
    $python = Initialize-LocaptioEnvironment -Root $projectRoot -Name '.venv-llm'
    function Install-Llama([string]$Build) {
        # The wheel installs read-only folders (licenses, lib/pkgconfig, …), and uv cannot remove a read-only
        # folder on Windows: clear the flag so an installed llama.cpp can be replaced.
        Get-ChildItem -LiteralPath (Join-Path $projectRoot '.venv-llm/Lib/site-packages') -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Attributes -band [IO.FileAttributes]::ReadOnly } |
            ForEach-Object { $_.Attributes = $_.Attributes -band (-bnot [IO.FileAttributes]::ReadOnly) }
        uv pip install --python $python --index "https://abetlen.github.io/llama-cpp-python/whl/$Build" --index-strategy unsafe-best-match --only-binary llama-cpp-python --reinstall-package llama-cpp-python llama-cpp-python==0.3.36 huggingface-hub==0.36.0
        if ($LASTEXITCODE) { throw 'llama.cpp依存関係の準備に失敗しました。ネット接続と空き容量を確認してください。' }
    }
    function Test-Llama { & $python -I -c 'import llama_cpp' 2>&1 | Out-Host; return $LASTEXITCODE -eq 0 }
    # Downloading another model reuses the installed library (checked without loading its DLLs).
    & $python -I -c "import importlib.util as u, sys; sys.exit(0 if u.find_spec('llama_cpp') and u.find_spec('huggingface_hub') else 1)" 2>&1 | Out-Null
    if ($Reinstall -or $LASTEXITCODE) {
        Install-Llama $Backend
        # Loading the library checks its DLLs. The GPU (Vulkan) build needs vulkan-1.dll from the graphics
        # driver; without it, use the CPU build, which needs no GPU driver.
        if (-not (Test-Llama) -and $Backend -ne 'cpu') {
            Write-Host 'The GPU build of llama.cpp could not be loaded; installing the CPU build.'
            Install-Llama 'cpu'
        }
        if (-not (Test-Llama)) {
            throw ('AIライブラリ（llama.cpp）を読み込めません。Microsoft の「Visual C++ 再頒布可能パッケージ (x64)」をインストールしてから、もう一度お試しください: https://aka.ms/vs/17/release/vc_redist.x64.exe')
        }
    }
    Write-Output 'LOCAPTIO_MODEL_DOWNLOAD'
    if (-not (Test-LocaptioPython $python)) { $python = Initialize-LocaptioEnvironment -Root $projectRoot -Name '.venv-llm' }
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot "models/llm/$File"))) {
        & $python -u inference/setup_llm.py --repo $Repository --file $File
        if ($LASTEXITCODE) { throw 'LLMモデルの準備に失敗しました' }
    }
} finally { Pop-Location }
