# ShiftKiosk — Security Solution (decided proposal)

> **HISTORICAL PROPOSAL, not the current implementation/deployment status.**
> "Nothing implemented" below describes the original proposal's date only.
> Canonical current state: [KIOSK_HARDENING_DEPLOY_NOTE.md](KIOSK_HARDENING_DEPLOY_NOTE.md).
> Client evidence/install: `C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md`.

Status: **proposal. Nothing was implemented, built, run, tested or deployed.** Read-only
source and `git log`/`git show` only. Date: 2026-10-02.

Labels: **CONFIRMED** (file + line or commit I opened) · **ASSUMPTION** · **NOT FOUND**.
Where documentation and code disagree, the code wins and the disagreement is recorded.

Owner constraints that govern every decision: no change may endanger the 60 installed
kiosks; no flag day; **no user-side authentication and no user-visible burden**; every
server change ships in shadow mode first; fail-safe = allow-and-log, never deny; the
legacy key stays alive until the usage log proves zero use; no noticeable latency on
`/api/status`.

---

## 1. Decision summary

The kiosk server authenticates with one shared secret that every agent can read, and takes
the acting identity from the request body, so any agent can post, read or acknowledge as
anyone else. Worse than reported: `GET /api/session` hands an 8-hour token to anonymous
callers and that token is accepted wherever the master key is, and `/api/attendance`
returns every agent's `employee_id` with no credential at all — which is a **working Pulse
login**, because Pulse sign-in is a single KID/EmployeeId field with no password.

**Recommended design — "device certificate always, Kerberos user identity when available":**
per-device mTLS client certificate with a non-exportable machine key provisioned at
install (silent, no user action), plus silent Negotiate/Kerberos on the new on-prem server
so the server derives `agent_username`/`performed_by`/`acked_by` from the authenticated
principal and ignores the body. Kerberos is the only layer that actually *stops*
impersonation; it is conditional on the laptops being domain-joined, which is **NOT FOUND**
and is the one prerequisite to confirm. Until then the device certificate plus server-side
device↔user binding **limits and detects** impersonation; it does not prevent it.

For the 60 unchanged kiosks the server alone can remove everything except impersonation on
six endpoints, because the kiosk has only ever called six (verified across all client
copies and 25 commits). Stop installing the old client now.

**First five actions:** (1) read-only SQL to settle the schema/KID questions in §10;
(2) turn on observation-only logging — credential type, source IP, hostname, client
version, path; (3) require auth on `/api/session` and gate `/dashboard` together;
(4) disable `/openapi.json`, `/docs`, `/redoc`; (5) confirm domain-join status, which
decides the identity design.

---

## 2. Step 1 — Authoritative kiosk call inventory

Searched the whole repository: client, all install packages, installers, scheduled-task
definitions, self-check, backups, and `git log`/`git show` across 25 commits of the client.

### 2.1 Every HTTP call made by code that ships to a kiosk

| # | Method | Path | Credential sent | Response fields read | File:line |
|---|---|---|---|---|---|
| 1 | POST | `/api/status` | `X-API-Key` header | `r.status_code` only | `kiosk/main_kiosk.py:388-389` |
| 2 | GET | `/health` | **none** | `r.status_code` | `kiosk/main_kiosk.py:400` |
| 3 | GET | `/api/agent-lookup?username=` | `X-API-Key` header | `agent_id` | `kiosk/main_kiosk.py:414-417` |
| 4 | GET | `/api/today-holiday` | `X-API-Key` header | `holiday`, `name` | `kiosk/main_kiosk.py:450-451` |
| 5 | GET | `/api/agent-location-options?agent_id=` | `X-API-Key` header | `home_office_allowed`, `wic_centers[].{id,name,role}` | `kiosk/main_kiosk.py:703-706` |
| 6 | GET | `/api/shift-status?username=` | `X-API-Key` header | `open`, `status`, `shift_start` | `kiosk/main_kiosk.py:818-821` |

All six pass `verify=False` — TLS certificate validation is disabled on every kiosk call.
CONFIRMED `kiosk/main_kiosk.py:389, 400, 417, 451, 706, 821`.

`kiosk_selfcheck.py` makes **no HTTP calls** — it only imports `requests`, `urllib3`,
`tkinter` and `theme`. CONFIRMED `KIOSK_PACKAGE/Files/kiosk_selfcheck.py` (whole file read).
`install_kiosk.bat` and `Deploy-Application.ps1` make no HTTP calls to the kiosk server.
There is **no updater, watchdog, tray app or helper tool** that talks to the server —
CONFIRMED by searching every `.py`/`.ps1`/`.bat`/`.cmd` in the repository for
`requests.`/`urllib`/`Invoke-RestMethod`/`Invoke-WebRequest`/`curl`; the only hits outside
the client and server are `vendor/requests/*` (the bundled library itself).

### 2.2 Endpoint set across all client copies on disk

| Copy | Endpoints referenced |
|---|---|
| `kiosk/main_kiosk.py` (installed build) | status, health, agent-lookup, today-holiday, agent-location-options, **shift-status** |
| `KIOSK_PACKAGE/Files/main_kiosk.py` | identical set |
| `deploy/Files/main_kiosk.py` | identical set (differs from `kiosk/` only in the keyboard-hook flag at line 740 — your uncommitted work, not an endpoint change) |
| `deploy/main_kiosk.py` (older) | status, health, agent-lookup, today-holiday, agent-location-options — **no shift-status** |
| `kiosk/main_kiosk_backup_20260803_141808.py`, `…_b_20260803_141822.py` | same as above, no shift-status |
| `backup/2026-05-28_17-07/kiosk/main_kiosk.py` (oldest) | **status, health only** |

### 2.3 Git history — 25 commits of `kiosk/main_kiosk.py`

CONFIRMED by `git show <commit>:kiosk/main_kiosk.py` for each of 25 commits: the endpoint
set is stable. Commits `ef43b8d`, `c6f348a`, `37f2a1f`, `27c05d7`, `dd608a2` call four
(`status`, `agent-lookup`, `today-holiday`, `agent-location-options`); from `40bb3af`
("GRC fix: kiosk resume-on-restart …") onward `shift-status` is added. No commit ever
introduces another endpoint.

### 2.4 Verdict on the stated claim

> "the kiosk uses only /api/status, /api/agent-lookup, /api/shift-status, /api/agent-location-options"

**PARTIALLY TRUE — and the omission matters.** It misses two real calls:

- **`/api/today-holiday`** — called at startup with the API key (`kiosk/main_kiosk.py:450-451`). Present in every client copy and every commit examined.
- **`/health`** — the reachability probe (`kiosk/main_kiosk.py:400`), sent **without** any credential.

Locking down either would break all 60 kiosks. The authoritative allow-list is the **six**
rows in §2.1. **No kiosk build, current or historical, ever calls `/api/session`,
`/api/agents`, `/api/attendance`, `/api/teams`, `/ws`, any export, `/api/history`,
`/api/audit-log`, `/api/import-agents`, the manual actions, `/api/emergency/ack`, or the
`wic-centers` writes.** This is the evidence base for every "breaks installed kiosks: no"
claim below.

Also CONFIRMED: **no kiosk build uses the `?token=` query form.** All five credentialed
calls use the header (`kiosk/main_kiosk.py:389, 417, 451, 706, 821`).

---

## 3. Step 2 — Endpoint access matrix (from the decorators, not the schema)

`verify_api_key` is the only auth code in the server — `server.py:955-962`:

```python
key = x_api_key or token
if key and (key == API_KEY or _verify_session_token(key)):
    return
raise HTTPException(status_code=401, detail="Invalid API key")
```

It returns `None`: **no endpoint learns who called.** CONFIRMED. Counts: 41 `@app.` routes,
**32** carry `dependencies=[Depends(verify_api_key)]`, **9** carry none, plus 3 FastAPI
defaults (`/docs`, `/redoc`, `/openapi.json` — CONFIRMED by omission at `server.py:951`,
which passes only `title` and `version`). **Zero** routes perform an authorization check.

**`KEY` is not a real barrier:** `GET /api/session` (`server.py:1011-1013`) has no
dependency and returns `{ts}.{hmac(API_KEY, ts)}` valid 8 h (`server.py:50-55`), which
`verify_api_key` accepts (`server.py:960`). One anonymous round-trip ⇒ every `KEY` endpoint.

### 3.1 No credential at all

| Method | Path | Identity trusted | Returns / changes | Caller | Route |
|---|---|---|---|---|---|
| GET | `/health` | — | liveness | **kiosk** (§2.1 #2), watchdog | `server.py:1007` |
| GET | `/api/session` | — | **8 h token accepted on all 32 KEY routes** | dashboard JS (`server.py:2791, 3595, 3621`) | `server.py:1011` |
| GET | `/api/agents` | — | `SELECT * FROM agents` (`server.py:483`) | dashboard | `server.py:1052` |
| GET | `/api/teams` | — | distinct `team_leader` | dashboard | `server.py:1056` |
| GET | `/api/attendance` | — | all active agents incl. `employee_id`, `windows_username`, `SICK`, `status_note` | dashboard **and Pulse frontend** (`BLUEPRINT_SCREENS.md:163`) | `server.py:1060` |
| GET | `/api/today-holiday` | — | holiday flag | **kiosk** (§2.1 #4 — sends a key the route ignores) | `server.py:1320` |
| GET | `/api/agents-template` | — | blank xlsx | dashboard | `server.py:1639` |
| WS | `/ws` | — | full attendance on connect + every event pushed | dashboard | `server.py:1649` |
| GET | `/dashboard` | — | the admin UI, whose JS performs every write | browser | `server.py:3635` |
| GET | `/docs`, `/redoc`, `/openapi.json` | — | full API schema | — | `server.py:951` (by omission) |

### 3.2 Behind the shared key — the six the kiosk needs

| Method | Path | Identity trusted | Route |
|---|---|---|---|
| POST | `/api/status` | `agent_username`, `agent_hostname` **from body** | `server.py:1015` |
| GET | `/api/agent-lookup` | `username` **from query** (404 if unknown ⇒ enumeration oracle, `server.py:1291`) | `server.py:1285` |
| GET | `/api/shift-status` | `username` **from query** | `server.py:1295` |
| GET | `/api/agent-location-options` | `agent_id` **from query** | `server.py:1303` |

### 3.3 Behind the shared key — the 28 the kiosk never calls

Writes: `/api/import-agents` (`:1042`, rewrites the `agents` table), `/api/manual-checkin`
(`:1068`), `/api/manual-logout` (`:1088`), `/api/emergency/ack/{id}` (`:1106`),
`/api/wic-centers` POST/PUT/DELETE (`:1241`, `:1254`, `:1267`).
Reads: `/api/audit-log` (`:1125`), `/api/history` (`:1128`), `/api/stats` (`:1132`),
`/api/summary` (`:1135`), `/api/worked-report` (`:1426`), `/api/logout-audit` (`:1466`),
`/api/wic-attendance` (`:1554`), `/api/wic-centers` GET (`:1237`), `/api/wic-coverage`
(`:1275`), `/api/wic-agents` (`:1281`).
Exports: `server.py:1144, 1156, 1167, 1211, 1337, 1361, 1435, 1476, 1513, 1563, 1597`.

Identity on the manual actions is `performed_by` / `acked_by` from the body, validated only
against empty and the literal `"supervisor"` (`server.py:974-983`, `987-994`). The in-code
comment claims this makes actions "attributable" (`server.py:971-973`); it does not — any
other string is stored verbatim. **Code wins over the comment.**

---

## 4. Step 3 — Data exposure per credential type

| Credential | What it yields |
|---|---|
| **None** | Every agent's `full_name`, `windows_username`, **`employee_id`**, `team_leader`, `location`, check-in/out times, `attendance_status` incl. **`SICK`**, `status_note`, `work_location`, `emergency_acked_by` (`server.py:395-416`, route `:1060`); the whole `agents` table via `SELECT *` (`server.py:483`, route `:1052`); live push of all of it on `/ws` (`:1649-1655`); the full API schema |
| **Anonymous session token** (one GET to `/api/session`) | everything the master key yields — see next row |
| **Master key / kiosk credential** | all of the above **plus** `SELECT * FROM status_log` unbounded (`server.py:124`, route `:1128`), the complete `manual_actions` audit log (`server.py:490-496`, route `:1125`), 11 bulk exports, agent-table rewrite, WIC-centre writes, manual check-in/logout as any agent attributed to any name, emergency ack |

### 4.1 Columns `SELECT *` publishes beyond what the code names

`GET /api/agents` and `/api/agents/export/csv` run `SELECT * FROM agents`
(`server.py:483`). Columns established from code: `id`, `full_name`, `windows_username`,
`team_leader`, `active` (`server.py:1071`, `481-488`); `employee_id`, `location`
(`server.py:396`); `primary_kid`, `secondary_kid`, `infosys_email`, `eon_email`,
`wic_location` (`server.py:504-508`); `first_name`, `last_name` (`server.py:1643`).

**NOT FOUND:** the authoritative DDL. `db_init()` only *adds* columns to tables it assumes
exist (`server.py:501-562`), and `C:\ShiftKiosk\db\` contains one view
(`db/views/vw_shift_summary.sql`). So the list is a **lower bound** — any column added
out-of-band is published automatically, with no code change. Exact read-only check in §10.

`status_log` columns (from the INSERT at `server.py:98-107` and the ALTER list at
`server.py:533-551`): `id`, `agent_username`, `agent_hostname`, `status`, `note`,
`agent_timestamp`, `ip_address`, `agent_id`, `work_location`, `wic_center_id`,
`server_received`, `emergency_acked`, `emergency_acked_by`, `teams_sent`, `email_sent`.

Two fields are accepted and thrown away — both needed for the migration:
`client_version` (accepted `server.py:966`, absent from the INSERT `server.py:98-107`) and
the caller IP (`/api/status` passes `""`, so `ip_address` is always empty —
`server.py:1021`). **CONFIRMED.**

---

## 5. Step 4 — The Pulse connection, and a separate critical finding

### 5.1 How the two apps are linked

| Fact | Finding |
|---|---|
| Kiosk database | `ShiftKioskDB` on `localhost\SQLEXPRESS`, `trusted_connection = yes` — CONFIRMED `server/server_config.ini [Database]` |
| Pulse database | `GSDDashboard` on `localhost\SQLEXPRESS`, `Trusted_Connection=true` — CONFIRMED `Backend/appsettings.json` |
| Shared tables | **None.** Two separate databases on the same instance, both via Windows auth as the service account. CONFIRMED |
| Shared credentials | **None** — Pulse never uses the kiosk API key. CONFIRMED: no `X-API-Key` anywhere under `Backend/` |
| Actual coupling | (a) the Pulse browser frontend reads kiosk `/api/attendance` directly (`BLUEPRINT_SCREENS.md:163`, `PROJECT_BLUEPRINT.md:279` `VITE_KIOSK_API_URL`); (b) the kiosk server server-side fetches Pulse `localhost:5000/api/publicholidays` (`server.py:1325-1329`); (c) agent data is **duplicated** into `ShiftKioskDB.agents` by `sync_shiftkiosk_agents.ps1` |

Correction to existing docs: `auth_rbac.md` § Notes says "no machine-to-machine traffic
hits this backend". True for inbound RBAC, but (b) above is kiosk→Pulse server-side
traffic. Already flagged in `KIOSK_GAPS_AND_CONTRADICTIONS.md` § C4.

### 5.2 FINDING — the unauthenticated kiosk endpoint leaks working Pulse credentials

This is a separate finding and the most severe cross-application issue.

Pulse sign-in is **one field, no password, no second factor** — CONFIRMED
`Backend/AuthModule.cs:22` (`public record LoginRequest(string? Kid);`) and
`AuthModule.cs:31-34`. The code's own comment says it: *"KID-only login, no password …
with no second factor, this is the only barrier between a guessed KID and the data"*
(`AuthModule.cs:30-31`).

`AuthService.ResolveLoginAsync` accepts **three** credential forms — CONFIRMED
`Backend/Services/AuthService.cs:31-89`:

| Rule | Input accepted | Role granted | Line |
|---|---|---|---|
| (a) | `AppUserRoles.Kid` | that row's role — **DEV / RTM / TEAM_LEAD / AGENT** | `AuthService.cs:37-59` |
| (b) | `Employees.PrimaryKid` or `SecondaryKid` | AGENT | `AuthService.cs:63-73` |
| (c) | **`Employees.EmployeeId`** | AGENT | `AuthService.cs:75-86` |

Now join that to the kiosk server: **`GET /api/attendance` requires no credential
(`server.py:1060`) and returns `a.employee_id` for every active agent
(`server.py:396`).**

> **Anyone who can reach the kiosk server — with no key, no token, no account — can read
> every agent's `employee_id` and use it as a Pulse login under rule (c), as that person,
> at AGENT level.** CONFIRMED by composition of `server.py:396`, `server.py:1060` and
> `AuthService.cs:75-86`.

`GET /api/agents` (also unauthenticated) additionally exposes `primary_kid` /
`secondary_kid` via `SELECT *` if those columns hold data, which would satisfy rule (b) and
— if any `AppUserRoles` KID appears there — rule (a) at **DEV or RTM** level
(`auth_rbac.md` seeds `S69307`=DEV, `S60028`/`Y6525`=RTM). Whether they hold data is
**NOT FOUND**: `import_agents_from_excel` maps only `emp id`, `name`, `team leader`,
`location`, `engagement`, `windows_username` (`server.py:782-787`) and never writes the KID
columns. A separate path may: `sync_shiftkiosk_agents.ps1:179` notes *"Agenti ciji je ID
zapravo primary_kid"* ("agents whose ID is actually the primary_kid"), i.e. some
`agents.employee_id` values **are** KIDs. Exact check in §10.

Mitigating today, aggravating tomorrow: Pulse enforcement is **off** —
`"EnforceAuthorization": false` in both `Backend/appsettings.json` and
`Backend/bin/Release/net8.0/appsettings.json` (CONFIRMED), so `RbacMiddleware` is a no-op
and Pulse data is currently unprotected regardless. The moment enforcement is switched on,
this leak silently defeats it for the AGENT role. **Fix the leak before flipping the flag.**

---

## 6. Step 1 answer — Group A: the 60 unchanged kiosks

What the server can enforce with **no kiosk change at all**, decided item by item. Every
"no" rests on §2.

| Decision | Verdict | Breaks installed kiosks | Why |
|---|---|---|---|
| Limit the kiosk credential to exactly the six endpoints of §2.1 | **DO IT** | **no** | 25 commits + 7 client copies never call anything else |
| Require auth on `/api/session`, and gate `/dashboard` in the same change | **DO IT, together** | **no** | no kiosk build calls `/api/session`. `/dashboard` must move at the same time or the dashboard breaks (`server.py:2791, 3595, 3621`) |
| Require a credential on `/api/agents`, `/api/teams`, `/api/agents-template`, `/ws` | **DO IT** | **no** | never called by a kiosk |
| Require a credential on `/api/attendance` | **DO IT — but sequence it last** | **no** | not called by a kiosk, but **is** called by the Pulse frontend (`BLUEPRINT_SCREENS.md:163`). Ship after Pulse sends a credential, or the WIC Attendance page breaks |
| Reject the **master key** in `?token=`, keep session tokens there | **DO IT** | **no** | kiosks are header-only (§2.4). Nine dashboard download buttons use `?token=` with a session token (`server.py:2999, 3009, 3016, 3066, 3136, 3144, 3392, 3400, 3560`) — keep that form working |
| Remove kiosk-credential access to history, audit-log, exports, import, wic-centers writes, manual actions, emergency ack | **DO IT** | **no** | the 28 routes of §3.3 |
| Disable `/openapi.json`, `/docs`, `/redoc` | **DO IT** | **no** | `server.py:951`; kiosks never fetch them |
| Rate limiting | **DO IT**, in-memory, generous | **no** | a kiosk makes ~5 calls at startup + 1 per status change. Set limits ≥10× that |
| Write server-observed facts to the audit trail (source IP, timestamp, credential type, path, hostname, client version) | **DO IT FIRST** | **no** | pure addition. Prerequisite for everything else |
| **Strip other users' data** from the four kiosk data endpoints | **DO IT, shadow first** | **no if allow-and-log** | the client only ever asks about itself — `USERNAME` is a module constant (`main_kiosk.py:302`) passed to `/api/agent-lookup` (`:416`) and `/api/shift-status` (`:820`), and `agent_id` comes from the client's own lookup (`:706`). Scope to the bound username; if no binding exists yet, **allow and log** |
| Hostname↔username binding, trust-on-first-use + approval for changes | **DO IT, shadow first** | **no if allow-and-log** | see the honesty paragraph below |
| Detect a second concurrent session / shift start from a new host | **DO IT** (detect + alert only) | **no** | read-only observation |
| Alert supervisors on the above | **DO IT** | **no** | notification path already exists (`server.py:859`, `884`) |
| Reject unknown `agent_username` on `/api/status` | **DO IT, shadow first** | **no** | today `agent_id` is left `NULL` and the row is written anyway (`server.py:93-107`, `921`). Audit existing NULL rows before enforcing |

### 6.1 What impersonation remains after all of that — honest answer

**Impersonation is not prevented for Group A. It is narrowed and made noisy.**

Hostname↔username binding is the strongest control available without touching the kiosk,
and it has a hard ceiling: **`agent_hostname` is client-supplied, exactly like
`agent_username`** (`StatusPayload`, `server.py:963-967`; written at `server.py:98-107`).
The tester changed both (`main_kiosk.py:302-303`). So binding stops only the attacker who
forges the username and forgets the hostname. An attacker who copies **both** fields from a
colleague's kiosk — and both are readable, since the whole client is plaintext and
`BUILTIN\Users` has RX on `C:\ShiftKiosk` (`install_kiosk.bat:115-118`) — passes the
binding check cleanly.

Source IP, which the server *can* observe and cannot be forged as easily, is weak here:
**`HOME_OFFICE` is a real, agent-selected work location** (`server.py:2800, 2819, 2829`),
so a large share of legitimate traffic arrives from arbitrary residential IPs. IP
allowlisting would break home workers and violate the fail-safe rule. IP is therefore
evidence for review, **not** an enforcement input. ASSUMPTION: a meaningful share of the
130 agents work from home; the code proves the feature exists, not the proportion.

After Phase A a legacy-credential holder can still:

1. Post any status (`ON_SHIFT`, `SICK_LEAVE`, `EMERGENCY`, `OFF`, `AUTO_LOGOUT`, `PAUSE` —
   `server.py:1002`) as any agent, at any timestamp, with any `note`, `work_location` and
   `wic_center_id` — if they also send that agent's hostname.
2. Trigger a Teams card and e-mail naming that agent and their team leader
   (`server.py:1025-1038`).
3. Read that agent's shift state, WIC centres and coverage role, and enumerate valid
   usernames via the 404/200 oracle (`server.py:1291`) — unless the binding scope is
   enforced, which reduces this to *their own* record.
4. Corrupt attendance data indistinguishably from a real kiosk.
5. Stop their own tracking silently: `Ctrl+Shift+Alt+A` → `_supervisor_exit()` →
   `_cancel_auto_timer(); self._exit()` with **no password, no confirmation and no status
   event** (`main_kiosk.py:1102-1112`, `1392-1395`). The shortcut is hardcoded and
   identical everywhere; the `[Security] supervisor_exit_shortcut` key in `config.ini` is
   **never read** — dead config (only `Company.name`, `Server.url`, `Server.api_key` at
   `main_kiosk.py:299-301` and `settings.language` at `:242` are read).

**Damage ceiling:** falsified attendance and sick-leave records, and spoofed notifications,
for any named colleague. **Accept this for Group A and shorten the transition** — it ends
only with Group B, because it requires a client change.

---

## 7. Step 2 answer — Group B: making the one visit count

### 7.1 How a kiosk is installed today (CONFIRMED)

| Aspect | Detail | Source |
|---|---|---|
| Installer | attended, **requires Administrator** | `KIOSK_PACKAGE/install_kiosk.bat:24-27` |
| Files copied | `main_kiosk.py`, `theme.py`, `config.ini` → `C:\ShiftKiosk\kiosk` | `install_kiosk.bat:103-108` |
| ACL | `icacls "C:\ShiftKiosk" /grant "*S-1-5-32-545:(OI)(CI)RX" /T`, **install aborts if it fails** | `install_kiosk.bat:115-118` |
| Server address | `config.ini [Server] url`, read at `main_kiosk.py:300` | `kiosk/config.ini` |
| Key | `config.ini [Server] api_key`, read at `main_kiosk.py:301` | same |
| Start mechanism | **scheduled task `ShiftKiosk`**, trigger at logon, `Delay=PT1M`, `RestartCount 3`, principal `-GroupId "S-1-5-32-545"` (BUILTIN\Users) `-RunLevel Highest`, host `pythonw.exe` | `install_kiosk.bat:131-141`, `Deploy-Application.ps1:165-173` |
| Update mechanism | **none** — manual re-run of the installer | `DEPLOY_AGENT_LAPTOPS.md:210, 318-320, 345`; no updater code exists (§2.1) |
| Inventory | **none** — the per-laptop record table is empty | `DEPLOY_AGENT_LAPTOPS.md:482-495` |

### 7.2 Cheapest realistic way to reach 60 laptops

The repository shows **only** the attended, admin-required installer. **NOT FOUND:** any
evidence of Intune, SCCM, GPO or any management agent — searched `DEPLOY_AGENT_LAPTOPS.md`,
both installers and the client for `domain`, `USERDOMAIN`, `azure`, `entra`, `aad`,
`dsregcmd`, `LOGONSERVER`: **no hits**. The installer uses only the local-group SID
`S-1-5-32-545` and a machine-wide Python path, i.e. nothing domain-specific.

**Decision:** do not design around a management system that is not proven to exist. Assume
one attended visit per laptop, and **make it the last one** by shipping a self-updater.

### 7.3 Decided design for the new client

The single visit must install all five of these, because none can be added later without
another visit:

1. **New server address** — the post-move hostname (§8), not the devtunnel.
2. **Per-device certificate**, generated **on the device** at install, private key in the
   **machine** store marked non-exportable (CNG); only the CSR/public part leaves. The
   shared key is not shipped. **Invisible to the user: yes** — mTLS handshakes carry no
   prompt.
3. **Silent user identity** — Negotiate/Kerberos (§9). **Invisible to the user: yes**, if
   domain-joined.
4. **Version reporting** — send `client_version` and have the server persist it
   (`server.py:966` is already accepted; the INSERT at `server.py:98-107` must store it).
5. **Self-updater** — a **second** scheduled task running as `SYSTEM` (the kiosk task runs
   as `Users`, `install_kiosk.bat:140`, so it cannot replace its own files). It polls a new
   `GET /api/client-package?current=<version>`, verifies an embedded signature against a
   public key pinned in the updater, and swaps files out of session. Signature verification
   is mandatory: the updater is a remote-code-execution channel onto 130 laptops.

Also fix in the same build: `verify=False` → `True` (`main_kiosk.py:389, 400, 417, 451, 706, 821`); make `_supervisor_exit` emit a status event (`main_kiosk.py:1105-1106`, `1395`); stop reading `api_key`.

**What Group B achieves:** real device identity, real user identity (if domain-joined),
version visibility, and the end of manual visits. **What it does not:** it cannot protect
a laptop whose local administrator is hostile, and it does not retroactively validate any
Group-A-era data.

---

## 8. Step 3 answer — Group C: the remaining 70

**Decision: STOP installing the old client now.** Reasons, all CONFIRMED:

1. Every new old-client install ships the compromised shared key — it is in git history
   (`ShiftKiosk/README.md:233`, commits `0717d87`, `560ba47`, `77304d3`), in every package,
   and made agent-readable by design (`install_kiosk.bat:115-118`).
2. Every new old-client install creates another mandatory manual visit later (§7.2), at
   roughly the cost of doing it right once.
3. It points at the devtunnel (`kiosk/config.ini`), which is being removed anyway (§8), so
   those installs need re-pointing regardless.

**What to install instead:** nothing yet — build the new client first; it is the gating
item on the whole plan. If a specific laptop needs coverage before the new client exists,
install the old client **but** record hostname, agent and date in the (currently empty)
table at `DEPLOY_AGENT_LAPTOPS.md:482-495`, and treat it as Group A.

---

## 9. Identity design and prerequisites

Evaluated against the code and the "no user burden" constraint.

| Option | Invisible to the user? | Stops impersonation? | Verdict |
|---|---|---|---|
| **Entra ID via MSAL, interactive** | **No** — sign-in prompt, possibly MFA | yes | **REJECTED** by constraint 2 |
| **Entra ID via MSAL silent / IWA broker** | yes, *if* the machines are Entra-joined and the broker is available | yes | **REJECTED as primary** — Entra-join is NOT FOUND, and it is the heavier of the two silent options once the devtunnel is gone |
| **Negotiate / Kerberos** on a domain-joined server | **yes** — the OS presents the ticket, no prompt | **yes** — the server reads the principal instead of the body | **CHOSEN as the user layer.** Was previously impossible because Kerberos cannot traverse the devtunnel; dropping the tunnel (§10 of your plan, §8 here) removes that blocker |
| **Per-device certificate, non-exportable machine key** | yes | **no** — authenticates the *device*, not the person | **CHOSEN as the device layer** |
| **Signed requests with a client-held key** | yes | **no** — the signing key is readable, so any username can be signed | **REJECTED**: cryptographic ceremony around an unchanged trust assumption |
| **Shared key, rotated** | yes | no | **REJECTED**: changes who else can impersonate, never whether an agent can |

### 9.1 The decision

**"Device certificate always, Kerberos user identity when available."**

- **Layer 1 — device.** mTLS client certificate per laptop, non-exportable machine key,
  provisioned at install. Always present. Gives a forgery-resistant device identity, which
  is what makes the device↔user binding of §6 meaningful instead of advisory.
- **Layer 2 — user.** Negotiate/Kerberos on the new server. The server derives the
  username from the authenticated principal and **ignores `agent_username`,
  `agent_hostname`, `performed_by`, `acked_by` and `agent_id` in the body** — delete them
  from `StatusPayload` (`server.py:963-967`), `ManualActionPayload` (`server.py:969-983`)
  and `AckPayload` (`server.py:985-994`). This is the only change in the entire plan that
  *prevents* rather than *detects* impersonation.
- **Degraded mode.** If Layer 2 is unavailable, the server derives the username from the
  certificate↔user binding. That **limits** impersonation to the bound user per device and
  does not prevent a second person using an unlocked laptop. Say so plainly; do not claim
  otherwise.

**Honest limit, stated once:** even with both layers, a **local administrator** on a laptop
can use the machine certificate and, with the Windows session, act as that user. The
construct protects against the agent who copies a script, not against a hostile admin on
their own machine. That is the best achievable under "no user-visible burden".

### 9.2 Prerequisites you must confirm

| # | Prerequisite | Why it decides the design |
|---|---|---|
| P1 | Are the agent laptops **domain-joined** (AD), Entra-joined, or neither? | Layer 2 exists only if AD-joined. **NOT FOUND** in the repository (§7.2). Check: `dsregcmd /status` and `systeminfo \| findstr /i domain` on one laptop |
| P2 | Will the new server be **domain-joined**, with an SPN (`HTTP/<fqdn>`) you can register? | Required for Kerberos |
| P3 | Do home-office laptops reach the new server over **VPN with a domain path**? | Kerberos needs a KDC path. If home users have no VPN, Layer 2 works on-site only and Layer 1 carries home traffic — acceptable, but must be designed for |
| P4 | Is there any management system (Intune/SCCM/GPO) to push the new client? | Would remove the 60 manual visits. **NOT FOUND** |
| P5 | A reverse proxy terminating TLS and performing mTLS + Negotiate in front of uvicorn | uvicorn runs plain HTTP with no TLS args (`server.py:3642`); it will not do either itself |

---

## 10. Transition to the real server, and legacy visibility

### 10.1 Where the address lives, and what redirecting costs

`config.ini [Server] url`, read once at `main_kiosk.py:300`. Changing it means editing one
line in one file — but that file is on the laptop under `C:\ShiftKiosk\kiosk`, which
`BUILTIN\Users` has **RX, not write** (`install_kiosk.bat:116`), so an agent cannot do it
and an admin visit is needed. **Therefore do not plan to re-point the 60.** Instead:

### 10.2 Decided transition

1. **Stand up the new server** (real host, TLS, domain-joined, one reverse proxy doing
   TLS + mTLS + Negotiate). Generate **all new secrets on the new host** — the old key is
   in git history and on ~60 laptops and must never be copied forward.
2. **Move `ShiftKioskDB` to the new host.** One database, no split: this is the
   non-negotiable part.
3. **Keep the old instance alive as a thin forwarder, not a second brain.** Leave the
   devtunnel endpoint answering, but point the old instance's `[Database]` at the **new**
   host so both groups write to one database. Decision: forward rather than migrate the 60,
   because §10.1 shows re-pointing them costs the same visit as replacing them — so spend
   the visit once, on the Group B client.
   ASSUMPTION: the old host can reach the new host's SQL instance. If not, the old instance
   must instead proxy HTTP to the new server's legacy route; same outcome, one database.
4. **Old instance lifetime** = until the legacy-usage log (§10.3) shows **zero** use for 30
   consecutive days. Then remove the tunnel and the old instance, and only then rotate the
   legacy key out of existence. Per owner constraint 6, no earlier.
5. New kiosks talk only to the new server with cert + Kerberos.

This keeps the fail-safe rule: at no point does a legitimate old kiosk get rejected.

### 10.3 Legacy visibility — the retirement criterion

Today this is impossible: `client_version` is discarded (`server.py:966` vs `98-107`),
`ip_address` is always `""` (`server.py:1021`), and **no authentication event is logged at
all** (`verify_api_key` raises without logging, `server.py:961`). The only existing signal
is `status_log.agent_hostname` (`server.py:100`), which is client-supplied and therefore
unreliable.

**Build this first (observation only, breaks nothing).** A new table
`kiosk_client_registry`, one row per `(hostname, credential_type)`, upserted per request:

| Field | Source |
|---|---|
| `hostname` | body (legacy) / certificate CN (new) |
| `client_version` | body `client_version` (`server.py:966`) |
| `credential_type` | `legacy_key` / `session_token` / `device_cert` / `kerberos` — from `verify_api_key` (`server.py:955-962`) |
| `source_ip` | `Request.client.host` — stop passing `""` at `server.py:1021` |
| `path`, `first_seen`, `last_seen`, `request_count` | server-observed |

**Cost per request (owner constraint 7):** one in-memory dict update plus a single
`UPDATE … WHERE hostname=? AND credential_type=?` against a table with a unique index on
that pair — one indexed row touch, no scan, no join. Flush counters from memory on a timer
rather than writing per request, so `/api/status` adds **one dict write** on the hot path.
`/api/status` already performs two queries and a full `db_get_attendance()` broadcast
(`server.py:1019-1024`), so this is far below existing cost. **NOT FOUND:** existing
indexes on `status_log` — check in §13.

**Retirement criterion:** `credential_type='legacy_key'` returns zero rows with
`last_seen` inside 30 days, **and** the distinct new-path hostname count equals the
inventory count from `DEPLOY_AGENT_LAPTOPS.md:482-495` (which must be populated first).

---

## 11. Shadow mode — the contract for every enforcement change

Per owner constraint 4, every change below ships in three stages. The switch lives in
`server_config.ini` under a new `[Security]` section, read by `configparser` at startup
(`server.py:29-30`) — so **flipping it requires restarting the `ShiftKioskServer`
scheduled task**, a seconds-long restart, not a downtime window. ASSUMPTION: a restart is
acceptable; if not, add a periodic re-read of the file (small change, same file).

| Stage | Value | Behaviour |
|---|---|---|
| off | `mode = off` | no checks, no logging — instant escape hatch |
| shadow | `mode = shadow` | evaluate, **allow everything**, log `WOULD_BLOCK` |
| enforce | `mode = enforce` | deny what shadow logged as blockable |

**Shadow period: 14 days minimum**, and it must include at least one full working week plus
one weekend (kiosks behave differently at logon after a weekend).

**Log fields to review:** `timestamp`, `path`, `method`, `credential_type`, `source_ip`,
`hostname`, `agent_username`, `client_version`, `rule_id`, `would_block_reason`.

**Criterion to switch to enforce:** zero `WOULD_BLOCK` entries whose `hostname` +
`client_version` match a known legacy kiosk, for 14 consecutive days. Any single unexplained
entry resets the clock — **fail-safe means an unexplained legitimate-looking call blocks the
rollout, not the kiosk.**

**Rollback for every item:** set `mode = off`, restart the task. No database or kiosk state
changes, so rollback is clean and immediate.

---

## 12. Prioritized implementation list

Server-only, breaks-nothing items first. "Breaks installed kiosks" is answered from §2.

| # | Change | File · function · line | Breaks installed kiosks | Effort | Rollback |
|---|---|---|---|---|---|
| 1 | Read-only SQL from §13 — settle schema, KID and index questions | none (DB queries) | **no** | small | n/a |
| 2 | Confirm P1–P4 (§9.2) | none | **no** | small | n/a |
| 3 | Add `[Security] mode` flag + the shadow/enforce helper | `server.py:29-45` (config block) | **no** | small | delete flag |
| 4 | Persist server-observed facts: pass the real IP and store `client_version` | `server.py:90` `db_insert_status` signature; `server.py:98-107` INSERT; `server.py:1021` the `""` argument; `server.py:533-551` `db_init` for the new column | **no** | small | revert; column is additive |
| 5 | `kiosk_client_registry` + in-memory counters (§10.3) | new function near `server.py:501` `db_init`; upsert called from `verify_api_key` `server.py:955-962` | **no** | medium | drop table, revert |
| 6 | Log auth outcomes and `/api/session` issuance | `server.py:955-962` `verify_api_key`; `server.py:1011-1013` `get_session` | **no** | small | revert |
| 7 | Disable `/openapi.json`, `/docs`, `/redoc` | `server.py:951` — add `docs_url=None, redoc_url=None, openapi_url=None` to `FastAPI(...)` | **no** | small | remove args |
| 8 | Require auth on `/api/session` **and** gate `/dashboard` — one change | `server.py:1011-1013` `get_session`; `server.py:3635-3636` `dashboard`; new `verify_admin` near `server.py:955` | **no** | small | `mode = off` |
| 9 | Reject the **master key** in `?token=`; keep session tokens in the query | `server.py:955-962` `verify_api_key` — split credential kinds | **no** | small | `mode = off` |
| 10 | Protect `/api/agents`, `/api/teams`, `/api/agents-template`, `/ws` | `server.py:1052`, `1056`, `1639`, `1649` | **no** | small | `mode = off` |
| 11 | Scope the legacy credential to the six endpoints of §2.1; move the other 28 to `verify_admin` | decorators at `server.py:1042, 1068, 1088, 1106, 1125, 1128, 1132, 1135, 1144, 1156, 1167, 1211, 1237, 1241, 1254, 1267, 1275, 1281, 1337, 1361, 1426, 1435, 1466, 1476, 1513, 1554, 1563, 1597` | **no** | medium | `mode = off` |
| 12 | In-memory rate limiting on `/api/session`, `/api/status`, `/api/agent-lookup` | `server.py:951-953`; route decorators `:1011`, `:1015`, `:1285` | **only if misconfigured (shadow mode prevents it)** | small | `mode = off` |
| 13 | Hostname↔username binding, TOFU + approval; allow-and-log when unbound | new table + check in `server.py:1016-1021` `receive_status`; `server.py:93-97` `db_insert_status` | **only if misconfigured (shadow mode prevents it)** | medium | `mode = off` |
| 14 | Scope `/api/agent-lookup`, `/api/shift-status`, `/api/agent-location-options` to the bound username | `server.py:1285-1293`, `1295-1301`, `1303-1317` | **only if misconfigured (shadow mode prevents it)** | medium | `mode = off` |
| 15 | Reject unknown `agent_username` on `/api/status` (after auditing NULL rows) | `server.py:1016-1021`; `server.py:914-921` `get_agent_info` | **only if misconfigured (shadow mode prevents it)** | small | `mode = off` |
| 16 | Detect new-host and concurrent sessions; alert supervisors | new check in `receive_status` `server.py:1016`; reuse `send_teams` `server.py:859` / `send_email` `server.py:884` | **no** | medium | disable notification flag |
| 17 | **Fix the Pulse credential leak** — protect `/api/attendance` after the Pulse frontend sends a credential | `server.py:1060`; consumer `Frontend/src/pages/WicAttendance.tsx` via `VITE_KIOSK_API_URL` | **no** (breaks the Pulse WIC Attendance page if sequenced wrongly) | medium | `mode = off` |
| 18 | Populate the kiosk inventory | `DEPLOY_AGENT_LAPTOPS.md:482-495` | **no** | medium | n/a |
| 19 | New server: TLS + reverse proxy with mTLS and Negotiate; fresh secrets; DB move; old instance repointed to the one DB | infra; `server_config.ini [Database]`; `server.py:3639-3642` bind | **no** if the legacy route keeps answering (§10.2) | large | keep the old instance serving |
| 20 | Server derives identity from the authenticated principal; drop identity fields from the payloads | `server.py:963-967` `StatusPayload`; `:969-983` `ManualActionPayload`; `:985-994` `AckPayload`; handlers `:1016-1021`, `1069-1082`, `1089-1099`, `1107-1116` | **no** on the new route; legacy route keeps the old behaviour | large | serve both routes |
| 21 | New client: new address, device cert, Kerberos, version reporting, self-updater, `verify=True`, exit emits a status event | `main_kiosk.py:300-303, 386-389, 414-417, 450-451, 703-706, 818-821, 1105-1106, 1395`; new package in `KIOSK_PACKAGE/Files/` | **yes — by design; this is the one-time visit** | large | re-run the previous installer per laptop |
| 22 | Retire the legacy route and rotate the old key — only at zero legacy use for 30 days | `server.py:31`, `955-962`; `server_config.ini`; `KIOSK_PACKAGE/Files/config.ini` | **yes if done early — gate on §10.3** | small | re-enable the legacy dependency |

Items 1–11 and 16–18 are server-only or documentation-only and **break no installed
kiosk**. They remove every capability in §4 except impersonation on the six kiosk
endpoints. Item 21 is the only one that fixes identity, and item 17 is the most urgent
single fix because of §5.2.

---

## 13. NOT FOUND — and the exact read-only checks

None of these can be settled without querying the running system. **Run these yourself; I
ran nothing.**

| # | Item | Why it cannot be settled from source | Exact read-only check |
|---|---|---|---|
| N1 | DDL for `agents`, `status_log`, `manual_actions` — so the true `SELECT *` exposure | Not in the repository: `db_init()` only ALTERs tables it assumes exist (`server.py:501-562`); `db/` holds one view | `SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME IN ('agents','status_log','manual_actions') ORDER BY TABLE_NAME, ORDINAL_POSITION;` on `ShiftKioskDB` |
| N2 | `vw_current_status` definition (read at `server.py:118, 131`) | Not in the repository | `SELECT OBJECT_DEFINITION(OBJECT_ID('vw_current_status'));` |
| N3 | **Do `agents.primary_kid` / `secondary_kid` hold data?** Decides whether the anonymous `/api/agents` leaks Pulse rule-(b) and rule-(a) credentials (§5.2) | `import_agents_from_excel` never writes them (`server.py:782-787`); another path may | `SELECT COUNT(*) AS total, COUNT(primary_kid) AS with_primary, COUNT(secondary_kid) AS with_secondary FROM agents WHERE active=1;` |
| N4 | **Does any `AppUserRoles` KID (DEV/RTM) appear in the kiosk `agents` table?** If yes, the anonymous endpoint leaks a privileged Pulse login | Two separate databases; needs a cross-DB comparison | `SELECT a.id, a.full_name FROM ShiftKioskDB.dbo.agents a JOIN GSDDashboard.dbo.AppUserRoles r ON r.Kid IN (a.primary_kid, a.secondary_kid, a.employee_id) WHERE r.IsActive=1 AND r.Kid<>'';` |
| N5 | Indexes on `status_log` — needed to prove the §10.3 upsert is cheap | No DDL in the repository | `SELECT i.name, i.type_desc, c.name AS col FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=i.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID('status_log');` |
| N6 | Which hostnames and builds are actually installed | No inventory (`DEPLOY_AGENT_LAPTOPS.md:482-495` empty); `client_version` discarded | `SELECT agent_hostname, COUNT(*) n, MIN(agent_timestamp) first_seen, MAX(agent_timestamp) last_seen FROM status_log GROUP BY agent_hostname ORDER BY last_seen DESC;` — observational and client-supplied, so treat as a lower bound |
| N7 | Rows with `agent_id IS NULL` — the blast radius of item 15 | Needs data | `SELECT COUNT(*) , MIN(agent_timestamp), MAX(agent_timestamp) FROM status_log WHERE agent_id IS NULL;` |
| N8 | **The tester's entries** — forged rows from the test | Identifiable only by a username/hostname mismatch | `SELECT id, agent_username, agent_hostname, status, note, agent_timestamp FROM status_log sl WHERE NOT EXISTS (SELECT 1 FROM agents a WHERE a.windows_username=sl.agent_username AND sl.agent_hostname LIKE '%'+a.windows_username+'%') ORDER BY agent_timestamp DESC;` plus `SELECT * FROM manual_actions ORDER BY performed_at DESC;` — then match against the test window |
| N9 | **P1: are the laptops domain-joined / Entra-joined?** Decides the whole identity design | No domain/Entra evidence anywhere in installer, client or deployment docs (§7.2) | On one laptop: `dsregcmd /status` and `systeminfo \| findstr /B /C:"Domain"` |
| N10 | P4: does Intune/SCCM/GPO exist to push the client? | NOT FOUND in the repository | `gpresult /r` on one laptop; check the Intune/MECM admin portal |
| N11 | Whether the devtunnel is reachable from outside the corporate network | Tunnel configuration is out of scope by instruction and was not inspected | Resolve and request the tunnel URL from an off-network connection |
| N12 | Whether a standard agent can **write** to `C:\ShiftKiosk` | Installer grants `Users` RX only (`install_kiosk.bat:116`); inherited ACLs are not visible in source | `icacls C:\ShiftKiosk` on an installed laptop |

---

## 14. Data-protection checklist (not legal advice)

**Rotate** — all on the new host, never copied forward:

| Secret | Why | Source |
|---|---|---|
| `api_key` | in git history (`0717d87`, `560ba47`, `77304d3`), in every package, agent-readable on ~60 laptops, used in the tester's curl | `README.md:233`; `install_kiosk.bat:115-118` |
| The session-token signing key | it **is** `API_KEY` (`server.py:53`) — give it its own secret so the two rotate independently | `server.py:52-55` |
| `[Email] sender_password` | a mailbox password in plaintext in the same file | `server.py:43` |
| `teams_webhook_url` | a bearer-equivalent capability URL in the same file | `server.py:35` |

Per owner constraint 6: **do not rotate the kiosk key until §10.3 shows zero legacy use.**
Treat the current value as compromised in the meantime.

**Clean / annotate:**

- The tester's forged rows (N8). **Annotate, do not silently delete** — there is no deletion
  audit trail (no delete endpoint exists for `status_log` or `manual_actions`), so a quiet
  delete is itself untraceable. Record the row ids and the decision.
- If the test produced `SICK_LEAVE`/`EMERGENCY` events, Teams cards and e-mails naming a
  real person were sent (`server.py:1025-1038`). Correct the record with the recipients.
- Preserve a copy of `server/server.log` before any cleanup — it is append-only with no
  rotation (single `FileHandler`, `server.py:20`) and holds the test's traces.
- **Define a retention period.** There is no purge job or retention setting for
  `status_log` or `manual_actions` anywhere, and `/api/audit-log` has no date filter at all
  (`server.py:490-496`) — unbounded sick-leave history is the underlying exposure.

**The tester's copies:** he holds the plaintext client, the shared key, the
`/openapi.json` dump, and exports containing names, usernames, hostnames, e-mail addresses
and `SICK_LEAVE` per person. Agree in writing what he retains, for how long, where it is
stored, and when it is destroyed — and keep the report itself, since it is the evidence
that the test was authorised.

**Inform:**

| Stakeholder | What they need to know |
|---|---|
| **Data protection officer / Betriebsrat** | Attendance including **`SICK_LEAVE` per named employee** was retrievable, and the live version was readable **with no credential at all** (`/api/attendance`, `/ws`). Sick-leave data is special-category under GDPR Art. 9; attendance monitoring is co-determination-relevant in Germany. This is the item with a possible reporting clock. |
| **The impersonated supervisor** | A shift was started in his name during an authorised test, and the audit log cannot distinguish forged from genuine manual actions (`server.py:974-983`). |
| **Pulse / application owner — urgently** | §5.2: the anonymous kiosk endpoint yields a working Pulse login for every agent. Fix the leak **before** setting `EnforceAuthorization: true`, or the flag gives false assurance. |
| **All ~130 agents** | Scope reachable: name, Windows username, employee id, KIDs, both e-mail addresses, hostname, presence times, work location, free-text notes, sick-leave status. |
| **Team leads / RTMs** | Attendance and worked-hours data from the legacy period is **not** reliable evidence about an individual (§6.1). Do not use it in a performance or disciplinary context. |
| **Security / IT** | N11 — whether the tunnel is externally reachable. It sets the severity of every unauthenticated finding. |
