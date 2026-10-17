"""SentencePiece wheels may not accept Windows Unicode file names."""
from pathlib import Path
import sentencepiece

def load_unicode(self, path):
    return self.LoadFromSerializedProto(Path(path).read_bytes())

sentencepiece.SentencePieceProcessor.LoadFromFile = load_unicode
