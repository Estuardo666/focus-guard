# FG-101 owner-present validation

This procedure validates normal-mode process termination only against the disposable
`FocusGuard.TestTarget.exe`. Do not substitute Chrome, Codex, system tools, security
tools, or another executable.

## Preconditions

- The owner is present at the machine.
- No administrator elevation is in use.
- `dotnet build FocusGuard.slnx --configuration Release` passes.
- `dotnet test FocusGuard.slnx --configuration Release` passes.
- The exact Release output path is added to `AllowedExecutablePaths`.
- `EnableRealProcessTermination` is set to `true` only for this test run.

Example development options:

```json
{
  "DeveloperFeatures": {
    "EnableRealProcessTermination": true,
    "AllowedExecutablePaths": [
      "C:\\Users\\Stuart\\Documents\\FocusGuard\\FocusGuard-Autopilot\\src\\FocusGuard.TestTarget\\bin\\Release\\net10.0\\FocusGuard.TestTarget.exe"
    ]
  }
}
```

No production configuration loader or service registration is included in FG-101.
The categorized integration test passes these values explicitly in memory.

## Procedure

1. Start the disposable target:

   ```powershell
   .\src\FocusGuard.TestTarget\bin\Release\net10.0\FocusGuard.TestTarget.exe --mode cooperative
   ```

2. Record the displayed PID and obtain its UTC start time:

   ```powershell
   $target = Get-Process -Id <PID>
   $target.StartTime.ToUniversalTime()
   ```

3. In the owner-present harness, create `ApplicationProcessTarget` with the exact
   executable path, PID, and UTC start time. Invoke `AuditedProcessTerminator` with
   `ProcessTerminationOptions.Normal`.
4. Confirm the result is `Terminated`, the returned PID matches, and one sanitized
   audit event exists.
5. Confirm the process no longer exists:

   ```powershell
   Get-Process -Id <PID> -ErrorAction Stop
   ```

   The expected result is a PowerShell error saying that the process was not found.
6. Repeat with a non-allowlisted executable path and confirm `NotAllowed`; verify
   that executable remains running.
7. Remove the opt-in immediately and confirm the same request returns `Disabled`.

## Recovery and rollback

- Before FocusGuard acts, Ctrl+C closes TestTarget normally.
- If the harness or FocusGuard UI fails, close it; TestTarget is harmless and may
  continue until Ctrl+C.
- Disable the feature by removing the opt-in configuration.
- Do not elevate or widen the allowlist to diagnose failures.

## Evidence to record

Record the build/test commands, TestTarget PID and start time, termination result,
sanitized audit event, negative allowlist result, disabled result, and confirmation
that no unrelated process was affected. Only then mark FG-101 complete.

## Automated owner-present command

Run only while the owner is present:

```powershell
$env:FOCUSGUARD_RUN_OWNER_PRESENT_PROCESS_TEST = '1'
try {
  & 'C:\Program Files\dotnet\dotnet.exe' test `
    .\tests\FocusGuard.Infrastructure.Windows.Tests\FocusGuard.Infrastructure.Windows.Tests.csproj `
    --configuration Release `
    --filter 'Category=OwnerPresent' `
    --logger 'console;verbosity=detailed'
} finally {
  Remove-Item Env:FOCUSGUARD_RUN_OWNER_PRESENT_PROCESS_TEST -ErrorAction SilentlyContinue
}
```

The test has mandatory cleanup and starts only the exact Release TestTarget path.
