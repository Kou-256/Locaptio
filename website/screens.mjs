// Real Locaptio screenshots (v1.0.48, Japanese interface) taken by the app's own
// `--verify-site-screenshots` mode with fictional sample text. Sizes are for layout stability.
export const screenSizes = {
  'bookmarks': [1500, 640], 'caption-ja': [1600, 900], 'compare': [1600, 703],
  'copy-menu': [1500, 660], 'dictionary': [1600, 779], 'edit': [1400, 880], 'file-import': [1600, 669],
  'library': [1350, 770], 'playback-search': [1600, 1027], 'recording': [1600, 782], 'recovery': [1534, 980],
  'settings-captions': [1600, 1013], 'settings-speech': [1600, 1013], 'subtitles': [1400, 690], 'summary': [1434, 1080],
  'sync': [1600, 775], 'translate': [1600, 606],
  'record-picker': [1600, 1219], 'video-recording': [1600, 543], 'video-review': [1600, 940], 'video-burn': [1230, 1040]
};

// Alt text describes what the image shows; the caption says what to notice in it.
export const screens = {
  ja: {
    label: '実際の画面', note: '画面の文章はサンプルです。',
    useCases: [
      ['caption-ja', '動画の上に重ねたLocaptioの字幕ウィンドウ。日本語の発話が2行で表示されている', '字幕ウィンドウは背景をドラッグして好きな位置へ動かせます。'],
      ['translate', '本体の画面。左に英語の原文、右に日本語訳が1文ずつ並んでいる', '英語と日本語訳を並べて表示。訳はローカルLLMで文脈に合わせて更新されます。'],
      ['recording', '録音中の本体。赤い録音中の表示、経過時間、ブックマークの一覧、会議の文字起こしが表示されている', '録音中は経過時間と保存先を表示。ブックマークは上に一覧で並びます。'],
      ['file-import', '音声ファイルを文字起こし中の画面。下部に62%の進捗バーと中止ボタンがある', '認識した部分から順に表示され、進捗と中止ボタンを下部に表示します。'],
      ['playback-search', '保存済み録音の再生画面。再生バー、検索欄と検索結果、再生位置の単語のハイライトが表示されている', '検索結果をクリックするとその発話から再生。再生中の単語をハイライトします。'],
      ['library', '録音一覧のダイアログ。録音名と日時、共有ZIP作成・移動・削除のボタンが並んでいる', '録音ごとに音声と文章をまとめて開く・共有・移動・削除できます。']
    ],
    groups: {
      captions: ['settings-captions', '設定の「字幕の見た目」ページ。字幕の配置、文字サイズ、行間、文字色の設定が並んでいる', '字幕の配置・サイズ・色・透明度を細かく調整できます。'],
      recording: ['recording', '録音中の本体。録音中の表示とブックマークの一覧', '入力の選択、録音、ブックマークを1つの画面で操作します。'],
      recognition: ['settings-speech', '設定の「音声認識・Windows連携」ページ。認識エンジンと推論デバイスの選択', '認識エンジン・デバイス・モデルは設定画面で選びます。'],
      controls: ['copy-menu', '複数行を選択して右クリックしたメニュー。コピー、この発話を編集、ブックマークを付ける、用語辞書に登録などの項目', '選択した文章を右クリックして、コピー・編集・ブックマーク・用語登録ができます。']
    },
    whatsNew: {
      autosave: ['recovery', '前回の未保存セッションのダイアログ。自動保存の日時、発話数、文章のプレビュー、復元・破棄・あとで決めるのボタン', '起動時に、自動保存された内容を確認してから復元できます。'],
      edit: ['edit', '発話を編集するダイアログ。修正後の文章の入力欄と、その下に認識時の原文、原文に戻すボタン', '修正欄の下に、AI補正や辞書で変わる前の原文を表示します。'],
      retranscribe: ['compare', '文字起こし結果の比較ダイアログ。現在の結果とmediumモデルの新しい結果が時刻ごとに並び、違う箇所が色付きで表示されている', '違う箇所だけを時刻順に並べ、採用するかを選べます。'],
      dictionary: ['dictionary', '設定の用語辞書ページ。正しい表記、誤認識されやすい表記、日本語訳の3列の表', '正しい表記・誤認識されやすい表記・訳語を表で登録します。'],
      sync: ['sync', '同期を改善した録音の再生画面。再生位置の単語がハイライトされ、下部に単語時刻を付けたというメッセージ', '単語ごとの時刻が付き、再生中の単語を正確にハイライトします。'],
      subtitles: ['subtitles', '字幕ファイル（SRT・VTT）を書き出すダイアログ。字幕にする文章の選択と保存先を選ぶボタン', '書き出す文章を選び、保存画面でSRTかVTTを選びます。'],
      bookmarks: ['bookmarks', 'ツールバーの重要ボタンと、重要・後で確認・TODOのブックマーク一覧。文字起こしの行の左に黄色い印', 'ブックマークは時刻付きで一覧に並び、印の付いた行は左端に黄色い線が出ます。'],
      summary: ['summary', 'AI要約のパネル。要点と決定事項の項目ごとに、根拠の発話の時刻へのリンクがある', '各項目の時刻を押すと、根拠の発話へ移動して再生します。']
    }
  },
  en: {
    label: 'Actual screen', note: 'Screens show the Japanese interface with sample text.',
    useCases: [
      ['caption-ja', 'The Locaptio caption window over a video, showing a Japanese utterance on two lines', 'Drag the caption background to place the window anywhere.'],
      ['translate', 'The main window with English source sentences on the left and Japanese translations on the right', 'English and Japanese side by side; a local LLM refines translations in context.'],
      ['recording', 'The main window while recording: a red recording indicator, elapsed time, a row of bookmarks and the meeting transcript', 'Elapsed time and the save location are shown while recording; bookmarks line up above the text.'],
      ['file-import', 'Transcribing an audio file: a progress bar at 62% and a cancel button at the bottom', 'Text appears as it is recognized, with progress and a cancel button at the bottom.'],
      ['playback-search', 'Replaying a saved recording: playback bar, search box with results and the current word highlighted', 'Click a search result to replay from that utterance; the current word is highlighted.'],
      ['library', 'The recordings library dialog listing recordings with buttons to create a share ZIP, move or delete', 'Open, share, move or delete each recording together with its text.']
    ],
    groups: {
      captions: ['settings-captions', 'The caption appearance settings page with layout, size, spacing and color controls', 'Fine-tune caption layout, size, colors and transparency.'],
      recording: ['recording', 'The main window while recording, with the recording indicator and bookmarks', 'Input selection, recording and bookmarks in one window.'],
      recognition: ['settings-speech', 'The recognition settings page with engine and inference device selection', 'Choose the engine, device and model in Settings.'],
      controls: ['copy-menu', 'The context menu for selected rows: copy, edit this utterance, bookmark, register in the term dictionary', 'Right-click selected text to copy, edit, bookmark or register a term.']
    },
    whatsNew: {
      autosave: ['recovery', 'The previous-session dialog showing the autosave time, utterance count, a text preview and Restore, Discard and Decide later buttons', 'Review what was autosaved before restoring it at launch.'],
      edit: ['edit', 'The edit dialog with the corrected text and, below it, the recognized original with a restore button', 'The original recognized text appears under the edit box.'],
      retranscribe: ['compare', 'The comparison dialog listing the current and medium-model results by time, with differences highlighted', 'Only the differences, in time order; choose which result to keep.'],
      dictionary: ['dictionary', 'The term dictionary page: a table of correct spellings, misheard spellings and Japanese translations', 'Register spellings, misheard forms and translations in a table.'],
      sync: ['sync', 'Replaying a synced recording with the current word highlighted and a message that word times were added', 'With word times, the word being spoken is highlighted precisely.'],
      subtitles: ['subtitles', 'The subtitle export dialog with the text choice and a button to choose where to save', 'Choose the text, then SRT or VTT in the save dialog.'],
      bookmarks: ['bookmarks', 'The Important button on the toolbar and a row of Important, Check later and TODO bookmarks; marked rows have a yellow bar', 'Bookmarks are listed with their times; marked rows get a yellow bar.'],
      summary: ['summary', 'The AI summary panel: key points and decisions, each with links to the times of its source utterances', 'Click a time to jump to and replay the source utterance.']
    }
  }
};
