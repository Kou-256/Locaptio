# Architecture / アーキテクチャ

Locaptio separates capture, recognition, translation, correction, recording and rendering so slower refinement does not block the first caption.

## Components

| Path | Responsibility |
| --- | --- |
| `src/WinUI` | Main WinUI 3 UI, floating caption bubbles, settings and engine coordination |
| `src/Core` | Audio capture/mixing, bounded pipeline queues, revision ordering and inference process clients |
| `src/Locaptio.Bridge` | Separate Windows UI Automation process that reads Windows Live Captions text |
| `inference` | Offline Python workers, conversions and explicit model setup |
| `src/Tests` | Deterministic coordination checks and optional hardware tests |
| `src/App` | Legacy WPF interface retained for compatibility/testing |

## Data flow

1. WASAPI captures system/microphone audio; process loopback captures a selected app and its children.
2. Streams become mono 16 kHz floats. Combined input mixes default playback and microphone on a common 20 ms clock with bounded per-input buffers.
3. Whisper emits replaceable partial/final caption revisions. A separate accurate pass can improve preview recognition.
4. FuguMT generates preview translations; optional Qwen3 uses recent context/terminology to refine them. Optional source correction is separate.
5. Revision checks prevent a late, older result from overwriting a newer one.
6. The main window keeps session data but realizes only nearby ListView rows. A separate clipped caption window shows recent text bubbles.

Windows integration replaces steps 1–3 with text observed from the Windows caption UI. It excludes an initial baseline and aligns rolling snapshots into rows. It does not control Windows recognition language/device. Combined input automatically uses Whisper because Windows cannot accept the app's mixed PCM.

## Recording and state

Recording uses the selected source independently of recognition, streams PCM WAV to disk and optionally encodes MP3/AAC after stopping. Stop cancels both operations; mode changes may preserve a recording, while input changes split it. Bounded audio queues prevent whole recordings from accumulating in RAM.

Settings live in `%LOCALAPPDATA%/Locaptio`. Explicit setup downloads models/environments beside the runtime root; normal local inference uses local paths and no cloud API. Python workers are separate processes. Windows' own caption process is not terminated with the disposable bridge.

## Rendering

Per-pixel composition applies background brush opacity independently of TextBlock opacity. The caption HWND has no native frame and is clipped to the union of text bubbles. User scrolling pauses automatic following; going to the bottom resumes it. Full-session text still consumes memory as it grows.

音声取得・速報・高精度更新・翻訳・補正・録音は独立した処理です。混雑時は有界キューで古い仕事を捨て、先行表示を優先します。Windows連携はOSの文字情報を読む方式です。詳細な動作と制限は[日本語README](../README.md)を参照してください。
