# OPERATIONS — build, deploy, monitor, recover

**Verification basis:** both repositories, their scripts, and the recorded live-state documents, read
**read-only** on **2026-10-11**. Deployment procedures are quoted from the documents that own them;
this document summarises and cross-links rather than replacing them.

**Authoritative sources:** `documentation/LIVE_DEPLOY_RUNBOOK.md` (server/Pulse sequence),
`DEPLOYMENT_AND_VERIFICATION.md` (verification rules), `documentation/KIOSK_HARDENING_DEPLOY_NOTE.md`
(current kiosk live state), `C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md` (client rollout).

> **Nothing in this document was executed.** No deploy, restart, profile change, enrolment or
> database write was performed while writing it.

---

## 1. The services, and what supervises them

| Service | Port | Launched by | Supervised by |
|---|---|---|---|
| WorkForce Pulse backend | 5000 | scheduled task → watchdog script | its own watchdog, plus an independent 5-minute health check |
| Pulse dev tunnel | — | separate scheduled task and its own watchdog | independent of the backend, so a backend restart does not drop the tunnel |
| ShiftKiosk server | 8000 | scheduled task → watchdog script | its own watchdog, plus the same 5-minute health check |
| ShiftKiosk dev tunnel | — | scheduled task | — |
| NG kiosk broker (per agent machine) | named pipe | scheduled task, **SYSTEM**, at startup | Windows Task Scheduler restart policy |
| NG kiosk UI (per agent machine) | — | scheduled task at logon, limited user | — |
| Legacy kiosk (per agent machine) | — | scheduled task at logon | — |

**Why two watchdogs matter:** the ShiftKiosk server is protected by both its own task watchdog and the
independent installer, so a `/health` 200 proves only that something is listening — not that the
deploy is correct. This is a recorded lesson, not a theory.

---

## 2. The deploy sentinel — the most important operational rule

**File:** `C:\HealthCheck\DEPLOY_IN_PROGRESS`

- While the file exists and is **younger than 30 minutes**, the watchdogs log `DEPLOY PAUSED` and
  refuse to start or restart the backend.
- If it is **30 minutes or older**, the watchdog treats it as stale, deletes it and resumes. A
  forgotten sentinel therefore cannot leave a service down forever.
- **Never remove the sentinel until every route has passed — not just `/health`.** Health answers
  before the database, the proxy and the authenticated routes are known good.

The canonical build-and-verify script creates the sentinel first, wraps the entire build in a
`try/finally`, stops its own temporary verification server **before** removing the sentinel, and only
then exits — the order matters, because removing the sentinel while a process still holds the port
makes the watchdog launch into a port conflict.

---

## 3. Building and deploying Pulse

### 3.1 Frontend

```powershell
cd C:\GSDDashboard_secure\Frontend
npm run build
```

The build writes directly into `Backend/wwwroot`. **No backend restart is needed for a
frontend-only change** — the server reads that folder from disk.

Verification is not optional and is not done on the development server:

1. Confirm the new bundle name appears in `Backend/wwwroot/assets/`.
2. `http://localhost:5000/` returns 200.
3. Fetch the **served** bundle and confirm the expected new text is present and the old text is absent.
4. Tell the user to hard-refresh — browsers cache the old bundle and will happily show the old UI.

### 3.2 Backend

```powershell
cd C:\GSDDashboard_secure\Backend
dotnet build --configuration Release
```

The documented sequence for a full deploy is: create the sentinel → stop the running process by
comparing exact command lines (never by image name, because this machine also hosts ShiftKiosk) →
copy → verify all routes → remove the sentinel → let the watchdog bring the final process up.

**Facts that must be respected every time:**

- The build source is the **working tree**, not `git HEAD`. `HEAD` can lag what is live.
- `C:\GSDDashboard` is a **different repository** with older sources; building it produces a different
  binary.
- The live binary was built with **.NET SDK 10.0.204**. Check `dotnet --version` before claiming a
  rebuild matches.
- The automated promotion script's `Promote` mode **fails its pinned-hash check**, so promotion is
  done by hand; its `Rollback` mode does work.
- The excluded-test-account list must **never be empty** — an empty collection breaks an EF query at
  runtime. Keep at least one sentinel entry.

### 3.3 Verification rules that exist because of real incidents

| Rule | Why |
|---|---|
| The tunnel, not the dev server, is what the user sees | Frontend changes were repeatedly declared done while the user saw the old UI |
| A 200 from `/health` is not proof | It answers before the database and the authenticated routes |
| Exercise the UI, the proxied routes and the authenticated API separately | Each can fail independently |
| Hard-refresh after every deploy | Cached bundles mask the new build |

---

## 4. ShiftKiosk operations

### 4.1 Restarting the server

Run only through its scheduled task — **never start the server manually**, because a manual start
leaves an orphan holding port 8000. The documented procedure is: end the task, start the task, wait
about 40 seconds (the watchdog's 30-second start delay plus boot), then call `/health`.

### 4.2 Flag profiles — staged enforcement

The nine security flags are set by profile (`dan0` … `dan3`) through the profile tool, which writes a
config backup before changing anything. Rules learned operationally:

- Invoke the tool with PowerShell 7, `-NoProfile`, an **absolute** path, and `-File`.
- After a server restart, wait about **20 seconds** before running the live verification even if the
  tool returns sooner.
- Avoid pasting multi-line conditional fragments into a console; use complete blocks.
- Never paste a key into a log or a document.

Current and planned profiles:

| Flag | Tracked profile | Live configuration |
|---|---|---|
| `docs_disabled` | enforce (dan1) | enforce |
| `legacy_scope` | enforce (dan1) | enforce |
| `key_in_query` | enforce (dan1) | enforce |
| `device_binding` | shadow (dan1, dan2, dan3) | **enforce** |
| `anon_identifiers` | shadow → enforce at dan2 | shadow |
| `session_auth` | shadow → enforce at dan3 | shadow |
| `supervisor_auth` | shadow → enforce at dan3 | shadow |
| `rate_limit` | shadow | shadow |
| `anomaly_detection` | shadow | shadow |

> **The live configuration does not match any tracked profile.** Device binding, recorded as
> observation-only with a fourteen-day promotion gate, is **enforcing in production**, and it **stays
> enforcing**: this is the owner's decision of 2026-10-11, and nothing is changed in code or
> configuration. The tracked profiles (`dan1.ini`, `dan2.ini`, `dan3.ini`) are the side that must be
> corrected. Read the live file, not the profile file, before making any statement about what is
> blocking. See [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) items **S17** and **C26**.

Promotion of the shadow flags requires **14 consecutive days without a kiosk-related would-block
event**. `rate_limit` stays in shadow until then; `anomaly_detection` is observational by design and
never blocks. There is no `dan4` and no `kiosk_min_data` flag — an apparent configuration key for the
latter exists in a test configuration but has no implementation and is silently ignored.

### 4.3 Building a kiosk package

The builder is the single script for this purpose, and it **forces** its output folder — a package
cannot be written into the repository or into a synced cloud folder.

```powershell
& C:\ShiftKiosk_secure\tools\build_kiosk_package.ps1 -Hostname <HOST> -Production
```

Update-only variant for a machine that already runs NG (keeps its existing token):

```powershell
& C:\ShiftKiosk_secure\tools\build_kiosk_package.ps1 -Hostname <HOST> -Production -NoEnroll
```

The builder:

1. refuses an invalid, duplicate or server hostname;
2. takes a fresh backup to the data-protection folder;
3. enrols the device (atomic: one active device per hostname) and writes the token into the package;
4. copies the client **only** from the repository's `kiosk-ng` folder;
5. rewrites the package configuration to device-only — no shared key, fallback disabled — and
   **aborts the build if any package file still contains the shared key**;
6. verifies the token, appends the computer to the distribution list, and prints the destination.

A convenience wrapper exists for the operator (a desktop shortcut and a batch file) which calls the
builder and, before adding the package to the list, compares **the SHA-256 of every file** against the
template. That check exists because of a recorded incident: a batch of packages was built from a
client file that had already been superseded in the template, so three packages shipped an older
client. If a file differs from the template the wrapper stops and writes nothing.

### 4.4 Installing on an agent laptop

1. Before anything else: the laptop's user must already exist as an **active agent** on the kiosk
   server, and the Windows account name must match. Otherwise the kiosk starts and every button reports
   "not registered", with no startup error.
2. Transfer the **whole package folder** to any location the operator can write, e.g. Downloads.
   **Never a OneDrive-synced desktop** — files there carry reparse attributes and a stale copy is
   indistinguishable from a fresh one. Verify the byte size after every transfer, never just existence.
3. In **administrator Windows PowerShell 5.1**, run the package's single command from that folder.
4. Exit codes: **0** done · **1** NG not installed and the old kiosk untouched · **2** NG is running
   but some old item could not be removed (close the program holding the file and re-run).
5. On success, delete the package folder and have the agent launch the new shortcut or sign out and
   back in — the at-logon trigger is not retroactive, so a restart alone does not start the kiosk.

The command: repairs the package permissions, installs Python only if missing, installs or updates NG
(code read-only for users, token readable only by SYSTEM/Administrators, broker as a SYSTEM task, UI
task at logon), verifies the broker answers with the device token, then removes the old client —
locating the legacy key only by its SHA-256 fingerprint and never printing it. It refuses to run on
the server or development machine and on a computer whose name does not match the package. Re-running
it is safe: it keeps the installed token, refreshes the code and tasks, and finds nothing to clean.

**Removing NG:** an uninstall batch file on the target, and a device revocation on the server.
**Revoking a device does not uninstall an old client** — an old client can still use the shared key,
so the task must be removed separately.

---

## 5. Backup

| What | How | Where |
|---|---|---|
| Pulse database | Verified backup through the repository's backup script | The SQL Server instance's default backup folder |
| Kiosk database | `sqlcmd` with the database-name variable and `-b` | the same backup folder |
| Code and configuration | Code snapshot plus the recorded rollback snapshot directory | protected directories outside the repositories |
| Roster batch overwrites | Automatic: the batch's snapshot column | inside the database |
| Deploy state | The sentinel plus the deploy script's `finally` block | — |

**Hard-won backup rules:**

- `-b` is mandatory with `sqlcmd`. Without it, `sqlcmd` returns success even when the command failed.
- The database-name variable is mandatory; `sqlcmd` refuses to run a script with an undefined variable.
- The backup script **must not** use compressed backups — SQL Server Express does not support them, and
  in an earlier version the request was silently ignored while the script still printed success. It now
  verifies the backup and fails loudly.
- Do not try to pass a Windows backup directory as a `sqlcmd` variable: the value is split on the colon
  and every Windows path is rejected.
- A database backup is **not** a code snapshot and neither of them is a rollback.
- Backups and the data-protection folders must not sit in a synced cloud folder.

**A second instance of the kiosk server may only be started against the test database, and only with
notifications disabled** so that a test cannot send messages to real people. Never start a second
instance against the live database.

---

## 6. Rollback — four separate operations

| Failure | Recovery | What it does **not** cover |
|---|---|---|
| A profile is too strict | The profile tool's rollback restores the previous config backup | not code, not the database, not Pulse |
| Bad server code | Restore the recorded code snapshot under an approved procedure | not config, not the database |
| A schema change must be undone | Revert the dependent code first | rollback SQL is not authorised casually |
| Supervisors cannot sign in | Break-glass server-side, not a configuration switch | the `Auth:SupervisorMode` switch these rows name is **NOT IMPLEMENTED IN CODE as of 2026-10-11** (no reader in any `.cs` file); does **not** disable the always-on proxy role gate |
| Pulse is broken | Restore the known-good build and configuration | reverting the supervisor mode does not turn off the proxy role gate |
| An NG client must be removed | The uninstall batch file; the legacy client can be restored separately | an old task removed during replacement must be restored explicitly |

---

## 7. Monitoring

| Signal | Meaning | Where |
|---|---|---|
| Backend watchdog log | start, exit, restart loop | the protected watchdog log directory |
| `/health` on 5000 and 8000 | process is listening | direct call |
| Independent 5-minute health check | after 3 consecutive failures it restarts the owning task | its own log |
| Kiosk observation log | one row per request: credential kind, host, path, status, decision, rule | `sec_observation_log` |
| Kiosk registry | which computers use which credential kind and client version | `sec_kiosk_registry` |
| Anomalies | unknown username, new host for a user, host/user not allowed | `sec_anomalies` |
| Authentication events | device enrolment and revocation | `sec_auth_events` |
| Manual actions | every supervisor manual check-in/log-out with actor and role | `manual_actions` |

**Reading the observation log correctly:** `allow`, `would_block` and `block` are different things. A
`would_block` row means a shadow-mode rule *would* have refused the request and the request was let
through anyway. Counting `would_block` rows as blocked requests is the most common misreading of this
data.

**Known monitoring gaps:** the device-listing command does not populate `last_seen`, so device activity
must be read from the observation log; the tunnel watchdog's health probe follows redirects, so a
redirect to a sign-in page is read as healthy; and the `alerted_at` column on anomalies is never
written, so nothing escalates automatically.

---

## 8. Known operational faults and their workarounds

| Fault | Effect | Workaround |
|---|---|---|
| Automated Pulse promotion fails its pinned-hash check | `Promote` mode cannot be used | Promote by hand: sentinel, stop, copy, verify all routes, remove sentinel, watchdog |
| A second manual backend instance occupies the port | watchdog-spawned children fail with an address-in-use error that looks like a permission error | Stop the manual instance; require that the watchdog parent is the owner |
| `/health` comes up before the database | a green health check with a broken application | Verify the UI, the proxy and an authenticated route as well |
| A suspended laptop did not auto-log out | elapsed-time timers do not count suspend time | Fixed in the NG client with a wall-clock deadline; requires installing the new package on that machine |
| Git or Bash fails with a Cygwin fork error | mandatory ASLR policy breaks the shell's fork | Run the provided ASLR-exclusion script as administrator; re-run after a policy sync |
| The kiosk shows a language placeholder | the settings section is missing from a re-deployed config | Restore the language setting |
| Agent absent from the dashboard | the Windows user name does not match the agent row | Correct the mapping in the agent list |
| Windows `-File` invocation of the profile tool does not work | the tool silently does not apply | Use PowerShell 7 with `-NoProfile -File` and an absolute path |
| A backup "succeeds" but nothing is written | compressed backup silently unsupported | The script now verifies and fails; never re-add compression |
| The kiosk attendance page shows an error status | the proxy could not reach the kiosk server | The status distinguishes timeout (504), outage (503) and a malformed feed (502); fix the cause, do not treat it as empty data |

---

## 9. Definition of done (the short version)

A change is done when **all** of the following are true:

1. The build artefact that the user actually receives contains the change.
2. The served application — not the development server — was exercised.
3. The specific route or page the change affects was verified, plus one authenticated route.
4. The sentinel is gone and the watchdog owns the running process.
5. For a kiosk change, the package was built from the repository client folder, hash-verified, and the
   client version on the target machine was confirmed in the data.
6. Anything that could not be verified is stated as unverified rather than assumed.
