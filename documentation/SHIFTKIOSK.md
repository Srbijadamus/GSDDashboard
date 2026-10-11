# SHIFTKIOSK — the attendance system

**Verification basis:** `C:\ShiftKiosk_secure` (working tree) and the live `ShiftKioskDB` database,
read **read-only** on **2026-10-11**. Every statement cites a file, or is a live-database aggregate
measured on that date.

**Location:** this document lives in `C:\GSDDashboard_secure\documentation` because the ShiftKiosk
repository's `documentation\` folder holds the owner's Serbian-language rollout ledger
(`KIOSK_NG_ROLLOUT.md`). Its subject is the ShiftKiosk repository. See
[PROJECT_DOCUMENTATION_INDEX.md](./PROJECT_DOCUMENTATION_INDEX.md) for the repository split.

**Related documents:** [INTEGRATION_AND_DATA_FLOWS.md](./INTEGRATION_AND_DATA_FLOWS.md) ·
[SECURITY_MODEL.md](./SECURITY_MODEL.md) · [DATA_MODEL.md](./DATA_MODEL.md) ·
[OPERATIONS.md](./OPERATIONS.md) · [GLOSSARY.md](./GLOSSARY.md)

---

## 1. What it is, in one paragraph

ShiftKiosk records where the GSD workforce is during a shift. A small fullscreen program runs on each
agent's Windows laptop. The agent picks a status (start shift, break, sick, emergency, off), and the
program sends that event to a central server on port 8000, which stores it in `ShiftKioskDB`,
broadcasts it to a supervisor dashboard, and optionally notifies by Teams or e-mail. Pulse reads the
same data through an authenticated server-side proxy, so the agent's attendance appears next to their
roster. Everything user-visible is English or German.

| Fact | Value on 2026-10-11 |
|---|---|
| Server process | FastAPI + uvicorn, port 8000, started and supervised by the `ShiftKioskServer` scheduled task through a watchdog |
| Kiosk-side agent rows | **139** (134 with an employee id, 122 with a primary KID, 128 with a Windows username) |
| Attendance events stored | **2,552** (first 2026-05-28, latest 2026-10-10) |
| Supervisor manual actions stored | **519** (331 check-ins, 188 log-outs) |
| Enrolled computer credentials | **71** (67 active, 4 revoked) |
| Computers seen by the server | **63** registry rows |
| Observation-log rows | **282,745** |
| Client versions actively sending | `2.1.1` (legacy) and `3.0.0-ng` (current) |
| Auto-logout threshold | **11 hours** from shift start |
| Language | EN / DE only |

---

## 2. The two clients

Two kiosk clients exist. **Both are in production.**

| | Legacy | NG (current) |
|---|---|---|
| Entry point | `kiosk/main_kiosk.py` | `kiosk-ng/main_kiosk.py` |
| Version string | `2.1.1` (`kiosk/main_kiosk.py:2`) | `3.0.0-ng` (`kiosk-ng/ngtransport.py:36`) |
| Credential | the shared legacy API key in `config.ini`, sent as `X-API-Key` | a per-computer **device token**, sent by the SYSTEM broker |
| Transport | direct HTTPS from the UI process | named pipe to a SYSTEM broker, which makes the HTTPS call |
| Offline queue | one JSON file next to the script, fixed 30-second retry, no de-duplication | a SYSTEM-owned durable queue with ordered flush, exponential back-off and a 5,000-event cap |
| Auto-logout clock | elapsed-time timer (does not advance while the laptop is suspended) | wall-clock deadline, checked every 15 seconds |
| Single-instance guard | mutex `ShiftKioskMutex_v21` | mutex `ShiftKioskNGMutex_v3` |
| Scheduled tasks | `ShiftKiosk` (at logon, limited) | `ShiftKioskNG-Broker` (at startup, **SYSTEM**) and `ShiftKioskNG-UI` (at logon, limited) |

**Which one ships:** the NG client. The package builder copies the client **only** from
`kiosk-ng\` (`tools/build_kiosk_package.ps1:35`), and the package's single command installs NG first
and only then removes the old client (`kiosk-ng/deploy/install_kiosk.bat:70-75`). The new-client
package deliberately contains **no legacy key** — the builder strips it and forces
`legacy_fallback = false`.

The legacy clients are nevertheless still working, which is why the shared key cannot be retired yet.
In the last 90 days the server recorded 438 events tagged `2.1.1` and 198 tagged `3.0.0-ng`.

---

## 3. The flow of one shift

Both clients present the same two screens. Phase 1 is a fullscreen status selection shown at logon;
after "start shift" it collapses to Phase 2, a small floating widget.

### 3.1 Start of shift

1. Phase 1 appears fullscreen, always on top, with no window frame and a low-level keyboard hook that
   blocks the Windows key, Alt+F4 and Alt+Tab. Four action cards are shown:
   **Start Shift / Sick Leave / Pause / Emergency**.
2. The agent picks "start shift". A work-location dialog asks **where they are working from**:
   a WIC centre, or "Virtual WIC Support" (the home-office mode).
3. The client sends `POST /api/status` with status `ON_SHIFT`, the chosen work location and, when
   applicable, the centre id.
4. On success Phase 1 closes and Phase 2 opens, carrying the shift start that the server reported.
5. On startup the client first calls `GET /api/shift-status`. **If a shift is already open, it goes
   straight to Phase 2 without sending a second start event** — this is what makes a reboot or a
   re-launch safe.

### 3.2 Break

Pause exists in both clients and on both screens. In Phase 2 the pause button **toggles**: the first
press sends `PAUSE`, the second sends `ON_SHIFT` again. In Phase 1 a pause is sent once, with no
in-place toggle. The live data confirms the status is used: 190 `PAUSE` events in the last 30 days.

### 3.3 Emergency

The agent confirms the action and can add a free-text note. The client sends `EMERGENCY`, cancels the
auto-logout timer, exits the UI and locks the workstation. The server accepts an emergency event even
when the agent identity cannot be resolved — an emergency must never be rejected.

### 3.4 End of shift

Only Phase 2 has "Off". Sending `OFF` **logs the Windows session off** (`shutdown /l`). Sickness from
Phase 2 locks the workstation instead.

### 3.5 Automatic logout

- Threshold: **11 hours** from the shift start (`AUTO_LOGOUT_SECONDS = 11 * 3600`).
- A 30-second warning countdown is shown fullscreen.
- On expiry the client sends `AUTO_LOGOUT` with the note "Auto-logout: agent did not sign out" and
  locks the workstation.
- **The clock counts from the shift start reported by the server**, not from the process start, so a
  restart does not reset the deadline.
- Shift-duration reminders appear at 3 h, 6 h and 8 h.

**A documented defect and its fix.** A shift was observed running past the threshold because the
legacy client uses an elapsed-time timer that does **not** advance while Windows is suspended: the
process had been running 4 h 22 min plus 1 h 51 min of real activity, so the timer never fired. The NG
client replaces this with a wall-clock deadline checked every 15 seconds, which fires within 15 seconds
of the laptop waking. The fix takes effect on a machine only once a package containing it is installed
there — the live data still contains NG events from before it.

**Supervisor exit** is the key combination Ctrl+Shift+Alt+A. It is hard-coded in the client, not read
from `config.ini`.

---

## 4. Offline behaviour — the queue

Attendance must not be lost when the network is down.

**Legacy client:** if the health check fails or a send does not return 200, the event is written to
`pending_queue.json` next to the script. A background thread retries every 30 seconds and re-sends
everything that succeeds. There is **no de-duplication and no event id**, so a send that actually
reached the server but whose response was lost can be inserted twice. The server performs a plain
insert with no idempotency key, so duplicate rows are possible; the "current" state is decided by the
newest row.

**NG client:** there are two queues.

- The **UI queue** lives in the agent's own profile (`%LOCALAPPDATA%\ShiftKioskNG`) and is used only
  when the broker says it did not take ownership of the event.
- The **broker queue** is the durable one (`broker_queue.json`, owned by SYSTEM). Its rules are the
  part worth knowing:
  - retry back-off from 5 seconds doubling to a maximum of 300 seconds, reset after a success;
  - flush stops at the first failure, so **events keep their original order**;
  - a permanent rejection (HTTP 400 or 422) is **dropped rather than retried**, because retrying it
    forever would block every later event;
  - the queue is capped at 5,000 events; on overflow the **oldest** event is discarded and an error
    is logged;
  - a connection error after hand-off returns "delivery unknown" instead of replaying the event, so
    the NG path does not duplicate on an ambiguous failure.

At first launch the NG client migrates **only the current user's** unsent legacy events into its own
queue, once.

---

## 5. Device identity and the broker

This is the core of the newer security model, and the honest limits are stated in §7.

**Enrolment.** An administrator runs the device-enrol command of the admin CLI with the computer's
hostname. It generates a device id and a token, stores **only the token's hash** in the database, and
writes the token to a file whose permissions allow SYSTEM and Administrators only. Enrolment is
atomic: a second active device for the same hostname is refused and nothing is changed.

**On the computer.**

- The token file is installed at `C:\ShiftKioskNG\device.token.json` with SYSTEM/Administrators-only
  permissions; its JSON fields are `device_id`, `device_token` and `hostname`, and the hostname must
  match the computer it is installed on.
- The **broker** runs as SYSTEM from a scheduled task. It loads the token, holds it in memory, owns
  the named pipe, and makes the outbound HTTPS calls. The user-facing UI never sees the token.
- The UI talks to the broker over the fixed named pipe `\\.\pipe\ShiftKioskBroker` using one JSON
  object per line. The pipe's security descriptor gives interactive users read/write and gives SYSTEM
  and Administrators full control.
- The broker **derives the agent's identity from the operating system**, by impersonating the pipe
  caller and reading the user name — it does not trust the username in the event body. This closes a
  spoofing path and has a dedicated regression test.
- **The broker is not a general proxy.** It only forwards an allow-list of GET paths
  (`/health`, `/api/agent-lookup`, `/api/today-holiday`, `/api/agent-location-options`,
  `/api/shift-status`) plus the status write. Anything else is refused.

**Fallback chain.** If a device token is present and a request is rejected with 401/403, the broker
retries once with the legacy key **only when `legacy_fallback` is enabled**. In the shipped package it
is disabled and there is no key at all, so the request simply fails; a missing token also refuses the
request. If the broker itself is unreachable, the UI falls back to a direct legacy-key call only when
fallback is enabled — and it never replays an event whose delivery is unknown. Revoking a device makes
its token unusable immediately, but **revoking a device does not uninstall an old client**, because an
old client can still use the shared key.

**Stated limitation, from the code's own comment:** a local administrator on the machine can read the
token or bypass the broker. A device token is machine hygiene, not strong user authentication.

---

## 6. What the agent sees, and what the supervisor sees

### Agent — the kiosk application

1. **Phase 1**, fullscreen: company header, a public-holiday banner when applicable, the agent's name
   and computer, the date, a pending-events counter, four action cards, and a feedback line.
2. A **work-location dialog** (WIC centre / Virtual WIC Support / cancel).
3. Confirmation and note dialogs (an emergency note is free text).
4. **Phase 2**, a small draggable floating widget: elapsed shift time, the date, and four buttons
   (Sick Leave, Emergency, Pause, Off). Clicking the clock collapses the buttons.
5. A **fullscreen auto-logout warning** with a 30-second countdown.
6. 3 h / 6 h / 8 h shift-duration reminders.

Both languages are selectable; the internal status codes are never translated.

### Supervisor — the dashboard on port 8000

The dashboard is a self-contained page served by the kiosk server itself. It has **eight views** in
three groups:

| Group | View | What it is for |
|---|---|---|
| Operations | Live (Anwesenheit) | Real-time table of all agents with check-in time, time on shift, check-out, status and notes; team-lead, status and name filters; counter tiles; manual check-in; manual log-out; emergency acknowledge |
| Operations | Shift Summary | Per-agent daily start/end/duration with the auto-logout flag, filterable by date |
| Operations | Hours Report | Days worked, total and average hours, auto-logouts, plus a Logout Audit section with self/manual/auto/open filters, threshold markers and CSV/XLSX export |
| WIC Management | WIC Coverage | A date and centre, per-agent check-in/check-out and a covered flag, Excel export |
| WIC Management | WIC Agents | MAIN / BACKUP / REGIONAL assignments, opening hours, presence indicators |
| WIC Management | WIC Attendance | Date range and centre, grouping by agent or centre, worked and break durations, CSV/XLSX |
| Administration | Import / Admin | Agent `.xlsx` upload, a template download, the agent list, and WIC-centre management |
| Administration | Audit Log | Read-only log of supervisor manual actions |

The dashboard obtains a short-lived session token from the server on page load; the master key is
never sent to the browser. Live updates arrive by WebSocket with a 60-second polling fallback.

**Honest limitation about the dashboard:** the token that grants supervisor access is issued to
anyone who can reach the server, and an anonymous caller can still use it to read history, exports and
the audit log. This is recorded in §7 and in
[OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md).

---

## 7. Security posture — what is closed and what is not

The kiosk server is governed by **nine named flags**, each of which can be `shadow` (record what it
would have blocked, block nothing) or `enforce`. The table below states the **live** state of each flag
as read from the running configuration on 2026-10-11.

| Flag | Live state | What it does |
|---|---|---|
| `docs_disabled` | **enforce** | The API documentation page is not served |
| `legacy_scope` | **enforce** | The shared legacy key may only reach the six kiosk endpoints; session tokens may not reach them |
| `key_in_query` | **enforce** | The credential may not be passed in the URL query string, only in a header |
| `device_binding` | **enforce** | Ties a device credential to one computer and one user, and rejects an unknown host. **Note: every tracked profile file and both canonical documents record this as still observing**, so the live state is ahead of the recorded promotion gate — see [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) |
| `anon_identifiers` | shadow | Would strip employee ids, KIDs and e-mail addresses from anonymous responses |
| `session_auth` | shadow | Would require a supervisor credential to obtain a session token |
| `supervisor_auth` | shadow | Would require supervisor sign-in for the dashboard |
| `rate_limit` | shadow | Would rate-limit requests |
| `anomaly_detection` | shadow | Observational only — never blocks |

**Closed.** The specific attack the 2026-10-02 review found — an anonymous caller obtaining a session
token and using it to write attendance as any agent — is closed by `legacy_scope`, which is symmetric:
the legacy key cannot call session endpoints, and session tokens cannot write status. The API
documentation page is off. Credentials cannot be placed in a URL. Device binding is blocking. And
Pulse has an authenticated, always-enforced proxy in front of the kiosk feed.

**One claim to correct.** The kiosk server's own supervisor sign-in **code** exists — accounts, password
hashing, a second factor, lockout, recovery codes and a session cookie — and the endpoint that verifies
a credential for another service is present. But the corresponding client that would call it from
Pulse **does not exist in the Pulse repository**, and no Pulse setting reads a supervisor mode. So the
supervisor path is usable on the kiosk dashboard once accounts are created, and **entirely unwired on
the Pulse side**. Do not describe supervisor sign-in as protecting Pulse.

**Still open, stated plainly.**

1. **Anonymous reading.** Anyone who can reach the server can still obtain a session token and use it
   to read history, the audit log and the exports. This is closed only at the **dan3** stage, which has
   not been applied. This is the most significant remaining exposure.
2. **The legacy key.** It is shared, it was committed to git history in three early commits, it is
   readable on machines by design, and roughly 60 legacy clients still depend on it. The retirement
   criterion is currently "no legacy inventory for 30 days"; that clock has not started.
3. **Anonymous identifiers.** Until `anon_identifiers` is enforced, anonymous responses still carry
   identifiers. Pulse now avoids this path, but the server-side control is still in shadow.
4. **Supervisor accounts.** Zero supervisor accounts exist in the database. The prepared
   password + TOTP path and the break-glass recovery codes are **not provisioned**, so the supervisor
   sign-in cannot be enforced yet.
5. **Device tokens are not user authentication.** A local administrator can read them or bypass the
   broker, as the code itself records.
6. **Team-lead scoping exists in the kiosk dashboard but not across Pulse.** Do not read the kiosk
   scoping as Pulse-wide filtering.
7. **Transport.** The server speaks plain HTTP; TLS is terminated at the tunnel, and the legacy client
   disables certificate verification on every call. The tunnel is configured to allow anonymous
   connections at the port level (the kiosk has no corporate account), so the tunnel is not an
   authentication boundary.
8. **Notifications.** Teams and e-mail notification are configured by flags and were disabled at the
   last recorded state. Any future enablement must be treated as a personal-data transfer.

None of these is a surprise: they are the recorded state, with the staged plan
(dan2 → dan3) documented in `documentation/KIOSK_HARDENING_DEPLOY_NOTE.md` and
`documentation/LIVE_DEPLOY_RUNBOOK.md`.

---

## 8. What is stored, and for how long

| Data | Where | Contains personal data? |
|---|---|---|
| Attendance events | `status_log` | Yes — agent, computer, times, status, notes, work location |
| Supervisor manual actions | `manual_actions` | Yes — the agent and the acting account |
| Agent list | `agents` | Yes — names, employee ids, KIDs, e-mail addresses |
| Enrolled computers | `sec_devices` | Yes — hostname, bound user, token hash |
| Computer inventory | `sec_kiosk_registry`, `sec_user_hosts` | Yes — hostnames, user names |
| Request observations | `sec_observation_log` (282,745 rows) | Yes — user, host, path, decision |
| Anomalies | `sec_anomalies` | Yes — user, host, detail |
| Supervisor accounts | `sec_accounts` | Yes — but **0 rows** |
| Sick status | part of `status_log` | **Special category** — the least-protected sensitive datum in the system |
| Manual check-in / log-out intent | `status_log.note` | Free text |

**No retention or deletion path is implemented anywhere**, and the observation log grows with every
request. This is an open item, not a solved one. Historical attendance is also **not valid evidence
of individual performance** — that is recorded as a data-protection position, not an assumption.

---

## 9. Failure modes worth knowing

| Symptom | Cause | Behaviour |
|---|---|---|
| Kiosk starts but every button says "not registered" | the Windows user name has no active agent row | The client resolves identity at startup and retries; it gives no startup error |
| Events accumulate and never send | tunnel down or the server URL changed | The client queues and retries; the pending counter is visible on screen |
| Dashboard shows zero rows | a SQL error in the attendance query, usually a missing column | Restarting the server lets the startup migration add it |
| Status write returns 401 | the client's credential does not match the server's | The two must be synchronised; for NG, re-enrol or re-issue the device |
| A laptop that was suspended never auto-logs out | the legacy elapsed-time timer does not count suspended time | Fixed in NG by the wall-clock deadline; the fix requires installing the new package |
| Backup script reports success but no backup exists | `WITH COMPRESSION` is unsupported on SQL Express and was silently ignored | The script now verifies the backup and fails loudly; `-b` and an explicit database name are mandatory |
| Git/Bash fails with `0xC0000142` | mandatory ASLR enforced by policy breaks the Cygwin fork used by the shell | Run the provided ASLR-exclusion script as administrator; it may need to be re-run after a policy sync |
