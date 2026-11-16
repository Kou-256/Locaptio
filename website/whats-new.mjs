// "How it works" for first-time visitors and the transcript tools added in v1.0.48.
// Each card names where the control is in the app, so a reader can find it immediately.
export const whatsNew = {
  ja: {
    nav: '新機能',
    flowTitle: 'Locaptioでできることを、3つの流れで',
    flowIntro: 'はじめての方へ。PCで流れている音声を文字にし、必要なら音声と一緒に残して、あとから読み返したり要点をまとめたりできます。',
    flowLabel: 'PCの音声がLocaptioで字幕になり、音声・文章・要約のファイルとして残る流れの図',
    flow: [
      ['sound', '1. 音声を選んで「開始」', 'ブラウザーの動画、オンライン会議、PCのマイク。聞き取りたい音声と「日本語の文字起こし」か「英語 → 日本語」を選びます。'],
      ['subtitles', '2. 字幕と全文がその場で出る', '話した内容が本体に1行ずつ追加され、字幕ウィンドウにも表示されます。誤認識はその場で直せます。'],
      ['file', '3. 音声・文章・要約を残す', '「新規録音」なら音声も保存。あとから文章をクリックして聞き直し、字幕ファイルやAI要約も作れます。']
    ],
    title: '文字起こしを「直して・残して・使う」ための新機能',
    intro: '認識した文章をそのままにせず、直す・確かめる・まとめるところまでPCの中で完結します。すべて手元のPCで処理し、文章を外部へ送信しません。',
    badge: 'v1.0.48で追加',
    whereLabel: '使う場所',
    items: [
      { key: 'autosave', icon: 'restore', title: '作業の自動保存・復元', body: '表示中の文章、修正、ブックマーク、録音ファイルとの対応を5秒ごとにPC内へ保存。**アプリが落ちても、次回の起動時に内容を確認して復元**できます。中断した録音も再生できる状態に直して、文章と対応付けて開きます。', where: '起動時のお知らせ／ファイル →「前回の未保存セッションを復元」' },
      { key: 'edit', icon: 'edit', title: '文字起こしの編集・元に戻す', body: '誤認識を**クリックひとつで直接修正**。AI補正や用語辞書で変わる前の「認識時の原文」と並べて比較でき、**Ctrl+Zで元に戻す・Ctrl+Yでやり直し**もできます。直した文章は、あとから届いた認識結果で上書きされません。', where: '文章を右クリック →「この発話を編集」／編集メニュー' },
      { key: 'retranscribe', icon: 'retranscribe', title: '録音の再文字起こし', body: '保存済みの音声を、**別のモデルや高精度設定で認識し直し**、以前の結果と時刻をそろえて並べます。違う箇所だけを表示でき、採用しなかった結果も「版」として残るので、いつでも戻せます。', where: '録音を開く →「再文字起こし」／「版を比較・切替」' },
      { key: 'dictionary', icon: 'dictionary', title: '専門用語辞書', body: '人名・製品名・研究用語の**正しい表記と、間違えやすい表記を登録**。認識のヒント、誤表記の自動置き換え、AI補正、英日翻訳の訳語に共通して使います。本体で選んだ語句を右クリックから登録することもできます。', where: '設定 →「用語辞書」／文章を選んで右クリック' },
      { key: 'sync', icon: 'sync', title: '音声と文章の同期精度向上', body: '既存の録音もあとから解析し、**各発話に単語ごとの時刻**を付けます。再生中のハイライト、文字をクリックしたときの再生位置、字幕ファイルの時刻のずれを減らします。文章そのものは変えません。', where: '録音を開く →「同期を改善」' },
      { key: 'subtitles', icon: 'subtitles', title: 'SRT・VTT字幕出力', body: '認識結果を、動画編集ソフトやプレーヤーで使える**SRT**、Web向けの**VTT**で保存。原文・日本語訳・2段表示を選べます。長い発話は読みやすい長さに分け、1つの字幕は2行までにします。', where: '「書き出し・AI」→「字幕ファイル（SRT・VTT）」' },
      { key: 'bookmarks', icon: 'bookmark', title: '録音中のブックマーク', body: '「重要」「後で確認」「質問」などを、**その時刻にワンクリック（Ctrl+B）**で付けられます。印は一覧に並び、押すとその発話へ移動・再生。保存するテキストにも残ります。種類は設定で変えられます。', where: 'ツールバーの「★ 重要」ボタン（▼で種類を選択）' },
      { key: 'summary', icon: 'summary', title: 'ローカルAIで要約・決定事項抽出', body: '録音を止めたあと、ローカルAIが**要点・決定事項・TODO**を作成。各項目に**根拠にした発話の時刻**が付き、押すとその発話へ戻って聞き直せます。録音停止時の自動作成も選べます。', where: '「書き出し・AI」→「AIで要約」／録音を開く →「AI要約」' }
    ],
    note: 'AIの補正・要約や自動の時刻合わせは誤りを含むことがあります。重要な内容は、根拠の発話や音声で確認してください。要約・再文字起こしで使うモデルは、初回だけダウンロードが必要です。'
  },
  en: {
    nav: 'What’s new',
    flowTitle: 'Locaptio in three steps',
    flowIntro: 'New to Locaptio? It turns audio playing on your PC into text, optionally keeps the audio with it, and lets you review and summarize it later.',
    flowLabel: 'Diagram: PC audio becomes captions in Locaptio and is kept as audio, text and summary files',
    flow: [
      ['sound', '1. Choose the audio and press Start', 'A video in your browser, an online meeting or your microphone. Pick the source and either Japanese transcription or English → Japanese.'],
      ['subtitles', '2. Captions and a full transcript appear', 'Each utterance is added to the main window and shown in the caption overlay. Fix misrecognitions as you go.'],
      ['file', '3. Keep the audio, text and summary', 'With New recording the audio is saved too. Click text to replay it later, export subtitle files or create an AI summary.']
    ],
    title: 'New tools to correct, keep and use your transcripts',
    intro: 'Go beyond raw recognition: correct, verify and summarize, all on your PC. Processing is local; transcripts are not sent anywhere.',
    badge: 'New in v1.0.48',
    whereLabel: 'Where to find it',
    items: [
      { key: 'autosave', icon: 'restore', title: 'Autosave and recovery', body: 'Every five seconds the transcript, edits, bookmarks and the link to the recording are saved on your PC. **If the app closes unexpectedly, review and restore the session at the next launch.** An interrupted recording is repaired so it plays again alongside its text.', where: 'Notice at launch / File → Restore the previous unsaved session' },
      { key: 'edit', icon: 'edit', title: 'Edit transcripts with undo', body: '**Fix a misrecognition directly.** Compare with the recognized text from before AI correction or the term dictionary, and **undo with Ctrl+Z or redo with Ctrl+Y**. Your edit is never overwritten by a recognition result that arrives later.', where: 'Right-click text → Edit this utterance / Edit menu' },
      { key: 'retranscribe', icon: 'retranscribe', title: 'Re-transcribe a recording', body: '**Recognize saved audio again with another model or higher-accuracy settings** and compare it with the previous result, aligned by time. Show only the differences; the result you do not adopt is kept as a version you can switch back to.', where: 'Open a recording → Re-transcribe / Compare versions' },
      { key: 'dictionary', icon: 'dictionary', title: 'Term dictionary', body: '**Register the correct spelling of names, products and research terms, with the spellings they are misheard as.** One list feeds the recognition hint, automatic replacement, AI correction and translation glossary. Register a selected phrase from the context menu too.', where: 'Settings → 用語辞書 / select text and right-click' },
      { key: 'sync', icon: 'sync', title: 'Tighter audio-text sync', body: 'Analyze an existing recording afterwards to give **each utterance word-level times**. Playback highlighting, click-to-replay positions and subtitle timing drift less. The wording itself is not changed.', where: 'Open a recording → Improve sync' },
      { key: 'subtitles', icon: 'subtitles', title: 'SRT and VTT subtitles', body: 'Save the transcript as **SRT** for video editors and players, or **VTT** for the web. Choose source, Japanese translation or both. Long utterances are split into readable cues of at most two lines.', where: 'Export & AI → Subtitle file (SRT/VTT)' },
      { key: 'bookmarks', icon: 'bookmark', title: 'Bookmarks while recording', body: 'Mark “Important”, “Check later”, “Question” and more **at that moment with one click (Ctrl+B)**. Bookmarks appear as a row of chips that jump to and replay the utterance, and they are kept in exported text. Labels are configurable.', where: 'The ★ button on the toolbar (▼ chooses a label)' },
      { key: 'summary', icon: 'summary', title: 'Local AI summary and decisions', body: 'After a recording stops, a local AI writes **key points, decisions and TODOs**. Every item cites **the time of the utterances it is based on**; click to jump back and listen. Optionally summarize automatically when recording stops.', where: 'Export & AI → AI summary / Open a recording → AI summary' }
    ],
    note: 'AI correction, summaries and automatic timing can be wrong. Check important content against the cited utterances or the audio. The models for summaries and re-transcription need a first-time download.'
  }
};
