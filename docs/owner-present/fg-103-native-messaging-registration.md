# FG-103 native messaging registration boundary

## Current authorized scope

The current FG-103 slice publishes the native host and creates Chrome/Edge host
manifests under `artifacts/FocusGuard.NativeMessaging`. It does not register the
host, install an extension, write browser policy, or change the registry.

Prepare staging from a normal PowerShell session:

```powershell
.\scripts\prepare-native-messaging-staging.ps1 `
  -ChromeExtensionId '<32 lowercase a-p characters>' `
  -EdgeExtensionId '<32 lowercase a-p characters>'
```

Both IDs are required and validated before publishing. The generated manifests
contain the exact extension origins and the absolute staged executable path.

## Pre-registration checks

Before a later registration milestone:

1. Build and automated tests must pass.
2. Inspect both generated manifests and verify the extension IDs.
3. Exercise the host with valid, malformed, truncated, and oversized frames.
4. Verify an origin not in `allowed_origins` exits before message dispatch.
5. Document the exact per-user Chrome and Edge registry keys and values.
6. Document and approve registry rollback before writing either key.

Registration must be per-user unless a separate least-privilege review justifies
machine-wide registration. Browser force-installation and browser policy remain
outside this milestone.

## Recovery and rollback

Before registration, rollback is:

```powershell
.\scripts\remove-native-messaging-staging.ps1
```

It removes only the exact repository-local staging directory and is idempotent.
No browser rollback is needed because this slice creates no browser or registry
state.

## Authorized Chrome drill

The Chrome-only per-user registration target is:

```text
Key:   HKCU\Software\Google\Chrome\NativeMessagingHosts\com.focusguard.native
Name:  (Default)
Type:  REG_SZ
Value: <absolute path>\artifacts\FocusGuard.NativeMessaging\manifests\chrome.json
```

The development extension ID is fixed by its public manifest key:
`emhdinkjpinjnmldipnejnbeefgonjnj`. The drill uses a temporary Chrome profile
under repository-local `artifacts`; it does not modify the owner's normal Chrome
profile, register Edge, or install browser policy.

Registration and exact rollback:

```powershell
.\scripts\register-chrome-native-host.ps1
.\scripts\unregister-chrome-native-host.ps1
reg.exe query "HKCU\Software\Google\Chrome\NativeMessagingHosts\com.focusguard.native"
```

The final `reg.exe query` must report that the key cannot be found. Staging may
then be removed separately with `remove-native-messaging-staging.ps1`.

## Authorized Edge drill

The Edge-only per-user registration target is:

```text
Key:   HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.focusguard.native
Name:  (Default)
Type:  REG_SZ
Value: <absolute path>\artifacts\FocusGuard.NativeMessaging\manifests\edge.json
```

The Edge drill uses the same fixed public-key-derived development extension ID,
`emhdinkjpinjnmldipnejnbeefgonjnj`, and a temporary profile under repository
`artifacts`. It does not change the normal Edge profile, Chrome registration, or
browser policy.

Registration and exact rollback:

```powershell
.\scripts\register-edge-native-host.ps1
.\scripts\unregister-edge-native-host.ps1
reg.exe query "HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.focusguard.native"
```
