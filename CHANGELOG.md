# Changelog

## 1.0.39 — Setup installer, automatic LLM preparation and recording replay

- 管理者権限不要の `Locaptio-Setup-x64.exe` を追加。スタートメニュー・アンインストールに対応し、アプリの更新もSetupを優先します。ZIP版は継続します。
- LLM環境・既定モデルがなければ、アプリが自動準備。uvの配布物は固定バージョンとSHA-256で検証します。工程表示、中断、準備済み環境の再利用に対応します。
- 録音の文字起こしと発話時刻をJSONへ保存。文字クリック、選択範囲の右クリック、検索結果から聞き直せます。音声とJSONを再度開いて再生・検索できます。
- Windows字幕の時刻は推定・発話単位です。音声の再取り込みを避けるため再生は録音停止後に使用します。
- Added a per-user Setup installer, automatic local LLM preparation, persisted utterance offsets, bilingual transcript search, replay/pause/seek, and reopening recordings with their transcript sidecars.

## 1.0.38 — Reliable Ctrl+C / 選択範囲のコピーを修正

- 選択開始時に表示中の行へキーボードフォーカスを設定し、本体全体のCtrl+Cショートカットで選択範囲をコピーします。入力欄のコピーは優先して保持します。
- クリップボードが一時的に使用中の場合は短時間再試行します。選択範囲の保持と複数行コピーは継続します。
- Keyboard focus now enters the selected row and a window-level Ctrl+C accelerator copies the retained range, while editable controls keep their own copy behavior. Transient clipboard contention is retried.

## 1.0.37 — Update status in settings / 設定内の更新表示

- 設定の「アプリの更新」に現在のバージョンを表示します。この画面からの確認結果・ダウンロード進捗と操作は同じ画面内に表示し、本体の通知へ転送しません。
- Settings now show the current app version and keep update-check results and download actions in the same settings page.

## 1.0.36 — Persistent selection, sidebar settings and current captions / 選択保持・設定ナビ・字幕の最新部分

- 左ドラッグで選択範囲を変更し、離した後も範囲と青色を保持します。押していない状態のマウス移動では範囲を変更しません。
- 設定は9カテゴリのSVGアイコン付き左ナビゲーションと右側の設定ページに変更。キーボード操作と選択中の項目表示にも対応します。
- 字幕の「…」による省略を廃止し、指定行数に収まる末尾の最新部分を表示します。Unicode文字の途中で分割せず、上端の部分行も表示しません。全文は本体・保存に残します。
- 本体の×による終了でも、認識・録音停止後にWindowsライブキャプションを閉じます。
- Selection stays highlighted after release; only a held left drag extends it. Settings now use nine SVG-icon navigation categories. Subtitles show their latest fitting text instead of an ellipsis. Main-window exit also closes Windows captions after cancellation.

## 1.0.35 — Stop windows, export details and updates / 停止・保存情報・更新確認

- Stopは録音・認識を止めてからWindowsライブキャプションと独立字幕を閉じます。本体の文章は保持します。
- 保存日時・セッション時間（準備や無音含む）・本文行数／発話数をそれぞれ設定でON/OFFできます。既定はON。
- テキスト保存時の初期名は年月日＋アンダースコア（例20261111_）。設定のテンプレートで日時や任意の文字へ変更可能。
- 起動時に公式GitHubの公開版を確認し、新版のZIPをアプリデータ内へダウンロード。確認と自動ダウンロードはそれぞれON/OFF可能（既定ON）。SHA-256照合後に完了扱いとし、実行中のアプリは置き換えません。
- Stop now cancels recognition/recording before closing Windows and popout captions, retaining the transcript. Optional export headers include save time, session duration and content-line/utterance counts. Filename templates default to a date prefix.
- Optional startup release checks and ZIP downloads use official GitHub Releases. Downloads are streamed, hash-verified and saved separately; they are never installed or executed automatically.

## 1.0.34 — Whole captions and natural scrolling / 字幕の見切れ・スクロール操作を改善

- 字幕の最大表示行数（既定4行）と、最新の発話を文章の塊で表示するモードを追加。字幕枠を内容の高さに合わせ、先頭行の途中を切らずに表示します。長い発話は行末を省略し、本体の全文は保持します。
- 最新の発話が画面内にある間は自動スクロールし、「最新へ」を非表示にします。過去の文章へ移動した場合は位置を保持します。
- 左ドラッグ中だけ選択色を表示。ボタンを離すと色は消え、直前の範囲をCtrl+C／右クリックでコピーできます。
- Windows字幕の自動復旧はON/OFFと待ち時間を設定可能。既定を180秒に変更。無音では再起動しません。字幕通知が続いている場合も復旧タイマーを評価します。
- Added a total subtitle line limit (default 4) and optional latest-utterance blocks. Captions resize to whole blocks; long utterances use an ellipsis while the main transcript retains full text.
- Follow the latest utterance while it is visible, preserve historical scroll positions, and hide selection highlighting on mouse release while retaining the selected text for copying.
- Windows caption recovery has a configurable timeout (default 180 seconds) and ON/OFF control. Silence alone never triggers recovery.

## 1.0.33 — Continue after Japanese caption notice / 日本語切替時の続行待ちを修正

- 「キャプションのみ」の案内が遅れて出る・再表示される場合も、案内内の閉じるボタンだけを操作し、消えたことを待って続行を再試行します。非表示の操作項目を避け、字幕画面に戻ったことを確認します。
- 日本語の選択を確認できた後は、続行の失敗だけで元の言語へ戻しません。処理全体にはタイムアウトを設けます。
- Japanese language confirmation now handles delayed/reappearing informational tips and bounded Continue retries, without accepting download/privacy prompts or undoing an already verified target language.

## 1.0.32 — Hide completed preparation / モデル準備100%の表示残りを修正

- 音声認識・翻訳・AIの全モデルが準備完了したら進捗欄を非表示にします。準備中止時も表示を片付け、失敗時の説明は保持します。
- Completed model preparation now hides its progress panel instead of leaving the last 100% message visible. Translation/AI preparation remains visible until all tracked components finish.

## 1.0.31 — Natural checkbox spacing / 録音・AI補正の余白を調整

- 録音とAI補正のチェックボックスの最小幅を解除し、文字に合わせた幅と共通の操作間隔で並べます。
- Toolbar checkboxes now fit their content instead of reserving a 120-DIP minimum width.

## 1.0.30 — Small corner status link / 状態を右下の小さなリンクに変更

- 「状態」を右下の小さな文字リンクに移動し、矢印とボタンの枠を廃止。ホバー表示をやめ、クリックでON/OFFします。
- 詳細欄は内容に合わせた幅に縮小し、本文の配置を動かさず表示します。
- Replaced the toolbar dropdown with a small bottom-right status link. Clicking toggles compact, content-sized diagnostics; hovering does not open them.

## 1.0.29 — Consistent rounded controls / 操作ボタンの丸角を統一

- 下段ボタン・チェックボックスにWinUI標準スタイルを継承し、上段の選択欄と揃う丸角に修正。コンパクトな余白は保持します。
- Toolbar buttons and checkboxes now inherit the standard WinUI styles and use rounded corners consistent with the input selectors.

## 1.0.28 — Name before recording / ファイル名を指定してから録音開始

- 録音チェックで保存画面を開き、ファイル名と保存先の確定後に録音と文字起こしを開始します。キャンセルでは開始しません。設定の録音フォルダーを初期表示します。
- Checking Record now opens a filename/destination dialog. Capture and transcription start only after confirmation; cancel does not start recording.

## 1.0.27 — Stable hover details and multi-row copy / 状態表示の位置固定・複数行コピー

- 下端の折りたたみ欄を上部の「状態 ▾」へ移動。ホバーで下方向に重ねて表示し、ボタンや本文の位置は動かしません。クリックで固定・解除、Escで閉じます。警告・モデル準備は従来どおり表示します。
- 本文の途中から別の行の途中までドラッグしてコピーできます。選択方向へのスクロール、表示外へ移動した選択の保持、Ctrl+C・右クリックのコピーに対応。仮想化を維持し、選択行の補正表示は解除まで保留します。
- Diagnostics now appear below a stationary toolbar anchor on hover. Cross-row partial text selection works with the virtualized history, language columns, edge scrolling, correction deferral and keyboard/context-menu copying.

## 1.0.26 — Recording shortcut and selectable transcript / 録音開始・コンパクト表示・本文選択

- 録音チェックで録音と文字起こしを開始。保存ダイアログを省き、設定の保存先（未設定ならミュージック/Locaptio）へ日時付きのファイルを自動作成します。チェック解除は録音のみ停止、Stopは両方停止します。
- メニュー・操作ボタンの余白を縮め、下部の状態・話者識別・診断表示を初期状態で折りたたみます。モデル準備と接続警告は引き続き表示します。
- 録音・文字起こし中も本文を選択してコピーできます。選択中の行の更新は選択解除まで保留し、認識・録音・独立字幕は継続します。
- The recording checkbox starts capture and transcription with automatic filenames. Compact controls and collapsed diagnostics give more space to transcripts. Text selection remains stable while recognition updates arrive; clearing selection applies the latest revision.

## 1.0.25 — Language recovery, preparation progress and grouped settings / 言語警告・準備進捗・設定整理

- Windows字幕が希望言語で準備済みなら言語画面を開かずに接続します。自動切り替えの失敗後も、手動変更による準備完了または字幕の更新を確認したら警告を解除します。内部の操作項目IDを警告に表示しません。
- モデル準備の完了工程％・残り工程％・現在の工程・経過時間を本体に表示します。WhisperのCPU/CUDA、AMD NPUの環境・encoder・decoder・検証、翻訳モデル、ローカルLLMを通知します。割合は完了工程に基づくもので、残り時間やNPUコンパイル内部の割合を推測しません。
- 設定を8つの折りたたみカテゴリに整理し、認識モデルをエンジン・デバイスと同じカテゴリへ移動しました。スクロールバーの余白を保持します。
- Fixed stale language warnings after manual recovery, added stage-based preparation reporting and grouped settings. Added tests for manual readiness/recovery, real CPU Whisper progress notifications and the WinUI progress/category layout.

## 1.0.24 — Windows caption language sync / Windows字幕の言語同期

- Windows連携のStart時に、本体モードに合わせてWindowsの字幕言語を自動選択するオプションを追加（初期値ON）。英日翻訳は英語（米国）、日本語文字起こしは日本語（日本）に合わせます。実行中のモード変更では既存の停止・再開処理を通じて同期します。
- Windows UIの既知の操作項目のみを使用し、選択の検証・準備完了の確認を行います。未対応UI・言語未準備などの失敗は専用警告を表示。ダウンロードやプライバシー同意は自動で実行しません。既存Windows字幕の画面は引き続き必要です。
- Added optional Windows caption language synchronization on Start and session mode changes, with exact locale matching, selection verification, fallback and a persistent language warning. This automates the known Windows UI and may need updates after Windows changes; it is not a public caption-language API or headless recognition engine.

## 1.0.23 — Local speaker colors / ローカル話者識別と色分け

- 音声から話者を識別するオプションを追加。CAMPPlusの声の特徴とSilero VADをローカルCPUで処理し、セッション内の最大8話者を色分けします。設定で有効化し、各話者の色・一致しきい値を変更できます。
- 字幕は先に表示し、話者の判定後に本体・独立字幕を更新します。補正・翻訳でも色を保持し、保存テキストに話者番号を付けます。話者色は補正色より優先します。
- Whisperは認識と同じ音声を共有。Windows連携は選択したPC音声を並行解析し、字幕の取得時刻から近似対応します。語単位の話者分離、氏名の特定、重複発話の分離には対応しません。
- Added optional, local CPU speaker embeddings and bounded anonymous clustering (up to eight session-local speakers), configurable colors/threshold, subtitle/history colors and saved speaker numbers. Audio is never uploaded or saved by the speaker worker. Windows caption alignment is approximate; mixed-speaker rows use a dominant speaker or stay unassigned. Recognition continues if speaker setup/inference fails.
- Added hash-verified model setup, bounded-work/cancellation/fallback tests, real two-speaker sample checks and a WinUI color/export check.

## 1.0.22 — Main-screen AI correction switch / 本体画面のAI補正チェックボックス

- 本体画面の録音チェックボックスの隣に「AI補正」を追加。設定画面と双方向に同期し、選択状態を保存します。Windows連携は即時、Whisperは次回Startから反映します。
- Added an AI correction checkbox beside Recording on the main screen. It shares the persisted setting with the Settings window; Windows captions apply changes immediately, Whisper on the next Start.

## 1.0.21 — Optional Windows caption correction / Windows字幕のAI補正

- Windowsライブキャプション連携でも、日本語・英語の文字起こしをローカルLLMで文脈補正できます。設定のON/OFFは連携中も即時反映します。
- 認識原文を先に表示し、補正結果は同じ行へ反映。OFF後の未反映結果は破棄し、補正済みの文章は保持します。LLMが使えない場合も原文表示を続けます。
- Added live ON/OFF control for local AI correction of Japanese and English Windows captions. Raw text appears immediately; corrections update the same row and feed English-to-Japanese translation. Pending results are discarded when disabled; previously applied corrections remain.
- Correction work and context are bounded. Added regression checks for toggling, stale results, cancellation, failure fallback and translation integration.

## 1.0.20 — Stable recording UI and scroll position / 録音表示とスクロールの安定化

- Keep caption rows and text controls in place during partial recognition and translation updates.
- Pause automatic following on wheel, scrollbar, keyboard and touch input. Layout changes no longer re-enable it. Use **最新へ** or End to resume.
- Update only the recording clock, keep the file path stable, and assign diagnostics text once only when changed.
- Retain the main window's native backing surface during ordinary repaint; clear it at creation/resize instead of every clock/text update.

録音中の本体のちらつきを抑え、少しスクロールしただけで最新位置へ戻される問題を修正しました。「最新へ」で自動スクロールを再開できます。

## 1.0.19 — Automatic Windows captions recovery / Windows字幕の自動復旧

- Add a Japanese/English official download website, a stable direct ZIP link and checksum verification.
- Detect sustained PC output audio without caption text updates for about 20 seconds; restart Windows captions and verify its window is visible.
- Replace a blocked caption bridge as well, preserving Locaptio's history, recording and translation backends.
- Enable recovery by default, with an ON/OFF setting, a 60-second cooldown and three attempts per ten minutes. Silence, audio-meter errors and sleep gaps do not trigger recovery.
- At startup, restore a hidden/minimized Windows caption window and recover a process that exists without a working window.

PC音声があるのに字幕の更新が止まると自動再起動し、ウィンドウの再表示を確認します。既定でON、設定で変更できます。音量による判定のため、音楽などでも作動する可能性があります。

## 1.0.18 — Windows captions disconnect warning / Windows字幕切断の警告

- Warn when the Windows Live Captions window closes or its caption provider disconnects, including when its process remains running.
- Restore the minimized main window and keep the warning visible until reconnection or Stop.
- Keep transcript history and recording running while waiting; reconnect automatically without importing old Windows captions.
- Ignore silence and debounce brief accessibility-provider rebuilds.

Windows字幕が閉じられた際に警告し、最小化中の本体を復帰します。文章と録音を保持し、Windows字幕を起動し直すと自動再接続します。無音では警告しません。

## 1.0.17 — First public release / 初回公開

- Publish Locaptio with Japanese and English usage guides and an MIT source license.
- Local English-to-Japanese translation and Japanese transcription.
- Floating text-only captions, independently adjustable background/text opacity.
- System, microphone, application, and combined system + microphone capture.
- UTF-8 transcript export; WAV, MP3 and M4A recording.
- Full-session virtualized history with retained scroll position.
- CPU, NVIDIA CUDA, DirectML translation and experimental AMD XDNA 2 recognition.
- Optional context-aware local LLM translation/correction and Windows captions integration.
- Portable x64 distribution; first-time model setup remains separate.

日本語・英語ガイドとともに初公開。上記機能は開発中で、全ハードウェア・実音声条件を検証したものではありません。
