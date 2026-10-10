# FG-128 Geist typography and profile-colored timer plan

## Summary

Refine the active Focus screen from the owner screenshots: bundle Geist for the
Desktop UI, strengthen and enlarge the profile title, derive the timer ring and
halo from the selected profile accent, and create more breathing room between
the timer copy and its ring.

## Design direction

- Type: Geist is the application family; icon glyphs retain the dedicated
  Segoe icon families. The profile name moves from 25 to 31.25 px (exactly 25%)
  and from Medium to Bold.
- Color: the ring arc and both glow layers use the existing runtime-generated
  `ProfileAccentGradient`, preserving one visual identity per profile.
- Layout: increase the semantic timer diameter token from the current 158 px
  composition to 184 px so the centered value and caption no longer crowd the
  stroke.
- Signature: the active timer becomes a calm halo belonging to the selected
  profile instead of a generic application gradient.

## Acceptance criteria

1. The Desktop bundle contains Geist and applies it globally without requiring
   a machine font install.
2. Icon controls keep their icon font overrides.
3. The profile title is Bold and exactly 25% larger than its previous 25 px
   token.
4. The active timer arc and both glow layers use `ProfileAccentGradient`.
5. The timer composition has visibly more internal breathing room.
6. Design-system tests, formatting, and the Release build pass.

## UX implications

Typography remains consistent offline, the selected profile is easier to scan,
and the timer has clearer separation between data and decoration. Existing
strict-mode and exact-expiration messaging remains visible and unchanged.

## Security implications

None. This slice changes bundled assets and presentation resources only; it
does not alter session state, enforcement, persistence, IPC, logging, or URL
handling.

## Recovery implications

None. The font is packaged with the application and the profile gradient
already has a default resource, so the UI retains a deterministic fallback
before profile resources are refreshed.
