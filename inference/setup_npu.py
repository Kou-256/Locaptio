"""Explicit first-time EP preparation. Never called by ordinary inference."""
import winui3.microsoft.windows.applicationmodel.dynamicdependency.bootstrap as bootstrap
with bootstrap.initialize():
    import onnxruntime as ort
    import winui3.microsoft.windows.ai.machinelearning as winml
    catalog = winml.ExecutionProviderCatalog.get_default()
    for provider in catalog.find_all_providers():
        if provider.name not in ('VitisAIExecutionProvider', 'QNNExecutionProvider', 'OpenVINOExecutionProvider'):
            continue
        print(f'Preparing {provider.name}...', flush=True)
        result = provider.ensure_ready_async().get()
        print(f'{provider.name}: {result.status.name}', flush=True)
        if result.status == winml.ExecutionProviderReadyResultState.SUCCESS:
            ort.register_execution_provider_library(provider.name, provider.library_path)
    devices = ort.get_ep_devices()
    for device in devices:
        print(f'{device.ep_name}: {device.device.type}', flush=True)
    if not any(d.device.type == ort.OrtHardwareDeviceType.NPU for d in devices):
        raise SystemExit('NPU EPを利用できません。GPU/CPUへのフォールバックは引き続き利用できます。')
