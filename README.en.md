<p align="center"><img src="src/WinUI/Assets/Locaptio.png" width="112" alt="Locaptio icon"></p>
<h1 align="center">Locaptio</h1>
<p align="center">Local captions, translation & audio recording for Windows 11</p>
<p align="center"><a href="README.md">日本語</a> · <b>English</b> · <a href="https://locaptio.vercel.app/en">Official website</a> · <a href="https://github.com/Kou-256/Locaptio/releases/latest/download/Locaptio-win-x64.zip">Download</a> · <a href="https://github.com/Kou-256/Locaptio/issues">Issues</a></p>

Locaptio is a Windows 11 app for local speech recognition, English-to-Japanese translation, floating captions, and recording. Read audio from videos or meetings and save the text and audio you need.

> **Experimental personal project.** The application UI is currently mostly Japanese; this guide explains its controls in English. Accuracy and latency depend on the audio, model and hardware.

## Features

- **English → Japanese translation / Japanese transcription** modes.
- System audio, a microphone, a selected application, or **system audio + microphone together**.
- Immediate English text, FuguMT preview translation, and optional Qwen3 context-aware refinement.
- Detachable **text-only caption bubbles** with configurable language order, fonts, colors and spacing.
- Separate background/text opacity for both windows. Defaults: **70% transparent background, fully opaque white text**.
- Full-session history with virtualization; scrolling up pauses automatic following.
- UTF-8 text export and **WAV / MP3 / M4A** recording, with independent destinations.
- CPU, NVIDIA CUDA, DirectML translation, and experimental AMD XDNA 2 NPU recognition.
- Integration with the recognition results from Windows Live Captions.

## Download and requirements

Open the [**latest release**](https://github.com/Kou-256/Locaptio/releases/latest), download `Locaptio-<version>-win-x64.zip`, **extract the entire ZIP**, and run `Locaptio.exe`. Do not move the executable out of its folder.

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
| 録音 (Record checkbox) | Select a destination and start recording; uncheck to stop recording only |
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

Corrections and translations can be wrong. Speed depends on the model, PC load, utterance length and initial loading. The same recognition model or speed as Windows captions is not guaranteed.

### Optional components

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

Solution: `Locaptio.sln`. WinUI 3 is current; WPF in `src/App` is retained for compatibility/testing. `scripts/build-msix.ps1` generates MSIX, but installation needs suitable signing. Public releases primarily use ZIPs, which do not require adding a certificate.

[Architecture](docs/architecture.md) · [Models](docs/model-selection.md) · [Tests and validation scope](docs/testing.md) · [Changelog](CHANGELOG.md)

## License

Locaptio's source is under the [MIT License](LICENSE). Models/dependencies retain their own licenses. Model weights, Python environments and CUDA/NPU libraries are not bundled in the repository or portable ZIP; setup downloads them separately. See [third-party notices](THIRD_PARTY_NOTICES.md).
