# FocusGuard

Current local workspace: `D:\Users\Stuart\Documents\Portafolio\Trabajos\Totem\FocusGuard` (migrated from E: on 2026-10-13). Run commands from this folder; historical reports retain their original paths.

FocusGuard is a local-first Windows productivity blocker inspired by strict focus tools.

## Stack

- C# 14 / .NET 10
- Avalonia UI 12.1
- Windows Service for enforcement
- Named Pipes for local IPC
- SQLite for local state
- TypeScript Manifest V3 extension for Chrome and Edge

Browser distribution packages are built and validated with
`scripts/build-browser-extension.ps1`; publisher signing details are documented
in `docs/browser-extension-distribution.md`.

The Windows distribution also includes the graphical elevated installer
`FocusGuard.Setup.exe`; see `docs/graphical-installer.md`.

## Current repository status

This repository is a **safe foundation and autopilot specification pack**. It contains:

- product and UX specifications
- security and recovery constraints
- architecture decisions
- an Avalonia desktop shell
- immutable domain models
- initial tests
- scripts and an ordered task queue
- prompts for Codex autonomous execution

Real application termination, website blocking, strict-mode persistence, privileged installation, and anti-tampering are intentionally deferred until their prerequisite tests and recovery mechanisms exist.

## Start here

1. Install the .NET 10 SDK.
2. Open the repository in Codex.
3. Ask Codex to read `AGENTS.md`, `SPEC.md`, `THREAT_MODEL.md`, `UX_SPEC.md`, and `AUTOPILOT.md`.
4. Run:

```powershell
./scripts/bootstrap.ps1
./scripts/build.ps1
./scripts/test.ps1
./scripts/run-desktop.ps1
```

5. Give Codex the prompt in `prompts/00-autopilot.md`.

## Non-negotiable safety rule

FocusGuard may resist impulsive bypasses, but it must never behave like malware. Every strict restriction must be finite, transparent, logged, tested, and recoverable.
