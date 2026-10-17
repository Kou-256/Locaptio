import sys
sys.stdout.reconfigure(encoding='utf-8')
import time
import json
from pathlib import Path
import worker  # Enforces offline network guard and Unicode SentencePiece support.
from directml_backend import DirectMlTranslationBackend

backend = DirectMlTranslationBackend(Path(__file__).resolve().parents[1] / 'models', profile=True)
for text in ['This is a test.', 'We need to improve the performance of this system.', 'This is a test.']:
    started = time.perf_counter()
    result = backend.translate(text)
    print(json.dumps({'English': text, 'Japanese': result, 'ms': (time.perf_counter() - started) * 1000}, ensure_ascii=False))
    if text == 'This is a test.':
        assert result == 'これはテストです。', result
print('Providers:', backend.encoder.get_providers(), backend.decoder.get_providers())
counts = {}
for session in [backend.encoder, backend.decoder]:
    path = Path(session.end_profiling())
    for event in json.loads(path.read_text(encoding='utf-8')):
        provider = event.get('args', {}).get('provider')
        if provider:
            counts[provider] = counts.get(provider, 0) + 1
print('Executed kernel events:', counts)
assert counts.get('DmlExecutionProvider', 0) > 0, 'No DirectML kernel execution was recorded'
