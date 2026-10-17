"""Explicit download of AMD's NPU-optimized multilingual Whisper-small."""
import hashlib
import json
from pathlib import Path
from huggingface_hub import HfApi, snapshot_download

root = Path(__file__).resolve().parents[1]
repo = 'amd/whisper-small-onnx-npu'
revision = HfApi().model_info(repo).sha
target = root / 'models' / 'speech-amd-npu'
snapshot_download(repo, revision=revision, local_dir=target)
snapshot_download('openai/whisper-small', local_dir=target / 'tokenizer',
                  allow_patterns=['*.json', '*.txt', 'README.md'])
files = {}
for file in target.glob('*'):
    if file.is_file():
        with file.open('rb') as stream:
            files[file.name] = {'bytes': file.stat().st_size, 'sha256': hashlib.file_digest(stream, 'sha256').hexdigest()}
(target / 'manifest.json').write_text(json.dumps({'repository': repo, 'revision': revision, 'files': files}, indent=2), encoding='utf-8')
print('AMD Whisper model downloaded:', target, flush=True)
