# Testing / テストと検証範囲

[日本語 README](../README.md) · [English README](../README.en.md)

## Automated checks without audio hardware

Windows, .NET 10 SDK and .NET 8 Runtime are required. These commands do not download speech/translation models.

```powershell
dotnet build src/WinUI/Locaptio.WinUI.csproj -c Release -p:Platform=x64
dotnet run --project src/Tests/Locaptio.Tests.csproj -c Release -- --windows-captions
dotnet run --project src/Tests/Locaptio.Tests.csproj -c Release --no-build -- --streaming-translation
```

The first test checks baselines, revisions, reconnects, rolling captions and retention over 20,000 Japanese/English phrases. The second uses fake translation backends to check immediate source updates, stale-result rejection, context, glossary and refined-result ordering. GitHub Actions runs both after building the WinUI app and packages a portable ZIP.

## Local hardware checks

```powershell
dotnet run --project src/Tests/Locaptio.Tests.csproj -c Release -- --mixed-input
dotnet run --project src/Tests/Locaptio.Tests.csproj -c Release -- --features
```

These commands capture real microphone/system/application audio and play test tones. Run them only on a suitable Windows desktop; they are excluded from hosted CI. Output fixtures are written to the ignored `artifacts` folder.

WinUI self-checks include `--verify-opacity`, `--verify-caption-style` and `--verify-history --verify-scroll-only` on the built exe. They temporarily change settings and restore their values on normal completion. Test copies are preferable to interrupting a real caption/recording session.

## Validation limits / 検証範囲

- Local tests covered a Windows 11 x64 desktop, CPU/CUDA recognition, AMD Ryzen AI 9 HX 370's experimental NPU path, Windows captions integration, audio format finalization and independent opacity.
- Synthetic long-caption tests retained 21,001 main-window rows with roughly 31 realized rows and preserved a scrolled position during updates.
- Short real combined-input recording/transcription tests and deterministic two-input mixer tests passed.
- These are targeted tests, not a general accuracy benchmark, an accessibility audit, or proof of hours-long continuous recording stability.
- CI cannot verify microphone routing, GPU/NPU execution, Windows captions language packs, DRM-protected audio or model quality.

実音声の精度・すべてのGPU/NPU・数時間の実録音を保証するものではありません。問題報告にはOS・入力・認識エンジン・モデル・backend・再現手順を含め、個人情報を除いてください。
