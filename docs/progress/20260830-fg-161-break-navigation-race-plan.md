# FG-161 plan — prevent stale break navigation redirects

## Symptom

During an authorized cycle break, opening a normally blocked URL can flash the
FocusGuard block page repeatedly and remain inaccessible.

## Root-cause hypothesis

The navigation listener reads extension policy asynchronously and redirects
after evaluation without confirming that the same decision is still current.
A navigation evaluated under the final focus policy can therefore redirect
after the service has stored the authorized break policy. The block page then
releases the URL, while another stale navigation callback redirects it again.

## Implementation plan

1. Add a regression that changes storage from focus to break between the
   listener's initial evaluation and its redirect decision.
2. Re-read and re-evaluate the authoritative stored policy immediately before
   redirecting a blocked navigation.
3. Run extension checks, full build/tests, package validation, graph refresh,
   and regenerate the next versioned standalone installer.

## Security, UX, and recovery implications

- Security: revalidation may only cancel a stale redirect when the latest
  integrity-checked policy allows the URL; it cannot authorize a break itself.
- UX: a break releases the URL once, without a focus/block-page redirect loop.
- Recovery: missing, invalid, or degraded policy keeps the existing fail-open
  behavior; focus enforcement resumes from the latest confirmed policy.

## Acceptance criteria

- A focus-era async evaluation cannot redirect after a break policy is stored.
- A URL still blocked by the latest policy continues to redirect normally.
- Extension and repository validation pass and the installer is regenerated.
