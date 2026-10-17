"""Only this explicitly invoked setup entry point permits network access."""
import argparse
import hashlib
import json
from pathlib import Path
import compat
from huggingface_hub import HfApi, snapshot_download
from ctranslate2.converters import TransformersConverter
from transformers import MarianTokenizer

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--speech', choices=['tiny.en', 'base.en', 'small.en'], default='tiny.en')
args = parser.parse_args()
models = root / 'models'
models.mkdir(exist_ok=True)
api = HfApi()
manifest = {}
speech_repo = 'Systran/faster-whisper-' + args.speech
speech_revision = api.model_info(speech_repo).sha
snapshot_download(speech_repo, revision=speech_revision, local_dir=models / 'speech',
                  allow_patterns=['model.bin', 'config.json', 'tokenizer.json', 'vocabulary.*', 'preprocessor_config.json'])
manifest['speech'] = {'repository': speech_repo, 'revision': speech_revision}
repo = 'staka/fugumt-en-ja'
revision = api.model_info(repo).sha
source = models / 'fugumt-source'
snapshot_download(repo, revision=revision, local_dir=source,
                  allow_patterns=['*.json', '*.spm', 'pytorch_model.bin', 'README.md'])
target = models / 'translation'
TransformersConverter(str(source)).convert(str(target), quantization='int8', force=True)
MarianTokenizer.from_pretrained(str(source), local_files_only=True).save_pretrained(str(target))
manifest['translation'] = {'repository': repo, 'revision': revision, 'quantization': 'int8'}
def describe(p):
    with p.open('rb') as stream:
        return {'bytes': p.stat().st_size, 'sha256': hashlib.file_digest(stream, 'sha256').hexdigest()}
for entry in ('speech', 'translation'):
    manifest[entry]['files'] = {p.name: describe(p)
                              for p in (models / entry).iterdir() if p.is_file()}
(models / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print('Models installed locally:', models)
