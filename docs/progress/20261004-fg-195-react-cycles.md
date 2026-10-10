# FG195 — React normal cycles
Plan: extend existing single-pending dashboard client, strict 64KiB cycle DTO validation and typed inputs, saved preset/custom durations/count-or-total controls, authoritative schedule review and distinct confirmation. Preserve pilot/fresh status/mutation no replay, keyboard/reduced motion and local CSP. Add meaningful wire/DOM tests and dark/light/720px visuals. Validate versioned standalone0.1.43; no installed-data changes. Preset editing/stop-skip/strict/history and installed parity remain pending.

Implemented: same-level native radio Timer/Cycle; saved preset pages/refresh; progressive custom duration/frequency/termination fields. Strict exact DTO validation bounds49phases/64KiB, UTC dates/TTL/contiguity/kind/number/duration/config identity/end/partial flag. Existing one-pending dashboard owns both reviews; separate cycle start consumes before response and unknown outcomes do not retry. Service remains sole session clock. BreakRulesInvalid is explicit; stored break policy is never dropped or rewritten. Cycle review reports rule counts without relabeling allowed break rules as blocks. Normal recovery still uses classic UI; stop/skip/current-phase parity remains pending.

Validation: 779.NET tests in14projects,50React tests,19extension tests pass. Eight new React wire/DOM cases cover typed custom input, preset UI flow, separate confirmation, no replay after lost reply, readonly/stale denial, malformed plan/config/page, count-vs-total configuration and keyboard focus restoration. Real service/SQLite acceptance remains FG194 host tests; visual fixtures never use installed IPC. All native owner/profile/service data untouched.

Design skills follow docs/design/FOCUSGUARD_DESIGN.md (UI/UX Pro Max,Emil,Taste scope limits,Frontend Design,Apple Design). Light/dark720px/forced colors/reduced motion/CSP fixture, preset7-phase/custom partial schedule/readonly/invalid break. Manual screenshot inspection found repetitive dates and misleading break count labels; compact phase times preserve full date on cross-day phases, review includes full exact end and preset/custom label, break counts use rule labels. Final production screenshot recheck required before package acceptance.

| Before | After | Why |
| --- | --- | --- |
| Timer only | Timer/Cycle at same level | selection matches the owner's task |
| No cycle controls | Saved preset first, custom disclosure | ordinary use needs fewer fields |
| No phase review | Service phase schedule, exact end, partial final label | makes consequences reviewable before activation |
| Long repeated phase dates | Times within same local date; full date across days | readable schedule without losing date context |
| Focus left behind after form replacement | Review heading focus and Back restores action | predictable keyboard flow with immediate feedback |

Final production visual recheck passed after label/focus/date fixes. Standalone0.1.43 final rebuilt into fresh package-final output; builder verified embedded UI DLL/index/JS/CSS hashes, all6 published UI assets match finaldist. Package-self-test/smoke-test and four published host runtime/recovery smokes exit0. SHA256 7EB201F26121532F7EB7833E3AFDDE7842D019E3ADF87D24D59C888F567C44A2. artifacts/fg195/FocusGuard-0.1.43-Setup.exe, not installed. Migration top now explicit current-phase matrix; historical notes marked by date. Goal not complete.
