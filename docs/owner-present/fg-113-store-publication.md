# FG-113 Chrome and Edge store publication

## Prepared artifacts

The installed distribution and repository artifact directory contain:

- `FocusGuard-0.1.0-chrome.zip`
- `FocusGuard-0.1.0-edge.zip`
- `distribution.json` with the exact SHA-256 values
- localized English/Spanish manifest metadata and blocked page
- 16, 32, 48, and 128 pixel FocusGuard icons

The installer registers the production native messaging host per user for both
browsers. Its allowlist accepts only extension identity
`emhdinkjpinjnmldipnejnbeefgonjnj`.

## Owner-only publication steps

1. Sign in to the owner's Chrome Web Store developer account.
2. Create or update the FocusGuard item with the Chrome ZIP.
3. Confirm the store-derived extension ID is exactly
   `emhdinkjpinjnmldipnejnbeefgonjnj`. Do not publish if it differs.
4. Complete the store privacy disclosure: navigation is evaluated locally;
   unrelated history is not collected; blocked audit URLs exclude query and
   fragment; no data is sold or used for advertising.
5. Submit for review. Chrome applies its publisher signature to the approved
   package; retain the submitted ZIP hash and resulting store version.
6. Repeat in Microsoft Edge Add-ons with the Edge ZIP. Confirm the resulting
   identity is compatible with the installed native-host allowlist before
   rollout.
7. Install each approved store version in a clean owner-present browser profile
   and run: native heartbeat, inactive fail-open, independent per-channel
   activation, one blocked page, one allowed page, extension-disable degradation
   while application enforcement remains active, reconnect, expiration release,
   and sanitized Activity verification.

## Stop conditions

Do not publish when the assigned identity differs, a permission is added, a
hash differs from `distribution.json`, the privacy disclosure is incomplete,
or the clean-profile verification fails. Never place publisher credentials or
private keys in the repository, installer, logs, or native messages.

## Rollback

Unpublish or roll back the affected store version, then disable/remove it from
test profiles. Extension loss makes FocusGuard visibly degraded and releases the
browser channel without revoking a current process-agent lease. Run
`uninstall-focusguard.ps1` only when no strict or pending recovery session exists;
it removes native-host registrations only when they point inside the FocusGuard
installation root.
