# GLOSSARY — WorkForce Pulse & ShiftKiosk

**Scope:** every acronym, code word and domain term used anywhere in this documentation set, in
either repository. Terms are grouped by area. A term that means two different things in this
codebase is explicitly flagged — do not silently pick one meaning.

**Verification date:** 2026-10-11. Sources are named per entry as `path:line` or as a live-database
fact. Anything not provable is marked **NIJE POTVRĐENO** (not confirmed) with what is missing.

**Related documents:** [SYSTEM_ARCHITECTURE.md](./SYSTEM_ARCHITECTURE.md) ·
[PULSE_FEATURES.md](./PULSE_FEATURES.md) · [SHIFTKIOSK.md](./SHIFTKIOSK.md) ·
[DATA_MODEL.md](./DATA_MODEL.md) · [SECURITY_MODEL.md](./SECURITY_MODEL.md)

---

## 1. Organisations, teams and locations

| Term | Expansion / meaning | Where verified |
|---|---|---|
| **GSD** | **Global Service Desk** — the E.ON customer service desk organisation this tool serves. Also used as a *domain* term: "GSD backlog", "GSD duty", `IsGSDDay`, `AgentTask = 'GSD'`, the `GsdBacklog` feature. The name is not renamed anywhere. | `documentation/PROJECT_BLUEPRINT.md:20,28` |
| **WIC** | In this system, **the customer-service centre / Kundenzentrum** — a physical walk-in location where agents serve customers in person. A `WicLocations` row, a `WIC_DUTY` shift, and the `WIC` agent group all use the same three letters. The word is never translated and never renamed in either UI language. | `ShiftKiosk_secure/README.md:130`; live DB `WicLocations` = 43 rows |
| **vWIC / VWIC** | **Virtual WIC.** An agent works from the GSD office but handles customer contacts for a WIC centre remotely; no travel. `VWIC` is both a `PrimaryRole` and an `agent_group` value, and the nav item "VWIC" is a Google-style coverage page. | `documentation/PROJECT_BLUEPRINT.md:57`; `Frontend/src/layout/navItems.ts:36` |
| **HOME_OFFICE** | The kiosk work-mode value stored in `status_log.work_location` when an agent checks in *not* physically at a centre. It is the value behind the kiosk labels **"Virtual WIC Support"** (EN) / **"Virtueller WIC-Support"** (DE) and the badge shown next to a checked-in person in Pulse. | `Frontend/src/pages/attendance/checkinStatus.ts:75`; `BLUEPRINT_SCREENS.md:222-228` |
| **VOICE** | The voice / call-taking team. Appears as an `agent_group` value in the kiosk database, as a `PrimaryRole`, and as an `AgentTask`. | live DB `ShiftKioskDB.agents.agent_group='VOICE'`; `ShiftKiosk_secure/README.md:130` |
| **BO** | **Back Office.** Internal GSD work — backlog tickets, quality work — as opposed to WIC travel. `BO` is an `agent_group` value, a Bulk RTM category, a Roster Generator fallback shift, and the name of the "BO List" page. | `documentation/PROJECT_BLUEPRINT.md:58`; `documentation/BULK_RTM.md` |
| **SSP** | **Special Support Position.** A backlog-focused role. SSP agents can be substituted into WIC duty with zero donor risk (substitution tier 2). | `documentation/overview.md:65,141` |
| **KID** | **Kundenzentrum-ID / agent identifier.** A short alphanumeric code (for example `A12345`) that is the agent's login credential in Pulse and the Windows account name on the kiosk laptop. Stored in `Employees.PrimaryKid` / `SecondaryKid` and in `AppUserRoles.Kid`. | `documentation/auth_rbac.md:45-56`; live DB `Employees.PrimaryKid` |
| **TL** | **Team Lead.** A manager of a group of agents. Six active TLs are present in the live data. | live DB, 6 distinct active `TeamLeadName` |
| **RTM** | ⚠ **Ambiguous — two unrelated meanings, both established in code.** (1) **Return to Main** — an agent coming back from WIC or another special duty into the main GSD workflow; this is the meaning on the `/bulk-rtm` page and in the `RtmEntries` table. (2) An **access role** (`AppRoles.Rtm`) with full read/write rights, identical to TEAM_LEAD. Neither is being renamed; context decides. | `documentation/PROJECT_BLUEPRINT.md:71`; `documentation/auth_rbac.md:222-227`; live DB `AppUserRoles` role=`RTM` |
| **WFM** | **Workforce Management** — the planning function (forecasting, scheduling, staffing). Used in this documentation for the planning team that owns the shift plan; there is no WFM system integration in either repository. | **NIJE POTVRĐENO as a product name** — no file in either repo defines this acronym. The planning function exists; the label is the requester's wording. |
| **NPP** | **Kernkraftwerk / nuclear power plant site.** A special WIC location category. Agents need `NppQualified = true` on `Employees` to be assigned; the assignment API returns a warning otherwise. | `documentation/PROJECT_BLUEPRINT.md:59`; live DB, 12 active employees with `NppQualified=1` |
| **NL / DE** | Country codes on `WicLocations.Country` and on location codes (`DE_Essen_BP1`, `NL_...`). Live distribution: 41 DE, 2 NL. | live DB `WicLocations` by `Country`, 2026-10-11 |
| **Bundesland** | German federal state. Used for regional (`LPH`) public-holiday matching and for distance estimation in substitution. | `documentation/BLUEPRINT_DATA_MODEL.md:39,171`; live DB, 10 distinct values |

---

## 2. Leave, absence and shift categories

These are the values of `ShiftEntries.ShiftType` (canonical constant class in the backend) and the
labels used on the Pulse pages and in Bulk RTM.

| Code | Plain-language meaning | Verified |
|---|---|---|
| **WORKING** | Normal GSD shift day. | `BLUEPRINT_DATA_MODEL.md:106` |
| **WIC_DUTY** | The agent is on WIC duty (usually on-site travel). Always compared with exact equality — never with a `Contains("wic")` substring match, because `"VWIC"` also contains `wic`. | `BLUEPRINT_DATA_MODEL.md:107,150` |
| **AL** | **Annual Leave** (paid holiday). | `BLUEPRINT_DATA_MODEL.md:108` |
| **HALF_AL** | Half-day annual leave (0.5 working day). | `BLUEPRINT_DATA_MODEL.md:109` |
| **SL** | **Sick Leave** (Krankmeldung). Also a `LeaveType` value for self-sickness (`Child` is the other). | `BLUEPRINT_DATA_MODEL.md:110` |
| **UL** | **Unpaid Leave** — one of the two unpaid types. | `BLUEPRINT_DATA_MODEL.md:111,123` |
| **OL** | **Other Leave** — the second unpaid type. | `BLUEPRINT_DATA_MODEL.md:117,123` |
| **OFF** | Day off. | `BLUEPRINT_DATA_MODEL.md:112` |
| **OFF_WEEKEND** | Weekend day off (system-generated; the Shift Plan asks for confirmation before it is overwritten). | `BLUEPRINT_DATA_MODEL.md:113`; `BLUEPRINT_SCREENS.md:79` |
| **CD** | **Compensation Day** — a day off earned by working a holiday or weekend. | `PROJECT_BLUEPRINT.md:69`; live DB `ShiftEntries.ShiftType='CD'` |
| **CO** | **Compensation Off** — legacy wording for the same idea as CD. Present in the shift-type constant list; **not present in the live data** on 2026-10-11. | `BLUEPRINT_DATA_MODEL.md:118`; live DB, no `CO` rows |
| **PH** | **Public Holiday** (national). Also a `DailyAttendance.AttendanceType` value. | `BLUEPRINT_DATA_MODEL.md:114` |
| **LPH** | **Local Public Holiday** — regional, matched against `WicLocations.Bundesland`. | `BLUEPRINT_DATA_MODEL.md:115` |
| **TRAINING** | The agent is on formal training. | `BLUEPRINT_DATA_MODEL.md:119` |
| **RESIGNED** | The agent has resigned. | `BLUEPRINT_DATA_MODEL.md:120` |
| **EMPTY** | No shift data. | `BLUEPRINT_DATA_MODEL.md:121` |
| **NIGHT** | A Bulk RTM Entry **category** for night duty. It is a Bulk RTM paste header, not a `ShiftType` value in the live data. | `documentation/BULK_RTM.md`; live DB, no `NIGHT` rows in `ShiftEntries.ShiftType` |
| **GSD / GSD Backlog** | A WIC assignment moved back to internal GSD backlog work for the day. In `WicShiftEntries` this is recorded as `IsGSDDay=1` with `IsOnSite=0`; the WIC Schedule and WIC Shifts screens render it as "GSD Backlog". | `BLUEPRINT_DATA_MODEL.md:141`; `Frontend/src/pages/WICShifts/ReassignModal.jsx:5` |

---

## 3. Attendance and kiosk status codes

Internal codes are **always English values** and are never translated; only displayed text changes.
The kiosk `status_log` uses the internal code; the Pulse/dashboard API exposes an
`attendance_status` derived from it.

| Internal code | Dashboard status | Meaning |
|---|---|---|
| `ON_SHIFT` | `ACTIVE` | Checked in and working. |
| `PAUSE` | `PAUSE` | On a break. Present in the NG kiosk client and in the live event data. |
| `SICK_LEAVE` | `SICK` | Reported sick. |
| `OFF` | `DONE` | Checked out normally. |
| `AUTO_LOGOUT` | `DONE` | Session ended automatically when the shift timer expired. |
| `EMERGENCY` | `EMERGENCY` | Emergency button pressed. |
| *(no event today)* | `NOT_CHECKED_IN` | No event received today. |

Verified: `ShiftKiosk_secure/README.md:185-192`; `ShiftKiosk_secure/PROJECT_DOCUMENTATION.md:784-791`;
live DB `ShiftKioskDB.status_log.status` (last 30 days: `ON_SHIFT`, `OFF`, `PAUSE`, `AUTO_LOGOUT`,
`EMERGENCY`, `SICK_LEAVE`).

---

## 4. Client, server and platform components

| Term | Meaning | Verified |
|---|---|---|
| **Pulse / WorkForce Pulse** | The user-facing product name of the dashboard in `C:\GSDDashboard_secure`. | `documentation/PROJECT_BLUEPRINT.md:16` |
| **GSDDashboard** | The *infrastructure* name retained on purpose: the folder, the SQL database, the scheduled task, the .NET namespace, the repository. The mismatch with "WorkForce Pulse" is intentional — renaming breaks the deploy chain. | `documentation/PROJECT_BLUEPRINT.md:16` |
| **Pulse Assistant** | The in-app question-answering assistant at `/assistant`. A keyword-scoring router over 12 in-repo domain handlers. **No external LLM and no external AI service is called.** | `documentation/overview.md`; `documentation/PROJECT_BLUEPRINT.md:305-341` |
| **ShiftKiosk** | The attendance product: a FastAPI server on port 8000 plus the desktop kiosk client on each agent machine. Own repository and own database. | `ShiftKiosk_secure/README.md:1-3` |
| **Kiosk** | The fullscreen desktop client on the agent's Windows machine. | `ShiftKiosk_secure/README.md:1` |
| **Legacy client (2.1.1)** | The older kiosk client (`kiosk/main_kiosk.py`, version `2.1.1`), authenticated with the shared legacy API key. Confirmed still sending events in the live data. | `PROJECT_DOCUMENTATION.md:411`; live DB, 438 events tagged `2.1.1` in the last 90 days |
| **NG client (3.0.0-ng)** | The newer kiosk client (`kiosk-ng/main_kiosk.py`, version `3.0.0-ng`), authenticated with a per-computer device token, with a SYSTEM broker and a durable offline queue. | `kiosk-ng/broker.py:63`; live DB, 198 events tagged `3.0.0-ng` in the last 90 days |
| **Broker** | The privileged helper of the NG client. Runs as **SYSTEM**, holds the device token, owns the named pipe `\\.\pipe\ShiftKioskBroker`, and performs the retry queue. The user-facing UI never handles the token itself. | `kiosk-ng/broker.py:59-72` |
| **kiosk_runtime** | The small runtime helper module of the NG client (`kiosk-ng/kiosk_runtime.py`) used for per-user file locations and the broker pipe client. | file exists, `kiosk-ng/kiosk_runtime.py` |
| **Watchdog** | A PowerShell loop that starts a service, restarts it if it exits, and honours the deploy sentinel. Watches the Pulse backend, the ShiftKiosk server, and the tunnels. | `documentation/PROJECT_BLUEPRINT.md:171`; `DEPLOYMENT_AND_VERIFICATION.md:146-183` |
| **Sentinel** | The file `C:\HealthCheck\DEPLOY_IN_PROGRESS`. While it exists and is younger than 30 minutes, the watchdogs refuse to start or restart the backend. It is the deploy's mutual-exclusion lock. | `DEPLOYMENT_AND_VERIFICATION.md:146-183`; `CLAUDE.md:29` |
| **ServiceHealthCheck** | An independent scheduled task that polls each service's `/health` every 5 minutes and, after 3 consecutive failures, restarts the owning task. | `documentation/PROJECT_BLUEPRINT.md:173` |
| **devtunnel** | Microsoft Dev Tunnels. Publishes the local HTTP ports to an authenticated HTTPS URL so the tools are reachable outside the machine. Tunnels expire and are restarted by scheduled tasks. | `documentation/README.md:15-24` |
| **Kiosk package** | A per-computer folder produced by the packaging tool: installer, that computer's device token, cleanup manifest and the client. Contains no shared legacy key. | `ShiftKiosk_secure/documentation/KIOSK_NG_ROLLOUT.md:31-39` |

---

## 5. Security and identity terms

| Term | Meaning | Verified |
|---|---|---|
| **Legacy key** | The shared API key (`api_key`) still used by the 2.1.1 clients and present in the kiosk `config.ini`. Because it is shared and was committed to git history, it is treated as a **known exposure**. Its retirement criterion: legacy inventory empty for 30 days. | `ShiftKiosk_secure/README.md:241`; `KIOSK_HARDENING_DEPLOY_NOTE.md:182-185` |
| **Master API key** | The server-side shared key from which session tokens are signed. It is never sent to a browser. | `ShiftKiosk_secure/README.md:237` |
| **Session token** | A short-lived `{timestamp}.{signature}` token, HMAC-SHA256-signed with the master key, TTL 8 hours. Issued by `GET /api/session` so the dashboard can call the API without ever holding the master key. | `ShiftKiosk_secure/README.md:237`; `CODE_OVERVIEW.md:29-30` |
| **Device token** | The per-computer credential of the NG client. Issued by the admin CLI, stored in a file readable only by SYSTEM/Administrators, sent as a request credential, and revocable per device. | `kiosk-ng/broker.py:59`; live DB `ShiftKioskDB.sec_devices` |
| **Device binding** | The control that ties a credential to one computer/user. Implemented as a *shadow* (observation-only) flag on the kiosk server; not enforcing. | `KIOSK_HARDENING_DEPLOY_NOTE.md:54,62` |
| **PBKDF2** | The password-hashing algorithm used for supervisor accounts, with a per-account salt and iteration count. | live DB `ShiftKioskDB.sec_accounts` (`pwd_algo`, `pwd_iterations`, `pwd_salt`, `pwd_hash`) |
| **TOTP** | Time-based one-time password — the second factor of supervisor sign-in. Stored encrypted. | live DB `sec_accounts.totp_secret_enc`, `totp_enrolled` |
| **HMAC-SHA256** | The signature scheme for kiosk session tokens. | `CODE_OVERVIEW.md:29`; `PROJECT_DOCUMENTATION.md:181-191` |
| **sec_* tables** | The additive security/observation tables in `ShiftKioskDB` (`sec_devices`, `sec_kiosk_registry`, `sec_observation_log`, `sec_auth_events`, `sec_anomalies`, `sec_sessions`, `sec_user_hosts`, `sec_accounts`, `sec_recovery_codes`). See [DATA_MODEL.md](./DATA_MODEL.md). | live DB `INFORMATION_SCHEMA` |
| **Observation / shadow mode** | A control that records what it *would* have blocked without blocking it. Used by the kiosk flags so enforcement can be promoted on evidence. | `KIOSK_HARDENING_DEPLOY_NOTE.md:40-62` |
| **Break-glass** | A documented emergency access path for supervisor accounts, to be used only when normal sign-in is unavailable. | live DB `sec_accounts.is_break_glass`; `KIOSK_HARDENING_DEPLOY_NOTE.md:169` |
| **RBAC** | Role-based access control. Four roles exist: `AGENT`, `TEAM_LEAD`, `RTM`, `DEV`. | `documentation/auth_rbac.md:28-35` |
| **RosterAccess** | The always-on server-side gate on `/api/roster/*` that allows only RTM, TEAM_LEAD and DEV — deliberately independent of the global RBAC switch. | `documentation/auth_rbac.md:216-221` |
| **GuardManualShifts** | The protection that stops automatic writers from overwriting a shift row a human edited. | `CLAUDE.md`; see [ROSTER_AND_SCHEDULING.md](./ROSTER_AND_SCHEDULING.md) |
| **ResolveManualActor** | The server-side resolver that derives the manual-edit actor from the *session* instead of trusting a client-supplied field, and stamps `ManualChangeBy` / `ManualChangeAt`. | `CLAUDE.md`; `documentation/auth_rbac.md:14-24` |
| **AutoGenerated** | The `ShiftEntries` flag that marks a row written by automation rather than by a person. `AutoGenerated=false` plus a manual actor marker is what makes a row protected. | `BLUEPRINT_DATA_MODEL.md:72` |

---

## 6. Data and engineering shorthand

| Term | Meaning |
|---|---|
| **DTO** | Data Transfer Object — the JSON shape returned by an API endpoint. |
| **i18n** | Internationalisation. In this project "i18n" means specifically **EN/DE only**: every user-visible string lives in the locale files (Pulse) or the `STRINGS`/`I18N` dictionaries (kiosk), and no view may mix languages. |
| **Roster batch** | One Roster Generator run, recorded in `RosterBatches`; deleting the batch removes its rows and restores whatever it overwrote from its snapshot. |
| **Snapshot / overwrite mode** | In the Roster Generator, an off-by-default mode that replaces existing rows after saving their originals into the batch, so the change is reversible. Absences are never replaced. |
| **Coverage status** | The per-location classification `COVERED` / `PARTIAL` / `UNCOVERED` / `CLOSED` computed from staffing against the location's requirement. |
| **Agent group** | The grouping field on the kiosk side (`agents.agent_group`, values `WIC` / `VOICE` / `VWIC` / `BO`). On the Pulse side the equivalent grouping is `Employees.PrimaryRole`; **there is no `agent_group` column in the Pulse database** (verified 2026-10-11). |
| **Effective task** | The task badge shown for a shift row: the stored `AgentTask` if set, otherwise a role-based default. A manual value is never overwritten by the default. |
| **Duplicate resolution** | Because an agent can have two `ShiftEntries` rows for the same date from different sources, services pick the highest `Id` (`ShiftDuplicateResolver.BestShiftEntry`). Do not assume uniqueness per employee+date in raw queries. |
| **Sentinel value `2099-12-31`** | In `SickLeaves.LastDay`, means "still open-ended" rather than a real end date. |
| **Mojibake / RepairDoubleEncoding** | Old Excel imports produced double-encoded German place names; `WicLocationMatcher.RepairDoubleEncoding()` repairs them at query time. |
| **B1 transaction bug / ghost rows** | A historical partial-write bug that could leave a `WicShiftEntries` row with a null `SupportLocation`. Cleared by an idempotent admin endpoint. |
