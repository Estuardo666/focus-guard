# FG-149 standalone installer and enforcement recovery checkpoint

## 1. Summary

Replaced the thin graphical setup bootstrapper with a genuinely standalone EXE
whose embedded payload is extracted and validated at runtime. Installation now
verifies the copied Desktop, service, agent, native host, manifests, and key
extension files by SHA-256 before success. Setup starts Desktop and verifies one
Desktop-supervised ProcessAgent; ProcessAgent also owns a per-session mutex.

## 2. Files changed

- `src/FocusGuard.Setup/EmbeddedPackage.cs`
- `src/FocusGuard.Setup/Program.cs`
- `src/FocusGuard.Setup/FocusGuard.Setup.csproj`
- `src/FocusGuard.ProcessAgent/Program.cs`
- `scripts/build-installer.ps1`
- `scripts/install-focusguard.ps1`
- `scripts/test-installer.ps1`
- `docs/graphical-installer.md`
- `TASKS.md`

## 3. Tests added

- Installer contract checks for embedded payload creation and extraction bounds.
- Installer contract checks for post-copy hash validation and single-agent startup.
- Standalone package self-test that extracts and validates the embedded payload
  without adjacent installer files.

## 4. Commands executed

- `scripts/test-installer.ps1`
- `dotnet format FocusGuard.slnx --verify-no-changes --no-restore`
- `npm test --prefix extension`
- `dotnet build FocusGuard.slnx --configuration Release --no-restore`
- `dotnet test FocusGuard.slnx --configuration Release --no-build --no-restore`
- `scripts/build-installer.ps1`
- Standalone embedded-payload self-test and graphical smoke test through a
  non-elevated framework-dependent host.
- `graphify update .`

## 5. Results

- Formatting passed.
- Release build passed with zero warnings and zero errors.
- Extension tests: 15 passed.
- .NET tests: 323 passed.
- Installer contracts passed.
- Embedded payload self-test and graphical smoke test passed.
- Payload contains 1,188 entries.
- Embedded and packaged `FocusGuard.Desktop.dll` SHA-256 match:
  `130AAF3F9B2D932A596F8B067B54EA7134B59185B37C65E22893D768779C772C`.
- Standalone installer SHA-256:
  `5ECC3D2ADE23DDECF6B23C753988985D35A92693B09E4EA41DA9BA408595409C`.

## 6. UX implications

The owner can run the versioned EXE alone. Setup no longer depends on whichever
scripts or publish folders happen to be adjacent, and success now means Desktop,
the service, and exactly one supervised agent are running from the verified
payload.

## 7. Security implications

Embedded ZIP extraction is bounded by entry count and total expanded bytes and
rejects paths outside its unique temporary root. Existing elevation, ACL,
service-account, rollback, strict-session, and recovery checks remain intact.

## 8. Limitations

The final elevated reinstall was intentionally not executed from this session;
the owner requested the regenerated installer for manual reinstall. Store-signed
Chrome/Edge publication remains the separate FG-113 owner-account blocker.

## 9. Next task

Run `FocusGuard-0.1.0-Setup.exe`, enable the visible blocking option for the
profile if it is off, start a short session, and confirm the active health label
reports connected or degraded rather than `Timer only`.
