"""Explicit download of a translation pack chosen in Settings. Workers never download models."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path
import compat
from huggingface_hub import HfApi, snapshot_download
from ctranslate2.converters import TransformersConverter

PACKS = {
    'ja-en': ('staka/fugumt-ja-en', 'translation-ja-en', ['*.json', '*.spm', 'pytorch_model.bin', 'README.md'], 'CC-BY-SA-4.0'),
    'multi': ('facebook/m2m100_418M', 'translation-m2m100', ['*.json', 'sentencepiece.bpe.model', 'pytorch_model.bin', 'README.md'], 'MIT'),
}
parser = argparse.ArgumentParser()
parser.add_argument('--pack', choices=sorted(PACKS), required=True)
args = parser.parse_args()
repo, folder, patterns, license_name = PACKS[args.pack]
models = Path(__file__).resolve().parents[1] / 'models'
source = models / (folder + '-source')
target = models / folder
revision = HfApi().model_info(repo).sha
snapshot_download(repo, revision=revision, local_dir=source, allow_patterns=patterns)
print('LOCAPTIO_CONVERT', flush=True)
TransformersConverter(str(source)).convert(str(target), quantization='int8', force=True)
if args.pack == 'multi':
    from transformers import M2M100Tokenizer
    M2M100Tokenizer.from_pretrained(str(source), local_files_only=True).save_pretrained(str(target))
else:
    from transformers import MarianTokenizer
    MarianTokenizer.from_pretrained(str(source), local_files_only=True).save_pretrained(str(target))
def describe(p):
    with p.open('rb') as stream:
        return {'bytes': p.stat().st_size, 'sha256': hashlib.file_digest(stream, 'sha256').hexdigest()}
manifest = {'repository': repo, 'revision': revision, 'license': license_name, 'quantization': 'int8',
            'files': {p.name: describe(p) for p in target.iterdir() if p.is_file()}}
(target / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
readme = source / 'README.md'
if readme.exists():
    shutil.copyfile(readme, target / 'README.md')
# The converted model is all that is used; the original weights are large.
shutil.rmtree(source, ignore_errors=True)
print('Translation pack installed:', target)
