# Changelog

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
