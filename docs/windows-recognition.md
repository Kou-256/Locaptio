# Windows音声認識の選択肢

## 現在のWindows字幕連携

LocaptioはWindowsの字幕ウィンドウから文字を読み取ります。そのため、閉じたり非表示にしたまま認識を利用する方式ではありません。1.0.24の言語同期は既知のWindows UIを操作する機能で、公開された言語変更APIではありません。Windows更新後の互換性には制約があります。言語の初回準備はWindows側で行います。

## Windowsを起動しない方法

既存のWhisper経路は独立して動き、本体のモードから日本語・英語を切り替えます。AMD XDNA 2 NPUを選べますが、Windows字幕と同じモデル・認識結果・速度を保証するものではありません。NPU処理に加えてCPUの前後処理も含みます。準備方法は[モデルの説明](model-selection.md)を参照してください。

## 新しいWindows AI Speech API（今回未実装）

2026-10-27に別アプリでAPI呼び出しとパッケージ登録を試しました。通常版への組み込みではありません。[検証結果と再実行手順](windows-speech-api-trial.md)を参照してください。

Microsoftは画面を介さずアプリ内で認識する[Speech Recognition API](https://learn.microsoft.com/windows/ai/apis/speech-recognition)を公開しています。公開資料ではCopilot+ PCのNPUを自動利用します。ただし現在は実験版で、対応する実験版Windows App SDK、MSIXとsystemAIModels capabilityなどの要件があります。このポータブル版にはまだ組み込んでいません。Windows字幕と同一のモデル・性能かは別途検証が必要です。

自前Whisperを実行する[Windows MLのASR API](https://learn.microsoft.com/windows/ai/new-windows-ml/runtime/speech-recognition)も別に存在します。こちらも現在は実験版です。Windowsの字幕画面から文字を読む現在の連携とは異なる技術です。
