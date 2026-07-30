# FG-126 Guided Profile Creation Plan

## Summary

Add an onboarding-style screen for creating new profiles from within the normal
Profiles workflow. This is not first-run onboarding and does not start a
session, install components, or request elevation.

## Design direction

Subject: a FocusGuard user preparing a reusable boundary around recurring
distractions. The screen's single job is to turn that intent into a saved,
editable profile.

- Palette: reuse Dark Zen surfaces (`#0E1318`, `#151C24`, `#1D2631`), calm
  FocusGuard blue (`#667DFF`), primary text (`#EDF2F5`), and warning
  (`#D8A657`).
- Type: retain the established FocusGuard system typography and use scale,
  weight, and spacing rather than introducing an unrelated display family.
- Layout: one centered preparation card with a dominant question, explicit
  suggestion chips, custom-rule input, compact summary, and one primary action.
- Signature: each suggestion exposes its exact domains, making the quick path
  transparent rather than magical.

The external HTML and supplied screenshot guide hierarchy and tone. Session
duration, UAC, service installation, and extension installation are deliberately
removed because they are not profile properties.

## Scope

- Open the guided screen from an explicit New profile action and the header add
  swatch.
- Require a valid, unique profile name.
- Offer unselected website suggestions for Instagram, X/Twitter, YouTube,
  TikTok, Facebook, Reddit, Netflix, and Twitch.
- Show the exact domains represented by each suggestion.
- Accept multiple custom website domains or executable file names.
- Show removable draft-rule chips and a live rule-count summary.
- Allow an empty profile.
- Persist, select, and open the new profile for normal editing.
- Cancel without persistence.
- Disable creation during active sessions and busy operations.

## Tests first

1. Suggestions start unselected and expose exact domains.
2. Custom domains and `.exe` names become the correct rule types.
3. Invalid and duplicate drafts are rejected.
4. Create persists and selects an independent profile with selected and custom
   rules.
5. Empty profiles are allowed.
6. Cancel and active-session paths do not persist.
7. English and Spanish resources remain in parity.

## Security implications

Creation changes future-session configuration only. No rule is activated until
a later explicit session activation. Suggestions never add hidden domains.

## UX implications

The screen uses styled 40-pixel targets, visible keyboard focus, bilingual copy,
explicit selected states, and a clear Cancel/Create choice. The existing
sidebar remains available because this is an in-product workflow, not global
onboarding.

## Recovery implications

Draft state is memory-only until Create profile succeeds. Cancel discards it.
Persistence failure leaves existing profiles unchanged.
