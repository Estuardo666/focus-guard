"""Run real Avalonia shell with isolated, already-downloaded libraries. No OS install."""
import os
from pathlib import Path
import subprocess
import tempfile

repo = Path(__file__).resolve().parents[2]
packages = repo / 'artifacts/cloud-phase0/toolchains/linux-native/downloads'
binary = repo / 'artifacts/cloud-phase0/linux-shell/LinuxShellProbe'
archives = sorted(packages.glob('*.deb'))
if len(archives) != 2 or not binary.is_file():
    raise RuntimeError('Need published shell plus the two verified libICE/libSM package archives')
with tempfile.TemporaryDirectory(prefix='focusguard-shell-libs-') as root:
    for archive in archives:
        subprocess.run(['dpkg-deb', '--extract', str(archive), root], check=True, timeout=20)
    env = dict(os.environ)
    env['LD_LIBRARY_PATH'] = str(Path(root) / 'usr/lib/x86_64-linux-gnu')
    result = subprocess.run([str(binary)], env=env, capture_output=True, text=True, timeout=45)
    print(result.stdout, end='')
    if result.stderr:
        print(result.stderr, end='')
    if result.returncode != 0 or 'SHELL_WINDOW_OPENED' not in result.stdout or 'SHELL_EXIT_NORMAL' not in result.stdout:
        raise RuntimeError('The real shell did not open and close successfully')
