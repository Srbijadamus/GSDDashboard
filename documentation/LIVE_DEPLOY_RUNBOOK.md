# Live deploy runbook

**State recorded on 2026-10-03, supplied by the operator, not rechecked here.**
This is an operator runbook for a later authorized maintenance window.
**Do not execute live commands during the documentation/package task.**
No deployment, restart, enrollment, SQL or profile change was performed by that
task. Engineering work stays in the secure copies and test databases/ports.

## CURRENT: backend-only kiosk access and isolated Pulse - 2026-10-04

This section supersedes the earlier "build cp failed"/attendance-only preparation
notes. No live Pulse deployment, restart, tunnel or ShiftKiosk profile/key change
was performed. One explicitly requested production **service-device enrollment**
was performed through `admin_cli.py` for **pulse-backend**, device
**pk9W31hcToIK_fLJ**. This was an enrollment write, not a SELECT-only operation.
The token exists only in `C:\ProgramData\PulseStage\backend.json`, inherited
SYSTEM/Administrators-only ACL, outside the repository and static roots.
The restricted temporary enrollment receipt was removed. Do not enroll again.

### Source inventory and behavior

Before this change `Frontend\src\pages\ManualCheckinModal.tsx:6-7` embedded the
kiosk tunnel/key; lines 49-51 read `/api/agents`; lines 76-83 wrote
`/api/manual-checkin` and `/api/manual-logout` with the key.
No other direct kiosk/key occurrence was found in the secure frontend source.
`Attendance.tsx:82` and `WicAttendance.tsx:768` already used the attendance proxy;
the operator's live older bundle was different. The prepared modal now uses
only `/api/kiosk/agents`, `/api/kiosk/manual-checkin` and
`/api/kiosk/manual-logout`. The caller no longer supplies an editable actor:
the backend takes performed_by from the authenticated Pulse session and logs
actor, agent, action and upstream outcome. Agent-fetch failures are visible.

All four proxy routes always require an authenticated DEV/RTM/TEAM_LEAD,
independent of general RBAC. Anonymous=401, other roles=403. Writes also require
the same browser Origin host and `X-Pulse-Request: 1`; no generic URL proxy exists.
The backend sends device headers, never X-API-Key, to the fixed upstream paths.
HTTP loopback is mandatory, production default `http://localhost:8000`;
HTTP redirects are disabled so device credentials cannot follow a redirect.
The proxy preserves upstream error status and reports missing configuration,
invalid response, timeout and network outage explicitly.

The build uses Vite's real `Backend\wwwroot` output, native PowerShell Copy-Item
into the Release static root, no cmd `cp`, no Frontend\dist assumption.
Old Release static assets are removed to avoid retaining an earlier secret
bundle. Built JavaScript was checked for the actual previous embedded key,
ShiftKiosk tunnel and X-API-Key, without printing them: none remained.
The pipeline no longer automatically stops any process or touches a tunnel.

### Operator commands and isolated acceptance

Elevated PowerShell, build both components with the existing pipeline:

```powershell
& 'C:\GSDDashboard_secure\PS1_19_FinalBuildVerify.ps1'
```

The service credential/configuration already exists. **Do not rerun**
`Tools\Prepare-PulseStage.ps1 -EnrollProductionService` for this rollout.
For a future fresh environment it explicitly enrolls one device and constructs
the restricted configuration; it refuses an existing configuration or receipt.

Second instance: loopback **5057**, no tunnel, DB **GSDDashboardCopyTest**,
separate cookie `pulse_stage_auth`. The same private configuration supplies the
service device for real upstream reads via localhost:8000. In a dedicated
PowerShell window, keep the command running:

```powershell
& 'C:\GSDDashboard_secure\PS1_19_FinalBuildVerify.ps1' -Mode Stage -SkipBuild -Port 5057
```

It refuses a used/live port or non-test Pulse database. Do not launch a second
copy if it is already running. Close/stop this stage window only, never stop
processes by executable name. A stage process started by the assistant is
attached to that CLI session; the command above is the operator-owned restart
after that session ends.

Open **http://127.0.0.1:5057/wic-attendance** on the cloud PC, sign in with a
DEV/RTM/TEAM_LEAD account present in the test database, then inspect Network:
only same-origin `/api/kiosk/*`, no ShiftKiosk tunnel/key in browser requests.
Compare Checked In against the returned feed, open manual check-in and require
the agent list to load. Anonymous proxy calls must be 401; wrong role 403.
No new credential or fabricated privileged session is created for UI testing.

**Manual submit on this stage still writes the real ShiftKioskDB**, because the
requested service upstream is the real localhost:8000. Test Pulse DB isolation
does not make this action a dummy. The assistant did not submit any action.
Only the operator, under explicit operational approval, may perform a check-in
and matching logout for a designated test agent. To test without live writes,
use an independently configured ShiftKiosk test instance and separately enrolled
test device, not the production token. Do not silently redirect the production
service credential to another database.

### Live promotion and single-command rollback - NOT executed

Only after signed-in stage acceptance, in a separately approved window:
the existing watchdog must own the live Release executable and port 5000.
Preserve live appsettings/connection string; never copy the stage test DB setting
or stage cookie name into production. The helper exports **only Kiosk** into
restricted `C:\ProgramData\PulseBackend\backend.json`, which the new backend
loads by default. It preserves the dedicated service credential, no reenrollment.

Choose one new snapshot path and invoke the **existing** pipeline:

```powershell
$Snapshot='C:\ProgramData\PulseRollback\20261004-203000'; & 'C:\GSDDashboard_secure\PS1_19_FinalBuildVerify.ps1' -Mode Promote -Snapshot $Snapshot
```

This pauses the existing watchdog with its normal sentinel; checks the exact
port-5000 owner's executable before stopping only that PID; snapshots the live
Release directory, root static files and previous private backend config under
a protected timestamp directory; installs the built binaries/static assets and
service config; then removes the sentinel so the existing watchdog resumes.
It rejects an existing snapshot, reparse paths and unexpected port owner.
No ShiftKiosk service, DB, tunnel, key or profile is touched. Build/backup
preconditions must pass; do not bypass guards. The snapshot path printed by the
operation is the path to use for rollback.

After the watchdog resumes, check localhost health and the **existing**
https://d2jn94qg-5000.euw.devtunnels.ms/wic-attendance, hard refresh or use a new
browser session; require signed-in attendance/agents 200, anonymous 401, no
direct kiosk browser requests, and manual actions accepted with the session's
actor. This authorized tunnel acceptance is required before declaring live done.
If acceptance fails, rollback with one command:

```powershell
& 'C:\GSDDashboard_secure\PS1_19_FinalBuildVerify.ps1' -Mode Rollback -Snapshot 'C:\ProgramData\PulseRollback\20261004-203000'
```

Rollback pauses the watchdog, stops only the checked live PID, restores the
snapshot's binaries/static files and prior private config (or removes the newly
introduced config when none existed), then resumes the watchdog. It does not
undo real manual actions or revoke the device; code/config rollback is not DB
rollback. Do not use this old-browser rollback after closing anonymous kiosk
attendance without also separately restoring that read-access policy.

### Only then close anonymous kiosk attendance - separate future operation

Keep dan1, shared key, device write protocol and the 60 old kiosks unchanged
during Pulse promotion. After production acceptance and supervisor/recovery
preparation, make a separately reviewed **route-scoped** ShiftKiosk change:
`GET /api/attendance` permits a valid device token or authenticated supervisor,
rejects anonymous callers and anonymously minted session tokens, with explicit
401/403. Prepare/test it in the secure repo before its authorized server rollout.
No such server change/profile promotion was executed by this task.

Do not claim that flipping anon_identifiers alone closes this endpoint:
it strips identifiers from successful anonymous feeds, not a 401 read gate.
The current `GET /api/attendance` has no verify_api_key dependency. Adding that
dependency alone would still accept anonymous session tokens. Before strict
closure prove ShiftKiosk dashboard supervisor login/recovery, since its old
anonymous session token must not be exempted. Then prove Pulse device-token
attendance still 200/full employee IDs, dashboard supervisor read still works,
anonymous/session-token reads denied, and legacy kiosk ON_SHIFT/OFF unaffected.
Apply dan2/dan3 only through their separate documented acceptance sequence.

**Premature closure breaks the current live Pulse:** its old browser attendance
requests receive 401/403 (or, with stripping only, lose matching identifiers).
Attendance/WIC show missing/zero check-ins or feed errors. The hardcoded key in
the old manual UI is not a valid replacement for device/supervisor access;
dan1 already restricts that key's non-kiosk routes. Neither a refresh of the old
bundle nor installing the new laptop client repairs old Pulse. Promote and
accept Pulse first, close attendance second.

## Completed baseline - do not replay

Live ShiftKiosk is **dan1**, after dan0 then dan1 via `tools\set_profile.ps1`.
Migrations **001 and 002** are applied. Config backups:
`C:\ShiftKiosk\server\server_config.ini.bak-*`.
Code backup: **`C:\ShiftKiosk_backup_20261002_221207`**.
Database backups:
**`C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\`**;
test database copies were made. [KIOSK_HARDENING_DEPLOY_NOTE.md](KIOSK_HARDENING_DEPLOY_NOTE.md)
is the canonical live-state record.
The 60 old kiosks still use the allowed legacy key.

dan1 blocks session-token access to the six kiosk endpoints, including the
forged status-write attack, disables docs, and rejects the master key in a query.
**Anonymous session tokens still read history/audit-log/exports until dan3.**
Do not mistake legacy-key 403 responses for closure of that exposure.
dan2 and dan3 are pending.

Pulse proxy protection is complete only in `C:\GSDDashboard_secure`:
`KioskEndpoints.cs`, its `Program.cs` registration, `KioskProxyAccessTests.cs`
and `auth_rbac.md`. It is **NOT deployed**. The one isolated HTTP test passed:
anonymous 401, DEV/RTM/TEAM_LEAD 200, other/missing roles 403, RBAC off and on.
This is not a live integration/deployment result.

## WIC zero-count investigation - 2026-10-03 21:23

No live HTTP, SQL, restart or deployment was performed for this investigation.
The exact live failure is **not established** without the signed-in browser's
`/api/kiosk/attendance` response and the deployed runtime settings.
The recorded not-deployed proxy state above is a deployment prerequisite, not
proof of which HTTP response the browser received at 21:23.

`Frontend\src\pages\WicAttendance.tsx` reads Pulse `/api/kiosk/attendance`.
Its global Checked In set includes all ACTIVE and DONE employee IDs, independent
of roster, country and location. `Virtueller WIC-Support` is not excluded from
that set. The per-location presence list is separate. Proxy roles gate the
viewer (DEV/RTM/TEAM_LEAD), not individual agents in the returned feed.
`Backend\Services\KioskProxyService.cs` reads kiosk `/api/attendance` using the
dedicated service device token; missing configuration returns 503.

Confirmed frontend defect: failed queries default to an empty feed and the
Check-in Status panel displayed zero even with `isError=true`. The prepared
source now replaces that panel's numbers with the existing localized offline
warning on query failure. It does not fabricate attendance or turn a valid
empty response into a positive count. TypeScript and Vite completed into the
**secure** `Backend\wwwroot`; the existing npm command's final `cp` failed under
Windows cmd. No live acceptance is claimed.

The kiosk query also had a reproducible identity-join defect: one latest row
per username, OR-joined by agent ID, could return both an old ACTIVE row and a
new OFF row for one agent. The secure ShiftKiosk fix resolves daily events to
agent IDs before ranking/aggregating. A single offline synthetic regression
proves alias OFF gives exactly one DONE row, and a subsequent ON_SHIFT correctly
reopens it. This is **not proof** that aliases caused the reported live row.
An old checkout can legitimately remain visible after a newer ON_SHIFT; do not
force every ACTIVE row with a checkout to DONE. Local first-check-in and the
existing shift-summary duration semantics are unchanged.

Operator actions in a separately authorized window:

1. Capture the signed-in browser's Network response for `/api/kiosk/attendance`,
   without exporting cookies, keys or tokens. 401 means no Pulse session; 403
   means an unauthorized viewer or upstream credential refusal; 404 means
   route/version mismatch; 503 includes missing service configuration or
   upstream unavailability; 502/504 mean invalid feed/timeout. A 200 empty array
   requires comparing the actual same-date kiosk feed. Record status, error
   message and the agent's non-secret fields; do not infer the cause from zero.
2. Promote the prepared Pulse backend/frontend commits and configure the
   **dedicated pulse-backend** service credential as in section 1 below.
   Before using the existing `C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1`,
   preflight its Windows-incompatible npm `cp`, obsolete `Frontend\dist`
   assumption (Vite writes directly to `Backend\wwwroot`) and stale verification
   URL. Use the existing pipeline after correcting those build/verification
   assumptions; do not change the tunnel itself or start a Vite server instead.
3. Promote the ShiftKiosk `server\server.py` attendance-query fix through the
   normal controlled server deployment, preserving configuration, dan1, keys,
   schemas and legacy protocol. This is server code, not a laptop hotfix.
4. After deployment, hard-refresh the real Pulse tunnel page as DEV/RTM/TEAM_LEAD.
   Require anonymous 401 and signed-in 200 with the employee's ACTIVE/DONE row
   and employee_id, plus a nonzero global Checked In count without roster.
   Check the kiosk feed has one row per agent and latest OFF yields DONE with
   fixed duration. If ACTIVE remains, compare latest event IDs/local timestamps;
   a newer ON_SHIFT is a reopened shift, not a stopped-clock defect.

## Operator variables and invocation rules

```text
SK   = C:\ShiftKiosk_secure
PULSE= C:\GSDDashboard_secure
PY   = C:\Users\S69307\AppData\Local\Programs\Python\Python312-32\python.exe
CFG  = C:\ShiftKiosk\server\server_config.ini
BAK  = C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\
CODE = C:\ShiftKiosk_backup_20261002_221207
```

Use **pwsh -NoProfile -File with absolute script paths** for `set_profile.ps1`;
Windows PowerShell `-File` did not work operationally. After a server restart
wait about **20 s** before `verify_live`, even if the profile tool returns sooner.
Avoid pasting multiline if/else fragments into the console; use complete
blocks without `else`. Do not paste keys into logs or documents.

The profile examples below are **future operator actions**, not commands to run
as part of this task.

## 1. Deploy Pulse first, with the service credential

Prepare the frontend/backend build containing:
the frontend calls to Pulse `/api/kiosk/attendance`,
`Services\KioskProxyService.cs`, the always-on gate in
`Modules\Auth\KioskEndpoints.cs`, and supervisor login fields.
Use the existing Pulse build/deploy pipeline in an authorized window, not a new
ad-hoc pipeline. Frontend assets must reach `Backend\wwwroot`; later deployment
acceptance must use the tunnel, not only a Vite development server.

Enroll **one** service device using `admin_cli.py device enroll` with hostname
`pulse-backend`, explicit config and explicit production approval. Store its
temporary token file in a restricted local directory outside OneDrive.
Configure Pulse's deployed/runtime settings, not just the source JSON:

```json
"Kiosk": {
  "BaseUrl": "http://localhost:8000",
  "ServiceDeviceId": "<from the service enrollment>",
  "ServiceToken": "<from the service enrollment>"
}
```

Never commit or display those credentials. Delete transfer copies after secure
configuration; retain the required service secret in its restricted runtime
store. A laptop device token is not the Pulse service credential.

**Acceptance before any profile change:** on deployed Pulse, anonymous attendance
proxy calls return 401; a valid supervisor session returns the full attendance
feed including `employee_id`. Use a real signed-in browser/cookie, not the old
anonymous `Invoke-RestMethod` example. Check Attendance and WIC Attendance over
the tunnel. An AGENT or missing/unknown role must get 403.

**STOP:** missing service token, unexpected 401/403 for supervisors, missing
identifiers, stale frontend assets or "No Check-in" for everyone. Stay at dan1.
The isolated regression test is not a substitute for this deployment acceptance.
The prepared proxy now reports unavailable/timeout as 503/504 and invalid/
identifier-less feed as 502, not a successful empty list. A virtual-support
ACTIVE agent is included in the global Pulse CHECKED IN count. The observed
live 0-versus-1 discrepancy's cause is not established; see the NG evidence ledger.

## 2. Prepare supervisors and recovery, BEFORE dan2/dan3

Create the nine supervisor accounts and break-glass with the existing
`tools\create_supervisors.ps1`, explicit config and production approval, under a
separately authorized operation. Skip existing accounts rather than resetting
their authenticators. Distribute each person's password, TOTP and recovery
codes securely; remove the one-time secrets file after handoff.

Keep `Auth:SupervisorMode` off/shadow during preparation. Prove account sign-in
on kiosk `/login` and Pulse, including a usable DEV session. Test the documented
escape/recovery before enforcing supervisor auth; restore intended shadow
settings afterwards. This preparation does not authorize agents as supervisors.

**STOP:** account/TOTP or recovery failure. Keep dan1, do not continue.
Pulse role protection is always on even while general RBAC is off; a legacy
AGENT session cannot use the full kiosk proxy.

## 3. Apply dan2 only after steps 1 and 2

dan2 enforces `anon_identifiers`; anonymous responses lose identifiers.
The old Pulse browser path would break, hence the hard dependency on step 1.
`session_auth`/`supervisor_auth` are still shadow: **history/audit/export reading
with an anonymous session token remains open at dan2**.

Future operator command:

```powershell
pwsh -NoProfile -File C:\ShiftKiosk_secure\tools\set_profile.ps1 -Profile dan2 -Config C:\ShiftKiosk\server\server_config.ini -Restart
```

Wait about 20 seconds, then run `verify_live.py` with the correct legacy key
without recording it. All six kiosk operations must remain healthy.
Check Pulse Attendance and WIC Attendance with a supervisor session.
Identifier dashes in the kiosk dashboard's Agents tab while session auth is
shadow are expected; they return for authenticated supervisors at dan3.

**STOP:** kiosk failure or broken attendance mappings. Restore the preceding
config backup with profile rollback; do not move on to dan3.

## 4. Apply dan3 only with proven sign-in and recovery

Preconditions: Pulse build/service credential accepted, supervisors enrolled,
account/TOTP sign-in proven, recovery tested, and the supervisor login UI
deployed. Set Pulse `Auth:SupervisorMode=enforce` in the actual runtime config
as well as the managed source configuration.

Future operator command:

```powershell
pwsh -NoProfile -File C:\ShiftKiosk_secure\tools\set_profile.ps1 -Profile dan3 -Config C:\ShiftKiosk\server\server_config.ini -Restart
```

Wait about 20 seconds before verification. `/api/session` now requires supervisor
authentication, and `/dashboard` redirects unauthenticated users to login.
Verify anonymous history/audit/export access is denied, including previously
issued session tokens; verify authorized reads still work. Kiosk supervisor
Team Leads should see only their team; this is not proof of Pulse-wide filtering.
Pulse refuses KID-only sign-in; agents lose `/my`. Old kiosks must keep working.

**STOP:** any kiosk failure, inability to sign in/recover or remaining anonymous
reads. Restore the previous profile/config and Pulse SupervisorMode as required;
keep the failure visible. Do not promote further flags.

## 5. Leave kiosk-sensitive flags alone

`device_binding` and `rate_limit` remain shadow until 14 consecutive days with
no would-block events belonging to known kiosks. `anomaly_detection` is
observational. `kiosk_min_data` does not exist; neither does a dan4 stage.
Do not rotate the legacy key while old clients remain. Retirement criterion:
legacy inventory empty for 30 days.

## Rollbacks are separate operations

| Operation | Scope |
|---|---|
| `pwsh -NoProfile -File C:\ShiftKiosk_secure\tools\set_profile.ps1 -Rollback -Config ... -Restart` | Restores previous config backup/flags; not code, database or Pulse |
| Code restore | Use `C:\ShiftKiosk_backup_20261002_221207` under an approved code-restore procedure |
| Schema rollback | Revert dependent code first; do not execute rollback SQL casually |
| Pulse rollback | Restore known-good build/config; reverting SupervisorMode does not turn off the proxy role gate |
| NG laptop uninstall | Removes NG tasks/client; restore any deliberately removed old task/shortcut separately |

Before future overwrites, take a fresh restricted code/config snapshot. Existing
backup location is the operator-reported path above, not the old invented
`C:\CodeBackups` snapshot. The database-backup directory is the SQL Server
instance default path recorded above.

## Client rollout and operational evidence

See `C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md` for package building,
Downloads/admin cmd/LAPSADMIN installation, old-client replacement, rollback
and known bugs. The clean-laptop token cycle is proven; the current stdlib-only,
protected-code/per-user-log installer changes are not clean-machine-tested.
Timer/start source is now proven: row 1906 contains 11:33:37.952512 local, the
first ON_SHIFT of the day; row 1907 closes that segment. MIN(agent_timestamp)
still anchors the kiosk and dashboard elapsed display after later OFF/ON cycles.
**Namera nepoznata**; no timezone error or server-definition fix is claimed.
Speculative offset/DST client changes were removed. One legacy-key POST is unexplained and the hotkey is
untested. Client evidence is maintained only in that canonical client document.

Never leave a token in OneDrive (including the synced Desktop).
`device list` does not populate `last_seen`; inspect `sec_observation_log` for
activity and credential type. Revoking a device does not retire an old client:
legacy fallback can still work, so remove the old task separately.

Do not rerun `Backend\schema.sql` against any database as part of verification:
its embedded `USE GSDDashboard` can override a test target. No SQL or live
verification is authorized by this file-only update.
