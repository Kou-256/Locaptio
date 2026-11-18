// "Major updates": the larger releases, newest first, summarized from CHANGELOG.md.
// Only user-visible features; small fixes stay in the changelog linked below the list.
export const updates = {
  ja: {
    nav: 'アップデート',
    title: 'これまでの主なアップデート',
    intro: '公開から現在までに追加した、おもな機能をまとめました。細かな修正を含むすべての変更は、GitHubの変更履歴で確認できます。',
    latest: '最新',
    all: 'すべての変更履歴を見る（GitHub）',
    items: [
      ['1.0.60', '字幕の文字色の既定を変更', [
        '翻訳時の既定を「原文: 白／翻訳: 水色」にし、「原文: 黄色／翻訳: 白」を追加しました。']],
      ['1.0.59', '字幕の文字色を「原文」「翻訳」で設定', [
        '色は言語ではなく役割で決まります。翻訳時は原文と翻訳、翻訳しないときは「文字起こしのみ」の色を別に選べます。']],
      ['1.0.58', '要約欄の表示をそろえ、AIの準備を早めに', [
        'ライブ要約がONなら、開始と同時に要約欄を表示。クリアやOFFで内容がなくなれば閉じます。',
        '「AI補正」「ライブ要約」にチェックを付けた時点で、AIモデルの準備を始めます。']],
      ['1.0.57', '要約欄のボタン表示を修正', [
        '要約欄が狭いときに「ライブ要約をクリア」が切れる問題を修正しました。']],
      ['1.0.56', 'ライブ要約を自分好みに、ウィンドウの重なりも切り替え', [
        'ライブ要約の**頻度（約1〜10分ごと）・1回に要約する量・長さ（短め／標準／詳しめ）**を設定で選べます。',
        '「ライブ要約をクリア」で、文章や全体の要約を残したままライブ要約だけを消せます。',
        '要約欄は**幅を変えたり、別ウィンドウで開いたり**できます。',
        '本体の「常に手前に表示」をON/OFFでき、**Ctrl+Alt+T**（変更可）でほかのアプリ使用中も切り替えられます。']],
      ['1.0.55', '文字起こし中も要約、要約は復元にも対応', [
        '文字起こし・録音を止めずに「今までを要約」で、そこまでの発話をまとめます。',
        '全体の要約とライブ要約は両方残り、起動し直して「復元」すると要約も戻ります。']],
      ['1.0.54', '日本語・英語・イタリア語・スペイン語に対応', [
        '話す言語と「文字起こしのみ」または翻訳先を**2つの選択欄**で選べます。英語の文字起こしも使えます。',
        'ライブ要約を録音中にもON/OFFでき、長くなると古い部分を「ここまでの流れ」にまとめ直します。',
        'ツールバーの**並び順の変更・項目の非表示**ができます。']],
      ['1.0.53', '日本語→英語・多言語の翻訳、コード署名', [
        '日本語の音声を英語などに翻訳。翻訳モデルは設定からあとでダウンロードします。',
        '配布物を自己署名証明書（CN=Locaptio）で署名し、作成者の表示と改ざんの検出ができるようにしました。']],
      ['1.0.52', 'ライブ要約、AI補正の見える化、その場で編集', [
        '数分ごとの発話をローカルAIが要約する**ライブ要約**を追加。',
        'AIが補正した箇所を色と「AI」印で示し、補正前の文章を確認・元に戻せます。',
        '文章のダブルクリックで直接編集、表示中のテキストを引き継いで録音、用語辞書のインポート・エクスポート。']],
      ['1.0.51', '録音した文章と音声の照合', [
        '録音の音声と本体の文章を1文字ずつ照合し、一致した発話には実際に話した単語の時刻を付けます。',
        '一致しない行が分かるので、聞き直すべき箇所を見つけやすくなります。']],
      ['1.0.50', '画面録画と字幕付き動画の書き出し', [
        '画面全体や特定のウィンドウを、PC音声・マイクと一緒に**MP4で録画**できます。',
        '動画を見返しながら発話を映像に重ねて表示し、**字幕を焼き込んだ動画**を書き出せます。',
        'MP4・MOVなどの動画ファイルからも文字起こしできます。']],
      ['1.0.48', '編集・自動保存・用語辞書・字幕ファイル・AI要約', [
        '作業の自動保存と、異常終了後の復元。',
        '発話の編集と元に戻す、録音の再文字起こしと比較、専門用語辞書。',
        '**SRT・VTT字幕**の出力、録音中のブックマーク、ローカルAIによる要点・決定事項・TODOの抽出。']],
      ['1.0.42', '音声ファイルの文字起こしと録音の管理', [
        'MP3・M4A・WAVの音声ファイルをPC内で文字起こし。',
        '録音一覧から共有用ZIPの保存、移動、削除ができます。']],
      ['1.0.39', 'Setupインストーラーと録音の聞き直し', [
        '管理者権限のいらない**Setup**でインストール・更新できるようになりました。',
        '文字をクリックすると、その発話から録音を再生して聞き直せます。']],
      ['1.0.23', '話者の色分け', [
        '声の特徴から話者を見分け、**最大8人**を色分けして表示します（PC内で処理）。']],
      ['1.0.17', '初回公開', [
        '英語→日本語の翻訳と日本語の文字起こし、独立した字幕ウィンドウ、PC音声・マイクの録音、テキスト保存。']],
    ]
  },
  en: {
    nav: 'Updates',
    title: 'Major updates so far',
    intro: 'The main features added since the first release. Every change, including small fixes, is in the changelog on GitHub.',
    latest: 'Latest',
    all: 'See the full changelog (GitHub)',
    items: [
      ['1.0.60', 'New default caption colors', [
        'While translating, the default is now original white / translation cyan; original yellow / translation white was added.']],
      ['1.0.59', 'Caption colors by role', [
        'Colors follow the role, not the language: original and translation while translating, and a separate color when only transcribing.']],
      ['1.0.58', 'Consistent summary pane; AI prepared earlier', [
        'With live summary on, the summary pane opens when listening starts and closes when clearing or turning it off leaves nothing to show.',
        'Checking "AI補正" or "ライブ要約" starts preparing the local AI model right away.']],
      ['1.0.57', 'Summary pane button fix', [
        'The summary pane buttons wrap instead of clipping "ライブ要約をクリア".']],
      ['1.0.56', 'Tune live summaries; switch "always on top"', [
        'Choose live summary **frequency (about 1–10 minutes), amount per summary and length (short / normal / detailed)**.',
        '"ライブ要約をクリア" clears live summaries while keeping the text and the full summary.',
        'The summary pane can be **resized or opened in its own window**.',
        '"Always on top" can be turned on or off, also with **Ctrl+Alt+T** (configurable) while another app is in use.']],
      ['1.0.55', 'Summaries while transcribing, and in restored sessions', [
        '"今までを要約" summarizes everything said so far without stopping transcription or recording.',
        'Full and live summaries are both kept, and come back when a session is restored after a restart.']],
      ['1.0.54', 'Japanese, English, Italian and Spanish', [
        'Choose the spoken language and transcription only or a translation target with **two selectors**. English transcription is available.',
        'Live summary can be turned on or off while recording; older parts are condensed into "so far".',
        'The toolbar can be **reordered and items hidden**.']],
      ['1.0.53', 'Japanese → English and multilingual translation, code signing', [
        'Translate Japanese speech into English and other languages; translation models download later from Settings.',
        'Release files are signed with a self-signed certificate (CN=Locaptio) to show the publisher and detect tampering.']],
      ['1.0.52', 'Live summary, visible AI corrections, in-place editing', [
        'Added **live summary**: the local AI summarizes every few minutes of speech.',
        'AI corrections are marked with color and an "AI" badge, with the original text one click away.',
        'Double-click to edit, keep existing text when recording, and import/export the term dictionary.']],
      ['1.0.51', 'Checking recorded text against the audio', [
        'Recorded audio is compared with the text character by character; matched utterances get the times of the spoken words.',
        'Lines that do not match stand out, so it is easy to find what to listen to again.']],
      ['1.0.50', 'Screen recording and subtitled video export', [
        '**Record the screen or a window to MP4** with system audio and microphone.',
        'Review videos with speech shown over the picture, and **export video with burned-in subtitles**.',
        'Transcribe video files such as MP4 and MOV.']],
      ['1.0.48', 'Editing, autosave, term dictionary, subtitle files and AI summaries', [
        'Autosave and recovery after an unexpected exit.',
        'Edit utterances with undo, re-transcribe and compare recordings, and use a term dictionary.',
        'Export **SRT and VTT subtitles**, add bookmarks while recording, and extract key points, decisions and TODOs with the local AI.']],
      ['1.0.42', 'Audio file transcription and recording management', [
        'Transcribe MP3, M4A and WAV files on your PC.',
        'Save shareable ZIPs, move and delete recordings from the library.']],
      ['1.0.39', 'Setup installer and recording replay', [
        'Install and update with a **Setup** that needs no administrator rights.',
        'Click text to replay the recording from that utterance.']],
      ['1.0.23', 'Speaker colors', [
        'Tell speakers apart by their voices and color **up to eight speakers** (processed on your PC).']],
      ['1.0.17', 'First public release', [
        'English-to-Japanese translation and Japanese transcription, floating captions, system audio and microphone recording, text export.']],
    ]
  }
};
