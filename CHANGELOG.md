# Changelog

## 1.0.57 — Summary pane buttons and live summary settings text

- 要約欄が狭いときに「ライブ要約をクリア」ボタンが途中で切れる問題を修正しました（入りきらないボタンは2段目に折り返します）。
- 設定のライブ要約の説明文が「約2〜3分」と固定になっていたのを、選んだ頻度・文字数に合わせた説明に直しました。

## 1.0.56 — Live summary settings and window, keep-on-top switch

- ライブ要約の頻度（約1〜10分ごと）、1回に要約する量（約500〜4000文字）、長さ（短め・標準・詳しめ）を設定（AI文字補正）で選べるようにしました。
- 要約欄に「ライブ要約をクリア」を追加しました（確認あり。文章と全体の要約は残し、これ以降の発話から要約します）。
- 要約欄の左端をドラッグして幅を変えられるようにし、「別ウィンドウで開く」で大きさを自由に変えられる別ウィンドウに表示できるようにしました。
- 本体の「常に手前に表示」をON/OFFできるようにしました（設定「本体の表示」、メニュー「設定」、ほかのアプリ使用中も効くショートカット Ctrl+Alt+T。キーは変更・無効化できます）。字幕ウィンドウは常に手前のままです。

## 1.0.55 — Summaries while transcribing, kept and restored

- 文字起こし・録音中も要約欄の「今までを要約」で、止めずにそこまでの発話を要約できるようにしました（ライブ要約がOFFならONにします）。ツールバーの「AI要約」も文字起こし中に使えます。
- 全体の要約を作ってもライブ要約は消えず、要約欄の下に並べて表示します。「今までを要約」も全体の要約を消しません。ライブ要約をOFFにしても、それまでの結果は表示されたままです。
- 要約（全体・ライブ要約・「ここまでの流れ」）を作業の自動保存に含め、起動し直して「復元」すると根拠へのリンク付きで戻るようにしました。復元後のライブ要約は未要約の発話から続けます。

## 1.0.54 — Four languages, live summary while recording, toolbar customization, setup fix

- 話す言語（日本語・英語・イタリア語・スペイン語）と「文字起こしのみ」または翻訳先（ほかの3言語）を、本体上部の2つの選択欄で選べるようにしました。英語の文字起こしも使えます。Windows字幕連携もイタリア語・スペイン語に自動で切り替えます。英語→日本語以外の翻訳は設定からダウンロードする翻訳モデルを使います。以前のモード設定は自動で引き継ぎます。
- Whisper使用時、日本語→英語などの翻訳先が音声認識の準備に渡らず、翻訳されないことがある問題を修正しました。
- ライブ要約を録音中にも使えるようにし、ツールバーのチェックボックスでいつでもON/OFFできるようにしました。途中でONにすると、それまでの発話もさかのぼって要約します。長くなると古いまとまりを「ここまでの流れ」にまとめ直します（元の要約は折りたたんで残ります）。メニューバーの項目に専用アイコンを付けました。
- ツールバーの項目の並び順を変えたり、使わない項目を隠したりできるようにしました（設定「本体の表示」→「ツールバー」、またはツールバーを右クリック）。隠した項目はいつでも戻せます。「開始」「停止」は隠せません。
- 翻訳モデルのダウンロード中に文字起こし・翻訳を始めると「Python環境の作成または修復に失敗しました（os error 32）」で止まる問題を修正しました。正常なPython環境は作り直さず、準備どうしは順番に実行します。Windows字幕連携の速報翻訳が不要な音声認識モデルをダウンロードしないようにしました。

## 1.0.53 — Japanese → English and multilingual translation, code signing

- 「日本語 → 翻訳」モードを追加しました。日本語の音声を英語（または選んだ言語）に翻訳します。「英語 → 翻訳」も日本語以外の言語を選べます。翻訳先は設定（翻訳・ローカルLLM →「翻訳の言語」）で選び、本体のモード名に表示します。
- 翻訳モデルを設定からダウンロードできます：「日本語 → 英語」（FuguMT ja-en、約0.12GB、CC-BY-SA-4.0）、「多言語（100言語）」（M2M100 418M、約1.9GB、MIT。中国語・韓国語・フランス語・ドイツ語・スペイン語など）。未ダウンロードのまま開始すると、必要なモデルを案内します。英語→日本語はこれまでどおり標準で使えます。
- チェックボックスがまれに「−」の表示になる問題を修正しました（チェックの動きのアニメーションが途中で止まることが原因。アニメーションしない表示に変更）。
- 配布物を自己署名のコード署名証明書（CN=Locaptio）で署名できるようにしました（`-Sign`、タイムスタンプ付き）。詳しくは docs/code-signing.md。

## 1.0.52 — Fixes for other PCs, live summary, editing and recording improvements

- AI補正（1文ずつの補正）を既定でOFFにしました（既存の設定も一度OFFにします。ツールバーの「AI補正」でONに戻せます）。
- 録画の既定を 60 fps・最高画質（新設）にしました。前回の録画で選んだフレームレート・画質・カウントダウン・対象（画面／ウィンドウ）を次回も引き継ぎます。カウントダウンは なし／1／2／3／5／10秒 から選べ、設定（保存先・録音）でも変更できます。
- 用語辞書をCSVでエクスポート・インポートできるようにしました（Excelで編集可。Excelが保存したShift-JISのCSV、タブ区切り、Locaptioの辞書テキストも読めます）。インポート時は「追加」か「置き換え」を選べます。
- AI補正で変わった文字を紫色で示し、行の右に「AI」印を付けます。印にカーソルを合わせると補正前の文章と変更点（消えた文字・加わった文字）を表示し、「補正前に戻す」で元に戻せます（Ctrl+Zで再び補正後へ）。設定（AI文字補正）でON/OFFできます。
- AI補正が話した語を消したり、話していない語を足したりした結果は採用せず、原文のままにします（漢字への変換や聞き間違いの修正は採用します）。
- ライブ要約：文字起こし中に約2〜3分（約1500文字）ごとの発話のまとまりをローカルAIで要約し、要点・決定事項・TODOを要約欄に追加します（「録音・AI」メニューまたは設定でON）。
- 録音・録画を始めるときに表示中のテキストを「引き継いで、続きに記録する」ことができるようにしました。開始時の確認画面で選べ、設定（保存先・録音）で「毎回確認する／引き継ぐ／クリアする」を決めておけます。
- 文章をダブルクリックすると、その場で編集できます（Enterで確定、Escで取り消し）。文字を選んで Delete／BackSpace キーを押すと、その文字だけを削除します。どちらも Ctrl+Z で元に戻せます。設定（本体の表示）でON/OFFできます（既定はON）。
- 見返し中の録音・録画で「クリア」を押すと、確認のうえ動画・音声も閉じるようにしました（ファイルは削除しません）。以前は文章だけが消え、動画が残ることがありました。
- 「ホーム」ボタン（ファイルメニューにも）で、見返し中の録音・録画、文章、要約を閉じて最初の画面に戻れます。保存していない文章は、閉じる前に保存するか確認します。
- ローカルLLMのモデルを設定（翻訳・ローカルLLM）で選べるようにしました：Qwen3 1.7B（約1.8GB・軽量）／4B（約2.5GB・標準）／8B（約5GB・高精度）／14B（約9GB・最高精度）、または自分のGGUFファイル。ダウンロード済みかどうかを表示し、「このモデルを今ダウンロード」で事前にダウンロード・中止できます。AI補正・文脈翻訳・AI要約は選んだモデルを使います。
- 2つ目以降のモデルをダウンロードするときは、インストール済みのllama.cppを再利用します。
- AI補正（ローカルLLM）が別のPCで「Failed to load shared library … llama.dll」で使えない問題を修正しました。原因は、llama.cpp が必要とする Microsoft Visual C++ ランタイム（msvcp140・vcomp140）や GPU の Vulkan ドライバー（vulkan-1.dll）がそのPCにないことです。Locaptio に同梱したランタイムを使うようにし、それでも読み込めないときは自動で CPU 版の llama.cpp に切り替えて続行します（初回のみ）。準備時にも読み込めるかを確かめます。
- llama.cpp の入れ替えが「アクセスが拒否されました」で失敗する問題を修正しました（読み取り専用のフォルダーが原因）。
- 録画：録画機能に必要な Microsoft Visual C++ ランタイムを Locaptio に同梱しました。再頒布可能パッケージが入っていないPCでも録画できます。読み込めない場合は、原因と公式ダウンロード先を表示します。
- 録画：ハードウェアエンコーダーや画面の取り込み方式（Desktop Duplication）を使えないPCでは、ソフトウェアエンコード、Windows Graphics Capture の順に自動で切り替えて録画を始めます。
- 録画：マイクの音声を既定で録音します（既存の設定もONにします）。
- 音声認識・AI（ローカルLLM）・話者識別の自動準備：Python環境の python.exe がウイルス対策ソフトなどで削除・ブロックされた場合に作り直し、それでも実行できないときは原因と対処を日本語で表示します。各スクリプトは絶対パスでPythonを呼び出します。
- 自動準備のエラーメッセージが文字化けする問題を修正しました（PowerShell が Shift-JIS で出力した場合も正しく表示）。
- Video recording ships the Visual C++ runtime app-local and falls back to software encoding / Windows Graphics Capture; the microphone is recorded by default. Setup scripts verify and repair the environment's python.exe and report security-software removal clearly; setup errors written in the console code page are decoded correctly.

## 1.0.51 — Checking recorded text against the audio

- 録音した音声と文章の照合：録音の音声をWhisperで聞き取り、本体の文章と1文字ずつ照合します。一致した発話には実際に話された位置の単語時刻を付け、再生中のハイライトと文字クリックの位置を音声に合わせます。音声と一致しない部分が多い発話には行の右に印（！）を付け、一致率をツールチップで表示します。文章は変更しません。
- Windows字幕で文字起こししながら録音しているときは、録音済みの部分を裏で順に照合するので、停止後すぐ（実測：3分の録音で0.4秒）に終わります。設定（保存先・録音）で「録音中に少しずつ／停止してから／照合しない」を選べます。録音中の照合は軽いモデル（small）をCPUで使います。
- Windows字幕の各単語が画面に出た時刻を記録し、表示の遅れを差し引いて単語ごとの時刻にします（モデル不要）。遅れの量は停止時に録音の音声から測ります。
- Setupの完了画面に、アイコンの色のグラデーションとアイコン・名前を表示します。
- Recorded text is checked against the audio (during recording with Windows captions, or after stopping); matched utterances get spoken word times and mostly unmatched ones are marked. Windows caption word times come from when each word appeared, minus the measured caption delay. Setup's finish page shows a brand sidebar.

## 1.0.50 — Screen recording, video review and subtitled video export

- 画面録画：画面全体または特定のウィンドウを、PC音声・マイクと一緒にMP4で録画。対象は縮小画像付きの一覧から選び、選んだ対象をプレビューで確認できます。録画しながら文字起こしもできます。
- 録画中：ライブプレビュー、一時停止・再開（一時停止中の映像と発話は動画・字幕に入りません）、スクリーンショット（PNG保存とブックマーク）、開始前のカウントダウン、カーソル・クリックの表示、フレームレート（15/30/60 fps）と画質、Locaptio本体を録画に映さない設定。
- 動画の見直し：録画・動画は本体の上部に動画を表示し、再生位置の発話を映像に重ねて表示。文章クリックで頭出し、字幕ウィンドウとの連動、編集・辞書・同期の改善・AI要約に対応。
- 字幕付き動画の書き出し：原文・日本語訳・2段、文字の大きさ・位置を選び、字幕を焼き込んだMP4を作成（元の動画は変更しません）。同じ名前のSRTも保存できます。
- 動画ファイルの読み込み：MP4・MOV・M4V・WMV・AVI・MKVの音声を文字起こし。ドラッグ＆ドロップにも対応。
- 追加したツールバーのボタンが表示されないことがあるレイアウトの問題を修正。
- Screen/window recording to MP4 with thumbnails, live preview, pause, screenshots, countdown, cursor/click display and quality settings; video review with captions over the video; subtitled MP4 export with matching SRT; video file transcription.

## 1.0.49 — Recording timing, clearer controls and replay captions

- 録音した音声と文字のずれを改善。Windows字幕連携では、文字が画面に出た時刻を発話の開始として記録しており、Windows側の表示の遅れ（約0.5〜2秒、PCの負荷で変動）が固定の1秒補正では吸収しきれていませんでした。録音停止時に録音の音声から発話の始まりを検出し、遅れを推定して各発話の開始位置を合わせます。長い発話を分割した後半も、分割した時刻ではなくその文字が表示された時刻から始まるようにしました。「同期を改善」でも同じ補正を先に行います。
- 押せないボタン・メニューを状況に合わせて無効化し、薄く表示。クリアボタンをツールバーに戻しました。
- ブックマークの種類ごとに色を設定可能（設定 → 保存先・録音）。一覧・行の印・ボタンに反映します。
- 保存済み録音の再生中も「字幕」ウィンドウを使え、再生位置の発話を表示します。
- AI要約・再文字起こし・同期の改善で、読み込み済みのモデルを破棄せず再利用。要約用のLLMは10分間保持します。自動保存は長いセッションほど間隔を延ばします（最大30秒）。
- 異常終了時の原因を %LOCALAPPDATA%\Locaptio\logs\crash.txt に記録します。
- Recording timing now follows the recorded speech: caption delays are estimated from the audio and each utterance starts at its speech onset; split rows keep the time their text appeared. Unavailable commands are disabled and dimmed, Clear is back on the toolbar, bookmark types have configurable colors, the caption window follows replayed recordings, loaded models are reused for recording tools, and autosave backs off for long sessions.

## 1.0.48 — Editing, autosave, term dictionary, subtitles and AI summaries

- 作業の自動保存と異常終了後の復元（中断した録音のWAVヘッダー修復・MP3/M4Aの途中WAVの回収を含む）。
- 発話の直接編集、Ctrl+Z／Ctrl+Y、認識時の原文との比較・原文へ戻す。手動編集は後続の認識・補正結果より優先。
- 保存済み録音の再文字起こしと時刻をそろえた比較、版の保存（最大10件）と切り替え。
- 専門用語辞書：誤表記の置き換え、Whisperの認識ヒント、AI補正、LLM翻訳の訳語に共通利用。
- 既存録音への単語時刻の後付け（文章は変更せず、ブックマークと要約の根拠も一緒に移動）。
- SRT・VTT字幕出力、録音中のブックマーク（Ctrl+B）、ローカルLLMによる要点・決定事項・TODOの抽出と根拠の発話へのリンク。
- ツールバー・メニュー・初期画面・状態表示を整理。
- 公開前の検証で見つけた不具合を修正：高精度更新を「AI補正」と区別し、補正前の原文を正しく保持／録音ファイルにもAI補正・辞書適用前の原文を保存／入力切替で録音が分かれたときの字幕・ブックマークの時刻の混在／要約の根拠番号の範囲指定（[4-6]）／復元した編集を原文に戻したときの編集マーク。
- Autosave/recovery, editing with undo, re-transcription with comparison and versions, a shared term dictionary, word-time alignment for existing recordings, SRT/VTT export, bookmarks, cited local-AI summaries and a reorganized toolbar.

## 1.0.47 — Timed replay highlights, drag-and-drop and save prompts

- 再生時のハイライトを発話全体／単語ごと／1文字ずつから選択可能に。新しいWhisperファイル取り込みでは単語時刻を保存。文字ごとの時刻、既存録音・Windows字幕・補正文の細かい時刻は推定です。
- MP3・M4A・WAVのドラッグ＆ドロップに対応。認識中でもファイルや録音一覧を開けます。実際の切り替え前に保存確認し、キャンセル時は認識と履歴を維持します。
- Clear・モード変更・新規録音・保存済み録音から新しい認識を開始するときにも、保存／保存せず続行／キャンセルを共通化。保存画面をキャンセル・保存失敗した場合は先へ進みません。
- 設定の「音声認識・Windows連携」からWhisper、音声＋翻訳、ローカルLLMを事前準備できます。進捗・中止・結果を同じ画面内に表示します。
- ファイル認識の待ち時間をリアルタイムの30秒から5分へ分離。
- Select phrase, word or character replay highlights; new Whisper imports store word timestamps. Character timing and old/Windows/corrected transcripts use estimates. Add audio drag-and-drop, active-session import, centralized save prompts and cancellable model preparation in Settings.

## 1.0.46 — Recording confirmation and single-pass Windows caption controls

- Setupの完了ページ以外のウィザード画像を非表示にしました。
- マイク設定を変更した後の確認用メニュー再表示を廃止。必要な変更を1回だけ実行し、メニューを閉じて操作前のウィンドウへフォーカスを戻します。
- Windows字幕の起動・言語・マイクの操作中は本体に「ライブキャプション操作中」と表示します。
- 新規録音時に既存テキストがある場合はクリア前に確認し、確認または保存画面をキャンセルした場合は履歴と動作中の認識を維持します。
- 録音開始・入力変更時にWindows字幕が未起動ならウィンドウを起動します。
- Only the final Setup page shows wizard artwork. Microphone changes use one menu pass and restore focus. The main window indicates Windows caption operations. Starting a new recording requires confirmation before clearing existing text; cancellation preserves history and recognition. Windows captions are launched when required.

## 1.0.45 — Stable Windows input switching and speech environment repair

- インストール進行中のページだけ右上のアイコンを非表示にしました。
- PC内音声／PC内音声＋マイクの変更は接続を維持してマイク設定だけを変更。言語を再選択せず、Windowsの字幕スタイルも変更しません。
- 新規録音でも動作中のWindows字幕連携を維持し、接続準備中・一時的なUI再取得で最初の発話を履歴として捨てないように修正。
- 新規録音の初期ファイル名を `yyyyMMdd_` に変更。
- 既存のPython環境も自動修復し、旧バージョンのジャンクション参照が残る場合に音声ファイルの読み込みがエラー448で失敗する問題へ対応。
- Hide the installation-progress header icon, retain the Windows caption connection and language during input/recording changes, preserve early utterances, use date-prefixed recording names, and repair existing Python environments before file transcription.

## 1.0.44 — Windows input selection and explicit update downloads

- Setupの標準ファイル名に版番号を追加し、ウィザードの画像をLocaptioのアイコンへ変更。
- 起動時は更新情報だけを確認し、旧設定にかかわらず、更新ファイルはボタンを押すまでダウンロードしません。
- Windows字幕連携の候補をPC内音声／PC内音声＋マイクに限定。Whisperへ自動で切り替わる動作を廃止。マイク単独・指定アプリはWhisperを選んだときに使用できます。
- Windowsのマイク設定を状態確認付きで自動ON/OFFし、設定を確認できない場合は診断を表示して停止します。実機でON→OFFを確認しました。
- Versioned Setup filenames and app branding; metadata-only startup checks; supported Windows input choices with verified microphone synchronization.

## 1.0.43 — Readable Japanese setup errors and Python preparation

- 自動準備のPowerShell・外部プロセス・アプリの出力をUTF-8へ統一し、日本語エラーの文字化けを修正。
- Pythonのバージョン別実体を直接指定し、マイナーバージョンのジャンクションで発生するWindowsエラー448を回避。利用可能な実体がなければアプリ用領域に準備します。
- モデル準備失敗・中止後に0%の進捗バーを残さず、診断を保持して再試行できるように修正。
- Preserve Japanese native diagnostics through Windows PowerShell, avoid Python minor-version junctions, and clear stale preparation progress after failure.

## 1.0.42 — Audio import and recording management

- MP3・M4A・WAVをローカルで文字起こし。不足する音声モデル・Python環境を自動準備。
- 録音一覧から共有ZIP保存、音声・文章一式の移動、確認後のごみ箱削除を追加。
- Local audio file transcription, automatic speech setup and recording bundle management.

## 1.0.41 — Recording sessions, retained history and stable captions

- インストール先の選択画面を常に表示。更新のSetup実行前に確認ポップアップを追加し、「後で」では録音・文字起こしを継続します。
- 初期値をWindows字幕連携・日本語文字起こし、メニュー背景一致、本体背景透過率10%、字幕背景透過率20%、英語水色／日本語白へ変更。
- 字幕の折り返し幅を設定可能に。移動中は再レイアウトを保留し、表示領域をクライアント座標で計算。背景の不要な消去とフレーム再設定を抑制します。
- 音声入力変更・文字起こしの再開で履歴を保持し、セッションをまたいでIDが衝突しないように修正。
- 「新規録音」と「録音一覧・再生」を分離。録音ごとに専用フォルダーを作り、音声・JSON・TXTを保存。録音のチェックを外すと認識も停止し、Startを再度押せます。
- Always show the destination picker; require approval before running an update installer; introduce per-recording folders/library, preserve input-switch history, and stabilize movable captions with configurable wrapping width.

## 1.0.40 — Setup checkboxes, playback highlighting and Windows microphone input

- Setupのカスタム描画をネイティブコントロールに切り替え、高DPI環境でのチェックボックスの見切れを修正。スタートメニューへの追加（既定ON）を追加タスクとして明示しました。
- 再生中の発話を強調し、発話に合わせて表示位置を追従。選択・スクロール中は追従を止め、コピーの選択色は再生の強調より優先します。
- 「マイク（既定の入力）」をWindows字幕連携でも使用可能にし、Windows側のマイクON操作を案内します。マイクのアクセス許可やWindowsの入力機器設定は自動変更しません。
- Fixed Setup checkbox rendering, added an explicit Start menu task, highlighted the current recorded utterance, and enabled default-microphone Windows captions integration with setup guidance.

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
