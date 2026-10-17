"""Local AMD NPU Whisper over JSONL; no downloads during inference."""
import argparse
import base64
import json
import os
import socket
import sys
import time
from pathlib import Path

os.environ['HF_HUB_OFFLINE'] = '1'
os.environ['TRANSFORMERS_OFFLINE'] = '1'
os.environ['HF_HUB_DISABLE_TELEMETRY'] = '1'
def offline(*a, **k):
    raise RuntimeError('NPU推論中のネットワーク接続は禁止されています')
for name in ('connect', 'connect_ex', 'sendto'):
    setattr(socket.socket, name, offline)
for name in ('create_connection', 'getaddrinfo', 'gethostbyname'):
    setattr(socket, name, offline)
sys.stdout.reconfigure(encoding='utf-8')
sys.stdin.reconfigure(encoding='utf-8')
def emit(value):
    print(json.dumps(value, ensure_ascii=False), flush=True)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--models', type=Path, required=True)
    parser.add_argument('--language', default='ja', choices=['en', 'ja'])
    parser.add_argument('--prepare-only', action='store_true')
    parser.add_argument('--ep-library', type=Path)
    args = parser.parse_args()
    # AMD's cached graph serializer mishandles non-ASCII absolute model paths.
    # LocalBackend starts this process in the runtime folder, so keep model/config
    # filenames relative and ASCII even when the runtime itself has a Japanese name.
    folder = Path(os.path.relpath(args.models, Path.cwd())) / 'speech-amd-npu'
    # VAIML builds shell arguments containing cache paths without quoting spaces.
    from locaptio_paths import profile_folder
    cache = profile_folder('AmdNpuCache')
    cache.mkdir(parents=True, exist_ok=True)
    if ' ' in str(cache):
        import ctypes
        buffer = ctypes.create_unicode_buffer(32768)
        if ctypes.windll.kernel32.GetShortPathNameW(str(cache), buffer, len(buffer)):
            cache = Path(buffer.value)
        if ' ' in str(cache):
            raise RuntimeError('AMDコンパイラには空白のないキャッシュ保存先が必要です: ' + str(cache))
    if not (folder / 'encoder_model.onnx').is_file():
        raise RuntimeError('AMD NPU用Whisperモデルがありません。scripts/setup-amd-speech.ps1を実行してください。')
    import numpy as np
    import winui3.microsoft.windows.applicationmodel.dynamicdependency.bootstrap as bootstrap
    with bootstrap.initialize():
        import onnxruntime as ort
        import winui3.microsoft.windows.ai.machinelearning as winml
        for provider in winml.ExecutionProviderCatalog.get_default().find_all_providers():
            if provider.name == 'VitisAIExecutionProvider' and provider.ready_state != winml.ExecutionProviderReadyState.NOT_PRESENT:
                result = provider.ensure_ready_async().get()
                if result.status == winml.ExecutionProviderReadyResultState.SUCCESS:
                    library = args.ep_library or profile_folder('AmdEp') / Path(provider.library_path).name
                    if not library.is_file():
                        library = Path(provider.library_path)
                    dll_directory = os.add_dll_directory(str(library.parent))
                    ort.register_execution_provider_library(provider.name, str(library))
        devices = [d for d in ort.get_ep_devices() if d.device.type == ort.OrtHardwareDeviceType.NPU and d.ep_name == 'VitisAIExecutionProvider']
        if not devices:
            raise RuntimeError('AMD NPU EPが準備されていません。scripts/setup-npu.ps1を実行してください。')
        from transformers import WhisperFeatureExtractor, WhisperTokenizer
        features = WhisperFeatureExtractor.from_pretrained(folder / 'tokenizer', local_files_only=True)
        tokenizer = WhisperTokenizer.from_pretrained(folder / 'tokenizer', local_files_only=True)
        tokenizer.set_prefix_tokens(language=args.language, task='transcribe', predict_timestamps=False)
        sessions = []
        for name in ('encoder', 'decoder'):
            options = ort.SessionOptions()
            options.log_severity_level = 3
            options.enable_profiling = True
            options.profile_file_prefix = str(folder / ('profile-' + name))
            options.add_provider_for_devices([devices[0]], {
                'config_file': str(folder / ('vitisai_config_whisper_' + name + '.json')),
                'cache_dir': str(cache), 'cache_key': 'locaptio-small-v3-' + name})
            emit({'type': 'status', 'message': 'AMD NPU: ' + name + 'を準備中（初回コンパイルは時間がかかります）'})
            sessions.append(ort.InferenceSession(str(folder / (name + '_model.onnx')), sess_options=options))
        encoder, decoder = sessions
        ids_input = next(item for item in decoder.get_inputs() if item.type == 'tensor(int64)')
        state_input = next(item for item in decoder.get_inputs() if item.type != 'tensor(int64)')
        length = int(ids_input.shape[1])
        def recognize(audio):
            if len(audio) > 16000 * 12:
                raise ValueError('音声の上限は12秒です')
            if len(audio) == 0 or float(np.sqrt(np.mean(audio ** 2))) < .002:
                return ''
            mel = features(audio, sampling_rate=16000, return_tensors='np')['input_features']
            hidden = encoder.run(None, {encoder.get_inputs()[0].name: mel})[0]
            tokens = list(tokenizer.prefix_tokens)
            while len(tokens) < min(length, 160):
                padded = np.full((1, length), tokenizer.eos_token_id, dtype=np.int64)
                padded[0, :len(tokens)] = tokens
                logits = decoder.run(None, {ids_input.name: padded, state_input.name: hidden})[0]
                token = int(np.argmax(logits[0, len(tokens) - 1]))
                if token == tokenizer.eos_token_id:
                    break
                tokens.append(token)
            return tokenizer.decode(tokens, skip_special_tokens=True).strip()
        # Force actual encoder/decoder work before claiming NPU use.
        recognize(np.full(16000, .01, dtype=np.float32))
        counts = {}
        for session in sessions:
            profile = Path(session.end_profiling())
            for item in json.loads(profile.read_text(encoding='utf-8')):
                name = item.get('args', {}).get('provider', '')
                if name:
                    counts[name] = counts.get(name, 0) + 1
            profile.unlink(missing_ok=True)
        npu_count = sum(count for name, count in counts.items() if 'Vitis' in name or 'Ryzen' in name)
        if not npu_count:
            raise RuntimeError('NPUに割り当てられた演算を確認できません。GPUを選択してください。')
        emit({'type': 'ready', 'pid': os.getpid(), 'backend': f'AMD XDNA 2 NPU (+ CPU) · kernels={npu_count}', 'model': 'AMD Whisper-small', 'providers': list(counts)})
        if args.prepare_only:
            return
        for line in sys.stdin:
            request = {}
            try:
                request = json.loads(line)
                started = time.perf_counter()
                audio = np.frombuffer(base64.b64decode(request['audio']), dtype='<f4').copy()
                emit({'type': 'result', 'id': request['id'], 'text': recognize(audio), 'ms': (time.perf_counter() - started) * 1000})
            except Exception as exc:
                emit({'type': 'error', 'id': request.get('id'), 'message': str(exc)})
if __name__ == '__main__':
    try:
        main()
    except Exception as exc:
        emit({'type': 'error', 'message': str(exc)})
        raise SystemExit(2)
