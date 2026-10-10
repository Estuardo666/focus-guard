# FG-157 plan — automatic break enforcement transitions

## Symptom

An active focus cycle advances into a break, but application and website
blocking remains active. The break should relax blocking without user action
and the next focus phase should restore it automatically.

## Root cause

Both domain and browser policy consumers combine the normal focus rules with
the Allow-only break rules. Normal block rules therefore remain effective
during an authorized break unless an identical Allow exception overrides each
one.

## Implementation plan

1. Make an authorized break select only its break rule set in both enforcement
   channels.
2. Add regression coverage proving blocked applications and websites are
   allowed during the authorized interval and blocked again at its exact end.
3. Run formatting, focused tests, the complete build and test suites, and the
   browser extension checks.
4. Update specifications and task tracking, refresh graphify, then regenerate
   and validate the versioned standalone installer.

## Security, UX, and recovery implications

- Security: relaxation remains gated by the integrity-bound service phase
  authorization. Missing, invalid, early, or expired authorization retains
  focus blocking.
- UX: breaks unlock automatically; focus blocking resumes automatically at the
  displayed phase boundary, with no prompt or manual action.
- Recovery: restart behavior remains fail-closed for break relaxation until the
  service validates and reauthorizes the current break.

## Acceptance criteria

- A normal focus block does not apply during a currently authorized break.
- The same block applies before the break and from the break end onward.
- Application and browser behavior agree.
- Existing integrity, expiry, build, test, and installer validations pass.

## Checkpoint

### Summary

Changed both enforcement consumers so an authorized short or long break selects
only the Allow-only break rules. The exact phase end selects the normal focus
rules again automatically.

### Files changed

- Domain effective-rule selection and its regression test.
- Browser navigation effective-rule selection and its regression test.
- Extension manifest version and version-aware package validation.
- Product, UX, threat-model, ADR, task, and graph documentation.

### Tests added

- Domain regression covering break entry and the exact break-end boundary.
- Browser regression covering unrelated focus blocks during the break and
  automatic blocking restoration afterward.

### Commands executed

- Focused domain and extension regression tests before and after the fix.
- `dotnet format FocusGuard.slnx --no-restore --include ...` and verification.
- `dotnet build FocusGuard.slnx --no-restore`.
- `dotnet test FocusGuard.slnx --no-build --no-restore`.
- `npm test` and `npm run check` under `extension`.
- `scripts/build-installer.ps1 -Version 0.1.6`.
- `scripts/test-installer.ps1`.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.6`.
- `graphify update .`.

### Results

- Build: passed with zero warnings and zero errors.
- .NET: 351 passed, zero failed.
- Extension: 16 passed, zero failed; TypeScript check passed.
- Installer contracts and Chrome/Edge package content/hash checks passed.
- Standalone installer `FocusGuard-0.1.6-Setup.exe` was regenerated; its build
  validated the embedded Desktop payload hash.

### UX implications

Breaks now release blocking without a prompt or button. Blocking resumes at
the next focus boundary without a prompt or button.

### Security implications

Only an integrity-valid, current service authorization can select break rules.
Missing, stale, early, or expired authorization retains normal focus rules.

### Limitations

The package was regenerated and validated without installing it or changing
the machine. Owner-present installed cycle and reboot drills remain under
FG-145.

### Next task

Continue FG-145 owner-present installed cycle and recovery validation.
