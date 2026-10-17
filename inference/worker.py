"""JSONL over redirected stdio. No HTTP server, audio files, or downloads."""
import os
os.environ['HF_HUB_OFFLINE'] = '1'
os.environ['TRANSFORMERS_OFFLINE'] = '1'
os.environ['HF_HUB_DISABLE_TELEMETRY'] = '1'
import socket
def deny_network(*args, **kwargs):
    raise RuntimeError('通常の推論プロセスではネットワーク接続は禁止されています')
socket.socket.connect = deny_network
socket.socket.connect_ex = deny_network
socket.create_connection = deny_network
socket.getaddrinfo = deny_network
socket.gethostbyname = deny_network
socket.gethostbyname_ex = deny_network
socket.gethostbyaddr = deny_network
socket.socket.sendto = deny_network
import argparse
import base64
import json
import sys
import time
from pathlib import Path
_dll_paths = []
cuda_bin = Path(__file__).resolve().parents[1] / 'runtime' / 'cuda' / 'bin'
if cuda_bin.is_dir():
    os.environ['PATH'] = str(cuda_bin) + os.pathsep + os.environ.get('PATH', '')
    _dll_paths.append(os.add_dll_directory(str(cuda_bin)))
import compat
import ctranslate2
import numpy as np

def emit(value):
    print(json.dumps(value, ensure_ascii=False), flush=True)

class ISpeechRecognitionBackend:
    def recognize(self, samples):
        raise NotImplementedError

class ITranslationBackend:
    def translate(self, text):
        raise NotImplementedError

class WhisperBackend(ISpeechRecognitionBackend):
    def __init__(self, path, device, language='en'):
        from faster_whisper import WhisperModel
        self.model = WhisperModel(str(path), device=device, compute_type='int8_float16' if device == 'cuda' else 'int8',
                                  cpu_threads=max(1, min(8, (os.cpu_count() or 4) // 2)), local_files_only=True)
        self.language = language
        self.context = []
        if language == 'ja' and not self.model.model.is_multilingual:
            raise RuntimeError('日本語には多言語モデルが必要です。scripts/setup-speech.ps1を実行してください。')
    def recognize(self, samples, vad=True, final=True, prompt='', draft=False):
        history = ' '.join(self.context)[-100 if draft else -500:]
        segments, _ = self.model.transcribe(samples, language=self.language, task='transcribe', beam_size=5 if final and not draft else 1,
                                            temperature=0, condition_on_previous_text=False,
                                            initial_prompt=(prompt[:200 if draft else 500] + '\n' + history).strip() or None,
                                            max_new_tokens=max(24, int(len(samples) / 16000 * 12) + 16) if draft else None,
                                            repetition_penalty=1.05 if draft else 1,
                                            vad_filter=vad, vad_parameters={'min_silence_duration_ms': 500})
        text = ' '.join(s.text.strip() for s in segments).strip()
        if final and text:
            self.context = (self.context + [text])[-4:]
        return text

class MarianBackend(ITranslationBackend):
    def __init__(self, path, device):
        from transformers import MarianTokenizer
        self.tokenizer = MarianTokenizer.from_pretrained(str(path), local_files_only=True)
        self.model = ctranslate2.Translator(str(path), device=device, compute_type='int8_float16' if device == 'cuda' else 'int8',
                                           inter_threads=1, intra_threads=max(1, min(8, (os.cpu_count() or 4) // 2)))
    def translate(self, text):
        ids = self.tokenizer.encode(text, truncation=True, max_length=256)
        tokens = self.tokenizer.convert_ids_to_tokens(ids)
        result = self.model.translate_batch([tokens], beam_size=2, max_decoding_length=256)[0].hypotheses[0]
        return self.tokenizer.decode(self.tokenizer.convert_tokens_to_ids(result), skip_special_tokens=True)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--kind', choices=['speech', 'translation'], required=True)
    parser.add_argument('--models', type=Path, required=True)
    parser.add_argument('--device', choices=['auto', 'cuda', 'cpu'], default='auto')
    parser.add_argument('--prefer-npu', action='store_true')
    parser.add_argument('--fast', action='store_true')
    parser.add_argument('--draft', action='store_true')
    parser.add_argument('--language', choices=['en', 'ja'], default='en')
    parser.add_argument('--speech-model', choices=['legacy', 'tiny', 'base', 'small', 'medium', 'large-v3-turbo'], default='legacy')
    args = parser.parse_args()
    path = args.models / args.kind
    if args.kind == 'speech' and args.speech_model != 'legacy':
        path = args.models / ('speech-' + args.speech_model)
    if not (path / 'model.bin').exists():
        emit({'type': 'error', 'message': '必要なモデルがインストールされていません: ' + str(path)})
        return 2
    def factory(path, device):
        if device == 'npu':
            from windowsml_backend import WindowsMlTranslationBackend
            return WindowsMlTranslationBackend(args.models)
        if device == 'dml':
            from directml_backend import DirectMlTranslationBackend
            return DirectMlTranslationBackend(args.models)
        if args.kind == 'speech':
            actual_model = args.speech_model
            if device == 'cpu' and args.speech_model == 'large-v3-turbo' and (args.models / 'speech-small' / 'model.bin').exists():
                path = args.models / 'speech-small'
                actual_model = 'small'
                emit({'type': 'status', 'message': 'CPUでは遅延を抑えるためsmallモデルを使用します。'})
            result = WhisperBackend(path, device, args.language)
            result.model_name = actual_model
            return result
        return MarianBackend(path, device)
    try:
        cuda_count = ctranslate2.get_cuda_device_count()
        device = 'cuda' if args.device == 'auto' and cuda_count > 0 else 'cpu'
    except Exception:
        cuda_count = 0
        device = 'cpu'
    if args.device == 'cuda':
        if cuda_count < 1:
            emit({'type': 'error', 'message': 'NVIDIA CUDA GPUを検出できません。NVIDIAドライバーとCUDA推論環境を確認するか、GPU自動／CPUを選んでください。'})
            return 2
        device = 'cuda'
    if args.prefer_npu and args.device == 'auto' and args.kind == 'translation':
        try:
            backend = factory(path, 'npu')
            device = 'npu'
        except Exception as exc:
            emit({'type': 'status', 'message': 'NPU利用不可、GPU/CPUへ切替: ' + str(exc)})
    if device != 'npu' and args.kind == 'translation' and args.device == 'auto' and (args.models / 'translation-onnx' / 'encoder_model.onnx').exists():
        import onnxruntime
        if 'DmlExecutionProvider' in onnxruntime.get_available_providers():
            device = 'dml'
    try:
        if device != 'npu':
            backend = factory(path, device)
        if args.kind == 'speech':
            backend.recognize(np.zeros(16000, dtype=np.float32), vad=False, final=False)
        else:
            backend.translate('This is a test.')
    except Exception as exc:
        if args.device == 'cuda':
            emit({'type': 'error', 'message': 'CUDA初期化に失敗しました。CUDA 12・cuDNN 9の推論環境を確認してください: ' + str(exc)})
            return 2
        if device == 'cpu':
            emit({'type': 'error', 'message': str(exc)})
            return 2
        device = 'cpu'
        backend = factory(path, device)
        emit({'type': 'status', 'message': 'GPU初期化失敗、CPUを使用: ' + str(exc)})
    import onnxruntime
    emit({'type': 'ready', 'pid': os.getpid(), 'backend': (backend.name if device == 'npu' else 'DirectML GPU (device 0)' if device == 'dml' else 'CUDA GPU' if device == 'cuda' else 'CPU INT8'),
          'model': getattr(backend, 'model_name', args.kind),
          'providers': onnxruntime.get_available_providers() + (['CUDA'] if cuda_count > 0 else [])})
    for line in sys.stdin:
        request = {}
        try:
            request = json.loads(line)
            started = time.perf_counter()
            def infer():
                if args.kind == 'speech':
                    samples = np.frombuffer(base64.b64decode(request['audio']), dtype='<f4').copy()
                    if len(samples) > 16000 * 12:
                        raise ValueError('Audio request exceeds 12 seconds')
                    if request.get('reset_context'):
                        backend.context = []
                    return backend.recognize(samples, vad=request.get('final', True) and not args.draft, final=request.get('final', True), prompt=request.get('prompt', ''), draft=args.draft)
                return backend.translate(request['text'])
            try:
                text = infer()
            except Exception as exc:
                if args.device == 'cuda' or device not in ('cuda', 'dml', 'npu'):
                    raise
                del backend
                device = 'cpu'
                backend = factory(path, device)
                emit({'type': 'status', 'backend': 'CPU INT8', 'message': 'GPU推論失敗、CPUへ切替: ' + str(exc)})
                text = infer()
            emit({'type': 'result', 'id': request['id'], 'text': text, 'ms': (time.perf_counter() - started) * 1000})
        except Exception as exc:
            emit({'type': 'error', 'id': request.get('id'), 'message': str(exc)})
    return 0

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stdin.reconfigure(encoding='utf-8')
    raise SystemExit(main())
