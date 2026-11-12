<p align="center"><img src="src/WinUI/Assets/Locaptio.png" width="112" alt="Locaptio icon"></p>
<h1 align="center">Locaptio</h1>
<p align="center">Local captions, translation & audio recording for Windows 11</p>
<p align="center"><a href="README.md">日本語</a> · <b>English</b> · <a href="https://locaptio.vercel.app/en">Official website</a> · <a href="https://github.com/Kou-256/Locaptio/releases/latest/download/Locaptio-Setup-x64.exe">Download</a> · <a href="https://github.com/Kou-256/Locaptio/issues">Issues</a></p>

Locaptio is a Windows 11 app for local speech recognition, English-to-Japanese translation, floating captions, and recording. Read audio from videos or meetings and save the text and audio you need.

> **Experimental personal project.** The application UI is currently mostly Japanese; this guide explains its controls in English. Accuracy and latency depend on the audio, model and hardware.

## Features

- **English → Japanese translation / Japanese transcription** modes.
- System audio, a microphone, a selected application, or **system audio + microphone together**.
- Immediate English text, FuguMT preview translation, and optional Qwen3 context-aware refinement.
- Detachable **text-only caption bubbles** with configurable language order, fonts, colors and spacing.
- Separate background/text opacity for both windows. Defaults: **10% main-background transparency, 20% caption-background transparency, fully opaque cyan English / white Japanese**.
- Full-session history with virtualization; scrolling up pauses automatic following.
- UTF-8 text export and **WAV / MP3 / M4A** recording, with independent destinations.
- CPU, NVIDIA CUDA, DirectML translation, and experimental AMD XDNA 2 NPU recognition.
- Integration with the recognition results from Windows Live Captions.

## Download and requirements

### Recording versus replay (v1.0.41)

- **Start** starts/resumes transcription without recording. Changing audio input retains the conversation.
- **New recording** names a fresh session and creates its own folder. Unchecking stops both recording and transcription, making Start available again.
- **Recordings / Replay** opens saved audio and text without starting a new recording. An alternate file picker supports older recordings.

Each session folder contains audio, `.locaptio.json` and `.txt`. Input changes during recording preserve history and split audio into another file in the same folder. A new recording, or Start after reviewing saved audio, opens a fresh workspace. Save unrecorded text manually when needed.

Fresh-profile defaults: Windows captions integration, Japanese transcription, matching menu background, 10% main-background transparency, 20% caption-background transparency, cyan English and white Japanese. Existing recognition/mode choices and customizations are retained. Caption width and wrapping are configurable in Settings.

Setup always offers a destination directory, including during updates. Downloaded updates require an explicit **Install** approval popup before Setup runs. Choosing Later keeps the current session running.

Download **Locaptio-Setup-x64.exe** from the [latest release](https://github.com/Kou-256/Locaptio/releases/latest), install without administrator rights, and launch Locaptio from Start. The default location is `%LOCALAPPDATA%/Programs/Locaptio`. A portable ZIP remains available.

### Recording search and replay (v1.0.39)

Since v1.0.40, the current **utterance is highlighted** during playback. Scrolling or selecting text pauses playback following; replaying from text or search resumes it. Exact word-level highlighting is not provided.

After stopping a recording, click transcript text to replay from just before that utterance. Drag to select and Ctrl+C still work; the selection context menu also offers replay. Use the magnifying-glass button to search source/translated text and play timestamped results. Playback includes pause and seeking.

The app saves an adjacent `audio-filename.locaptio.json` containing transcript text and media offsets. Keep it with the audio when moving or sharing files. Use **File → Open recording and transcript** to reopen them. Timing is utterance-level; Windows caption timing is estimated from display updates, without exact word timestamps. Older audio without this sidecar plays as audio only. Replay is available after recording stops to avoid recording its own playback.

### Automatic local LLM setup

Enabling AI correction or context translation automatically prepares missing Python dependencies and the default Qwen3 model. No manual PowerShell command is needed. Initial preparation needs internet and several GB of storage; the app shows preparation stages and Stop/exit cancels it. Prepared models are reused and inference does not send audio or text to a cloud service. Missing custom models must be provided or reset to the default in Settings.

### Microphone input with Windows captions (v1.0.40)

Select Windows captions integration and **Microphone (default input)**, then Start. In Windows captions, enable **Settings → Preferences → Include microphone audio**. Windows resets this option to OFF each time captions starts; privacy permission prompts must be handled in Windows. Windows uses its default input and prioritizes PC audio if both sources overlap. [Microsoft documentation](https://support.microsoft.com/en-us/accessibility/windows/use-live-captions-to-better-understand-audio). Specific devices, applications and mixed input use Whisper.

Setup offers Start menu (enabled by default) and desktop shortcuts as separate tasks. It adds an All apps entry without changing pinned items.

| Item | Requirement |
| --- | --- |
| OS / architecture | Windows 11 24H2 or later, x64; no published ARM64 build |
| App runtime | .NET / Windows App SDK are included in the portable ZIP |
| Windows captions only | Windows Live Captions and its recognition language pack |
| Whisper / translation / LLM | [uv](https://docs.astral.sh/uv/getting-started/installation/) and internet for initial setup |
| Storage | Allow at least 10 GB for models; additional LLM / GPU / NPU environments need more |
| Source build | [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0); app targets .NET 8. Tests also require .NET 8 Runtime |

The portable release is an unsigned personal build. Windows may display a warning. Verify checksums using the release's `SHA256SUMS.txt`. Setup does not change Windows protection settings.

## Quick start: Japanese transcription with Windows captions

This path requires neither Python nor a Whisper model.

1. Start `Locaptio.exe`.
2. Open **設定(S) → 設定を開く…** (Settings). Set **音声認識エンジン** (recognition engine) to **Windowsライブキャプション連携** (Windows Live Captions integration).
3. Choose **日本語の文字起こし** (Japanese transcription) and **PC全体の音声** (system audio).
4. Press **Start**. Complete Windows Live Captions' initial setup and select Japanese as its recognition language.
5. Play Japanese audio on the PC.

Keep the Windows caption window open. Integration reads caption text exposed by Windows; it does not embed Microsoft's recognition model. Windows updates or language settings can affect availability. English-to-Japanese translation also needs the local translation model below; select English in Windows captions.

If the Windows caption window closes or disconnects, Locaptio displays a warning and restores its main window when minimized. Existing text is retained and recording continues. Reopen Windows captions to reconnect automatically. Silence does not trigger the warning.

**Automatic recovery is ON by default.** About 20 seconds of PC output activity without caption text updates triggers a restart of Windows captions and its bridge, followed by a visible-window check. Transcript history, recording and translation models remain active. Retries are at least 60 seconds apart, with three attempts per ten minutes; failures leave a warning. Disable it using **PC音声があるのにWindows字幕が20秒以上止まったら自動復旧** in Settings. Detection uses output volume rather than speech classification, so music, sound effects or a mismatched recognition language may trigger it. It monitors the default PC output during Windows captions integration.

## Prepare Whisper and translation

Install uv, for example:

```powershell
winget install --id=astral-sh.uv -e
```

Open a new PowerShell window, change to the **extracted Locaptio folder**, then run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup.ps1 -SkipBuild
```

This prepares Python 3.12, base/small Whisper models and FuguMT. Downloads and conversion can take time. Set **モデル・推論環境** (model/runtime folder) to this folder in the app.

- **Independent recognition:** select Whisper and press Start.
- **Windows recognition + translation:** select Windows integration, English in Windows captions, and English → Japanese in Locaptio.
- If no LLM is installed, turn **ローカルLLMで文脈を踏まえて翻訳** (context-aware LLM translation) OFF to use FuguMT alone.

## Controls

| Control | Behavior |
| --- | --- |
| Start | Begin a new session; save earlier text first if needed |
| Stop | **Stop both recognition and recording** and finalize audio |
| Clear | Clear displayed/savable transcript history; recording continues |
| 字幕 (Captions) | Open floating captions and minimize the main window; retain text in both |
| テキスト保存 (Save text) | Export the full session as UTF-8 TXT |
| 録音 (Record checkbox) | Choose a filename and destination, then confirm to start recording and transcription; cancel starts neither, and uncheck stops recording and transcription |
| Scroll history | Pause automatic following and retain the reading position. Use **最新へ** (Latest) or End to resume |
| Change input/mode | Clear caption history; changing input splits recording into a new file |

Captions appear when recognition updates arrive and hide after a configurable idle interval. Drag their background to move them; Escape closes them. Translation and correction update existing lines.

### System audio + microphone

Choose **PC内音声＋マイク（既定の機器・同時入力）**, enable recording, and press Start. The default playback device and microphone are mixed at 50% each; a silent input does not block the other.

This input **automatically uses Whisper**, even when Windows integration is selected, because Windows captions cannot accept the app's mixed PCM. Prepare Whisper first. Speaker separation and echo cancellation are not implemented. Headphones help avoid picking up playback through the microphone.

## Speed, accuracy and models

| Goal | Option |
| --- | --- |
| Use Windows' recognition results | Windows integration; input, recognition language and device follow Windows |
| Lightweight Whisper | base; tiny is lighter but may make more Japanese errors |
| Balance speed and accuracy | small |
| Higher accuracy on a GPU | large-v3-turbo; download separately |
| Display early and improve later | Fast/realtime preview with an independent accurate pass |
| English → Japanese | FuguMT preview + optional Qwen3 with recent context and terminology notes |
| Revise recognized source text | Enable context correction; requires an LLM and is OFF by default |

Use **AI補正** beside Recording on the main screen to toggle correction directly. Its state stays synchronized with Settings and is saved for the next launch.

AI source correction also supports Windows Live Captions integration. Toggle **文字起こしをAIで補正** in Settings during a session; Windows integration applies the change immediately (Whisper applies it on the next Start). Raw Japanese or English text appears first. The local LLM checks finalized speech against up to eight preceding utterances and updates the same row. Disabling correction discards pending results and retains already applied corrections. The Windows caption window itself is unchanged. If the LLM environment or model is unavailable, raw captions continue and the status explains the required setup.

Corrections and translations can be wrong. Speed depends on the model, PC load, utterance length and initial loading. The same recognition model or speed as Windows captions is not guaranteed.

### Optional components

Settings are grouped into eight expandable categories: main appearance, subtitles, recognition/Windows integration, AI correction, speaker colors, translation/LLM, saving/recording and diagnostics. Model loading shows completed/remaining stage percentages, the current stage and elapsed time. Percentages represent completed preparation stages, not a time estimate or an unavailable internal NPU compiler percentage.

After automatic language selection fails, confirming the requested Windows caption language is ready or receiving resumed caption updates clears the stale warning. An already-ready language bypasses unnecessary picker automation.

Windows caption language synchronization is ON by default. Starting a session or changing its mode selects English (United States) for translation or Japanese (Japan) for Japanese transcription. Disable **本体のモードにWindows字幕の言語を自動で合わせる** in Settings to keep manual control. The Windows language UI may briefly appear. Complete any first-time language download in Windows yourself. Unsupported UI or setup failures display a dedicated warning. This integration still requires the Windows caption window; use the independent Whisper CPU/GPU/AMD NPU path for recognition without launching Windows captions.

**Speaker colors:** run `scripts/setup-speakers.ps1`, enable **音声から話者を識別し、話者ごとに色分け** in Settings, then Start. This is OFF by default. Configure colors for up to eight anonymous speakers and the matching threshold. Text appears first; local audio analysis later updates history/popout colors and adds `[話者1]`-style speaker numbers to saved text. IDs reset each session and do not identify real names. Speaker colors take priority over correction colors.

CAMPPlus speaker embeddings and Silero VAD run on the CPU. The speaker worker processes audio in memory, without saving or uploading it. Whisper shares its recognition input; Windows integration separately analyzes the selected PC audio and approximately aligns it with caption arrival times. Windows must use the same audio input. Short speech, overlapping voices, similar voices, noise, recognition delay and speaker changes within a row can cause errors. This estimates the dominant speaker per caption row rather than performing exact word-level diarization. Unassigned rows use the ordinary color. Japanese speaker accuracy has not been evaluated.

Run only the components you need, from the repository or extracted release root:

```powershell
# Local NVIDIA CUDA DLLs; compatible NVIDIA driver also required
powershell -ExecutionPolicy Bypass -File scripts/setup-cuda.ps1
# Higher-accuracy GPU Whisper
powershell -ExecutionPolicy Bypass -File scripts/setup-speech.ps1 -Model large-v3-turbo
# DirectML GPU translation
powershell -ExecutionPolicy Bypass -File scripts/setup-directml.ps1
# Qwen3-4B Q4_K_M + llama.cpp, Vulkan
powershell -ExecutionPolicy Bypass -File scripts/setup-llm.ps1
# Local CPU speaker identification, approximately 29MB of models
powershell -ExecutionPolicy Bypass -File scripts/setup-speakers.ps1
# CPU-only LLM alternative
powershell -ExecutionPolicy Bypass -File scripts/setup-llm.ps1 -Backend cpu
# Experimental AMD Ryzen AI / XDNA 2 Whisper-small
powershell -ExecutionPolicy Bypass -File scripts/setup-amd-speech.ps1
```

The experimental AMD path was tested on Ryzen AI 9 HX 370. Windows ML and compatible drivers are required; initial compilation can be lengthy. Only dedicated Whisper-small recognition runs on the NPU; translation/LLM remain on GPU/CPU. Other hardware is unverified. See [models and backends](docs/model-selection.md).

## Saving and privacy

- Initial setup downloads packages, models and optional runtimes. **Normal local inference uses no cloud API or API key.**
- Recording and text export happen only when requested; transcripts are not autosaved.
- Configure text/audio destinations independently. A synchronization service may upload files saved in its folders.
- Settings: `%LOCALAPPDATA%/Locaptio/settings.json`. Older settings are imported on first run only.
- WAV: 16 kHz / 16-bit / mono, approximately 115 MB/hour. MP3/M4A conversion runs after stopping; failure preserves `.recording.wav`.
- Main-window history has no row limit. Text memory grows with a session; audio queues and realized UI elements are bounded.

## Troubleshooting

| Problem | Check |
| --- | --- |
| Missing model / Start fails | Setup completion, model/runtime folder, selected engine |
| No Windows caption text | Keep Windows captions open and match recognition language to the mode |
| Microphone/app recognition fails | Choose Whisper; check Windows microphone access and selected input |
| CUDA unavailable | Driver, setup-cuda.ps1, displayed backend; CPU is also available |
| Captions lag | Lightweight preview model, GPU settings and competing workloads |
| Slow Stop with MP3/M4A | Wait for conversion; choose WAV for quick finalization of long recordings |
| App fails after moving it | Move the full folder, including DLLs, Assets and bridge |

Report problems in [Issues](https://github.com/Kou-256/Locaptio/issues) with OS, input, engine, model/backend and reproduction steps. Do not post private recordings or personal transcripts.

## Build and contribute

```powershell
git clone https://github.com/Kou-256/Locaptio.git
cd Locaptio
# Build app; model setup is separate
dotnet build src/WinUI/Locaptio.WinUI.csproj -c Release -p:Platform=x64
# Or prepare models and build together
powershell -ExecutionPolicy Bypass -File scripts/setup.ps1
# Run from source
powershell -ExecutionPolicy Bypass -File scripts/run.ps1
# Create a portable ZIP
powershell -ExecutionPolicy Bypass -File scripts/build-portable.ps1
```

Solution: `Locaptio.sln`. WinUI 3 is current; WPF in `src/App` is retained for compatibility/testing. `scripts/build-msix.ps1` generates MSIX, but installation needs suitable signing. Public releases offer Setup as the primary download and retain a portable ZIP.

[Architecture](docs/architecture.md) · [Models](docs/model-selection.md) · [Tests and validation scope](docs/testing.md) · [Changelog](CHANGELOG.md)

## License

Locaptio's source is under the [MIT License](LICENSE). Models/dependencies retain their own licenses. Model weights, Python environments and CUDA/NPU libraries are not bundled in the repository or portable ZIP; setup downloads them separately. See [third-party notices](THIRD_PARTY_NOTICES.md).

Checking Record opens a Save dialog. Confirm the filename to start recording and transcription; cancel leaves recording stopped. The initial folder is the configured recording folder, or Music/Locaptio if unset. Click the small 状態 link in the bottom-right corner to toggle compact diagnostics without moving transcript content. Hover does not open it; Escape also closes it. Select transcript text while recording and use Ctrl+C or the native Copy context menu. Updates to the selected row wait until selection is cleared; recognition and recording continue. Use 最新へ to resume following new captions.

Drag from the middle of one transcript row to another row to select a continuous range. Ctrl+C and the Copy selected range context menu include every intervening row, preserving the language column where the drag began. Drag near the viewport edges to scroll. Escape or clicking elsewhere in the transcript clears the selection.


## Caption display and scrolling

Under caption appearance, choose a total line limit (default 4) and optional latest-utterance blocks. Long subtitles display their latest fitting portion; the main transcript and exported text retain full content. Auto-follow stays active while the latest utterance is visible. Scroll into older history to pause it and show the return-to-latest button. The selection changes only during a left-button drag and remains highlighted after release. Ctrl+C or the context menu copies it; another click or Escape clears it.

Windows caption recovery can be disabled or given a custom timeout (default 180 seconds). It detects PC audio without caption updates; silence alone never causes a restart. If Live Captions itself causes slowdowns, save and stop the session, close Windows captions, then select Whisper to recognize independently.


## Export details and app updates

Stop cancels recognition and recording, then closes Windows Live Captions and the popout while preserving the main transcript. Save settings independently toggle save time, session duration (including preparation/silence, frozen after Stop) and nonblank content-line/utterance counts. Filename templates default to `{yyyy}{MM}{dd}_`, for example `20261111_.txt`; literal text and `{HH}{mm}{ss}` are supported.

Startup checks and automatic update downloads are independently configurable (both enabled by default). Help also offers a manual check. Only official public GitHub release metadata and assets are requested; audio and transcripts are never sent. Verified updates are saved in `%LOCALAPPDATA%/Locaptio/updates/<version>/`. The app offers an explicit action to run Setup. It does not run a downloaded installer automatically. Older ZIP-only releases can be extracted into a separate directory.


Settings use an SVG-icon sidebar; selecting a category displays its controls on the right. Closing the main window with × also closes Windows Live Captions after recognition is canceled.
