# FG-112 production enforcement reboot drill

## Scope and authorization

This drill uses the installed production `FocusGuard` service and one finite,
opt-in strict policy. It does not change the service account, ACLs, startup mode,
browser configuration, or recovery settings. The owner must explicitly approve
the Windows restart immediately before it is executed.

## Procedure

1. Confirm CI passes and no production strict policy is active.
2. Run `scripts/arm-fg112-production-reboot-drill.ps1`. It validates the exact
   automatic `LocalService` installation, activates a finite policy, obtains the
   two acknowledgements, verifies `Active`, and records only identifiers and UTC
   timestamps under `artifacts/fg112-reboot/`.
3. Confirm the reported expiration leaves enough time, then restart Windows once.
4. After sign-in, run `scripts/complete-fg112-production-reboot-drill.ps1`.
5. Verify a changed boot time, automatic service startup, the same recovered
   session, enforcement released before fresh acknowledgements, and reconnection
   after new acknowledgements. If verification occurs after expiration, require
   `Expired` and absence of the active snapshot instead.

## Recovery

The policy has a finite end time and the production service expires it without
Desktop. If the completion script cannot connect, inspect `Get-Service FocusGuard`.
Starting the existing automatic service is safe and does not extend the stored end
time. If the snapshot is malformed, existing strict persistence fails open and
quarantines it. No recovery code or other secret is written by the drill scripts.

## Evidence

`artifacts/fg112-reboot/state.json` records the pre-restart state and
`artifacts/fg112-reboot/result.json` records the post-restart observations.
