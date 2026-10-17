$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $synth.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::NotSet, [System.Speech.Synthesis.VoiceAge]::NotSet, 0, [System.Globalization.CultureInfo]::GetCultureInfo('en-US'))
    $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
    $synth.SetOutputToWaveFile((Join-Path $projectRoot 'artifacts/test-translation.wav'), $format)
    $synth.Speak("If you are a videographer, think of thirty two bit float like shooting in RAW. You are not necessarily getting a more detailed image. You are getting more latitude to work with in post. This is great when your subject suddenly gets loud. It gives you extra headroom and protects the recording from clipping.")
} finally { $synth.Dispose() }
