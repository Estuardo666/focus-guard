# FocusGuard UI and UX Specification

## Product character

FocusGuard should feel calm, deliberate, trustworthy, and modern. It must not look punitive, alarmist, or like a legacy Windows utility.

Visual references:

- Linear-style information density
- Raycast-style command clarity
- modern Windows 11 motion and accessibility
- premium productivity applications with restrained color

Do not clone another product.

## Design principles

1. Focus first: the active timer is visually dominant.
2. Consequences are explicit: strict actions always show exact effects and end time.
3. Calm, not gamified: no manipulative streaks or shame language.
4. Progressive disclosure: ordinary users see simple controls; advanced details remain accessible.
5. Trust through transparency: enforcement health and degraded states are visible.
6. Accessibility is never themed away.

## Application shell

- custom modern window chrome
- left navigation rail on desktop widths
- compact top bar
- content max-width for readability
- responsive collapse for narrow windows; through720 CSS px, aligned with the Host minimum width, use a localized native section selector instead of squeezing all destinations into one row
- command palette prepared for later phases

Primary navigation:

- Focus
- Profiles
- Schedule
- Activity
- Settings

## Main screen

Active session state:

- large remaining-time display
- profile name
- exact end date and time
- normal/strict badge
- application and website rule counts
- service and extension health
- recent block count
- one contextual action area

When a session is active at desktop widths above1000px, place the same current
service, extension, and protection facts beside the session card so timer and
channel health share the first viewport. At narrower widths, stack the facts
after the session card. Only show affirmative facts while the status snapshot is
current; preserve the existing stale/unavailable explanation and hide stale
facts.

Inactive state:

- profile selector
- duration presets
- custom duration
- strict mode toggle with explanation
- clear Start Focus button

The inactive Focus screen also offers `Timer | Cycles`, cycle presets, a
termination choice, a readable phase preview, and the exact local end time.
The active screen shows the current phase duration, focus cycle number, next
transition, and total session end time. A partial final phase is called out in
plain text.

The Protection fact distinguishes explicit “not requested,” requested-but-not-
acknowledged, confirmed, and unknown consent. Use the same SessionId-matched
session intent as the session summary; a missing or legacy unknown consent must
be shown as unknown, not “not requested” or “not confirmed.” Stale or
unavailable status hides current health facts instead of retaining affirmative
values.

Announce Protection state changes politely while a session is active. Keep
Service and Extension facts out of live regions so routine polling does not
reannounce the entire panel.

Entering a short or long break releases ordinary focus application and website
blocking without prompting. Profile-configured break exceptions remain blocked
throughout that break. Reaching the next focus boundary restores the ordinary
focus rules without prompting. The displayed transition time must match the
authoritative UTC boundary used by enforcement.

## Profile editor

Use a two-pane or stepped editor:

- identity and icon
- applications
- websites
- exceptions
- schedules
- strictness
- review

Application rows display icon, name, resolved path, matching mode, and warning status.

Website rows display normalized pattern, block/allow status, and validation feedback.

At viewport widths through 1450 CSS px, stack Applications and Websites into one rule column; retain the global navigation and profile list beside the work area.

## Strict confirmation

The strict confirmation is a centered in-window modal over the current Focus
screen. Opening and closing use a short fade, and the underlying interface is
dimmed, blurred, and non-interactive while the review is present.

It must show:

- exact end time
- impacted applications
- impacted website rules
- browser-extension health
- recovery-code workflow
- unsaved-work warning
- the exact saved profile name, visible in the review, typed exactly (owner
  decision 2026-10-10, FG-413; replaces the earlier random two-word English phrase)

Cancel and Escape dismiss the review without activation and clear its one-time
recovery code and typed state. Keyboard focus enters the modal on open and
returns to the previous control after the fade-out.

The final activation button uses explicit language such as `Start strict focus until 18:30`.

## Theming system

Central semantic tokens:

- background
- surface
- elevated surface
- text primary
- text secondary
- border
- accent
- success
- warning
- danger
- focus ring

Also centralize:

- typography scale
- spacing scale
- corner radius
- elevation
- control heights
- icon sizes
- motion duration and easing

Required themes:

- Light
- Dark
- High Contrast

Theme switching must not require restart.

Optional later themes may load from validated JSON containing visual tokens only. No executable code, arbitrary XAML, assemblies, remote resources, behavior changes, or hiding of warnings.

## Motion

- 120–220 ms for common transitions
- honor reduced-motion preferences
- no decorative looping animations
- timer updates must not visually jump
- use motion to explain navigation and state changes

## Accessibility

- full keyboard navigation
- visible focus rings
- screen-reader names and descriptions
- minimum practical target size of 40x40
- color is never the sole status signal
- scalable text
- high contrast testing
- exact dates and durations available as text

## Copy style

- concise Spanish and English
- no shame-based copy
- no false security promises
- explain degraded enforcement plainly
- use exact dates and times for strict sessions

## Windows accessibility refinement (FG-162)

Keep the existing Geist typography, Catppuccin identity, rounded control geometry and native Windows caption. Small enabled primary, secondary and tertiary copy must achieve WCAG AA 4.5:1 contrast on the semantic surfaces used in the application. Enabled primary-action labels must meet this threshold throughout the default, hover and pressed gradients. Health foregrounds must meet it on their matching soft backgrounds. Light status foregrounds and the primary-action midpoint may use darker accessibility variants of the palette.

Form-control boundaries that identify editable fields must achieve at least 3:1 contrast against adjacent semantic surfaces. Forced-colors mode must use system colors for those boundaries.

Single-choice collections use a native radio group or provide the complete keyboard behavior of a composite radio/listbox pattern; do not assign composite ARIA roles to ordinary buttons without implementing their keyboard model.

Navigation rows have a minimum 40 DIP target and reserve their keyboard-focus border so focus does not move their content. The profile navigation region scrolls vertically within the available height; Settings stays pinned outside this region and reflects its open state.

## Desktop focus workflows (FG-163)

Preserve Geist, the existing semantic palette and rounded geometry. Quick start shows
the last accepted profile/duration and one primary action with its duration. Objective
is optional. Advanced configuration is disclosed on demand and scrolls independently
of the primary action. Strict consequences remain visible when advanced details close.

During a session the dominant clock measures the current phase. Objective, next break
and exact session end are text; blocks are secondary and become a "View blocks" flyout
below an 820 DIP workspace width. Allow exceptions never appear as blocked targets.
The compact window preserves time, phase, profile and actionable warnings when collapsed;
it supports native dragging and optional always-on-top. Closing it never stops a session.

Applications use names and local executable icons, searchable with the keyboard; paths
and match kind are in details. Sites show domain, Allow/Block and inline validation.
Profile rules distinguish ordinary focus blocks from apps and sites that remain blocked
during breaks. The latter are clearly described, expose Block-only controls, and stay
under a keyboard-operable disclosure to keep the common profile view compact.
Each profile's default cycle preset is visible and editable; starting a cycle preselects
it, while the person can choose a different preset for that session.
Activity has a Today summary and focus/break chronology separate from technical logs.
Closure shows actual completed focus time, objective, optional local reflection, Repeat
and Back. Repeat restores the original profile/configuration; strict repeats open a new
strict review. Missing original profiles must be resolved before repeat can activate.

Schedule shows a local-time week and next start/end. Saving and enabling are explicit;
name, weekdays, start, duration, profile, rule counts and enforcement state are visible.
A late start uses only the time remaining before the displayed scheduled end; explain
this in the final review so the shortened session is not surprising. Scheduling is normal
timer mode only and does not replace a running session. Context menus support
edit/duplicate/delete profiles; Ctrl+F searches, Ctrl+Enter starts and
Ctrl+Shift+M opens compact mode. All new copy has English and Spanish resources.
After a successful empty schedule read, show an explanatory empty state. A failed read
must show a localized recovery path and must not look like an empty schedule list.

## Fixed horizontal desktop (FG-164, supersedes the resizable Focus layout)

The owner requests a substantial macOS-inspired structural redesign using Opal as a
reference. MainWindow has a fixed 1008 × 532 DIP client area; resizing and maximizing
are disabled. At 125% this is 1260 × 665 physical pixels, leaving space for native
Windows caption and taskbar on a 1366 × 768 display. Keep the native Windows caption
and window movement. The compact window remains available separately.

Use a 176 DIP navigation rail and solid semantic surfaces. Remove the ambient backdrop
and clock dial from the main workspace. A left session panel contains configuration
or large numeric phase time with linear progress; the right panels contain block targets
and protection status. Keep the primary action and session end outside the scroll area.
Profile accent identifies progress without recoloring warning states. Geist remains.

Profiles use identity, sites and applications across parallel panels. Activity places
totals beside the chronology. Scheduling places all seven days across the top and the
saved schedules beside the creation form below. Long lists and advanced content scroll
within their regions. Strict review has a bounded scroll area for long consequences;
its two steps, recovery handoff and exact end remain mandatory. Do not use a fake macOS
caption, decorative gems, stock photography, ambient glow or looping animation.

## Session home and monitor sizing (FG-165, supersedes FG-164)

The owner's reference images call for sessions as wide rows grouped into Now and
Upcoming, with the profile library below. The main view opens on this overview.
Quick start uses the last accepted configuration; Configure, New session or selecting
a profile opens a bounded sheet. The sheet keeps its primary action visible, traps
keyboard focus, disables navigation and closes with Escape. Strict activation still
opens its separate mandatory review. Profiles retain their context menus.

During a session, profile, objective, current phase and numeric time share one row.
Linear progress, next phase, exact end and normal-mode stop follow beneath. Blocks
open in a contextual flyout. Protection warnings remain visible outside the scroll
area. Upcoming rows use real enabled schedules, and the daily summary uses real history.
Failed overview reads do not change authoritative session state or imply an empty schedule.

Use graphite surfaces and a pale mint action accent in dark mode, neutral light
surfaces and a darker green action accent in light mode. Geist remains. Preserve
semantic warning colors and per-profile colors. Body/caption sizes are 14/13 DIP.
The 212 DIP sidebar contains New session and four destinations, with Settings pinned.

Manual resizing/maximizing remains disabled. Client size is chosen from the current
monitor's physical working area and scale, capped at 1440 × 840 DIP and reserving
48 DIP for the native frame and margins. At 1920 × 1080, 100%, the default client is
1440 × 840; with a 1366 × 720 working area at 125%, it is 1044 × 528. Recalculate and
position the window inside the new working area when moving to a different scale.
