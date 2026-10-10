# FG-163 — Desktop focus workflows

Owner request: implement quick start, clear active session, compact window, visual rule editor, Today, useful closure, weekly scheduling, keyboard and responsive layouts.

## Plan and acceptance

1. Add backwards-compatible desktop preferences and local session intentions/reflections; retain last accepted profile/duration. Keep strict activation explicit.
2. Add read-only service history IPC, persisted phase spans and Today metrics. Completed minutes exclude breaks; technical audit remains separate.
3. Add validated, persisted weekly schedules and an independent service worker. Explicit enable, finite end, normal mode only, no replacement of an active session, durable occurrence deduplication, missed sessions skipped.
4. Build desktop views with Geist and existing semantic tokens: progressive disclosure, active objective/end, responsive blocks, compact draggable/collapsible window, searchable app catalog and domain validation, weekly view and closure actions.
5. Add regression tests for persistence, IPC bounds, history, scheduling boundaries/restarts and viewmodel workflows. Format changed C#, build and run suites.
6. Update specifications/TASKS, refresh graph and rebuild/validate versioned installer 0.1.13. Do not install or modify service/machine state.

## Security and recovery

Desktop remains configuration/status only. Service remains authoritative. Schedules never enable strict mode, have exact finite deadlines, do not catch up missed occurrences or replace active sessions. Schedule snapshots use existing protected-process and domain rule validation. Opt-in enforcement stays visible. Intentions/reflections are bounded, local user data and optional. Application discovery only reads installed shortcuts; it never launches applications. Closing compact UI never stops enforcement.

## Checkpoint

Source acceptance and final standalone package validation passed. Prior
FG-162 and unrelated working tree changes are preserved; no commit/reset was performed.

## Summary and design review

Implemented every requested desktop workflow while preserving Geist, Catppuccin
semantic colors, rounded controls and the existing cycle selection. Schedules execute
in the service with explicit enable and finite normal timer deadlines.

| Before | After |
| --- | --- |
| Session setup exposes all configuration first | Last accepted profile/duration, optional objective, one Start action, advanced disclosure |
| Cycle clock measures the entire session | Dominant current-phase clock, objective, next break and exact session end |
| Fixed secondary block panel competes for space | Secondary named targets; View blocks flyout below 820 DIP; Allow exceptions excluded |
| Tray is the only small status surface | Movable, collapsible compact window with optional topmost; strict/health warnings remain visible |
| Rules lead with technical values | Searchable installed application names/icons; paths in details; domain/action/inline validation |
| Activity mixes metrics and technical events | Today focus minutes/completed sessions/phase chronology; technical logs in separate disclosure |
| Closure has generic start actions | Actual focus time, objective, optional local reflection, repeat original configuration or return |
| Schedule navigation disabled | Week view, next occurrence, explicit opt-in, persisted independent service execution |

## Files changed

- Contracts: IpcContracts.cs, CycleContracts.cs and new DesktopWorkflowContracts.cs
  (validated Today/schedule IPC, bounded responses, optional phase start).
- Domain/storage: new WeeklyOccurrenceCalculator.cs; additive history phases and
  UTC range query in SqliteSessionHistoryStore.cs.
- Service: new DesktopWorkflowService.cs/worker; InMemoryFocusService.cs finite
  scheduled activation, actual phase history and elapsed-break retention;
  FocusControlPipeService.cs handlers and Program.cs registrations.
- Desktop: new FocusWorkflowViewModel.cs, RuleEditorViewModel.cs, WorkflowControlClient.cs,
  SessionJournalStore.cs, ApplicationCatalog.cs and ApplicationPresentation.cs;
  FocusViewModel.cs, FocusControlClient.cs, DesktopSettings.cs and UiStrings EN/ES.
- Views: new CompactFocusWindow, RuleEditorView, TodayView and ScheduleView; updated
  FocusWorkspaceView, MainWindow, SessionSummaryWindow/ViewModel, TrayFlyoutWindow,
  App.axaml.cs and semantic Controls.axaml. Native compact dragging is display-only.
- Tests: new Contracts/DesktopWorkflowProtocolTests, Service/DesktopWorkflowTests,
  Desktop/SessionJournalStoreTests and Desktop.UiTests project; updated named-pipe,
  design-system and summary regressions. Central headless/xunit-v3 package references
  and solution registration added without replacing existing xunit-v2 projects.
- Specifications: SPEC.md, UX_SPEC.md, THREAT_MODEL.md, ADR 0011 clarification,
  new ADR 0013, TASKS.md and this checkpoint. Extension manifest version 0.1.13 only
  for this task. Generated graph outputs refreshed.

## Tests and commands

- Scoped dotnet format and --verify-no-changes: passed for the explicit C# list
  recorded at artifacts/fg163-format-files.json. Existing unrelated formatting debt
  remains outside scope. Edited text files use repository CRLF.
- dotnet build FocusGuard.slnx -c Release --no-restore: passed, zero warnings/errors.
- dotnet test FocusGuard.slnx -c Release --no-build: 395 passed, zero failed/skipped:
  Application 4, Contracts 46, Desktop 158, Desktop.UiTests 11, Domain 34,
  Infrastructure 15, Monitoring 12, NativeMessaging 23, Service 74, Storage 18.
- After moving strict compact warnings outside collapsible details and pinning exact
  session end outside the active scroll area, affected UI tests and the full solution
  were rebuilt/rerun: all 395 passed. Tests assert the collapsed strict warning and
  small-window end label remain visible. No enforcement behavior changed.
- npm test: all 19 extension cases passed. npm run check: TypeScript passed.
- Real named-pipe workflow test round-trips schedule save/list and Today, using the
  actual service/storage and validators. Scheduling tests cover disabled/enable-mid-
  interval, original deadline clipping, busy/missed occurrences, overlap rejection,
  persistence/restart/clock rollback deduplication, corruption and DST.
- History tests prove focus excludes breaks, clips at day boundaries, and retaining
  10 elapsed seconds of a skipped break does not overlap/shorten later focus spans.
- UI tests use native Avalonia/Skia with the actual App resources, SQLite and service
  (in-memory policy delivery, no machine enforcement). They render 760/1100/1440 DIP,
  keyboard search/validation, current-phase clock/break, strict repeat, accepted setting
  restart, objective/reflection, actual Windows shell icons, schedule/Today dark/light,
  compact topmost/collapse/close and tray Exit reachability.
- graphify update .: passed; final graph 4,393 nodes, 7,914 edges, 772 communities.
- scripts/test-installer.ps1: passed. scripts/test-browser-extension-package.ps1
  -Version 0.1.13: Chrome/Edge packages, hashes and identity passed.

## Visual artifacts

Native render frames are under artifacts/ui/fg163: quick-start-760/1100/1440,
advanced-small, active-small/cycle/break, compact/compact-collapsed/compact-strict-collapsed,
rule-editor, weekly-schedule, today/today-light, session-summary and tray PNGs.
Inspected these app-owned renders; corrected clipped compact icons and retained 40 DIP
targets. This is rendered UI evidence, not OS screen capture or installed-window signoff.

## UX, security and recovery implications

Accepted settings persist after successful activation only. Repeat resolves the original
profile and restores timer/cycle/objective; deleted profiles report an error and strict
sessions require fresh review/recovery acknowledgment. Closing any compact window never
requests stop. Service-unavailable/degraded text stays visible even when collapsed.

Desktop remains status/configuration-only; the service owns finite execution and expiry.
Schedule snapshot rule validation retains protected-process exclusions and opt-in
enforcement. No strict schedule, OS task, credentials, telemetry, executable plugin,
policy interception, ACL weakening or machine installation is introduced. Invalid
schedule persistence starts no sessions. A durable pre-activation claim trades one
potential skipped normal session after a crash for prevention of duplicate activation.
Objectives/reflections are bounded optional user-local data and never become policy.

## Limitations and environment

Today summarizes recorded ended sessions; running phases appear after session closure.
Legacy records retain their focused minutes without manufactured timelines. Reads are
bounded at 2,000 recent session records and 128 displayed phase spans; truncation copy
does not promise unlimited totals. App discovery reads Start Menu shortcuts, so portable
apps can require technical entry. Schedule snapshots do not follow later profile edits;
cycle scheduling and arbitrary external executable scheduling are outside this request.

Native drag behavior, actual OS z-order across other applications, assistive technology
and high-contrast installed Desktop interaction require a real interactive Windows
review. No installed app/service state was changed. UI tests use a test strict persistence
provider for review gates; existing strict service persistence/recovery tests remain real.

First 0.1.13 package built successfully. Regeneration after the compact warning correction
encountered Access denied recreating the removed artifacts/installer/FocusGuard-0.1.13
directory. Read-only diagnostics confirmed the output directory was inaccessible; no
ACLs were changed and no existing process was killed. Regeneration uses a fresh verified
output directory under the checked workspace artifacts root. The final package also
includes the pinned active end label discovered during narrow-window render inspection.

## Package validation

Final scripts/build-installer.ps1 -Version 0.1.13 -OutputDirectory
artifacts/installer/FocusGuard-0.1.13-release passed. Embedded Desktop SHA-256 matched
the freshly published DLL before copying the standalone. The generated full ZIP was
renamed with checked native PowerShell paths to the standard versioned ZIP name.
Standalone --package-self-test and --smoke-test both exited 0. These checks do not
install or alter the Windows service. Latest build log: artifacts/fg163-installer-build.log;
final solution build/test logs: artifacts/fg163-final-build.log and fg163-final-tests.log.

Standalone installer: artifacts/installer/FocusGuard-0.1.13-Setup.exe
SHA-256: 661AC07EB60269B450C58AA824907FEC4C3AD707C3E31F52CD1B81DBAA472819

Full package: artifacts/installer/FocusGuard-0.1.13-win-x64.zip
SHA-256: A6EE66B36B8A94660B1A572AEDEF1C69B26C564184B7CFD48D15771F7D59A562

Fresh Desktop DLL: artifacts/installer/FocusGuard-0.1.13-release/publish/FocusGuard.Desktop/FocusGuard.Desktop.dll
SHA-256: D12AF96169574EAC1E778ABEFA14342E90B9A55743D3FF1F841B0DDA4BF34193

## Next task

After package acceptance, use the new installer for an owner-authorized update and
verify real window dragging/z-order, keyboard and accessibility in the installed app.
Existing owner-present recovery/install milestones remain unchanged.
