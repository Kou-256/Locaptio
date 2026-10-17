param([string]$Source)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetFolder = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/WinUI/Assets'
if ($Source) { Copy-Item -LiteralPath $Source -Destination (Join-Path $assetFolder 'Locaptio.png') -Force }
$original = [Drawing.Image]::FromFile((Join-Path $assetFolder 'Locaptio.png'))
function IconPng([int]$width, [int]$height, [int]$size) {
    $bitmap = New-Object Drawing.Bitmap($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $ratio = $size / [Math]::Max($original.Width, $original.Height)
        $renderWidth = [int][Math]::Round($original.Width * $ratio)
        $renderHeight = [int][Math]::Round($original.Height * $ratio)
        $graphics.DrawImage($original, [Drawing.Rectangle]::new([int](($width-$renderWidth)/2), [int](($height-$renderHeight)/2), $renderWidth, $renderHeight))
        $stream = New-Object IO.MemoryStream
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    } finally { $graphics.Dispose(); $bitmap.Dispose(); if ($stream) { $stream.Dispose() } }
}
try {
    foreach ($entry in @(@('Square150x150Logo.scale-200.png',300,300,300),@('Square44x44Logo.scale-200.png',88,88,88),@('Square44x44Logo.targetsize-24_altform-unplated.png',24,24,24),@('Square44x44Logo.targetsize-48_altform-lightunplated.png',48,48,48),@('StoreLogo.png',50,50,50),@('LockScreenLogo.scale-200.png',48,48,48),@('Wide310x150Logo.scale-200.png',620,300,300),@('SplashScreen.scale-200.png',1240,600,400))) {
        [IO.File]::WriteAllBytes((Join-Path $assetFolder $entry[0]), (IconPng $entry[1] $entry[2] $entry[3]))
    }
    $sizes = @(16,24,32,48,64,128,256)
    $images = @($sizes | ForEach-Object { ,(IconPng $_ $_ $_) })
    $stream = [IO.File]::Create((Join-Path $assetFolder 'Locaptio.ico'))
    $writer = New-Object IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i=0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
    } finally { $writer.Dispose(); $stream.Dispose() }
} finally { $original.Dispose() }
