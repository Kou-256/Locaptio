# Models and backends / モデルと実行デバイス

Models are downloaded explicitly, never bundled with the source or portable release. Setup records revisions/hashes in local manifests. Source, model and runtime licenses are separate; see [third-party notices](../THIRD_PARTY_NOTICES.md).

## Recognition / 音声認識

| Engine/model | Purpose and limits |
| --- | --- |
| Windows Live Captions | Reuse Windows' text; no downloadable Microsoft model in this repository. Language, input and inference device follow Windows |
| Whisper tiny/base | Lighter preview; base is preferred over tiny for Japanese in local tests |
| Whisper small | Accuracy/speed balance; also a CPU fallback when available |
| Whisper medium | Larger model; higher memory/compute cost |
| Whisper large-v3-turbo | GPU-oriented accurate recognition; requires separate setup |
| AMD Whisper-small ONNX | Dedicated experimental AMD Windows ML / XDNA 2 path, not arbitrary Whisper acceleration |

Whisper uses faster-whisper/CTranslate2 on CPU/CUDA. With realtime preview enabled and suitable models available, preview and accurate recognition can run separately. CPU-only settings may substitute an installed lighter model. The displayed backend/model is the actual session, not just the requested device.

## Translation and correction / 翻訳・補正

- **FuguMT en-ja:** neural machine translation for early Japanese output. CTranslate2 CPU/CUDA or optional DirectML ONNX execution.
- **Qwen3-4B Q4_K_M:** optional local GGUF LLM through llama.cpp / llama-cpp-python. Uses recent context and terminology notes for translation; optional source correction is independently switchable.
- LLM inference runs on Vulkan GPU or CPU, not the AMD speech NPU path. Inference queues are bounded and recognition does not wait for a completed refinement.
- Corrections may still be incorrect; short/synthetic examples do not establish general accuracy.

## Devices / デバイス

| Device | Setup |
| --- | --- |
| CPU | Baseline setup; no accelerator required |
| NVIDIA CUDA | Compatible NVIDIA driver + `scripts/setup-cuda.ps1` |
| DirectML translation | `scripts/setup-directml.ps1`; supported DirectX GPU; some operators may use CPU |
| AMD XDNA 2 recognition | `scripts/setup-amd-speech.ps1`, compatible Windows ML/provider/driver; initial compilation can be lengthy |
| LLM Vulkan/CPU | `scripts/setup-llm.ps1` or `-Backend cpu` |

The AMD path was exercised on Ryzen AI 9 HX 370. Other NPUs and hardware combinations are not established by that test. Explicit NPU initialization failures are reported; do not interpret device detection alone as proof of accelerated inference.

日本語の認識ではbaseを軽い速報用、large-v3-turbo等をGPUでの高精度更新用として選べます。FuguMTは翻訳AI、Qwen3は文脈を見るローカルLLMです。Windowsと同一のモデル・速度・認識精度を保証しません。最初は軽い構成で動作確認し、必要な追加モデルだけ準備してください。
