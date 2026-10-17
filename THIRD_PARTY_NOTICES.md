# Third-party notices

Locaptio's own code is MIT-licensed. The model weights and optional Python/CUDA/NPU environments are not bundled in this repository or its portable ZIP. Explicit setup downloads them and records revisions/hashes in local manifests. Third-party licenses are separate from Locaptio's MIT license.

モデルは明示的なセットアップで取得します。配布ZIPの `licenses` フォルダーには、ビルドで使用したNuGet依存物・.NET runtime packから取得したLICENSE/NOTICEも含めます。

* STT: [Systran faster-whisper-tiny.en](https://huggingface.co/Systran/faster-whisper-tiny.en), derived from OpenAI Whisper. MIT.
* Multilingual STT: [Systran faster-whisper-small](https://huggingface.co/Systran/faster-whisper-small) and [dropbox-dash faster-whisper-large-v3-turbo](https://huggingface.co/dropbox-dash/faster-whisper-large-v3-turbo), derived from OpenAI Whisper. MIT. CTranslate2 conversion; per-model manifests record source revision and hashes.
* Local LLM: [Qwen3-4B-GGUF](https://huggingface.co/Qwen/Qwen3-4B-GGUF), Qwen, Apache-2.0. Q4_K_M GGUF; source and hashes recorded in models/llm/manifest.json.
* AMD NPU STT: [amd/whisper-small-onnx-npu](https://huggingface.co/amd/whisper-small-onnx-npu), AMD, Apache-2.0 model card; derived from OpenAI Whisper-small. Unmodified downloaded ONNX models and tokenizer are local runtime assets; revision and hashes are recorded by setup_amd_speech.py. AMD's Windows ML execution provider is prepared separately and its supplied third-party notices are preserved with the local compiler copy.
* llama.cpp / llama-cpp-python: https://github.com/ggml-org/llama.cpp and https://github.com/abetlen/llama-cpp-python — MIT.
* Windows App SDK / WinUI: https://github.com/microsoft/WindowsAppSDK — MIT; bundled SDK/runtime license files also apply.
* NVIDIA CUDA / cuDNN: proprietary NVIDIA runtime licenses supplied in the downloaded component directories. Explicit optional setup only; these libraries are external to the MSIX.
* Translation: [FuguMT en-ja](https://huggingface.co/staka/fugumt-en-ja), author staka. [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). モデル重みをCTranslate2形式、INT8に変換した。変換モデルを再配布する際はCC BY-SA 4.0、作者・出典・変更表示を保持する。オリジナルのモデルカードはmodels/fugumt-source/README.md。
* NAudio: https://github.com/naudio/NAudio — MIT.
* faster-whisper: https://github.com/SYSTRAN/faster-whisper — MIT.
* CTranslate2: https://github.com/OpenNMT/CTranslate2 — MIT.
* Transformers: https://github.com/huggingface/transformers — Apache-2.0.
* SentencePiece: https://github.com/google/sentencepiece — Apache-2.0.
* PyTorch (setup/conversion): https://github.com/pytorch/pytorch — BSD-style.
* huggingface-hub: https://github.com/huggingface/huggingface_hub — Apache-2.0.
* ONNX Runtime / DirectML: https://github.com/microsoft/onnxruntime — MIT; DirectML runtimeのライセンスも同梱物に従う。
* ONNX: https://github.com/onnx/onnx — Apache-2.0.
* Optimum / Optimum ONNX: https://github.com/huggingface/optimum-onnx — Apache-2.0.

これは出典一覧。配布時には各依存物のLICENSE/NOTICEの全文も同梱する。
