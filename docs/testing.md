# Testing / テストと検証範囲

[日本語 README](../README.md) · [English README](../README.en.md)

## Automated checks without audio hardware

Windows, .NET 10 SDK and .NET 8 Runtime are required. These commands do not download speech/translation models.

```powershell
dotnet build src/WinUI/Locaptio.WinUI.csproj -c Release -p:Platform=x64
dotnet run --project src/Tests/Locaptio.Tests.csproj -c Release -- --windows-captions
dotnet run --project src/Tests/Locaptio.Tests.csproj -c Release --no-build -- --streaming-translation
dotnet run --project src/Tests/Locaptio.Tests.csproj -c Release --no-build -- --windows-recovery
```

The first test checks baselines, revisions, reconnects, rolling captions and retention over 20,000 Japanese/English phrases. The second uses fake translation backends to check immediate source updates, stale-result rejection, context, glossary and refined-result ordering. GitHub Actions runs both after building the WinUI app and packages a portable ZIP.

## Local hardware checks

`--caption-history` checks stable caption items and targeted property notifications. The WinUI self-check `--verify-history --verify-scroll-only` additionally tests a 16px scroll away from the bottom while new rows and partial revisions arrive. Automatic following must stay paused until **最新へ** is pressed. `--verify-recording-ui` silently records only the test process for about five seconds, checks stable text controls and viewport height, and verifies that repeated native repaint requests do not repeatedly erase the backing surface. It does not capture the user's microphone or other applications. `--preview-recording-ui` extends that check for visual inspection.

`--windows-recovery` uses fake audio levels and window operations to verify the stall threshold, silence, text updates, cooldown, retry budget, cancellation and failure handling. It does not restart Windows or capture audio. The built WinUI exe's **`--verify-windows-recovery`** self-check deliberately blocks its first bridge, simulates sustained audio activity, waits for the watchdog, **restarts the real Windows Live Captions process**, verifies the visible window and real replacement bridge connection, and checks retained transcript/recording state. It plays/records no audio; run it only when interrupting Windows captions is acceptable. An actual hung Windows recognizer is not required or reproduced by that fixture.

```powershell
dotnet run --project src/Tests/Locaptio.Tests.csproj -c Release -- --mixed-input
dotnet run --project src/Tests/Locaptio.Tests.csproj -c Release -- --features
```

These commands capture real microphone/system/application audio and play test tones. Run them only on a suitable Windows desktop; they are excluded from hosted CI. Output fixtures are written to the ignored `artifacts` folder.

WinUI self-checks include `--verify-opacity`, `--verify-caption-style` and `--verify-history --verify-scroll-only` on the built exe. `--verify-windows-warning` checks the persistent warning, restoration from minimized state, preserved transcript/recording state, and clearing the warning after reconnection, without closing the user's Windows captions or recording audio. The `--windows-captions` tests also cover disconnect debounce, silence and repeated reconnections. Self-checks temporarily change settings and restore their values on normal completion. Test copies are preferable to interrupting a real caption/recording session.

## Validation limits / 検証範囲

- Local tests covered a Windows 11 x64 desktop, CPU/CUDA recognition, AMD Ryzen AI 9 HX 370's experimental NPU path, Windows captions integration, audio format finalization and independent opacity.
- Synthetic long-caption tests retained 21,001 main-window rows with roughly 31 realized rows and preserved a scrolled position during updates.
- Short real combined-input recording/transcription tests and deterministic two-input mixer tests passed.
- These are targeted tests, not a general accuracy benchmark, an accessibility audit, or proof of hours-long continuous recording stability.
- CI cannot verify microphone routing, GPU/NPU execution, Windows captions language packs, DRM-protected audio or model quality.

実音声の精度・すべてのGPU/NPU・数時間の実録音を保証するものではありません。問題報告にはOS・入力・認識エンジン・モデル・backend・再現手順を含め、個人情報を除いてください。
`--windows-correction` tests live correction with a controlled backend: immediate raw text, Japanese/English, runtime OFF/ON, stale native revisions, same-row updates, bounded history/pending work, model failure fallback, cancellation and corrected-source translation. It does not start Windows captions or require model downloads.

Speaker checks: run 'dotnet run --project src/Tests/Locaptio.Tests.csproj -c Release -- --speakers'. This covers clustering, ambiguous/invalid embeddings, speaker capacity, bounded timeline and audio backlog, cancellation, fallback and saved labels without model downloads. Add '--real-models' after setup-speakers.ps1 and place the four official fangjun-sr-1/2.wav and leijun-sr-1/2.wav fixtures from the sherpa speaker-model release in artifacts/speaker-fixtures. This exercises the real Silero/CAMPPlus worker over C# IPC, silence rejection and two-voice grouping. '--verify-speakers' on the built WinUI exe checks main/popout colors, same-row correction updates and exported labels with synthetic timestamps; it does not capture audio. These checks do not establish diarization accuracy for Japanese conversations, overlapping speech or arbitrary Windows ASR delays.

Windows language policy checks: '--windows-language' covers exact ja-JP/en-US selection, existing language, absent locales, failed selection verification, setup-not-ready and invalid arguments using a fake UI. The production adapter uses the observed Windows 11 SettingsButton / ChangeLanguageMenuFlyoutItem / SpeechModelDropDown / ContinueButton accessibility controls; compatibility across OS builds requires separate UI verification. It never confirms download or privacy prompts.

Windows 11 build 26200 local UI verification (1.0.24): Japanese startup, automatic English (United States) selection from the main mode control, return to Japanese with the caption-only informational tip, readiness while silent, and Stop were checked. Controls outside the main UIA root and potentially blocking Invoke calls need special handling. This does not establish compatibility with other Windows builds.

1.0.25: '--windows-language' additionally checks bypassing the picker after manual readiness and clearing stale warnings after verified/manual resumption. '--preparation' checks stage aggregation, failure and reset; '--preparation --real-worker' exercises real CPU Whisper initialization without capturing audio. '--verify-settings-preparation' on the built app checks 50%/remaining stages, completion, all eight settings categories, model/AI grouping and the scrollbar gutter. '--preview-settings-preparation' keeps that test visible for 20 seconds for visual inspection. NPU compiler internals expose no reliable overall percentage; the displayed percentage counts preparation stages.

1.0.26 (2026-11-03): `--verify-recording-ui` checks recording-checkbox automatic file creation in a configured folder, unchecking without stopping transcription, initially collapsed/expandable status details, native text selection during recording, retaining selected text while a correction arrives, and applying the correction after selection clears. It records only the verification process, without microphones or other applications' audio. Existing retained-surface, stable-row and recording-clock checks passed. `--verify-history --verify-scroll-only` retained 21,041 rows with 22 realized rows and 0 px scroll drift. Both UI checks and the core checks passed; this does not measure real speech recognition accuracy or hours-long recording stability.

1.0.27 (2026-11-03): `--caption-history` checks forward/reverse partial ranges, skipped row IDs, empty ranges and independent translation columns. `--verify-recording-ui` checks that hover details are overlaid below the toolbar without moving its button or changing the transcript viewport; it also checks multi-row copy text, delayed corrections, virtualized/recycled selection highlights and correction resumption. A real mouse drag selected from the middle of row 1 to the middle of row 3; Ctrl+C and the native context-menu action both invoked the same range-copy path with the expected three-line synthetic text. Diagnostics appeared below the fixed toolbar control. These tests do not require microphone or other applications’ audio.

1.0.28: `--verify-recording-ui` now holds a controlled filename chooser pending and verifies that recording cannot start until it resolves, cancellation leaves recording stopped, and confirmation creates the specified filename. It then checks the existing recording, selection and overlay behavior. The controlled chooser substitutes for the native Save UI; no user microphone/system audio is captured by this test.

1.0.30: Status is a small bottom-right hyperlink with no chevron or toolbar button. Click toggles visibility; hover/focus no longer opens it. Details use their measured content width (up to 360 DIPs), overlaid without changing the transcript viewport. Recording UI verification passed, including click-to-hide, small-link sizing and stable viewport; the small link and compact panel were also visually inspected.

1.0.32: `--verify-settings-preparation` checks that the 100% panel is hidden, English-to-Japanese preparation stays visible until recognition/translation/AI all complete, stale completion from a previous preparation is ignored, and failures remain readable. All checks passed using controlled progress notifications without loading models or capturing audio. The UI also includes the 1.0.31 content-sized toolbar checkbox spacing fix.

1.0.33 (2026-11-03): `--windows-language` covers delayed and reappearing captions-only tips, temporarily inaccessible pickers, bounded Continue retries/timeouts, stable caption-view confirmation, manual completion and preserving an already verified Japanese selection after a Continue failure. On this Windows 11 PC, the Japanese captions-only popup was reproduced after English-to-Japanese selection; starting the updated Locaptio in Japanese mode dismissed the popup, completed Continue and reached the Japanese ready message with no language warning. The test app was stopped afterward. These checks do not establish compatibility with every Windows UI version.
