// Screen recording section (v1.0.50). Screens are real app captures made with sample content.
export const recording = {
  ja: {
    nav: '画面録画',
    badge: 'v1.0.50で追加',
    title: '画面やアプリを録画して、そのまま字幕付きの動画に',
    intro: '画面全体か特定のウィンドウを、音声と一緒にMP4で録画します。録画しながら文字起こしもでき、あとから文章を直して**字幕を焼き込んだ動画**や**SRT字幕**を書き出せます。手元の動画ファイルの文字起こしにも対応しました。',
    steps: [
      ['record-picker', '1. 録画する対象を選ぶ', '「新規録画」を押すと、画面全体と開いているウィンドウが**縮小画像付き**で並び、選んだ対象を**プレビュー**で確認できます。PC音声・マイク、カーソル、フレームレート、画質もここで選びます。', '画面全体・ウィンドウの一覧と、選んだ対象のプレビュー'],
      ['video-recording', '2. 録画しながら文字起こし', '録画中は小さな**ライブプレビュー**と経過時間を表示。**一時停止・再開**、**スクリーンショット**、★ブックマークもその場で使えます。Locaptioの本体は録画に映さず、字幕ウィンドウだけを映すこともできます。', '録画中の画面。左上にライブプレビュー、右に一時停止とスクリーンショット'],
      ['video-review', '3. 動画と文章を並べて見直す', '保存した録画は、**動画の上に字幕を重ねて再生**します。文章をクリックするとその場面へ移動。録音と同じように、編集・用語辞書・同期の改善・AI要約が使えます。', '録画した動画の再生画面。動画の下に字幕、その下に文字起こし'],
      ['video-burn', '4. 字幕付き動画を書き出す', '文章（原文・日本語訳・2段）、文字の大きさ、位置を選んで、**字幕を映像に焼き込んだMP4**を作成。プレーヤーで表示を切り替えられる**同じ名前のSRT**も保存できます。元の動画は変更しません。', '字幕付き動画を書き出すダイアログ']
    ],
    extrasTitle: '録画に合わせて追加した機能',
    extras: [
      ['restore', '一時停止・再開', '一時停止中の映像と発話は動画に入らず、字幕の時刻もずれません。'],
      ['bookmark', 'スクリーンショット', '録画中の画面をPNGで保存し、その時刻にブックマークを付けます。'],
      ['sync', 'カウントダウン', '開始前に3秒数えるので、準備してから録画を始められます。'],
      ['edit', 'カーソル・クリック表示', 'マウスカーソルと、クリックした位置の強調を映せます。'],
      ['settings', '画質とフレームレート', '15・30・60 fps と、標準・高画質・ファイルサイズ優先を選べます。'],
      ['file', '動画ファイルの読み込み', 'MP4・MOV・M4V・WMV・AVI・MKVの音声を文字起こしし、録画と同じように見直せます。']
    ],
    note: '録画はWindowsの画面キャプチャ機能を使い、PC内で保存します。著作権で保護された映像や、保護されたウィンドウは録画できない（黒く映る）場合があります。字幕付き動画の書き出しには、動画の長さと同じくらいの時間がかかることがあります。'
  },
  en: {
    nav: 'Screen recording',
    badge: 'New in v1.0.50',
    title: 'Record your screen or an app, then export it with subtitles',
    intro: 'Record the whole screen or one window to MP4 with sound. Transcribe while recording, correct the text afterwards, and export a **video with burned-in subtitles** or an **SRT file**. Existing video files can be transcribed too.',
    steps: [
      ['record-picker', '1. Choose what to record', 'New recording lists every display and open window **with thumbnails**, and **previews** the one you select. Choose PC audio, microphone, cursor, frame rate and quality here.', 'Displays and windows with thumbnails, and a preview of the selection'],
      ['video-recording', '2. Record and transcribe', 'A small **live preview** and elapsed time stay on screen. **Pause/resume**, **screenshots** and ★ bookmarks work while recording. Locaptio’s main window can be kept out of the video while its caption window stays in.', 'Recording in progress with a live preview, pause and screenshot buttons'],
      ['video-review', '3. Review video and text together', 'Saved recordings **play with the caption over the video**. Click a sentence to jump to that moment. Editing, the term dictionary, sync improvement and AI summaries all work as for audio.', 'A recorded video replayed with its caption and transcript'],
      ['video-burn', '4. Export a subtitled video', 'Choose source, translation or both, the text size and position to create an **MP4 with burned-in subtitles**, plus an **SRT with the same name** for players. The original video is not changed.', 'The subtitled video export dialog']
    ],
    extrasTitle: 'Recording extras',
    extras: [
      ['restore', 'Pause and resume', 'Paused video and speech are left out, and subtitle times stay aligned.'],
      ['bookmark', 'Screenshots', 'Save the recorded screen as PNG and bookmark that moment.'],
      ['sync', 'Countdown', 'A 3-second countdown before recording starts.'],
      ['edit', 'Cursor and clicks', 'Show the mouse cursor and highlight clicks.'],
      ['settings', 'Quality and frame rate', '15, 30 or 60 fps; standard, high quality or small files.'],
      ['file', 'Video file import', 'Transcribe MP4, MOV, M4V, WMV, AVI and MKV audio and review them like recordings.']
    ],
    note: 'Recording uses Windows screen capture and saves on your PC. Protected video and protected windows may not be recordable (they appear black). Exporting a subtitled video can take about as long as the video.'
  }
};
