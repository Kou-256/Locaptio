// Top-page spotlights for the newest major features, each with real screenshots
// (captured by `Locaptio.exe --verify-site-screenshots --only-new` with fictional sample text).
export const spotlight = {
  ja: [
    {
      id: 'languages', nav: '4言語', badge: 'v1.0.54〜',
      title: '日本語・英語・イタリア語・スペイン語の文字起こしと翻訳',
      intro: '本体の上部で**話す言語**を選び、その隣で**「文字起こしのみ」か翻訳先**を選ぶだけ。4つの言語どうしを自由に組み合わせられます。',
      points: [
        '**それぞれの言語を単体で文字起こし**：日本語・英語・イタリア語・スペイン語。',
        '**ほかの3言語へ翻訳**：たとえばイタリア語を話してスペイン語で読む、日本語を英語にする、など。',
        '英語→日本語は標準で使えます。それ以外の翻訳は、設定から翻訳モデルを一度ダウンロードします（PC内で翻訳し、文章は外部へ送りません）。',
        'Windowsのライブキャプション連携でも、話す言語に合わせてWindows側の言語を自動で切り替えます。'
      ],
      main: ['languages-target', 'Locaptioの本体。上部で「イタリア語を話す」と「→ スペイン語に翻訳」を選び、左にイタリア語、右にスペイン語訳が並んでいる。翻訳先の一覧が開いている', '翻訳先は「文字起こしのみ」と、ほかの3言語から選べます。'],
      more: [
        ['languages-spoken', '話す言語の一覧を開いたところ。日本語・英語・イタリア語・スペイン語を話す、の4つが並んでいる', '話す言語は4つから選びます。']
      ]
    },
    {
      id: 'live-summary', nav: 'ライブ要約', badge: 'v1.0.52〜',
      title: '録音しながら、話の流れをライブ要約',
      intro: '録音・文字起こしを止めずに、数分ごとの発話をローカルAIが**要点・決定事項・TODO**にまとめて右の欄へ追加します。長い会議でも、いま何が決まったかがすぐ分かります。',
      points: [
        'ツールバーの**「ライブ要約」のチェック**で、録音中でもいつでもON/OFF。',
        '**「今までを要約」**を押すと、次のまとまりを待たずにそこまでを要約します。',
        '**頻度（約1〜10分ごと）・1回の量・長さ（短め／標準／詳しめ）**を設定で選べます。長くなると古い部分を「ここまでの流れ」にまとめ直します。',
        '要約欄は**幅を変えたり、別ウィンドウで大きく開いたり**できます。各項目の時刻を押すと、根拠の発話へ移動します。'
      ],
      main: ['live-summary', '録音中の本体。左に会議の文字起こし、右の要約欄に2つのまとまりの要約（要点・決定事項・TODO）が並んでいる', '録音中も、右の要約欄に数分ごとの要約が追加されていきます。'],
      more: [
        ['live-summary-window', '別ウィンドウで開いたAI要約。要点・決定事項・TODOが縦に並んでいる', '「別ウィンドウで開く」で、大きさを自由に変えられます。'],
        ['settings-live-summary', '設定の「AI文字補正」ページ。ライブ要約の頻度・1回に要約する量・長さの選択欄が並んでいる', '頻度・量・長さは設定で選べます。']
      ]
    }
  ],
  en: [
    {
      id: 'languages', nav: '4 languages', badge: 'v1.0.54+',
      title: 'Transcription and translation in Japanese, English, Italian and Spanish',
      intro: 'Choose the **spoken language** at the top of the window, and next to it **transcription only or a translation target**. Any of the four languages can be combined with the others.',
      points: [
        '**Transcribe each language on its own**: Japanese, English, Italian and Spanish.',
        '**Translate into one of the other three**: for example, hear Italian and read Spanish, or turn Japanese into English.',
        'English → Japanese works out of the box. Other directions use a translation model you download once in Settings (translated on your PC; text is not sent anywhere).',
        'With Windows Live Captions, Locaptio switches the Windows caption language to match the spoken language.'
      ],
      main: ['languages-target', 'The Locaptio window with “Italian” as the spoken language and “→ Spanish” as the target; Italian on the left, Spanish on the right, and the target list open', 'Targets are “transcription only” or one of the other three languages.'],
      more: [
        ['languages-spoken', 'The spoken-language list open, showing Japanese, English, Italian and Spanish', 'Choose from four spoken languages.']
      ]
    },
    {
      id: 'live-summary', nav: 'Live summary', badge: 'v1.0.52+',
      title: 'Live summaries while you record',
      intro: 'Without stopping, the local AI turns every few minutes of speech into **key points, decisions and TODOs** in the side pane, so you can see what has been decided during a long meeting.',
      points: [
        'Turn it on or off at any time, even while recording, with the **“ライブ要約” checkbox** on the toolbar.',
        '**“今までを要約”** summarizes everything so far without waiting for the next block.',
        'Choose **frequency (about 1–10 minutes), amount per summary and length (short / normal / detailed)**. Older parts are condensed into “so far”.',
        'The pane can be **resized or opened in its own window**. Click a time to jump to the utterance it is based on.'
      ],
      main: ['live-summary', 'The main window while recording: a meeting transcript on the left and two summary blocks (key points, decisions, TODOs) in the pane on the right', 'Summaries are added to the pane every few minutes while recording.'],
      more: [
        ['live-summary-window', 'The AI summary opened in its own window, with key points, decisions and TODOs', '“Open in a separate window” lets you resize it freely.'],
        ['settings-live-summary', 'The AI correction settings page with live summary frequency, amount and length selectors', 'Frequency, amount and length are set in Settings.']
      ]
    }
  ]
};

export const pages = {
  ja: {
    guideNav: '使い方', updatesNav: 'アップデート', home: 'トップ',
    guideTitle: '使い方の詳細', guideIntro: '録音の手順、文字起こしを直して残すための機能、認識エンジンの選び方、すべての機能と設定をまとめています。',
    updatesTitle: 'アップデート履歴',
    moreTitle: 'もっと詳しく',
    more: [
      ['guide', '使い方の詳細', '録音の手順、編集・辞書・字幕ファイル・AI要約などの機能、認識エンジンの選び方、すべての機能と設定。'],
      ['updates', 'アップデート履歴', '公開から最新版までに追加した、おもな機能の一覧。']
    ]
  },
  en: {
    guideNav: 'Guide', updatesNav: 'Updates', home: 'Home',
    guideTitle: 'How to use Locaptio', guideIntro: 'Recording step by step, tools for correcting and keeping transcripts, choosing a recognition engine, and every feature and setting.',
    updatesTitle: 'Update history',
    moreTitle: 'Learn more',
    more: [
      ['guide', 'How to use Locaptio', 'Recording steps, editing, dictionary, subtitle files and AI summaries, choosing an engine, and every feature and setting.'],
      ['updates', 'Update history', 'The main features added from the first release to the latest version.']
    ]
  }
};
