"""Explicit setup: keep AMD's compiler out of the long WindowsApps path."""
import os
import shutil
from pathlib import Path
import winui3.microsoft.windows.applicationmodel.dynamicdependency.bootstrap as bootstrap

with bootstrap.initialize():
    import winui3.microsoft.windows.ai.machinelearning as winml
    for provider in winml.ExecutionProviderCatalog.get_default().find_all_providers():
        if provider.name != 'VitisAIExecutionProvider':
            continue
        result = provider.ensure_ready_async().get()
        if result.status != winml.ExecutionProviderReadyResultState.SUCCESS:
            raise SystemExit('AMD NPU EPの準備に失敗しました: ' + result.status.name)
        source = Path(provider.library_path).parent
        target = Path(os.environ['LOCALAPPDATA']) / 'Locaptio' / 'AmdEp'
        target.mkdir(parents=True, exist_ok=True)
        for file in source.iterdir():
            if file.is_file():
                shutil.copy2(file, target / file.name)
        (target / 'source-package.txt').write_text(str(source), encoding='utf-8')
        print('AMD EP staged: ' + str(target))
        break
    else:
        raise SystemExit('AMD NPU EPが見つかりません。Windows MLとAMD NPUドライバーを確認してください。')
