"""Offline staged inference check using a locally generated test WAV."""
import argparse
import json
import time
import wave
import sys
sys.stdout.reconfigure(encoding='utf-8')
from pathlib import Path
import numpy as np
from worker import WhisperBackend, MarianBackend
import socket
try:
    socket.getaddrinfo('example.com', 443)
    raise AssertionError('Offline DNS guard was not enforced')
except RuntimeError:
    pass

parser = argparse.ArgumentParser()
parser.add_argument('phase', choices=['speech', 'translation', 'combined'])
parser.add_argument('--wav', type=Path)
args = parser.parse_args()
models = Path(__file__).resolve().parents[1] / 'models'
text = 'This is a test.'
if args.phase in ('speech', 'combined'):
    with wave.open(str(args.wav)) as audio:
        assert audio.getframerate() == 16000 and audio.getnchannels() == 1 and audio.getsampwidth() == 2
        samples = np.frombuffer(audio.readframes(audio.getnframes()), dtype='<i2').astype(np.float32) / 32768
    backend = WhisperBackend(models / 'speech', 'cpu')
    started = time.perf_counter()
    text = backend.recognize(samples)
    print(json.dumps({'English': text, 'STT_ms': (time.perf_counter() - started) * 1000}))
    assert 'test' in text.lower(), text
if args.phase in ('translation', 'combined'):
    backend = MarianBackend(models / 'translation', 'cpu')
    started = time.perf_counter()
    translated = backend.translate(text)
    print(json.dumps({'English': text, 'Japanese': translated, 'Translation_ms': (time.perf_counter() - started) * 1000}, ensure_ascii=False))
    assert any('\u3040' <= c <= '\u9fff' for c in translated), translated
    if text == 'This is a test.':
        assert 'テスト' in translated, translated
