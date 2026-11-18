"""Explicit download of a local LLM (official Qwen3 GGUF). The inference worker never downloads models."""
import argparse
import hashlib
import json
import re
from pathlib import Path
from huggingface_hub import HfApi, hf_hub_download

parser = argparse.ArgumentParser()
parser.add_argument('--repo', default='Qwen/Qwen3-4B-GGUF')
parser.add_argument('--file', default='Qwen3-4B-Q4_K_M.gguf')
args = parser.parse_args()
# Only the official Qwen3 GGUF builds offered in Settings.
if not re.fullmatch(r'Qwen/Qwen3-[0-9.]+B-GGUF', args.repo) or not re.fullmatch(r'Qwen3-[0-9.]+B-Q[0-9A-Z_]+\.gguf', args.file):
    raise SystemExit(f'Unsupported model: {args.repo} {args.file}')
root = Path(__file__).resolve().parents[1]
folder = root / 'models' / 'llm'
revision = HfApi().model_info(args.repo).sha
path = Path(hf_hub_download(args.repo, filename=args.file, revision=revision, local_dir=folder))
with path.open('rb') as stream:
    entry = {'repository': args.repo, 'revision': revision, 'file': args.file, 'bytes': path.stat().st_size,
             'sha256': hashlib.file_digest(stream, 'sha256').hexdigest(), 'license': 'Apache-2.0'}
stem = Path(args.file).stem
(folder / f'{stem}.manifest.json').write_text(json.dumps(entry, indent=2), encoding='utf-8')
if args.file == 'Qwen3-4B-Q4_K_M.gguf':
    # Earlier versions read this name for the default model.
    (folder / 'manifest.json').write_text(json.dumps(entry, indent=2), encoding='utf-8')
readme = Path(hf_hub_download(args.repo, filename='README.md', revision=revision, local_dir=folder / '.readme' / stem))
(folder / f'{stem}.README.md').write_bytes(readme.read_bytes())
print('Local LLM installed:', path)
