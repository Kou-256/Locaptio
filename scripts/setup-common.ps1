# Windows PowerShell otherwise decodes native UTF-8 errors using the console code page.
$utf8 = New-Object System.Text.UTF8Encoding $false
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONUTF8 = '1'
$env:UV_PYTHON_NO_REGISTRY = '1'
$env:UV_PYTHON_INSTALL_DIR = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools/python'
$env:UV_LINK_MODE = 'copy'

function Get-LocaptioPython([string]$Root) {
    # Use a versioned installation directly. uv's minor-version junction can fail
    # Windows' trusted-mount-point check (error 448), even when its target works.
    $private = Join-Path $Root 'tools/python'
    foreach ($directory in @($private, (Join-Path $env:APPDATA 'uv/python'))) {
        if (-not (Test-Path -LiteralPath $directory)) { continue }
        $versions = Get-ChildItem -LiteralPath $directory -Directory -Filter 'cpython-3.12.*-windows-x86_64-none' |
            Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) } | Sort-Object Name -Descending
        foreach ($version in $versions) {
            $candidate = Join-Path $version.FullName 'python.exe'
            if (-not (Test-Path -LiteralPath $candidate)) { continue }
            try {
                & $candidate -I -c 'import sys; assert sys.version_info[:2] == (3, 12)'
                if ($LASTEXITCODE -eq 0) { return $candidate }
            } catch { Write-Verbose 'Existing Python could not be used; preparing a private installation.' }
        }
    }
    # Only this process uses the private location; no PATH/registry/security settings change.
    & uv python install 3.12.13 --install-dir $private --no-bin --no-registry | Out-Host
    if ($LASTEXITCODE) { throw 'Python download failed. Check the connection and free disk space.' }
    $python = Join-Path $private 'cpython-3.12.13-windows-x86_64-none/python.exe'
    if (-not (Test-Path -LiteralPath $python)) { throw 'The downloaded Python executable was not found.' }
    return $python
}

function Test-LocaptioPython([string]$Python) {
    if (-not (Test-Path -LiteralPath $Python)) { return $false }
    try { & $Python -I -c 'import sys' 2>&1 | Out-Null; return $LASTEXITCODE -eq 0 } catch { return $false }
}

# Creates or repairs a Python environment and returns the full path of its python.exe, checked to run.
function Initialize-LocaptioEnvironment([string]$Root, [string]$Name) {
    $python = Get-LocaptioPython -Root $Root
    $environment = Join-Path $Root $Name
    # Repair existing environments too: v1.0.42 could leave home pointing at
    # uv's minor-version junction. Keep installed packages and all user data.
    & uv venv --allow-existing --python $python $environment | Out-Host
    if ($LASTEXITCODE) { throw 'Python環境の作成または修復に失敗しました。' }
    $config = Join-Path $environment 'pyvenv.cfg'
    $pythonHome = Split-Path $python -Parent
    $lines = [IO.File]::ReadAllLines($config)
    $lines = @($lines | ForEach-Object { if ($_ -match '^home\s*=') { 'home = ' + $pythonHome } else { $_ } })
    [IO.File]::WriteAllLines($config, [string[]]$lines, (New-Object System.Text.UTF8Encoding $false))
    $environmentPython = Join-Path $environment 'Scripts\python.exe'
    if (-not (Test-LocaptioPython $environmentPython)) {
        # Security software can remove or block the launcher uv places in the environment.
        # Python's own venv module writes a fresh launcher; installed packages stay.
        Write-Host "python.exe in $Name is missing or blocked; recreating it with Python's venv launcher."
        & $python -m venv --without-pip --upgrade $environment | Out-Host
    }
    if (-not (Test-LocaptioPython $environmentPython)) {
        $state = if (Test-Path -LiteralPath $environmentPython) { '実行がブロックされています' } else { 'ファイルが削除されています' }
        throw ("Python（$environmentPython）を実行できません（$state）。ウイルス対策ソフトが削除・ブロックした可能性があります。" +
            "Windows セキュリティの「保護の履歴」を確認し、Locaptio のフォルダー（$Root）を除外に追加するか、ダウンロード フォルダー以外に置いてから、もう一度お試しください。")
    }
    return $environmentPython
}
