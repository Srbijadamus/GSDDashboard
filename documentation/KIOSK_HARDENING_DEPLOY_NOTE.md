# ShiftKiosk hardening - deployment note

**Canonical live-state record: 2026-10-03.** Live-state and laptop results below were supplied
by the operator; this documentation update did not recheck them or access live
servers, databases or ports. This replaces the obsolete "nothing deployed",
day-one four-flags and dan4 instructions.

Use [LIVE_DEPLOY_RUNBOOK.md](LIVE_DEPLOY_RUNBOOK.md) for the remaining server/Pulse
sequence. Client rollout is documented in
`C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md`.
Pulse authorization details are in [auth_rbac.md](auth_rbac.md).

## Actual state, not a deployment instruction

| Surface | State on 2026-10-03 |
|---|---|
| Live ShiftKiosk | **dan1**; dan0 then dan1 applied using `tools\set_profile.ps1`; previous configs are `server_config.ini.bak-*` |
| Schema | Migrations **001 and 002 applied** |
| Code backup | **`C:\ShiftKiosk_backup_20261002_221207`** |
| Database backups | **`C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\`**; test copies also created |
| Old kiosks | Approximately **60** still work with `legacy_key`; do not revoke/rotate the shared key now |
| Pulse secure copy | Attendance proxy gate completed, **NOT deployed** |
| New kiosk | `3.0.0-ng` proven on `DESKTOP-1302198`; see evidence below |
| Next stages | **dan2 and dan3 NOT applied**; Pulse build/config and supervisor preparation must come first |

Do not replay the completed migrations/dan0/dan1 steps as if they were pending.
The working copies are `C:\ShiftKiosk_secure` and `C:\GSDDashboard_secure`.
Engineering verification is restricted to `ShiftKioskTest`, `GSDDashboardTest`,
`GSDDashboardCopyTest`, ports 8011/5056. No live operation is authorized by this
documentation task.

## What dan1 closes, and what remains open

`legacy_scope=enforce` is symmetric: the legacy key is restricted to the six
kiosk endpoints, and session tokens cannot call those endpoints. It closes the
anonymous `/api/session` token -> `POST /api/status` impersonation chain without
requiring agents to authenticate. `docs_disabled` and `key_in_query` are also
enforced at dan1.

**This is NOT closure of anonymous reading.** An anonymous caller can still
obtain a session token and use it to read **history, audit-log and exports until
dan3**. A 403 for a *legacy-key* call to those paths does not prove a session
token is blocked. At dan1 `anon_identifiers`, `session_auth` and `supervisor_auth`
remain shadow. The old browser attendance path still exposes identifiers.

| Flag | dan1 now | dan2 planned | dan3 planned |
|---|---|---|---|
| docs_disabled | enforce | enforce | enforce |
| legacy_scope | enforce | enforce | enforce |
| key_in_query | enforce | enforce | enforce |
| anon_identifiers | shadow | enforce | enforce |
| session_auth | shadow | shadow | enforce |
| supervisor_auth | shadow | shadow | enforce |
| device_binding | shadow | shadow | shadow |
| rate_limit | shadow | shadow | shadow |
| anomaly_detection | shadow | shadow | shadow |

There are nine flags, no implemented `kiosk_min_data`, and profiles dan0..dan3.
Keep device binding and rate limiting at shadow until observation supports
promotion (14 consecutive days without kiosk-related would-block events).
Anomaly detection is observational, not a blocking control.

## Pulse change prepared, not live

The old frontend requests kiosk attendance directly and matches on `employee_id`.
That identifier is a complete credential in legacy Pulse sign-in; stripping it
before deploying the replacement makes Attendance/WIC show "No Check-in".

The replacement frontend calls Pulse `/api/kiosk/attendance`, whose backend uses
`Services\KioskProxyService.cs` and a service device token. The completed gate in
`Modules\Auth\KioskEndpoints.cs`, registered by `Program.cs`, always returns:

| Caller | Result, even with Auth:EnforceAuthorization=false |
|---|---|
| Anonymous | JSON **401**, before any proxy call |
| Authenticated DEV / RTM / TEAM_LEAD | May receive the kiosk response |
| AGENT, missing or unknown role | JSON **403** |

`Backend.Tests\KioskProxyAccessTests.cs` passed as **one focused HTTP test** on
5056 with real `gsd_auth` cookies and a kiosk stub: anonymous 401, three allowed
roles 200, other roles 403, RBAC flag false and true. It did not execute startup
SQL or contact a live kiosk. This proves the isolated endpoint/cookie behavior,
not deployment or an end-to-end connection to the live service.

**Required remaining order:** Pulse frontend/backend build with proxy gate and
`Kiosk:ServiceToken`/`ServiceDeviceId` -> supervisor accounts and tested recovery/
sign-in -> dan2 -> dan3 with Pulse `Auth:SupervisorMode=enforce`.
General RBAC remains a separate rollout; this change does not turn it on.

## Proven client result and packaging status

On a clean test laptop `DESKTOP-1302198`, the operator proved the cycle
**ON_SHIFT -> PAUSE -> ON_SHIFT -> OFF**, reporting `status_log` IDs **1920..1922**,
`client_version=3.0.0-ng`, `device_id=iNf8-aq1u50U-KGH`, and
`sec_observation_log` `credential_type=device_token`, HTTP **200**.
This is the supplied evidence range, not an assertion of four distinct row IDs.
`iNf8-aq1u50U-KGH` is active; `AmSirLHoiQHsOsuY` and `VLOYZdA1vDIm3IIm` were revoked.

The working package was `C:\ShiftKiosk\KIOSK NOVI SA ZASTITOM\`.
Current secure-copy client removes unused requests/vendor imports, keeps UI
logs/queues in the agent's LocalAppData, and
uses a protected code DACL/owner, UI Limited and a separate NG desktop shortcut.
Builder includes the new runtime helper and PowerShell installer. These changes
**have not been tested on a clean machine**; focused offline tests/parser passed.
A legacy-key POST at **11:47:57 UTC** and the live Pulse count discrepancy remain
unexplained; the hotkey is untested.
The canonical client evidence/defect ledger is the NG rollout document.

Code investigation: `server.py:494-498` returns MIN(agent_timestamp) unchanged,
not server_received. Operator-supplied live evidence identifies row **1906**:
S69307 ON_SHIFT at **2026-10-03 11:33:37.9525120 local**, received **09:33:35 UTC**,
v2.1.1; row **1907** OFF at **11:36:21 local**. The server-resumed kiosk timer and
dashboard Check-in / Im Dienst remain anchored to the first ON_SHIFT after OFF/ON
cycles. This is **not a timezone defect**; intention of the shift definition is
unknown (**namera nepoznata**). Server/old-client/API behavior is unchanged.
The speculative offset/DST timer change and test were removed. Runtime helper
remains only for per-user files/queue migration. See the canonical NG ledger.

Pulse global CHECKED IN includes ACTIVE/DONE regardless of virtual location;
0 versus kiosk ACTIVE 1 is not explained by HOME_OFFICE alone for the same feed.
The actual live cause is not determined. A confirmed prepared-proxy defect
(network failure becoming 200 []) was fixed: outage/timeout now 503/504 and
malformed/identifier-less feeds 502. Valid HOME_OFFICE feeds are preserved.
One new focused handler-stub test passed; it did not contact any kiosk or database.

## Operational and rollback boundaries

Invoke `set_profile.ps1` with **pwsh**, `-NoProfile -File` and an **absolute path**,
not Windows PowerShell `-File`. After an authorized server restart, wait about
**20 seconds** before `verify_live`. Avoid pasted multiline if/else console
fragments; use complete blocks without `else`.

Never put enrollment files in OneDrive (the desktop is synced); use Downloads
for laptop transfer. `device list` does not populate `last_seen`; use
`sec_observation_log` to establish activity.

Flags/config rollback, code rollback, schema rollback and Pulse rollback are
different operations:

| Failure | Recovery boundary |
|---|---|
| Profile too strict | `set_profile.ps1 -Rollback` restores the previous config backup, not code/Pulse/schema |
| Bad kiosk server code | Restore the code snapshot `C:\ShiftKiosk_backup_20261002_221207` under an approved recovery procedure |
| Pulse sign-in lockout | Break-glass or revert SupervisorMode; does not bypass the always-on proxy role gate |
| Schema removal | Revert dependent code first; rollback SQL is not authorized by this task |
| NG client removal | `uninstall_kiosk_ng.bat`; an old task removed during replacement must be restored explicitly |

## Security limits and data protection

Supervisor roles are DEV, RTM and TEAM_LEAD. At enforce, agents have no supervisor
accounts and lose Pulse `/my`. Kiosk agents never authenticate; broker identity
comes from the OS. Device tokens are not user authentication: a local administrator
can read them/bypass the broker, and the legacy key remains a known exposure.
Kiosk supervisor Team-Lead scoping is implemented; do not infer Pulse-wide team
filtering from this fact or from the new proxy role gate.

Retire/rotate the legacy key only after the legacy inventory has been empty for
30 days. Treat SMTP passwords/webhooks and enrollment tokens as secrets; neither
tokens nor Python installers belong in git. Separate token signing secrets from
the shared API key in a future change.

Preserve the tester's report/logs and annotate forged attendance rather than
silently deleting it. Review retention and handling of named sickness data with
data protection/Betriebsrat/IT; historical attendance is not reliable individual
performance evidence. Agree storage/deletion rules for tester exports. These
controls and the remaining anonymous-read exposure are not solved by packaging.

Historical schema footgun: `Backend\schema.sql` contains `USE GSDDashboard` and
can override a test database argument. A previous note recorded one accidental
execution with statements failing against existing objects and no observed data
change. Do not run that script for tests; use the retargeted test schema.
