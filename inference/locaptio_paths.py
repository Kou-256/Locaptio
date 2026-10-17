"""Application data paths and one-time compatibility import for Locaptio."""
import os
import shutil
import tempfile
from pathlib import Path


def profile_folder(name):
    parent = Path(os.environ['LOCALAPPDATA']) / 'Locaptio'
    target = parent / name
    previous = parent.parent / 'LocalCaptionTranslate' / name
    if not target.exists() and previous.is_dir():
        parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='migration-', dir=parent) as work:
            staging = Path(work) / name
            shutil.copytree(previous, staging)
            if name == 'AmdNpuCache':
                for item in list(staging.iterdir()):
                    if item.name.startswith('localcaption-'):
                        item.rename(staging / item.name.replace('localcaption-', 'locaptio-', 1))
            if not target.exists():
                staging.rename(target)
    target.mkdir(parents=True, exist_ok=True)
    return target
