"""Download only public model assets; no login, microphone or audio upload."""
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
MODELS = (
    ("embedding.onnx", "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_campplus_sv_zh_en_16k-common_advanced.onnx", "aa3cfc16963a10586a9393f5035d6d6b57e98d358b347f80c2a30bf4f00ceba2"),
    ("silero_vad.onnx", "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx", "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6"),
)


def main():
    folder = ROOT / "models" / "speakers"
    folder.mkdir(parents=True, exist_ok=True)
    manifest = []
    for name, url, expected in MODELS:
        target = folder / name
        if not target.exists() or hashlib.sha256(target.read_bytes()).hexdigest() != expected:
            temporary = target.with_suffix(".download")
            urllib.request.urlretrieve(url, temporary)
            if hashlib.sha256(temporary.read_bytes()).hexdigest() != expected:
                temporary.unlink(missing_ok=True)
                raise ValueError(f"Model checksum mismatch: {name}")
            temporary.replace(target)
        manifest.append({"file": name, "source": url, "sha256": expected})
    (folder / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print("Speaker models verified. Enable speaker colors in Locaptio Settings, then Start.")


if __name__ == "__main__":
    main()
