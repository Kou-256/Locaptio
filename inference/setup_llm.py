"""Explicit first-time download. The inference worker never downloads models."""
import hashlib
import json
from pathlib import Path
from huggingface_hub import HfApi, hf_hub_download
root = Path(__file__).resolve().parents[1]
repo = 'Qwen/Qwen3-4B-GGUF'
filename = 'Qwen3-4B-Q4_K_M.gguf'
revision = HfApi().model_info(repo).sha
path = Path(hf_hub_download(repo, filename=filename, revision=revision, local_dir=root / 'models' / 'llm'))
with path.open('rb') as stream:
    entry = {'repository': repo, 'revision': revision, 'file': filename, 'bytes': path.stat().st_size, 'sha256': hashlib.file_digest(stream, 'sha256').hexdigest(), 'license': 'Apache-2.0'}
(path.parent / 'manifest.json').write_text(json.dumps(entry, indent=2), encoding='utf-8')
hf_hub_download(repo, filename='README.md', revision=revision, local_dir=path.parent)
print('Local LLM installed:', path)
