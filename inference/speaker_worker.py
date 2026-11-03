"""Local-only, bounded speaker embeddings. Audio arrives in memory via stdin."""
import argparse
import base64
import json
from pathlib import Path
import sys

import numpy as np
import sherpa_onnx


def send(message):
    print(json.dumps(message, ensure_ascii=True), flush=True)


class Embeddings:
    def __init__(self, models):
        config = sherpa_onnx.SpeakerEmbeddingExtractorConfig(
            model=str(models / "embedding.onnx"), num_threads=1, provider="cpu")
        if not config.validate():
            raise ValueError("Invalid speaker embedding model")
        self.extractor = sherpa_onnx.SpeakerEmbeddingExtractor(config)
        vad = sherpa_onnx.VadModelConfig()
        vad.silero_vad.model = str(models / "silero_vad.onnx")
        vad.silero_vad.min_speech_duration = 0.15
        vad.silero_vad.min_silence_duration = 0.15
        vad.sample_rate = 16000
        vad.num_threads = 1
        self.vad = sherpa_onnx.VoiceActivityDetector(vad, buffer_size_in_seconds=4)
        self.window = vad.silero_vad.window_size

    def extract(self, samples):
        if not 12000 <= len(samples) <= 32000 or not np.isfinite(samples).all():
            return None
        self.vad.reset()
        for offset in range(0, len(samples), self.window):
            chunk = samples[offset:offset + self.window]
            if len(chunk) < self.window:
                chunk = np.pad(chunk, (0, self.window - len(chunk)))
            self.vad.accept_waveform(chunk)
        self.vad.flush()
        speech = []
        while not self.vad.empty():
            speech.append(np.array(self.vad.front.samples, dtype=np.float32))
            self.vad.pop()
        if not speech:
            return None
        speech = np.concatenate(speech)
        if len(speech) < 10400:
            return None
        stream = self.extractor.create_stream()
        stream.accept_waveform(16000, speech)
        stream.input_finished()
        if not self.extractor.is_ready(stream):
            return None
        vector = np.asarray(self.extractor.compute(stream), dtype=np.float32)
        norm = float(np.linalg.norm(vector))
        return (vector / norm).tolist() if norm > 1e-6 and np.isfinite(vector).all() else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--models", type=Path, required=True)
    args = parser.parse_args()
    engine = Embeddings(args.models)
    send({"type": "ready"})
    for line in sys.stdin:
        try:
            if len(line) > 180000:
                raise ValueError("Audio message too large")
            request = json.loads(line)
            samples = np.frombuffer(base64.b64decode(request["audio"], validate=True), dtype="<f4").copy()
            send({"type": "result", "embedding": engine.extract(samples)})
        except Exception as error:
            send({"type": "error", "message": str(error)})


if __name__ == "__main__":
    main()
