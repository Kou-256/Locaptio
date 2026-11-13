export const languages = {
  ja: {
    title:'Locaptio — Windowsの日本語文字起こし・英日翻訳',
    description:'Windows 11の日本語文字起こし・英日翻訳アプリ。字幕、PC音声とマイクの録音、AI補正、話者の色分け。無料・オープンソースで、認識はローカル処理。',
    nav:['機能','はじめ方','English'], switch:'/en', skip:'本文へ移動',
    eyebrow:'Windowsの文字起こし・英日翻訳', headline:['日本語の文字起こし','英語から日本語への翻訳'],
    intro:'Windows 11向けの**日本語文字起こし・英日翻訳**アプリです。PC音声やマイク音声を取り込み、字幕表示、録音、テキスト保存ができます。',
    download:'Windows版をダウンロード', guide:'使い方を見る', meta:'Windows 11 24H2以降 / x64 / 無料・MIT License',
    release:'更新内容', version:'現在の配布版', preview:'字幕の表示イメージ。音声の取得・再生は行いません。',
    demoModes:['日本語の文字起こし','英語 → 日本語'], demoSource:'PC音声 ＋ マイク',
    demoJa:'字幕の文字サイズと表示行数は、\n設定画面で変更できます。', demoEn:'You can change the caption size in Settings.', demoTranslation:'字幕の文字サイズは設定画面で変更できます。',
    demoNote:'字幕表示のプレビュー',
    highlights:[['caption','独立字幕と表示設定','本体とは別に字幕だけを表示。英日翻訳と日本語文字起こしを切り替え、動画や会議と並べて読めます。'],['record','音声録音とテキスト保存','PC音声・マイク・両方の入力に対応。録音はWAV・MP3・M4A、文字起こしはテキストで残せます。'],['local','ローカルでの認識・翻訳','Whisper、翻訳モデル、ローカルLLMをPC内で実行。Windowsライブキャプションの認識結果を使う方式も選べます。']],
    featureTitle:'機能一覧', featureIntro:'表示、入力、認識、保存。それぞれを自分の使い方に合わせて設定できます。',
    groups:[
      {id:'captions',label:'字幕と表示',icon:'caption',title:'字幕の表示設定',items:[
        ['独立字幕と本体の同時表示','字幕ボタンで本体を最小化し、字幕だけを表示。認識した文章は本体にも残ります。無音時は一定時間後に字幕を隠します。'],
        ['最新の文章を表示','字幕の最大行数を設定可能。長い発話は「…」で省略せず、収まる**最新部分**を表示します。最新の発話をひと塊で表示するモードも選べます。'],
        ['文字と背景を別々に調整','文字色、背景色、枠、文字サイズ、太さ、行間、配置、**折り返し幅**を変更できます。**背景と文字の透明度は独立**して設定できます。初期値は本体背景透過率10%、字幕20%、英語水色・日本語白です。'],
        ['英語と日本語の位置を選ぶ','英語が上、日本語が上、左右表示を切り替え。補正済みの文字を別の色にするか、通常と同じ色にするかも選べます。'] ]},
      {id:'recording',label:'録音と保存',icon:'record',title:'音声入力・録音・テキスト保存',items:[
        ['入力を選ぶ','**Windows字幕連携ではPC内音声／PC内音声＋マイク**を選択。マイク単独・指定アプリ・指定機器はWhisperを選んだときに使えます。Windows連携からWhisperへ勝手に切り替わりません。'],
        ['名前を付けてから録音','録音にチェックすると保存画面を開き、**名前と場所を確定してから開始**します。録音ごとに**音声・JSON・TXTを専用フォルダー**へ保存。チェックを外すと録音と文字起こしを停止し、Startを再度押せます。Stopは認識・録音・字幕を停止します。'],
        ['テキストに保存情報を添える','**保存日時、セッション時間、本文行数・発話数**の記載を個別にON/OFF。初期ファイル名は年月日＋「_」で、設定から書式を変更できます。'],
        ['文字から録音を聞き直す','**文字のクリックや検索結果から再生**。日本語・英語・訳文を検索し、録音とJSONを一緒に保存して再度開けます。**再生中の発話をハイライト**します。発話単位の時刻で、Windows字幕は推定です。再生は録音停止後に使えます。'],
        ['音声ファイルから文字起こし','**MP3・M4A・WAVを読み込み**、日本語文字起こしや英日翻訳を実行。進捗を表示し、Stopで中止できます。原本を残し、コピーと文章を録音一覧へ保存します。'],
        ['録音を整理・共有','録音一覧から選んで、音声・文章・検索データを**共有ZIPとして保存／別の場所へ移動／確認後にごみ箱へ削除**できます。'],
        ['新規録音と録音一覧','**新規録音**は新しい会話を開始し、**録音一覧・再生**は保存済みの音声と文章を開きます。名前と日時から選べます。'],
        ['保存先と形式を指定','文章と音声の保存先は別々に設定。WAV、MP3、M4Aの録音形式を選択できます。'] ]},
      {id:'recognition',label:'認識とAI',icon:'wave',title:'音声認識とAI補正',items:[
        ['音声モデルを自動準備','Whisper用の日本語モデルやPython環境がなければ、**Start・ファイル読込で初回のみ自動取得**します。手動でPowerShellを実行する必要はありません。ネット接続と数GBの空き容量が必要です。'],
        ['Windows字幕連携とWhisper','Windowsライブキャプションの認識結果を取り込む方式と、アプリ単独のWhisperを選択。Windows側の認識言語の切り替えも補助します。PC内音声＋マイクではWindowsの「マイクオーディオを含める」を自動でON、PC内音声へ戻すとOFFにします。PC音声と重なるとPC音声が優先されます。'],
        ['CPU・CUDA・AMD NPU','WhisperはCPU、GPU自動、**NVIDIA CUDA**、**AMD XDNA 2 NPU**を選べます。NPUには専用モデルと環境の準備が必要です。Windows字幕の使用デバイスはWindows側に従います。'],
        ['速報と文脈AI補正','軽いモデルで先に字幕を出し、後から高精度モデルやローカルLLMで更新します。AI補正は**本体のチェックボックス**で切り替えられ、Windows字幕連携にも対応します。LLM環境・既定モデルがなければ初回に自動取得します。'],
        ['翻訳・話者の色分け','英日翻訳は翻訳モデルを使用し、任意のローカルLLMで文脈を改善。追加モデルを準備すると声の特徴から話者を分け、**最大8色**で表示できます。'] ]},
      {id:'controls',label:'操作と更新',icon:'settings',title:'コピー・履歴・アプリの更新',items:[
        ['選択してCtrl+C','ドラッグで複数行を選択し、離した後も範囲を保持。**Ctrl+Cや右クリック**でコピーできます。選択中も認識と録音は続きます。'],
        ['履歴を読み返す','本体の履歴は**行数制限なし**。最新の発話が見えている間は自動追従し、過去へ戻ると位置を保持。「最新へ」で追従を再開できます。**音声入力を切り替えても会話履歴を保持**します。'],
        ['カテゴリ別の設定','SVGアイコン付きの左メニューから設定を選び、右側で調整。モデルの説明はヘルプから、準備の進み具合は本体から確認できます。'],
        ['更新確認とWindows字幕の復旧','起動時は更新の有無だけを確認（設定でOFF可）。**ダウンロードを押すまで更新ファイルを取得しません**。PC音声があるのにWindows字幕の更新が止まった場合の自動復旧も設定でき、既定の待ち時間は**3分**です。Stopや×終了でWindows字幕も閉じます。'] ]}
    ],
    modelNote:'認識・翻訳の精度と速度は、音声・モデル・PCの負荷によって変わります。AI補正や話者識別も誤る場合があります。Windows字幕と同じ性能を保証するものではありません。',
    settingsTitle:'カテゴリ別の設定画面', settingsText:'字幕、AI、保存先、更新。9つのカテゴリから必要な項目へ。設定画面には現在のバージョンと更新確認の結果も表示します。', settingsCaption:'Locaptio v1.0.38の設定画面（日本語UI）',
    startTitle:'ダウンロードと初期設定', note:'日本語のWindows字幕連携なら、Python・Whisperモデルなしで試せます。英日翻訳・Whisper・AI補正は初回のモデル準備が必要です。AI補正のLLMは未準備ならアプリが自動取得します。',
    steps:[['Setupをダウンロードして実行','**Locaptio-Setup-1.0.44-x64.exe**を開き、画面に従ってインストールしてください。管理者権限は不要です。インストール先も選べます。スタートメニュー・デスクトップへの追加も選べます。'],['スタートメニューから起動','Locaptioを開きます。.NETとWindows App SDKは同梱しています。ZIPの展開は不要です。'],['認識方式と入力を選んでStart','設定でWindowsライブキャプション連携かWhisperを選択。録音は保存する名前を決めてから始まります。']],
    details:'セットアップガイド', faqTitle:'使い始める前に',
    faq:[['インターネット接続は必要ですか？','モデルの初回取得や任意の更新確認・Setup取得には接続が必要です。モデル準備後の認識・翻訳はローカルで実行し、音声や文章を**クラウドへ送信しません**。このサイトも音声を取得しません。'],['Windows字幕を開かずに使えますか？','Whisperを選ぶと、Windowsライブキャプションを起動せずに認識できます。Windows字幕連携を使う場合は、Windows側の字幕も起動します。'],['GPUやNPUは必須ですか？','必須ではなくCPUでも使えます。CUDAには対応NVIDIA GPUと推論ライブラリ、AMD NPUには対応機種と専用環境が必要です。WhisperのStart・ファイル読込時に不足する専用環境を自動準備します。対応ハードウェアとドライバーは別途必要で、NPU対応は実験的です。'],['長時間使うとPCが重くなる場合は？','保存・Stop後にWindows字幕を閉じ、認識エンジンをWhisperへ切り替えて比較してください。Windows字幕自体の長時間負荷が解消したとは確認できていません。'],['更新は自動でインストールされますか？','いいえ。起動時は更新情報だけを確認します。**ダウンロードを押した場合だけ**公式GitHubの新版Setupを保存し、SHA-256を確認します。「セットアップを実行」を選んで更新できます。ダウンロードだけで勝手にインストールしません。']],
    caveat:'未署名の開発ビルドです。アプリの画面表示は主に日本語です。音声認識モデル・推論ライブラリにはそれぞれの利用条件があります。',
    checksums:'SHA-256で配布ファイルを確認', checksumCopy:'SHA-256をコピー', copied:'コピーしました', copyFailed:'コピーできませんでした。値を選択してコピーしてください。',
    license:'MIT License', help:'問題を報告', privacy:'音声入力なし / 広告・解析ツールなし / 外部フォントの通信なし', menu:'メニュー', closeMenu:'メニューを閉じる'
  },
  en: {
    title:'Locaptio — Japanese transcription and translation for Windows', description:'Local Japanese transcription and English-to-Japanese translation for Windows 11. Floating captions, system audio and mic recording, AI correction and speaker colors. Free and open source.',
    nav:['Features','Get started','日本語'], switch:'/', skip:'Skip to content', eyebrow:'Local captions for Windows', headline:['Japanese transcription','English → Japanese'],
    intro:'A Windows 11 app for **Japanese transcription** and **English-to-Japanese translation**. Capture PC or microphone audio, display captions, record audio and export transcripts.', download:'Download for Windows', guide:'How to get started', meta:'Windows 11 24H2+ / x64 / Free / MIT License',
    release:'Release notes', version:'Current release', preview:'Caption appearance demo. No audio is captured or played.', demoModes:['Japanese transcription','English → Japanese'], demoSource:'System audio + mic',
    demoJa:'字幕の文字サイズと表示行数は、\n設定画面で変更できます。', demoEn:'You can change the caption size in Settings.', demoTranslation:'字幕の文字サイズは設定画面で変更できます。', demoNote:'Caption display preview',
    highlights:[['caption','Floating captions and display settings','Open a separate caption overlay alongside videos and meetings. Switch between Japanese transcription and English-to-Japanese translation.'],['record','Audio recording and text export','Capture system audio, microphone, or both. Save recordings as WAV, MP3 or M4A, and export transcripts as text.'],['local','Recognition stays on your PC','Run Whisper, translation models and an optional local LLM locally, or import recognition results from Windows Live Captions.']],
    featureTitle:'Features',featureIntro:'Choose how you see, capture, recognize and save your audio.',
    groups:[
      {id:'captions',label:'Captions',icon:'caption',title:'Caption display settings',items:[
        ['Overlay and main transcript','The caption button minimizes the main window and opens a separate overlay. The transcript stays in the main window. Captions hide after a configurable idle period.'],
        ['Show the latest words','Set a maximum number of caption lines. Long utterances show the **latest fitting text** instead of an ellipsis. An optional mode displays only the latest utterance as a block.'],
        ['Separate text and background controls','Adjust text, background and border colors, font size, weight, spacing and alignment. **Text and background opacity are independent.**'],
        ['Choose the language layout','Put English or Japanese on top, or show them side by side. Corrected text can keep the normal color or use a custom one.'] ]},
      {id:'recording',label:'Recording & export',icon:'record',title:'Audio input, recording and text export',items:[
        ['Choose your input','**Windows integration offers PC audio or PC audio + microphone**. Microphone-only, per-app and specific-device inputs are available when Whisper is explicitly selected. Windows integration does not silently switch to Whisper.'],
        ['Name it before recording','Checking Record opens the filename dialog. Capture starts **after confirmation**. Each recording gets its **own folder with audio, JSON and TXT**. Unchecking stops recording and transcription; Start becomes available again. Saved audio opens from the recording library.'],
        ['Search and replay from text','**Click transcript text or a timestamped search result to replay**. Source and translated text are searchable. Reopen audio with its JSON sidecar later. The **current utterance is highlighted**. Timing is utterance-level and estimated for Windows captions; replay is available after recording stops.'],
        ['Optional export details','Independently include **save time, session duration and content-line/utterance counts**. Suggested filenames use a date prefix and can be customized in Settings.'],
        ['Transcribe audio files','**Import MP3, M4A or WAV** for Japanese transcription or English-to-Japanese translation. Track progress and cancel with Stop. The original is preserved; a copy and transcript are added to the library.'],
        ['Manage and share recordings','Select a library recording to **save a shareable ZIP, move the audio/transcript bundle, or delete it to the Recycle Bin after confirmation**. ZIP exports stay local until you choose to send them.'],
        ['New recording versus replay','**New recording** starts a fresh session; **Recordings / Replay** opens saved audio and text by name/date without starting capture.'],
        ['Folders and formats','Choose separate folders for transcripts and audio. WAV, MP3 and M4A recording formats are available.'] ]},
      {id:'recognition',label:'Recognition & AI',icon:'wave',title:'Recognition engines and AI correction',items:[
        ['Automatic speech setup','Missing Whisper models and Python components are **downloaded automatically on Start or file import**. No manual PowerShell commands are required. Initial setup needs internet access and several GB of free space.'],
        ['Windows captions or Whisper','Windows integration offers **PC audio or PC audio plus microphone**, automatically enabling/disabling the Windows microphone option. Select Whisper explicitly for microphone-only, specific-device or per-app capture.'],
        ['CPU, CUDA and AMD NPU','Choose CPU, automatic GPU, **NVIDIA CUDA** or **AMD XDNA 2 NPU** for Whisper. NPU use requires dedicated models and runtime setup. Windows captions follow Windows device selection.'],
        ['Fast preview, contextual correction','A lightweight model can show early text before more accurate recognition or local LLM correction. Toggle AI correction on the main screen, including for imported Windows captions. Missing LLM dependencies and the default model are prepared automatically.'],
        ['Translation and speaker colors','A translation model provides English-to-Japanese text; an optional local LLM improves context. Additional speaker models can distinguish voices and assign **up to eight colors**.'] ]},
      {id:'controls',label:'Controls & updates',icon:'settings',title:'Copy, history and app updates',items:[
        ['Select and copy','Drag across multiple rows. The range stays highlighted after release and copies with **Ctrl+C or the context menu**. Recognition and recording continue during selection.'],
        ['Browse the full history','The main transcript has **no row limit**. Auto-follow continues while the latest utterance is visible; scrolling back preserves your position. Return to latest to resume. **Switching audio input retains the conversation**.'],
        ['Organized settings','An SVG-icon sidebar opens each settings category on the right. Model explanations are in Help; preparation progress appears on the main screen.'],
        ['Updates and Windows caption recovery','Startup checks only fetch release information and can be disabled. **Update files are not downloaded until you click Download**. Configure Windows caption stall recovery and its timeout, defaulting to **three minutes**. Stop or app exit also closes Windows captions.'] ]}
    ],
    modelNote:'Accuracy and speed depend on audio, models and PC load. AI correction and speaker identification can make mistakes. Performance equivalent to Windows Live Captions is not guaranteed.',
    settingsTitle:'Settings organized by category',settingsText:'Captions, AI, folders and updates: nine categories keep the controls in reach. The update page shows the current version and check results.',settingsCaption:'Locaptio v1.0.38 settings (Japanese interface)',
    startTitle:'Download and initial setup',note:'Try Japanese Windows captions integration without Python or Whisper models. English-to-Japanese translation, Whisper and AI correction require initial model setup.',steps:[['Download and run Setup','Open **Locaptio-Setup-1.0.44-x64.exe** and follow the installer. Administrator rights are not required; the destination directory is selectable. Start menu and desktop shortcuts are optional.'],['Launch from Start','Open Locaptio from the Start menu. .NET and the Windows App SDK are included; no ZIP extraction is needed.'],['Choose an engine and input, then Start','Select Windows captions integration or Whisper in Settings. Recording begins after you confirm a filename.']],details:'Read the setup guide',faqTitle:'Before you start',
    faq:[['Do I need an internet connection?','Initial model downloads, optional update checks and Setup downloads need a connection. Once models are ready, recognition and translation run locally without sending audio or transcripts to the cloud. This website does not capture audio.'],['Can I use it without opening Windows captions?','Choose Whisper to recognize independently, without launching Windows Live Captions. Windows integration supports PC audio or PC audio plus the default microphone. Locaptio turns Include microphone audio ON for the latter and OFF when switching back. PC audio takes priority when sources overlap. Microphone-only and per-app inputs are available when Whisper is explicitly selected.'],['Is a GPU or NPU required?','No, CPU inference is available. CUDA requires compatible NVIDIA hardware and inference libraries; AMD NPU use needs supported hardware and a dedicated runtime. Start or file import prepares missing runtime components automatically. Compatible hardware and drivers are still required; AMD NPU support is experimental.'],['What if long sessions slow down my PC?','Save and Stop, close Windows captions, then compare with Whisper. Long-session slowdowns caused by Windows captions itself have not been confirmed fixed.'],['Are updates installed automatically?','No. Startup checks only fetch release information. **Update files are downloaded only after you click Download**, then verified with SHA-256. An **approval popup** appears before Setup can run. Choose Later to keep the current session running.']],
    caveat:'An unsigned development build. The app interface is currently mostly Japanese. Models and inference libraries have their own license terms.',checksums:'Verify the download with SHA-256',checksumCopy:'Copy SHA-256',copied:'Copied',copyFailed:'Copy failed. Select and copy the displayed value.',license:'MIT License',help:'Report an issue',privacy:'No audio input / No ads or analytics / No external font requests',menu:'Menu',closeMenu:'Close menu'
  }
};
