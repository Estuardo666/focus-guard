# Legacy vault migration and device-session renewal

The installed .154 Host matches its package hash. The user's screenshot confirms the checking loop stops and login succeeds, but device linking reports failure. Read-only in-memory DPAPI diagnostics confirmed an exact historical twelve-field FG-DEVICE-VAULT-1 payload: the file is decryptable and lacks epoch/history/session/pairing fields. No secret values were printed and no personal file was rewritten.

Implemented ADR0023: exact legacy/intermediate shape readers, preserving identity/content/recovery; a native atomic session update preserving historical content keys; token-bound signed renewal by an existing active member only; stored session hashes, eight-hour absolute/30-minute idle limits, replay and stale-member rejection. Renewal is followed by verified membership read before linked state is reported.

Legacy tests failed before compatibility was added and now pass. Devices 19/19, Host221/221 and React235/235 pass. Setup .155 is built and passed package self-test and installer script tests. It is not installed or yet approved as end-to-end ready.

Integration encountered a separate existing foreign-key error: challenge cleanup removed expired records referenced by pairings. Cleanup now excludes referenced challenges, with a real database regression. The expanded fixture also exceeded the rate-limit window at replay verification; both replay checks now run after that window, and accept genuine 403/404 denial rather than a 429. The final fixture run is pending; deployment and installed migration acceptance remain pending.

The final fixture passed 120 checks against real Keycloak/Neon/DPAPI plus persisted realm restart. It includes unknown-field HTTP400, expiry denial and a fresh signed secret after expiry. The owner reports .155 installed; its screenshot still shows device-link failure before deployment. Development tools did not rewrite personal device material. API deployment is now running via Deploy-TncApi, tag phase2-session-renewal-20261012; inspect its live result and verify staging before claiming installed recovery works.

Deployment completed: `tnc-api-staging-00015-6tt` serves 100% of traffic, pinned digest `sha256:f5588cb1837ec2d23915a27e778d90e1b7064b07130944eb3b6c0a4b684679c5`. Live/ready returned200; Test-TncStaging passed56 checks including anonymous renewal denial401. Existing IAM and identity service remain verified. Installed personal migration is still awaiting observation: allow the next device check or fully exit/reopen the app; no additional pairing or logout is required.

Package verification: .155 installer tests, standalone self-test and Chrome/Edge packaging pass. Setup SHA256 `C92D7615E2CD47AE202FC9E08DD7FCB3F2F1BE92EF3C0872FDA31AC94FE9411C`; ZIP `32A820921CECFB1AF5CD79DE5A550D3ADB1D1DD501791DEFB8D0EAC0320CBA63`. The final fixture run completed successfully; no process remains pending from that run.

## Installed acceptance

The owner's screenshot `codex-clipboard-524fede2-4cf6-4ca0-96ab-9139aece8e63.png` shows «Linked to this account», no error/checking notice, no pending approvals, and enabled share/import controls. Installed Host DLL SHA256 `5DF934AF60E148A1E8A02CE04897C1ECAADAB908A186C00463DC38DC6C6121D1` equals the published .155 payload. This accepts recovery of this Windows installation and the visible account loop fix. It does not prove two physical Windows pairing, actual settings transfer, password-reset E2EE separation, QR UI or Android vault acceptance; Phase2 remains open.
