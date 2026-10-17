from pathlib import Path
import json
import numpy as np
import onnxruntime as ort
from transformers import MarianTokenizer

class DirectMlTranslationBackend:
    def __init__(self, models: Path, profile=False, session_factory=None):
        if session_factory is None and 'DmlExecutionProvider' not in ort.get_available_providers():
            raise RuntimeError('DirectML Execution Provider is unavailable')
        path = models / 'translation-onnx'
        options = ort.SessionOptions()
        options.enable_mem_pattern = False
        options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
        options.log_severity_level = 3
        options.enable_profiling = profile
        if profile:
            artifacts = models.parent / 'artifacts'
            artifacts.mkdir(exist_ok=True)
            options.profile_file_prefix = str(artifacts / 'directml-profile')
        providers = [('DmlExecutionProvider', {'device_id': 0}), 'CPUExecutionProvider']
        create_session = session_factory or (lambda file: ort.InferenceSession(str(file), options, providers=providers))
        self.encoder = create_session(path / 'encoder_model.onnx')
        self.decoder = create_session(path / 'decoder_model.onnx')
        self.tokenizer = MarianTokenizer.from_pretrained(str(models / 'translation'), local_files_only=True)
        self.config = json.loads((path / 'config.json').read_text(encoding='utf-8'))
        self.encoder_names = {value.name for value in self.encoder.get_inputs()}
        self.decoder_names = {value.name for value in self.decoder.get_inputs()}

    def translate(self, text):
        encoded = self.tokenizer(text, return_tensors='np', truncation=True, max_length=256)
        ids = encoded['input_ids'].astype(np.int64)
        mask = encoded['attention_mask'].astype(np.int64)
        encoder_inputs = {'input_ids': ids, 'attention_mask': mask}
        hidden = self.encoder.run(None, {k: v for k, v in encoder_inputs.items() if k in self.encoder_names})[0]
        result = [self.config['decoder_start_token_id']]
        for _ in range(128):
            inputs = {'input_ids': np.array([result], dtype=np.int64), 'encoder_hidden_states': hidden,
                      'encoder_attention_mask': mask}
            logits = self.decoder.run(['logits'], {k: v for k, v in inputs.items() if k in self.decoder_names})[0][0, -1].copy()
            logits[self.tokenizer.pad_token_id] = -np.inf
            next_token = int(np.argmax(logits))
            if next_token == self.tokenizer.eos_token_id:
                break
            result.append(next_token)
        return self.tokenizer.decode(result[1:], skip_special_tokens=True)
