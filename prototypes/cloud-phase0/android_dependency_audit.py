"""Offline artifact/ABI audit only; it does not run Android native code."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('aar', type=Path)
parser.add_argument('--libsodium-runtimes', type=Path)
args = parser.parse_args()
expected = 'b5378c1d9db2573d61b304e89cf83db187a05c3e4ff081d9b2ef3d0bb00ca314'
if hashlib.sha256(args.aar.read_bytes()).hexdigest() != expected:
    raise RuntimeError('Artifact changed: review publication/lock before adoption')
machines = {'arm64-v8a': 183, 'armeabi-v7a': 40, 'x86': 3, 'x86_64': 62}
def inspect(binary, machine):
    if binary[:4] != b'\x7fELF' or binary[5] != 1 or struct.unpack_from('<H', binary, 18)[0] != machine:
        raise RuntimeError('Unexpected ELF architecture')
    return {'bytes': len(binary), 'elfMachine': machine, 'containsVersion1_0_20': b'1.0.20\0' in binary,
            'containsVersion1_0_22': b'1.0.22\0' in binary, 'sha256': hashlib.sha256(binary).hexdigest()}
result = {'artifact': 'lazysodium-android 5.2.0', 'publicationHashVerified': True,
          'runtimeValidated': False, 'productionGate': 'blocked-pending-native-update-and-runtime-validation',
          'nativeBundled': {}, 'newerNativeCandidates': {}}
with zipfile.ZipFile(args.aar) as archive:
    for abi, machine in machines.items():
        result['nativeBundled'][abi] = inspect(archive.read('jni/' + abi + '/libsodium.so'), machine)
if args.libsodium_runtimes:
    mapping = {'android-arm64': 183, 'android-arm': 40, 'android-x64': 62, 'android-x86': 3}
    for rid, machine in mapping.items():
        candidate = args.libsodium_runtimes / rid / 'native/libsodium.so'
        result['newerNativeCandidates'][rid] = inspect(candidate.read_bytes(), machine)
print(json.dumps(result, indent=2))
