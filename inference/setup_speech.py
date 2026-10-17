"""Explicit download of an additional multilingual speech model."""
import argparse
import hashlib
import json
from pathlib import Path
from huggingface_hub import HfApi, snapshot_download

parser = argparse.ArgumentParser()
parser.add_argument('--model', choices=['tiny', 'base', 'small', 'medium', 'large-v3-turbo'], default='small')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
repo = 'dropbox-dash/faster-whisper-large-v3-turbo' if args.model == 'large-v3-turbo' else 'Systran/faster-whisper-' + args.model
revision = HfApi().model_info(repo).sha
target = root / 'models' / ('speech-' + args.model)
snapshot_download(repo, revision=revision, local_dir=target,
                  allow_patterns=['model.bin', 'config.json', 'tokenizer.json', 'vocabulary.*', 'preprocessor_config.json', 'README.md'])
manifest_path = root / 'models' / 'manifest.json'
manifest = json.loads(manifest_path.read_text()) if manifest_path.exists() else {}
files = {}
for file in target.iterdir():
    if file.is_file():
        with file.open('rb') as stream:
            files[file.name] = {'bytes': file.stat().st_size, 'sha256': hashlib.file_digest(stream, 'sha256').hexdigest()}
manifest['speech-' + args.model] = {'repository': repo, 'revision': revision, 'multilingual': True, 'files': files}
manifest_path.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print('Installed multilingual speech model:', target)
