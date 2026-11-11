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
        ['文字と背景を別々に調整','文字色、背景色、枠、文字サイズ、太さ、行間、配置を変更できます。**背景と文字の透明度は独立**して設定できます。'],
        ['英語と日本語の位置を選ぶ','英語が上、日本語が上、左右表示を切り替え。補正済みの文字を別の色にするか、通常と同じ色にするかも選べます。'] ]},
      {id:'recording',label:'録音と保存',icon:'record',title:'音声入力・録音・テキスト保存',items:[
        ['入力を選ぶ','PC全体、マイク、特定アプリ、PC音声＋マイクを選択できます。混合入力やアプリ別の認識にはWhisperを使用します。'],
        ['名前を付けてから録音','録音にチェックすると保存画面を開き、**名前と場所を確定してから開始**します。チェックを外すと録音だけ停止。Stopは認識・録音・字幕を停止します。'],
        ['テキストに保存情報を添える','**保存日時、セッション時間、本文行数・発話数**の記載を個別にON/OFF。初期ファイル名は年月日＋「_」で、設定から書式を変更できます。'],
        ['保存先と形式を指定','文章と音声の保存先は別々に設定。WAV、MP3、M4Aの録音形式を選択できます。'] ]},
      {id:'recognition',label:'認識とAI',icon:'wave',title:'音声認識とAI補正',items:[
        ['Windows字幕連携とWhisper','Windowsライブキャプションの認識結果を取り込む方式と、アプリ単独のWhisperを選択。Windows側の認識言語の切り替えも補助します。'],
        ['CPU・CUDA・AMD NPU','WhisperはCPU、GPU自動、**NVIDIA CUDA**、**AMD XDNA 2 NPU**を選べます。NPUには専用モデルと環境の準備が必要です。Windows字幕の使用デバイスはWindows側に従います。'],
        ['速報と文脈AI補正','軽いモデルで先に字幕を出し、後から高精度モデルやローカルLLMで更新します。AI補正は**本体のチェックボックス**で切り替えられ、Windows字幕連携にも対応します。'],
        ['翻訳・話者の色分け','英日翻訳は翻訳モデルを使用し、任意のローカルLLMで文脈を改善。追加モデルを準備すると声の特徴から話者を分け、**最大8色**で表示できます。'] ]},
      {id:'controls',label:'操作と更新',icon:'settings',title:'コピー・履歴・アプリの更新',items:[
        ['選択してCtrl+C','ドラッグで複数行を選択し、離した後も範囲を保持。**Ctrl+Cや右クリック**でコピーできます。選択中も認識と録音は続きます。'],
        ['履歴を読み返す','本体の履歴は**行数制限なし**。最新の発話が見えている間は自動追従し、過去へ戻ると位置を保持。「最新へ」で追従を再開できます。'],
        ['カテゴリ別の設定','SVGアイコン付きの左メニューから設定を選び、右側で調整。モデルの説明はヘルプから、準備の進み具合は本体から確認できます。'],
        ['更新確認とWindows字幕の復旧','起動時の更新確認・新版ZIPの自動取得は個別にON/OFF。PC音声があるのにWindows字幕の更新が止まった場合の自動復旧も設定でき、既定の待ち時間は**3分**です。Stopや×終了でWindows字幕も閉じます。'] ]}
    ],
    modelNote:'認識・翻訳の精度と速度は、音声・モデル・PCの負荷によって変わります。AI補正や話者識別も誤る場合があります。Windows字幕と同じ性能を保証するものではありません。',
    settingsTitle:'カテゴリ別の設定画面', settingsText:'字幕、AI、保存先、更新。9つのカテゴリから必要な項目へ。設定画面には現在のバージョンと更新確認の結果も表示します。', settingsCaption:'Locaptio v1.0.38の設定画面（日本語UI）',
    startTitle:'ダウンロードと初期設定', note:'日本語のWindows字幕連携なら、Python・Whisperモデルなしで試せます。英日翻訳・Whisper・AI補正は初回のモデル準備が必要です。',
    steps:[['ZIPをダウンロードして展開','**フォルダー全体**を展開してください。exeだけを取り出すと動作しません。'],['Locaptio.exeを起動','展開したフォルダーのLocaptio.exeを開きます。.NETとWindows App SDKは同梱しています。'],['認識方式と入力を選んでStart','設定でWindowsライブキャプション連携かWhisperを選択。録音は保存する名前を決めてから始まります。']],
    details:'セットアップガイド', faqTitle:'使い始める前に',
    faq:[['インターネット接続は必要ですか？','モデルの初回取得や任意の更新確認・ZIP取得には接続が必要です。モデル準備後の認識・翻訳はローカルで実行し、音声や文章を**クラウドへ送信しません**。このサイトも音声を取得しません。'],['Windows字幕を開かずに使えますか？','Whisperを選ぶと、Windowsライブキャプションを起動せずに認識できます。Windows字幕連携を使う場合は、Windows側の字幕も起動します。'],['GPUやNPUは必須ですか？','必須ではなくCPUでも使えます。CUDAには対応NVIDIA GPUと推論ライブラリ、AMD NPUには対応機種と専用環境が必要です。設定の選択だけでは環境の導入は完了しません。詳しくはガイドをご覧ください。'],['長時間使うとPCが重くなる場合は？','保存・Stop後にWindows字幕を閉じ、認識エンジンをWhisperへ切り替えて比較してください。Windows字幕自体の長時間負荷が解消したとは確認できていません。'],['更新は自動でインストールされますか？','いいえ。自動取得をONにすると公式GitHubの新版ZIPを保存し、SHA-256を確認します。ZIPを別フォルダーへ展開して起動してください。録音中のアプリを自動置換しません。']],
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
        ['Choose your input','Capture all PC audio, a microphone, one application, or system audio and mic together. Mixed input and application-specific recognition use Whisper.'],
        ['Name it before recording','Checking Record opens the filename dialog. Capture starts **after confirmation**. Unchecking stops recording only; Stop ends recognition, recording and caption windows.'],
        ['Optional export details','Independently include **save time, session duration and content-line/utterance counts**. Suggested filenames use a date prefix and can be customized in Settings.'],
        ['Folders and formats','Choose separate folders for transcripts and audio. WAV, MP3 and M4A recording formats are available.'] ]},
      {id:'recognition',label:'Recognition & AI',icon:'wave',title:'Recognition engines and AI correction',items:[
        ['Windows captions or Whisper','Import Windows Live Captions results or recognize independently with Whisper. Locaptio can also help switch the Windows recognition language.'],
        ['CPU, CUDA and AMD NPU','Choose CPU, automatic GPU, **NVIDIA CUDA** or **AMD XDNA 2 NPU** for Whisper. NPU use requires dedicated models and runtime setup. Windows captions follow Windows device selection.'],
        ['Fast preview, contextual correction','A lightweight model can show early text before more accurate recognition or local LLM correction. Toggle AI correction on the main screen, including for imported Windows captions.'],
        ['Translation and speaker colors','A translation model provides English-to-Japanese text; an optional local LLM improves context. Additional speaker models can distinguish voices and assign **up to eight colors**.'] ]},
      {id:'controls',label:'Controls & updates',icon:'settings',title:'Copy, history and app updates',items:[
        ['Select and copy','Drag across multiple rows. The range stays highlighted after release and copies with **Ctrl+C or the context menu**. Recognition and recording continue during selection.'],
        ['Browse the full history','The main transcript has **no row limit**. Auto-follow continues while the latest utterance is visible; scrolling back preserves your position. Return to latest to resume.'],
        ['Organized settings','An SVG-icon sidebar opens each settings category on the right. Model explanations are in Help; preparation progress appears on the main screen.'],
        ['Updates and Windows caption recovery','Startup checks and ZIP downloads are independently optional. Configure Windows caption stall recovery and its timeout, defaulting to **three minutes**. Stop or app exit also closes Windows captions.'] ]}
    ],
    modelNote:'Accuracy and speed depend on audio, models and PC load. AI correction and speaker identification can make mistakes. Performance equivalent to Windows Live Captions is not guaranteed.',
    settingsTitle:'Settings organized by category',settingsText:'Captions, AI, folders and updates: nine categories keep the controls in reach. The update page shows the current version and check results.',settingsCaption:'Locaptio v1.0.38 settings (Japanese interface)',
    startTitle:'Download and initial setup',note:'Try Japanese Windows captions integration without Python or Whisper models. English-to-Japanese translation, Whisper and AI correction require initial model setup.',steps:[['Download and extract the ZIP','Extract **the entire folder**. The executable needs the other files alongside it.'],['Open Locaptio.exe','Launch Locaptio.exe from the extracted folder. .NET and the Windows App SDK are included.'],['Choose an engine and input, then Start','Select Windows captions integration or Whisper in Settings. Recording begins after you confirm a filename.']],details:'Read the setup guide',faqTitle:'Before you start',
    faq:[['Do I need an internet connection?','Initial model downloads, optional update checks and ZIP downloads need a connection. Once models are ready, recognition and translation run locally without sending audio or transcripts to the cloud. This website does not capture audio.'],['Can I use it without opening Windows captions?','Choose Whisper to recognize independently, without launching Windows Live Captions. The Windows integration option opens Windows captions too.'],['Is a GPU or NPU required?','No, CPU inference is available. CUDA requires compatible NVIDIA hardware and inference libraries; AMD NPU use needs supported hardware and a dedicated runtime. Selecting a device does not install these requirements. See the guide for setup.'],['What if long sessions slow down my PC?','Save and Stop, close Windows captions, then compare with Whisper. Long-session slowdowns caused by Windows captions itself have not been confirmed fixed.'],['Are updates installed automatically?','No. Optional automatic downloads save a new official GitHub ZIP and verify SHA-256. Extract it into a separate folder and open the new version. The running app is not replaced automatically.']],
    caveat:'An unsigned development build. The app interface is currently mostly Japanese. Models and inference libraries have their own license terms.',checksums:'Verify the download with SHA-256',checksumCopy:'Copy SHA-256',copied:'Copied',copyFailed:'Copy failed. Select and copy the displayed value.',license:'MIT License',help:'Report an issue',privacy:'No audio input / No ads or analytics / No external font requests',menu:'Menu',closeMenu:'Close menu'
  }
};
