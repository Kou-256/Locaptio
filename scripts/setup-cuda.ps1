$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$runtime = Join-Path $projectRoot 'runtime/cuda'
$cache = Join-Path $runtime 'downloads'
$bin = Join-Path $runtime 'bin'
$null = New-Item -ItemType Directory -Path $cache,$bin -Force
$cuda = Invoke-RestMethod 'https://developer.download.nvidia.com/compute/cuda/redist/redistrib_12.8.1.json'
$cudnn = Invoke-RestMethod 'https://developer.download.nvidia.com/compute/cudnn/redist/redistrib_9.10.2.json'
$components = @(
    @{ Name='cublas'; Base='https://developer.download.nvidia.com/compute/cuda/redist/'; Item=$cuda.libcublas.'windows-x86_64' },
    @{ Name='cudart'; Base='https://developer.download.nvidia.com/compute/cuda/redist/'; Item=$cuda.cuda_cudart.'windows-x86_64' },
    @{ Name='cudnn'; Base='https://developer.download.nvidia.com/compute/cudnn/redist/'; Item=$cudnn.cudnn.'windows-x86_64'.cuda12 }
)
foreach ($component in $components) {
    $zip = Join-Path $cache ($component.Name + '.zip')
    if (!(Test-Path -LiteralPath $zip) -or (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $component.Item.sha256) {
        Write-Output "Downloading NVIDIA $($component.Name)..."
        Invoke-WebRequest ($component.Base + $component.Item.relative_path) -OutFile $zip
    }
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $component.Item.sha256) { throw 'NVIDIA archive checksum mismatch' }
    $expanded = Join-Path $runtime $component.Name
    Expand-Archive -LiteralPath $zip -DestinationPath $expanded -Force
    Get-ChildItem -LiteralPath $expanded -Recurse -Filter '*.dll' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $bin -Force }
    Write-Output "$($component.Name) ready"
}
$components | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $runtime 'manifest.json') -Encoding utf8
Write-Output 'Local CUDA 12 / cuDNN 9 runtime ready; no driver or system PATH changes.'
