"""Installed Windows ML NPU EPs only; no provider/model downloads at app start."""
import json
import tempfile
import atexit
from pathlib import Path

_runtime = None


class WindowsMlTranslationBackend:
    def __init__(self, models):
        import winui3.microsoft.windows.applicationmodel.dynamicdependency.bootstrap as bootstrap
        global _runtime
        if _runtime is None:
            _runtime = bootstrap.initialize()
            atexit.register(_runtime)
        import onnxruntime as ort
        import winui3.microsoft.windows.ai.machinelearning as winml
        catalog = winml.ExecutionProviderCatalog.get_default()
        self.notes = []
        for provider in catalog.find_all_providers():
            if provider.ready_state == winml.ExecutionProviderReadyState.NOT_PRESENT:
                continue  # Downloading is confined to setup-npu.ps1.
            try:
                ready = provider.ensure_ready_async().get()
                if ready.status == winml.ExecutionProviderReadyResultState.SUCCESS:
                    ort.register_execution_provider_library(provider.name, provider.library_path)
            except Exception as exc:
                self.notes.append(f'{provider.name}: {exc}')
        self.devices = [d for d in ort.get_ep_devices() if d.device.type == ort.OrtHardwareDeviceType.NPU]
        if not self.devices:
            raise RuntimeError('使用できるWindows ML NPU EPがありません。scripts/setup-npu.ps1で準備してください。')
        from directml_backend import DirectMlTranslationBackend
        last_error = None
        for device in self.devices:
            try:
                with tempfile.TemporaryDirectory(prefix='LocaptioNpu-') as folder:
                    sessions = []
                    def create(file):
                        options = ort.SessionOptions()
                        options.log_severity_level = 3
                        options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_DISABLE_ALL
                        options.enable_mem_pattern = False
                        options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
                        options.enable_profiling = True
                        options.profile_file_prefix = str(Path(folder) / file.stem)
                        if file.name == 'encoder_model.onnx':
                            prepared = models / 'translation-npu-onnx' / file.name
                            if not prepared.exists():
                                raise RuntimeError('NPU用encoderモデルがありません。scripts/setup-npu.ps1を実行してください。')
                            options.add_provider_for_devices([device], {})
                            session = ort.InferenceSession(str(prepared), sess_options=options)
                        else:
                            session = ort.InferenceSession(str(file), sess_options=options, providers=['CPUExecutionProvider'])
                        session.disable_fallback()
                        sessions.append(session)
                        return session
                    candidate = DirectMlTranslationBackend(models, session_factory=create)
                    text = candidate.translate('This is a test.')
                    if 'テスト' not in text:
                        raise RuntimeError('NPU試験翻訳の検証に失敗しました')
                    counts = {}
                    for session in sessions:
                        profile = Path(session.end_profiling())
                        for event in json.loads(profile.read_text(encoding='utf-8')):
                            name = event.get('args', {}).get('provider')
                            if name:
                                counts[name] = counts.get(name, 0) + 1
                    npu_events = counts.get(device.ep_name, 0)
                    if npu_events == 0:
                        raise RuntimeError('NPUに割り当てられた演算がありません（CPU実行のみ）')
                    self.model = candidate
                    self.name = f'NPU {device.ep_name} (+ CPU); verified kernels={npu_events}'
                    return
            except Exception as exc:
                last_error = exc
        raise RuntimeError(f'NPUモデル初期化／実推論が失敗しました: {last_error}')

    def translate(self, text):
        return self.model.translate(text)
