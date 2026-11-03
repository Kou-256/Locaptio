# Windows AI Speech API 検証

検証日: 2026-10-27〜28。通常のLocaptio 1.0.25とは分けた検証アプリです。クラウドSDK・APIキー・課金設定・クラウドへの自動フォールバックは含めません。マイクやPC音声も自動取得せず、指定したローカルWAVだけを対象にできます。

## 費用とローカル処理

[MicrosoftのSpeech Recognition仕様](https://learn.microsoft.com/windows/ai/apis/speech-recognition)では端末内で推論し、認識のためのネットワーク接続やクラウドへの音声送信は不要です。[Microsoft AI Decision Framework](https://microsoft.github.io/Microsoft-AI-Decision-Framework/docs/decision-framework.html)もWindows AI APIsのローカル推論を従量推論費用なしと説明しています。Windows・対応ハードウェア、モデル取得時の通信・保存容量は別途必要です。実験版APIのためMicrosoft Storeへの本番公開には使いません。

## このPCで得られた結果

Windows build 26200、AMD Ryzen AI 9 HX 370で検証しました。初回は登録が`0x80073CFF`で拒否されましたが、ユーザー自身による開発者モードの有効化後、専用パッケージの登録に成功しました。`systemAIModels` capability、MaxVersionTested `10.0.26226.0`、実行時のパッケージ識別を確認済みです。

| SDK | モデル準備 | モデル作成・認識 |
| --- | --- | --- |
| `2.5.4-experimental` | `NotReady`、`EnsureReadyAsync`が`Failure / 0x8000FFFF` | 強制作成も`0x8A1F0228`。日本語＋NPU指定、英語＋CPU指定でも失敗 |
| `2.4.1-experimental` | `Ready`、`EnsureReadyAsync`が`Success` | `TryCreateAsync`がモデルを返さず、内部エラー`0x8007007E`（モジュールが見つからない） |
| `2.4.1-experimental`＋モデルDLLの明示ロード | `Ready`、`Success` | モデル作成と日本語WAVの認識に成功（2026-10-28再起動後の追加検証） |

旧SDKは別名パッケージ`Locaptio.SpeechApiProbe.Legacy24`として登録し、それぞれのSDKのDLLを専用フォルダーで使用しました。同梱`onnxruntime.dll`と`Microsoft.Windows.AI.Speech.dll`自体のロードは成功します。ONNX Runtimeを明示ロードしてからのモデル作成でも同じエラーでした。以下の追加調査でモデルDLLのロード問題を特定し、回避できました。NPU使用率とリアルタイム字幕の表示遅延は未測定です。`Ready`だけを認識成功と判断しません。

### 2026-10-28：旧SDKの失敗原因と回避結果

再起動後も通常実行では同じエラーでした。検証プロセスだけにネイティブデバッガーを接続して出力を取得したところ、`asr_api_model.cpp(346)`で`Failed to load :asrmodelapi`、`0x8007007E`を確認しました。

`Microsoft.Windows.AI.Speech.Preview.CPU`バージョン`1.2607.112.0`はインストール済みで、その`winml/`内に`asrmodelapi.dll`、`cacheapi.dll`、`asrapi.dll`、`vadapi.dll`が存在しました。これらをインストール先の絶対パスから先に読み込むと、モデル作成・認識に成功しました。旧SDKの既定ロード経路では認識用DLLを解決できないことが直接の失敗要因です。SDK内部のどの検索処理／構成が根本原因かまでは確定していません。

回避策は検証アプリ内の`NativeLibrary.Load`のみです。モデルやDLLの再配布、WindowsAppsの権限変更、システムDLLの入れ替えは行いません。Windows MLの明示初期化のみでは解消しませんでした。

日本語の合成音声（16kHz、mono、16bit、6.590秒）で、次の文章を全文正しく認識しました。

> これは日本語の文字起こしのテストです。会議の内容を正確に記録します。

この実行のモデル作成は10.305秒、WAV一括認識は6.744秒でした。初期化時のモデルライブラリの明示ロード等は別途時間を要します。CPU用モデルパッケージでの結果で、NPU実行、連続認識の初回表示速度、一般の会話音声での精度を証明するものではありません。製品版への認識エンジン追加はまだ行っていません。

英語の合成音声（4.855秒）でも`This is a test. We need to improve the performance of this system.`を正しく認識しました（出力先頭に空白あり）。モデル作成10.004秒、一括認識4.887秒でした。両言語とも短いテスト1件であり、精度評価のベンチマークではありません。

Windows側のCBSランタイムは`2.4.1.100`でした。SDKとOS側コンポーネントの互換性／モデル依存関係が候補ですが、原因は断定できません。Windows Updateで提供される通常のOS・AIコンポーネント更新後に再検証するための状態を残しています。検証のためにInsider参加、証明書の信頼追加、ポリシー変更はしていません。製品版Locaptio 1.0.25の認識方式も変更していません。

実行記録（リポジトリ管理対象外の`artifacts/`）:

- `speech-api-probe-packaged.json`: 新SDKの準備失敗
- `speech-api-ja-npu.json`、`speech-api-en-cpu.json`: 言語・デバイス指定での切り分け
- `speech-api-creation-diagnostics.json`: 旧SDKのモデル作成エラー詳細
- `speech-api-preload-diagnostics.json`: DLL明示ロードでも解消しなかった結果
- `speech-api-native-debug.json`: 検証プロセス内の`asrmodelapi`ロード失敗記録
- `speech-api-model-libraries.json`: モデル内DLLの明示ロードで日本語認識に成功した記録
- `speech-api-winml-initialized.json`: Windows ML初期化のみでは解消しなかった記録
- `speech-api-english-recognition-success.json`: 回避策を再実行して英語認識に成功した記録

過去に生成した`artifacts/Locaptio.SpeechApiProbe.msix`は現ソースより古い未署名成果物です。再検証には以下のビルド・登録スクリプトを使用してください。

## 再検証

Windowsの開発者モードをユーザー自身が有効にした環境で、リポジトリ直下から次を実行します。通常のLocaptioとは別名`Locaptio.SpeechApiProbe`で登録します。

```powershell
# 状態確認のみ（モデル取得を要求しない）
./scripts/test-windows-speech-api.ps1
# 必要ならWindows Update経由のモデル準備も要求する
./scripts/test-windows-speech-api.ps1 -PrepareModel
# 明示したローカルWAVで認識を試す。ライブキャプションは起動しない
./scripts/test-windows-speech-api.ps1 -PrepareModel -WaveFile ./artifacts/test-ja.wav
# 旧SDKを独立パッケージとして検証する
./scripts/test-windows-speech-api.ps1 -SdkVersion 2.4.1-experimental -PrepareModel -WaveFile ./artifacts/test-ja.wav
# このPCで日本語認識に成功した、旧SDK向けのDLLロード回避策を使う
./scripts/test-windows-speech-api.ps1 -SdkVersion 2.4.1-experimental -PrepareModel -PreloadModelLibraries -WaveFile ./artifacts/test-ja.wav
```

結果は新SDKが`artifacts/speech-api-probe-packaged.json`、旧SDKが`artifacts/speech-api-probe-24-packaged.json`。指定なしでは状態照会のみで、モデル作成・取得は要求しません。`-PrepareModel`を付けた場合にモデル準備・作成を試します。`-WaveFile`は指定ファイルの認識を要求しますが、それだけではモデル取得を要求しません。モデルの初回取得にはインターネットが必要な場合がありますが、Azureなどの有料APIは呼びません。登録後の検証アプリはWindowsの「インストールされているアプリ」から削除できます。

`-PreloadModelLibraries`は旧SDK限定の検証オプションで、インストール済みのMicrosoft CPUモデルパッケージからDLLの場所を取得します。モデル未取得なら、先に`-PrepareModel`だけを実行して準備してください。この回避策で最新SDKのモデル準備失敗が解消したわけではありません。

Microsoftの現仕様ではCopilot+ PCはNPU、それ以外はCPUを自動選択し、GPU実行は非対応です。型としてデバイス指定が存在することと、その指定がこのPCで機能することは別です。また、このAPIがWindowsライブキャプションと同じモデル・認識結果・速度を保証する記載はありません。Locaptioへ統合する前に、音声入力の連続認識、実測速度と言語対応を確認する必要があります。
