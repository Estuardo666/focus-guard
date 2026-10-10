# FocusGuard — Design contract

Design read: native Windows productivity tool for the owner configuring finite focus sessions, with calm graphite/mint, deliberate hierarchy and accessible React controls. Preserve the established local Geist family and semantic themes; no marketing hero, urgency, fake statistics, background loops or remote font/network dependencies.

Explicit user skills: UI/UX Pro Max (ui-ux-pro-max-upstream), Emil Design Engineering (emil-design-eng), Taste (taste-skill), Frontend Design (frontend-design), Apple Design (apple-design), read2026-10-04 under C:/Users/Stuart/.codex/skills/. Taste's own scope excludes dashboards/multi-step products: use its brief inference/anti-default/dependency/visual consistency discipline, not landing-page hero/image/scroll choreography rules. Emil initial-only greeting does not apply because the user supplied a specific ongoing migration/design task.

UI/UX database query: productivity focus timer desktop calm accessible cycle planning; variance3, motion2, density6, detected React/Vite/Tailwind/Radix. Result artifacts/fg193/design-system-search.txt mismatched a webinar/landing and serif pair, so those pattern/font/urgency suggestions are rejected. Priority accessibility/form/keyboard/feedback/responsive rules remain applicable. Existing brand/stack/context outranks generic search output.

Tokens: graphite canvas #101214; raised surfaces #191D20/#21262A; primary ink #F1F5F4; supportive ink #B9C3C0; mint action #B6E3CD with dark ink #122019; amber warning #F0C987. Semantic CSS variables drive light/dark; no warning recoloring by profile accent. Geist600 titles/important status, Geist400 body, tabular Geist digits for authoritative times. Icons: Radix Icons rendered at18px; decorative SVGs stay hidden from assistive technology while nav text labels remain visible. At widths through720px, matching the full Host minimum width, expose a localized native section selector instead of a compressed tab row. Type role distinction comes from weight/tracking/leading rather than arbitrary mixed fonts. Body/error text must remain readable; future cycle controls target14–16px. Interactions44px minimum target, keyboard focus visible, disabled state explains the actual prerequisite.

Cycles layout: two options (Timer/Cycle) at the same level, choose a saved preset first, advanced custom fields behind progressive disclosure. Show focus/short/long/frequency as grouped durations with units. Termination by cycle count or finite total duration is mutually exclusive. The review is a schedule, not a chart: phase kind, start/end from the service, final deadline and saved profile/blocking consequences. A partial final phase is explicitly labeled. No local countdown/session authority and no activation until distinct confirmation. Current/pending/strict/offline state takes priority over configuration.

Profile rules: ordinary focus rules and break exceptions are separate concerns. The Profile editor keeps apps and websites in distinct groups and places cycle settings in a full-width disclosure below them. At viewports through1450px, the two rule groups stack so the profile list plus one focused editor remain visible. The profile's saved default preset is visible/editable and preselects the cycle form; the user can still choose a different preset for a session. Block-only break exceptions are described as targets that stay blocked, with paginated labels, inline add/remove, and an empty state. Do not provide an Allow action for break exceptions. All controls remain keyboard-operable and use semantic theme tokens; no special motion is added.

Profile editor hierarchy (FG-272): the selected name and monogram remain in a compact native disclosure summary; rename, color, duplicate and delete controls start collapsed. Saved application/site rules precede catalogs and add forms, with explicit empty states. The first viewport prioritizes the rules being edited. Keep delete confirmation, keyboard-visible focus, ES/EN copy, and forced-colors contrast; do not alter policy semantics.

Activation state (FG-273): `Activating` is distinct from `Active`. Keep the authoritative timer, but show an inline localized status that the requested channels have not acknowledged and blocking has not started. Remove it as soon as the service reports `Active`; never imply protection from extension availability alone.

Saved profile details must not wait for Start menu/application catalog discovery. When the catalog has not been loaded for the current WebView document, show the stored rule target and match kind without claiming a name/path resolution or delaying the profile; search and icon lookup remain on demand.

Compact focus companion (FG-225): one native Windows window becomes a 480×340 DIP React status card. It preserves current phase time, named profile when known, exact end and protection/recovery warnings; Strict mode offers a direct route to recovery instructions and stale status offers refresh. Use the native caption as the drag surface and an explicit `Always on top` toggle. No session controls, rule editing, duplicate tray, or local timer authority. `Ctrl+Shift+M` switches immediately without animation; focus moves to the destination view. `CompactFocus.js` is a local CSP-allowlisted chunk, semantically hidden in full view and shown alone in compact mode.

Compact detail folding (FG-226): fold secondary objective/end/service/TopMost details into a 220 DIP window while retaining the time, phase, profile, warnings, refresh and full-view route. Expand restores 340 DIP and exposes the original context. Controls use a visible text label and `aria-expanded`/`aria-controls`; no icon-only toggle or motion.

Pilot wayfinding (FG-227): keep the Classic tray as the single always-available control point during migration. Place `React pilot` beside Classic compact mode in the flyout and context menu; tell the owner Classic remains available. Launch only the installed sibling Host with an explicit pilot flag, focus an existing window instead of duplicating it, and show a localized toast if the Host is missing or cannot start. Do not transfer tray ownership until Phase10 acceptance.

Spatial consistency: sidebar stable, same location for primary action/Back/error, inline validation beside field and summary before confirmation. Modal deletion/strict review keeps focus, Escape/Back where allowed, restores trigger focus; destructive action never auto-focuses. No gesture physics where no gesture exists. Screen changes by keyboard are immediate. Optional transient opacity feedback under200ms for pointer actions; reduced motion removes movement, high contrast/transparency settings preserve readability. Busy prevents duplicate command submission, never locks input merely for animation.

Summary session review (FG-237): keep the single `Revisar sesión` action beside the new-session heading, visible before scrolling through configuration. In read-only mode, pair the disabled action with the specific reason; retain the separate review/confirmation step and exact expiry. Do not duplicate a second submit button at the end of the form.

Emil review:

| Before | After | Why |
| --- | --- | --- |
| Small pointer targets36–40px |44px control target overrides | predictable pointer/touch acquisition |
| Hover paint regardless of input device | fine-pointer/hover media query | no sticky hover on touch |
| Disabled cursor indicates waiting even readonly | not-allowed for disabled; explicit busy text | state matches available action |
| No distinct press feedback | immediate background/inset emphasis, no keyboard motion | confirms input without delaying navigation |
| Generic search suggests webinar/serif/urgency | retained native tool identity and local Geist | product context and owner references win |

Acceptance: real text content, no fabricated health, source/TS/bounded bridge validation, keyboard and Escape, dark/light/forcedcolors/reducedmotion, no horizontal overflow720px and smaller where supported, strict CSP/local fonts/no console errors, native DPI/installed flow remains a separate unproven gate. Review before/after screenshots for every meaningful layout change; produce no silent backend/policy changes for aesthetics.

Read-only session preview (FG-298): when the installed Host is not running with pilot authorization, keep the saved session values visible as a preview while native controls and the review action remain disabled. The subtitle says this window cannot prepare or start a session; the button's associated note gives the valid next step (open the pilot or use Classic). Disabled controls use neutral semantic surfaces; the selected timer/cycle option must not retain the active mint accent. Keep the exact values readable. This is a presentation contract only: authorization and the service-side session rules remain authoritative.

Minimum window (FG-305): at the native 720×480 DIP bound, keep brand, Pilot/Read-only mode, localized section selector, page heading, live service state and the session-review action reachable without first scrolling through configuration. At widths≤720px use the native selector in a compact header row; below520px it may wrap to two rows. At heights≤560px reduce top chrome and panel spacing while preserving legible labels, 44px controls and vertical scrolling. Keep the session action beside the heading above600px; stack it below that width.

Minimum-height responsive acceptance (FG-305): at the native 720×480 DIP window, show the localized section selector, Pilot/Read-only state and the single session-review action without scrolling. Use one compact header row at widths720–521px; below520px allow a second row. For viewport heights≤560px reduce vertical chrome and panel spacing, while preserving readable copy and the vertical scroll path.

## Profile application catalog hierarchy (FG-356)

Design Read: native Windows profile editor for people maintaining app/site blocking rules; preserve graphite/mint, Geist, current navigation and field order. Use the existing dials (variance3, motion2, density6). UI/UX Pro Max's focused UX search emphasized keyboard reachability, visible names and focus indication; its React search supports lazy work. Taste excludes dashboards from landing-page composition, so only its preserve-brand audit and anti-default discipline apply. Emil and Apple motion principles support immediate, interruptible controls, but this disclosure has no decorative animation because it is a frequent configuration action without a drag/gesture.

| Before | After | Why |
| --- | --- | --- |
| The detected-application list, search and add controls render open beneath saved application rules on every profile visit; the search request runs before the owner asks to browse the catalog, and its label is screen-reader-only. | A native disclosure starts closed with a concise bilingual purpose statement. Opening it reveals the catalog and starts the debounced first search. The search field has a visible label and its discovery explanation is associated with the input. | Saved rules and manual edits remain the first content. The large optional catalog no longer competes with the task, and the app catalog does no work until requested. |

The disclosure remains keyboard-operable through native `details`/`summary` semantics, uses a 68px summary hit area, visible focus, semantic theme tokens, and forced-colors borders. No field order, navigation, profile data, bridge method, application matching rule, Allow/Block behavior or authorization changes. English/Spanish copy is localized. The update is scoped to the ApplicationsPicker; it does not add motion.

## Service-owned next schedule interval (FG-358)

Design Read: people scanning weekly schedules need the next occurrence as one interval, in the time zone that the service will use. Keep the start and end on the schedule card with the saved zone visible. Use the `WeeklyOccurrenceCalculator` output; React only formats the two returned instants with the service-provided IANA zone.

| Before | After | Why |
| --- | --- | --- |
| A saved schedule showed its next start and duration, leaving the owner to calculate the end; dates formatted in the computer's current zone even if the saved zone differed. | The revisioned schedule response can opt into the service-calculated `NextEndUtc` and the schedule's IANA zone; the card shows both times in that zone. | The next interval is explicit and matches the zone used by the scheduler, including DST rules. |

The two new IPC response fields are omitted unless the revisioned client asks for them. Legacy schedule clients keep the same exact JSON shape; if a service does not support the optional end, the Host labels it unavailable. No recurrence or DST calculation moves into React. The change is read-only and does not alter schedule claims or enablement.

## Schedule review end-time clarity (FG-357)

Design Read: weekly schedule editor for people who need to know what the service will do before saving. Preserve the existing two-step form/review flow and exact service-owned scheduling behavior. The field helper already says the service keeps the scheduled end; carry that information into the final review where the owner confirms the schedule.

| Before | After | Why |
| --- | --- | --- |
| The duration field explains that a late start may shorten the session, but the review step only says to confirm the profile and local schedule. | The review summary says in EN/ES that a late start uses the remaining time and still ends at the scheduled time. | The consequence remains visible at the decision point, especially for a 1-minute schedule. |

No schedule arithmetic or time-zone/DST logic moves into React; the service remains authoritative for claims and the saved end. No new warning color or motion is added. The 1-minute editor test verifies that the review copy appears while keeping the flow a draft until the owner confirms.

## Profile read-only clarity (FG-314)

Design Read: FocusGuard is a desktop focus-session tool; state and the next useful action should be obvious before advanced rule controls.

This is a preserve-brand redesign: keep the existing Geist/Catppuccin tokens, profile/navigation information architecture and the established Spanish/English copy. The UI Pro Max search returned an App Store landing pattern, exaggerated-minimalism style, Lora/Raleway web fonts and download CTAs. Those recommendations describe a marketing page, not this local desktop workflow, so they were rejected in favor of this design contract. Keep the existing dials at variance3, motion2 and density6.

| Before | After | Why |
| --- | --- | --- |
| Read-only explanation followed the profile list and create form inside the narrow profile rail. | The same localized explanation is the first note before the profile/editor columns when the catalog explicitly reports `pilotEnabled=false`. | Users see why controls are disabled and the valid next path before scrolling or trying each disabled field. |

The note uses a semantic surface, a token border and a text label through its existing copy; color does not carry the state alone. It has no animation: changing profile views is frequent, keyboard navigation should be immediate, and reduced-motion behavior remains unchanged. `pilotEnabled=null` does not show the explanatory note, so a loading/error state is not mistaken for confirmed read-only mode. No bridge method or permission changed.

Installed .102 screenshot before the move: `artifacts/fg305-output/fg313-profiles-readonly-0.1.102-1216x799.png`. React .104 tests and package pass, but the after screenshot and installed acceptance are still pending. Orca status/capability queries returned `runtime_access_denied` (EPERM) from the current executor; neither .103 nor .104 was installed over .102.

## Page-title hierarchy (FG-318)

The installed .102 screenshots showed the product name twice in the first viewport: once in the persistent navigation rail and again as the uppercase `FOCUSGUARD` eyebrow above every page heading. Remove only the repeated page-header label. Keep the rail wordmark, localized page title/subtitle, and the Strict Review eyebrow that communicates its actual `step/2` sequence. This reduces chrome without changing information architecture or button copy, and makes the destination title the first content label. No animation is added; the change is static and preserves reduced-motion behavior.

## Active-session channel visibility (FG-320)

Keep the timer, exact session end and channel health visible together while focus is active. Above1000px, put the session card and the current Service/Extension/Protection facts in a balanced two-column overview, with the timer as the primary region and compact health cards in a secondary side column. At≤1000px, stack the cards after the session so the timer keeps its width. Keep the same authoritative localized values and explanatory text; stale/unavailable state hides the facts rather than carrying old green statuses forward. Idle configuration is unchanged. No motion or new status color is introduced. Installed screenshot review at1216×799 remains a separate acceptance gate.

## Protection intent truth (FG-321)

The Protection fact reads the journal intent only for the exact active SessionId. If that record explicitly has `enforcementEnabled=false`, show “Not requested” / “No solicitado” and explain that app/site blocking was not requested. If it is true, continue to show the service-reported acknowledgement state. If consent is null, missing, legacy-unknown or belongs to another SessionId, show “Unknown” / “Desconocido”; do not infer either an opt-out or a missed acknowledgement. This is read-only presentation and does not change policy or session behavior.

## Protection status announcement (FG-328)

While Active/Activating, expose the Protection value as a polite, atomic live status so Narrator can announce a transition from unknown to the SessionId-matched intent. Keep its heading semantics. Do not put Service/Extension or the full facts grid in a live region; periodic refreshes must not repeat three cards. Terminal/idle sessions retain a static value.

## Next schedule interval (FG-358)

Keep the weekly schedule card grounded in the service's `WeeklyOccurrenceCalculator`: show both the next start and its end, formatted in the saved schedule's local time zone. React only formats the returned UTC instants; it does not duplicate recurrence, DST-gap, or end-time calculations.

| Before | After | Why |
| --- | --- | --- |
| A saved schedule showed its next start and duration, leaving the owner to calculate its end. | It shows the service-calculated start and end together, formatted with the saved zone's IANA equivalent. If the running service is too old to provide the optional end, the UI says it is unavailable. | The schedule card answers when the next interval begins and finishes without implying the Host is the scheduling authority. |

The revisioned IPC list/change request opts in to the extra end-time fields. Requests from older clients omit that flag, and the service omits the new response fields so their strict JSON contracts remain unchanged. No schedule is created, enabled, or recalculated in the UI; keyboard order, tokens, and motion remain unchanged.
