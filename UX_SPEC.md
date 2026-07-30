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
- responsive collapse for narrow windows
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

Inactive state:

- profile selector
- duration presets
- custom duration
- strict mode toggle with explanation
- clear Start Focus button

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
- a visible random two-word English phrase that must be typed exactly

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
