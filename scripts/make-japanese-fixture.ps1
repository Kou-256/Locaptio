# Local synthetic test audio, not a recording.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$null = New-Item -ItemType Directory -Force (Join-Path $projectRoot 'artifacts')
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $synth.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::NotSet, [System.Speech.Synthesis.VoiceAge]::NotSet, 0, [System.Globalization.CultureInfo]::GetCultureInfo('ja-JP'))
    $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
    $synth.SetOutputToWaveFile((Join-Path $projectRoot 'artifacts/test-ja.wav'), $format)
    $synth.Speak('これは日本語の文字起こしのテストです。会議の内容を正確に記録します。')
} finally { $synth.Dispose() }
