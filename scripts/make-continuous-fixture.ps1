# Synthetic Japanese speech with no sentence-ending pauses until the end.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $synth.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::NotSet, [System.Speech.Synthesis.VoiceAge]::NotSet, 0, [System.Globalization.CultureInfo]::GetCultureInfo('ja-JP'))
    $synth.Rate = 1
    $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
    $synth.SetOutputToWaveFile((Join-Path $projectRoot 'artifacts/test-ja-continuous.wav'), $format)
    $synth.Speak('今日は音声認識の表示速度について説明します、話している途中でも字幕を素早く更新することが大切です、大きなモデルは認識精度が高い一方で処理に時間がかかります、そこで軽いモデルで先に表示してから正確な文章に直します、字幕の行間や文字の色は設定で変更できます、録音した音声と文字起こしは別々の場所に保存できます、ここまでが連続した音声のテストです。')
} finally { $synth.Dispose() }
