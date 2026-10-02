# ShiftKiosk Hardening — Deployment Note

Branch: **`security/kiosk-hardening`** in two worktrees —
`C:\ShiftKiosk_secure` (kiosk server + new client) and `C:\GSDDashboard_secure`
(Pulse + this documentation). **Nothing was deployed. Nothing in your working tree
was touched.** All testing ran against `ShiftKioskTest` and `GSDDashboardTest` on
`.\SQLEXPRESS` and against test instances on ports 8011/8021-8031 and 5055.
`ShiftKioskDB` and the live server on port 8000 were never written to.

> One incident to know about, reported in full: `Backend/schema.sql` contains
> `USE GSDDashboard;` on line 16, so it **always targets the live database
> regardless of `sqlcmd -d`**. I ran it once before noticing. It contains no
> DROP/DELETE/UPDATE/ALTER — only CREATE and one guarded INSERT — every statement
> failed harmlessly against existing objects, and I verified afterwards:
> WicLocations 43, Employees 128, ShiftEntries 41931, AppUserRoles 9, and **no
> table created or modified in the preceding 3 hours**. No data changed. A
> retargeted copy is committed as `tests/pulse_test_schema.sql`. Treat the
> original as a footgun.

---

## 1. What this changes, in one paragraph

The kiosk server gets a security layer governed by ten independent flags, each
with three modes (`off` / `shadow` / `enforce`). With every flag `off` the server
behaves exactly as it does today. Four flags are recommended at `enforce` on day
one because the call inventory **and** the replay harness prove they cannot touch
the six endpoints the 60 installed kiosks use. Six stay at `shadow`. Supervisors
(Team Lead, RTM, Developer) get real accounts with password + TOTP; Pulse stops
accepting a bare KID or employee number. A new kiosk client installs side by side
with the old one and derives the agent's username from the operating system
instead of the request body.

---

## 2. Deployment steps, in order

Do these in this order. Steps 1–3 are reversible in seconds; step 4 onwards
changes behaviour.

### Step 0 — Back up the live database (you run this)

```powershell
sqlcmd -S .\SQLEXPRESS -i C:\ShiftKiosk_secure\tools\backup_live_db.sql
```

Writes a `COPY_ONLY` full backup to `C:\SQLBackup\` and runs
`RESTORE VERIFYONLY`. **Do not continue until this prints `RESTORE VERIFYONLY
passed.`**

### Step 1 — Apply the additive migration

```powershell
sqlcmd -S .\SQLEXPRESS -d ShiftKioskDB -i C:\ShiftKiosk_secure\sql\001_security_additive.sql -b
sqlcmd -S .\SQLEXPRESS -d ShiftKioskDB -i C:\ShiftKiosk_secure\sql\002_seed_user_hosts.sql -b
```

Adds nine `sec_*` tables and five columns (`status_log.client_version`,
`status_log.device_id`, `manual_actions.actor_account/actor_role/actor_ip`).
Nothing is dropped or altered. Verified idempotent — a second run prints
`already present` for every object and changes nothing.

`002` seeds the known user/host map from existing history so the "new host for
this user" check is not one big alarm on day one. It produced 62 pairs on the
test copy.

**Rollback:** `sql\001_security_additive_rollback.sql`. Verified: drops exactly
what it added, leaves `status_log` back at its original 17 columns with all rows
intact, idempotent on a second run. **But read §7 before running it — the flags
are the real rollback; this script is for removing the schema.**

### Step 2 — Deploy the server code with every flag OFF

Copy `C:\ShiftKiosk_secure\server\security\` and the patched `server.py` into
`C:\ShiftKiosk\server\`, then add this to `server_config.ini` and restart the
`ShiftKioskServer` task:

```ini
[Security]
docs_disabled     = off
legacy_scope      = off
key_in_query      = off
anon_identifiers  = off
session_auth      = off
supervisor_auth   = off
kiosk_min_data    = off
device_binding    = off
rate_limit        = off
anomaly_detection = off
```

With all ten `off`, the only behaviour change is that `status_log.ip_address` and
`client_version` are now filled instead of being discarded. Confirm the kiosks are
happy, then move on.

The server diff is **five touch points** in `server.py`: the import + `install()`
call, `db_insert_status`, `receive_status`, the two anonymous endpoints, and the
manual actions. Config and log paths became overridable (`SHIFTKIOSK_CONFIG`,
`SHIFTKIOSK_LOG`) with the production paths as defaults, so a test instance can
never read the live config.

### Step 3 — Switch observation on (still blocks nothing)

```ini
anomaly_detection = shadow
```

Leave it for a day. Then check what you have:

```powershell
cd C:\ShiftKiosk_secure
python tools\admin_cli.py --config C:\ShiftKiosk\server\server_config.ini --i-mean-production legacy-report
```

This is your **kiosk inventory** — the thing that does not exist today. It lists
every hostname still using the legacy credential, its `client_version`, request
count and last-seen time. It is also the criterion for §8.

### Step 4 — Turn on the four safe enforcements

```ini
docs_disabled     = enforce
legacy_scope      = enforce
key_in_query      = enforce
anon_identifiers  = enforce
```

| Flag | What it does | Why it is safe on day one |
|---|---|---|
| `docs_disabled` | `/openapi.json`, `/docs`, `/redoc` → 404 | No kiosk build, current or historical, ever fetches them |
| `legacy_scope` | the legacy credential may call ONLY the six kiosk paths | The six are the complete inventory (§3); the other 28 routes are never called by a kiosk |
| `key_in_query` | the **master key** is refused in `?token=`; session tokens still work there | Every kiosk call is header-only. Nine dashboard download buttons use `?token=` with a *session* token and keep working |
| `anon_identifiers` | `employee_id`, KIDs and e-mail addresses are stripped for unauthenticated callers of `/api/agents`, `/api/attendance`, `/ws` | This is the Pulse credential leak. Verified: the anonymous `/api/attendance` response no longer contains `employee_id` or `windows_username` |

**Before this step**, run the replay harness against the production server once in
`shadow` to confirm your real kiosks look like the recorded ones:

```powershell
python C:\ShiftKiosk_secure\tools\replay_harness.py --base http://localhost:8000 --key <legacy key> --expect-scope shadow
```

### Step 5 — Create the supervisor accounts

```powershell
cd C:\ShiftKiosk_secure
set SHIFTKIOSK_CONFIG=C:\ShiftKiosk\server\server_config.ini

python tools\admin_cli.py --i-mean-production breakglass

python tools\admin_cli.py --i-mean-production account create --username n.stojnic  --name "Stojnic Nebojsa"       --role DEV       --kid S69307
python tools\admin_cli.py --i-mean-production account create --username s.angelova --name "Silvija Angelova"      --role RTM       --kid S60028
python tools\admin_cli.py --i-mean-production account create --username y.qiang    --name "Yiting Qiang"          --role RTM       --kid Y6525
python tools\admin_cli.py --i-mean-production account create --username t.rossberg --name "Tobias Rossberg"       --role TEAM_LEAD --kid T31561 --team-leader "Tobias Rossberg"
python tools\admin_cli.py --i-mean-production account create --username d.panaitescu --name "Delia Panaitescu"    --role TEAM_LEAD --kid M73784 --team-leader "Delia Panaitescu"
python tools\admin_cli.py --i-mean-production account create --username o.schleusen --name "Oliver Schleusen"     --role TEAM_LEAD --kid O7164  --team-leader "Oliver Schleusen"
python tools\admin_cli.py --i-mean-production account create --username k.coric    --name "Karlo Coric"           --role TEAM_LEAD --kid K33261 --team-leader "Karlo Coric"
python tools\admin_cli.py --i-mean-production account create --username i.ciuceanu --name "Ion Ciuceanu"          --role TEAM_LEAD --kid I15078 --team-leader "Ion Ciuceanu"
python tools\admin_cli.py --i-mean-production account create --username j.brzeszkiewicz --name "Jaroslaw Brzeszkiewicz" --role TEAM_LEAD --kid J51904 --team-leader "Jaroslaw Brzeszkiewicz"
```

**That is 9 accounts plus break-glass = 10.** The KIDs come from your live
`AppUserRoles` (9 active rows, read-only, 2026-10-02). Note that
`documentation/auth_rbac.md` is **stale**: it lists the six TEAM_LEAD rows as
placeholders with `Kid=''` and `IsActive=0`; they all have real KIDs and are
active now.

Each `create` prints the initial password, the TOTP secret (plus an `otpauth://`
URI and an ASCII QR when the optional `qrcode` package is present) and ten
single-use recovery codes — **once**. Hand each person their own. First sign-in
forces a password change.

**You now own these secrets and the reset process:**

| Need | Command |
|---|---|
| Lost password | `account reset-password --username <u>` |
| Lost phone | `account reset-totp --username <u>` (new secret + new recovery codes, old ones void) |
| Someone leaves | `account disable --username <u>` (revokes their sessions immediately) |
| Role change | `account set-role --username <u> --role RTM` (revokes their sessions) |

The break-glass credentials are written to `C:\ShiftKiosk\server\BREAKGLASS.txt`
with the ACL restricted to Administrators + SYSTEM. **Read it once, put it in your
password manager, delete the file.**

### Step 6 — Supervisor sign-in, in shadow

Kiosk server:

```ini
session_auth    = shadow
supervisor_auth = shadow
```

Pulse (`Backend/appsettings.json` **and** `bin\Release\net8.0\appsettings.json`):

```json
"Auth": {
  "SupervisorMode": "shadow",
  "SupervisorVerifyUrl": "http://localhost:8000"
}
```

In `shadow` the old KID-only Pulse sign-in still works, and every use is logged as
`SHADOW would-deny: KID-only login accepted for …`. **Sign in yourself with your
new account and TOTP on both the dashboard and Pulse before going further.**

### Step 7 — Enforce sign-in

Only after step 6 works for you:

```ini
session_auth    = enforce
supervisor_auth = enforce
```
```json
"SupervisorMode": "enforce"
```

From this point `/api/session` and `/dashboard` require a supervisor session, and
Pulse refuses KID-only sign-in.

**Consequence you must accept:** agents lose their Pulse `/my` self-service page.
You asked for exactly three roles to have Pulse access, and this delivers that.
If you want agents back, they need accounts too — but then they are authenticating,
which contradicts the kiosk rule, so decide deliberately.

### Step 8 — Device binding and the rest, later

`kiosk_min_data`, `device_binding` and `rate_limit` stay at `shadow` until the
observation log shows a clean week. They are the only flags that can touch the
six kiosk endpoints.

---

## 3. Flags: defaults, what to watch, rollback

| Flag | Day-one mode | Can it break a kiosk? | Watch | Rollback |
|---|---|---|---|---|
| `docs_disabled` | **enforce** | no | — | `= off`, restart |
| `legacy_scope` | **enforce** | no | `rule_id='R-SCOPE'` rows with an unexpected hostname | `= off`, restart |
| `key_in_query` | **enforce** | no | `R-KEYQ` | `= off`, restart |
| `anon_identifiers` | **enforce** | no | Pulse WIC Attendance page still renders | `= off`, restart |
| `session_auth` | shadow | no | `R-SESSION` would-blocks | `= off`, restart |
| `supervisor_auth` | shadow | no | `sec_auth_events` | `= off`, restart |
| `kiosk_min_data` | shadow | **yes if misconfigured** | would-blocks on the six paths | `= off`, restart |
| `device_binding` | shadow | **yes if misconfigured** | `sec_anomalies.kind='device_binding_mismatch'` | `= off`, restart |
| `rate_limit` | shadow | **yes if misconfigured** | `R-RATE-*` | `= off`, restart |
| `anomaly_detection` | shadow | no (log-only by design; `enforce` is refused and downgraded) | `sec_anomalies` | `= off`, restart |

A flag change requires a restart of the `ShiftKioskServer` task — seconds, not a
window. There is deliberately **no HTTP reload endpoint**: a config reload
reachable over the network is a way to turn security off remotely.

**What to review in `sec_observation_log`:** `ts`, `credential_type`, `source_ip`,
`hostname`, `agent_username`, `client_version`, `path`, `status_code`, `decision`,
`rule_id`, `reason`. The criterion for promoting a shadow flag to enforce is
**zero `decision='would_block'` rows that belong to a known kiosk hostname, for 14
consecutive days including a weekend**.

---

## 4. Test laptop procedure (one page)

**On the server:**

```powershell
cd C:\ShiftKiosk_secure
python tools\admin_cli.py --i-mean-production device enroll --hostname <LAPTOP-NAME> --out C:\temp\<LAPTOP-NAME>.token.json
```

**On the laptop (as Administrator):**

1. Copy `C:\ShiftKiosk_secure\kiosk-ng\` and the token file to the laptop.
2. Edit `kiosk-ng\config.ini` → `[Server] url` = the current server address,
   `api_key` = the current legacy key (it is the **fallback**), `verify_tls = true`.
3. `install_kiosk_ng.bat C:\temp\<LAPTOP-NAME>.token.json`
4. Delete your copy of the token file.
5. Log off and back on.

The installer writes only to `C:\ShiftKioskNG` and registers two **new** tasks
(`ShiftKioskNG-Broker` as SYSTEM at startup, `ShiftKioskNG-UI` at logon). It never
touches `C:\ShiftKiosk` or the `ShiftKiosk` task, so the old kiosk keeps starting
exactly as before and both can run side by side.

**Smoke-test checklist:**

| # | Check | Expected |
|---|---|---|
| 1 | Phase 1 appears at logon | identical to the old client |
| 2 | Click ON SHIFT | Phase 2 widget appears, no prompt, no password |
| 3 | `python -c "import sys;sys.path.insert(0,r'C:\ShiftKioskNG');import ngtransport as t;print(t.diagnostics())"` | `broker: True`, `mode: device-token`, `identity: <the agent's Windows user>` |
| 4 | On the server: `SELECT TOP 5 agent_username, agent_hostname, client_version, device_id FROM status_log ORDER BY id DESC` | real username, real hostname, `3.0.0-ng`, a device id |
| 5 | Pull the network cable, click SICK LEAVE, plug back in | `broker.log` shows `queued`, then `flushed 1 queued event(s)`; the event lands in the DB |
| 6 | `Ctrl+Shift+Alt+A` | the client exits **and** an `OFF` row appears (the old client exited silently) |
| 7 | Revoke the device: `device revoke --device-id <id>`, wait 60 s, click a status | still works, on the legacy credential — an agent is never locked out |
| 8 | Reboot | both tasks come back, no crash loop in `broker.log` |
| 9 | `uninstall_kiosk_ng.bat` | `C:\ShiftKioskNG` gone, old kiosk still registered and working |

### Standard user versus administrator — the honest result

| | Standard user | Local administrator (your test laptop) |
|---|---|---|
| Read the device token | **No.** `device.token.json` is ACL'd to SYSTEM + Administrators | **Yes.** Reads the file directly |
| Use the device token | Only through the broker, only for the six paths, only as themselves | Freely, from anywhere |
| Claim a colleague's username | **No.** The broker overrides it with the OS user (verified: a client sending `ATTACKER-CHOSEN` is recorded as the real user) | **Yes.** Bypass the broker and call the server with the token or the legacy key |
| Exit without a trace | No — the hotkey now sends `OFF` | Yes — kill the process |

**State it plainly: on a laptop where the agent is a local administrator, none of
this prevents impersonation.** It is device binding plus evidence. The control is
real only for standard users. Nothing here is user authentication, and I am not
going to call it that.

---

## 5. Role → permission table

Three roles. Nobody else gets dashboard or Pulse access.

| | Team Lead | RTM | Developer (DEV) |
|---|---|---|---|
| Pulse sign-in | yes | yes | yes |
| Kiosk `/dashboard` | yes | yes | yes |
| `/api/session` | yes | yes | yes |
| Manual check-in / logout / emergency ack | yes | yes | yes |
| History, audit log, exports | yes | yes | yes |
| `/api/import-agents`, WIC-centre writes | yes | yes | yes |
| `/api/security/flags` (security screen) | **no (403)** | **no (403)** | **yes** |
| Agents (`AGENT` role) | no account, no Pulse, no dashboard — they never authenticate | | |

`sec_accounts.team_leader` is populated for Team Leads and maps to
`agents.team_leader`. **Not yet enforced as a data filter** — see §6.

Verified by test: `DEV` reads `/api/security/flags` (200); `TEAM_LEAD` gets 403.

---

## 6. What is NOT done, and why

| Not done | Why | What it needs |
|---|---|---|
| **Team Lead data scoping** (a TL sees only their own team) | The role and the `team_leader` link exist and are tested, but filtering every read endpoint by team is a change to ~15 query functions. Shipping it half-done would have been worse than shipping it next. | Apply `WHERE team_leader = @sessionTeamLead` in `db_get_attendance`, `db_get_agents`, `db_get_audit_log`, the reports and the exports; add the matrix test. |
| **Full Pulse HTTP end-to-end** | The Pulse test database would need every table the startup migrations assume (`WicOpeningHours`, `VwicRotationSlots`, `RosterBatches`, …), and building it meant either a live backup or a long schema hunt. I tested the **new integration point** instead — the real provider against the real kiosk test server, 4/4 — and the Pulse backend builds clean. | Restore `GSDDashboard` into `GSDDashboardTest`, then run Pulse against it on port 5055. |
| **Agent self-service `/my` after enforce** | Deliberate: you specified three roles only. | A decision from you, not code. |
| **Kerberos / client certificates / signed self-updater** | Not available tonight (no Entra app registration, no domain confirmation). | §9. |
| **Legacy key rotation** | Forbidden until the observation log shows no kiosk uses it. | §8. |
| **Pulse frontend authenticated path to `/api/attendance`** | The page calls the kiosk server directly from the browser. Identifiers are now stripped for it, but names and statuses are still visible anonymously. | Proxy it through the Pulse backend with the session cookie, then require a credential on `/api/attendance`. |

---

## 7. Rollback, per item

| Item | Rollback |
|---|---|
| Any flag | Set it to `off` in `[Security]`, restart the task. Seconds. |
| All new server behaviour | All ten flags `off`. The server then behaves exactly as before. |
| Server code | Restore the previous `server.py`, remove the `security\` folder, restart. |
| Pulse | `"SupervisorMode": "off"` — the KID-only path returns, `AuthService.cs` was deliberately left unchanged so this is exact. |
| Supervisor lockout | Break-glass account (§5), or set `session_auth`/`supervisor_auth`/`SupervisorMode` to `off`. Documented inside `BREAKGLASS.txt` itself. |
| Database schema | `sql\001_security_additive_rollback.sql`. **Prefer the flags** — this destroys the legacy-usage evidence and the accounts. |
| A kiosk laptop | `uninstall_kiosk_ng.bat` — removes `C:\ShiftKioskNG` and the two NG tasks; the old kiosk is untouched throughout. |
| A device token | `device revoke --device-id <id>`; the kiosk degrades to the legacy credential and keeps working. |

---

## 8. Retiring the legacy key

Run `admin_cli.py legacy-report` weekly. Rotate the key **only** when the
"LEGACY KIOSKS STILL ACTIVE" list has been empty for 30 consecutive days. Until
then the key is compromised (it is in three git commits, in every install package
and readable by all ~60 agents) and you are accepting that, knowingly.

Rotation, when the day comes: new key in `server_config.ini`, new key in the NG
`config.ini` fallback, restart. Old clients are gone by definition.

---

## 9. Authentication handover to IT later

The seam is **one interface**: `ISupervisorAuthProvider`
(`Backend/Services/SupervisorAuthClient.cs`). Implement it, register it in
`Program.cs` line 89, and **no authorization code changes**.

| Option | What IT must provide | The one code point |
|---|---|---|
| **Entra ID** | An app registration (client id, tenant id, a secret or certificate) and consent for `openid profile email`. You currently get 401 on App registrations — this is the blocker. | New `EntraSupervisorAuthProvider : ISupervisorAuthProvider`; swap the `AddHttpClient<>` line |
| **Kerberos / Windows SSO** | The laptops domain-joined, the server domain-joined with an `HTTP/<fqdn>` SPN, a reverse proxy doing Negotiate, and a domain path for home workers (VPN) | Read the principal in the proxy, pass it as a trusted header, implement the provider |
| **Client certificates** | A CA, per-device enrollment at install, a reverse proxy doing mTLS | Replace the device token check in `security/core.py:classify` with the certificate thumbprint |

The kiosk protocol does not change in any of the three: the broker already
separates "who is the device" from "who is the user", and `sec_devices` already
has the columns a certificate thumbprint would use.

---

## 10. Data-protection checklist (not legal advice)

**Rotate — but only in this order:**

| Secret | When |
|---|---|
| Kiosk `api_key` | **Not yet.** After §8. In three git commits (`0717d87`, `560ba47`, `77304d3`), in every install package, readable by ~60 agents. |
| Session-token signing key | It *is* `API_KEY` (`server.py:53`). Give it its own secret so the two rotate independently. |
| `[Email] sender_password` | Now — a mailbox password in plaintext in `server_config.ini`. |
| `teams_webhook_url` | Now — a bearer-equivalent URL in the same file. |

**Clean:**

- The tester's forged rows. Find them by the username/hostname mismatch:
  `SELECT id, agent_username, agent_hostname, status, note, agent_timestamp FROM status_log sl WHERE NOT EXISTS (SELECT 1 FROM agents a WHERE a.windows_username = sl.agent_username AND sl.agent_hostname LIKE '%'+a.windows_username+'%') ORDER BY agent_timestamp DESC;`
  **Annotate, do not silently delete** — there is no deletion audit trail, so a
  quiet delete is itself untraceable. Record the row ids and the decision.
- If the test produced `SICK_LEAVE` or `EMERGENCY` events, Teams cards and
  e-mails naming a real person were sent. Correct the record with the recipients.
- Preserve a copy of `server.log` before any cleanup — it is append-only with no
  rotation and holds the test's traces.
- **Define a retention period.** There is still no purge job for `status_log` or
  `manual_actions`, and `/api/audit-log` has no date filter at all. Unbounded
  sick-leave history is the underlying exposure.

**The tester's copies:** he holds the plaintext client, the shared key, the
`/openapi.json` dump and exports containing names, usernames, hostnames, e-mail
addresses and `SICK_LEAVE` per person. Agree in writing what he keeps, where, for
how long, and when it is destroyed. Keep his report — it is the evidence the test
was authorised.

**Inform:**

| Who | What |
|---|---|
| Data protection officer / Betriebsrat | Attendance including **`SICK_LEAVE` per named employee** was readable **with no credential at all**. Special-category data under GDPR Art. 9; attendance monitoring is co-determination-relevant in Germany. This is the item with a possible reporting clock. |
| The impersonated supervisor | A shift was started in his name during an authorised test, and the audit log cannot distinguish forged from genuine manual actions. |
| All ~130 agents | What was reachable: name, Windows username, employee id, KIDs, both e-mail addresses, hostname, presence times, work location, free-text notes, sick-leave status. |
| Team leads / RTMs | Attendance data from the legacy period is **not** reliable evidence about an individual. Do not use it in a performance or disciplinary context. |
| Security / IT | Whether the devtunnel is reachable from outside the corporate network. It sets the severity of every unauthenticated finding and I could not determine it. |
