"""Export downloaded FuguMT locally. No additional model downloads."""
import os
os.environ['HF_HUB_OFFLINE'] = '1'
os.environ['TRANSFORMERS_OFFLINE'] = '1'
import compat
from pathlib import Path
import json
import hashlib
from optimum.exporters.onnx import main_export
import optimum.exporters.onnx.base as export_base
import onnxruntime as ort
# Optimum 0.1 checks the generic distribution name, while the DirectML wheel
# provides the same imported module. Verify the module before overriding that check.
assert 'CPUExecutionProvider' in ort.get_available_providers()
export_base.is_onnxruntime_available = lambda: True
root = Path(__file__).resolve().parents[1]
main_export(str(root / 'models' / 'fugumt-source'), output=root / 'models' / 'translation-onnx',
            task='text2text-generation', opset=18, device='cpu', local_files_only=True,
            do_validation=True, no_post_process=True)
print('ONNX export complete')
manifest_path = root / 'models' / 'manifest.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
entry = {'repository': 'staka/fugumt-en-ja', 'revision': manifest['translation']['revision'],
         'format': 'ONNX opset18 encoder/decoder', 'license': 'CC-BY-SA-4.0', 'files': {}}
for p in (root / 'models' / 'translation-onnx').iterdir():
    if p.is_file():
        with p.open('rb') as stream:
            entry['files'][p.name] = {'bytes': p.stat().st_size, 'sha256': hashlib.file_digest(stream, 'sha256').hexdigest()}
manifest['translation-onnx'] = entry
manifest_path.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
