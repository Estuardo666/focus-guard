# Plan — Android Keystore vault spike

## Objective

Extend the existing isolated Android capability probe with a small, testable local vault that demonstrates how Android can protect FocusGuard device material before its Android client exists.

## Scope

- Encrypt opaque, synthetic key-material bytes with AES-256-GCM.
- Generate the AES key in Android Keystore; never export or write it to disk.
- Store the authenticated envelope only in app-private `noBackupFilesDir`, using `AtomicFile` for crash-safe replacement.
- Bind the ciphertext to the account binding as authenticated data; reject wrong-binding reads, corruption and truncation.
- Show whether the key is hardware-backed, without requiring StrongBox or claiming emulator behavior represents Samsung hardware.
- Verify save/read and persistence after force-stop/relaunch on the existing Android 15 API35 emulator. No Internet, live account, real keys, or physical phone mutation.

## Acceptance

The emulator UI must report a successful first save/read and a separate successful reload after a process restart. Automated probe checks must show the plaintext marker is absent from the saved envelope, wrong account binding and tampering fail authentication, the vault file is under the app's no-backup directory, and the application has no Internet permission. A software-backed emulator result is recorded honestly; Samsung S24/ARM64 acceptance remains open.

## Security and recovery notes

This spike protects file-at-rest material from ordinary file access and backup. It does not implement identity, pairing, account logout cleanup, E2EE membership, Android app lifecycle recovery, or a production integration. Keystore invalidation or reinstall is reported as unreadable vault material; no plaintext fallback or cloud recovery is added.

## Resultado del spike — 2026-10-12

`AndroidDeviceVault.java` and `AndroidVaultRuntimeProbe.java` are compiled and run in the existing Android15/API35 AVD with synthetic values. The UI reports:

- `VAULT_RELOAD_PASS` after updating the APK, force-stopping the app, and relaunching it.
- Readback succeeds; the Android Keystore key is non-exportable; the file is in the package's `no_backup` directory.
- The synthetic cleartext marker is absent; a different account binding and modified ciphertext both fail authentication.
- The API35 emulator Keystore reports software-backed. This is shown explicitly, not represented as hardware security.
- The installed diagnostic manifest declares only `PACKAGE_USAGE_STATS`; it has no `INTERNET` permission and does not block apps.

Observed data file: `/data/user/0/app.focusguard.phase0/no_backup/device-vault-v1-<SHA256 account binding>.bin`, mode `0600`, size 75 bytes. The screenshot is `artifacts/cloud-phase0/android-vm/vault-probe-screen.png` (SHA256 `A3F38A4F003F266D59ACAF6890305F5C231FA6077F9FB539AE45BF86BE1BF615`). Signed local diagnostic APK SHA256 `14AECA9CA8CF8E11B7B51C6A200973927A522750A4CE61CD2C76C1A458945431`; test certificate SHA256 `bca06d8c601b67053a4cc1768e47dae41d744474d012968750cc2aacc7b45570` matches the previous probe, preserving app data during update.

The build first failed to update the old app because a new disposable signing certificate had been generated. Rebuilding with the existing local test certificate allowed the update and preserved the synthetic vault. The original signed APK is preserved separately.

The first UTF8 build showed `CRYPTO_PROBE_FAILED` because Windows `javac` read accented Java source with its default encoding. The deterministic cipher matched until the first accented byte. `build-probe.ps1` now passes `-encoding UTF-8`; the rebuilt emulator screenshot shows both `CRYPTO_PROBE_PASS` and `VAULT_RELOAD_PASS`. This also makes the prior C#/Node comparison deterministic on Windows.

The Android CLI SDK installer once tried to download a second emulator package; it was cancelled before replacing the installed emulator. The emulator metadata remained 37.3.3/build 16489710. The AVD's configured 6GiB data partition would not start on E: with only 1.2GiB free, so a verified 3.32GB copy was placed at `%LOCALAPPDATA%\FocusGuard\AndroidVM\avd`. `android-vm.ps1` uses that copy when present and otherwise keeps the original AVD in the repository artifacts. The source AVD remains unchanged. No ADB global server or unrelated device was stopped.

The build prerequisites now exist inside the ignored VM artifacts: portable Temurin 17.0.20.1+1 at `artifacts/cloud-phase0/android-vm/jdk/jdk-17.0.20.1+1`, platform API35 revision 2 at `sdk/platforms/android-35`, and build-tools 35.0.0 at `sdk/build-tools/35.0.0`. The two SDK archives were downloaded directly after cancelling the unrelated emulator update.

Temurin ZIP SHA-256 matched the [official Adoptium release API](https://adoptium.net/installation/ci-scripts/): `e53a79c3c3d86865bd7e787903884331068e71321714ffd44f145785affc7cb0`. SDK archives matched checksums from Google's repository manifest: platform `0bb560a90a7a2cbd0dd8348224d518b638fe7949`, build-tools Windows `af059bb67cf7786f45ee0db85e2d24985df1b4b6` (SHA-1 as published). The [Android CLI documentation](https://developer.android.com/tools/agents/android-cli) describes the explicit metrics opt-out.

Remaining Android gates: Samsung S24/ARM64 Keystore behavior, a production Android app/key lifecycle, integration with identity/pairing and membership, and physical approval tests. This VM is x86_64 and software-backed; it does not validate hardware-backed keys on the phone.
