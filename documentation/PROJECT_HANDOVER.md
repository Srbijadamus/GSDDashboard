# Project handover — WorkForce Pulse & ShiftKiosk

Written for an AI model (or engineer) onboarding onto this codebase for the first time. Every claim
about code behaviour cites `file:line` or is marked **nepotvrđeno**. Facts that could not be confirmed
from the repository are not invented. This document intentionally contains no secret values, no
employee names, no KID-to-person mapping, no internal tunnel URLs and no kiosk-security exploitation
detail; where such content would belong, it is written as `[izostavljeno: poverljivo]`.

Authoritative companions: `LIVE_DEPLOY_RUNBOOK.md` (deploy), `OPEN_ITEMS_AND_RISKS.md` (risks),
`ROSTER_AND_SCHEDULING.md` (shifts), `SYSTEM_ARCHITECTURE.md` (architecture), `GLOSSARY.md` (terms).

---

## 1. What the system is

**Two products, two repositories, one machine, one SQL Server Express instance, two databases.**

| | WorkForce Pulse | ShiftKiosk |
|---|---|---|
| Purpose | Workforce management and visibility for the E.ON **GSD** (Global Service Desk): shift plan, WIC coverage, absences, attendance. | Attendance capture: agents clock in/out on their laptops; the events feed the supervisor dashboard and Pulse. |
| Repository | `C:\GSDDashboard_secure` (live checkout is the older `C:\GSDDashboard`) | `C:\ShiftKiosk_secure` (live is `C:\ShiftKiosk`) |
| Backend | ASP.NET Core 8 Minimal API, port **5000**, namespace `GSDDashboard.API` | Python FastAPI + uvicorn, port **8000** |
| Frontend | React 19 + TypeScript + Vite, served by the backend from `/` | Server-rendered HTML embedded in the server file (supervisor dashboard) + a desktop kiosk client (Python/tkinter) |
| Database | `GSDDashboard` | `ShiftKioskDB` |
| Users | Team leads, RTM (planner role), administrators | Agents (on their own laptops), supervisors |

**What Pulse does:** it is the single source of truth for *who should be where*. It holds the shift
plan (`ShiftEntries`), the WIC centre directory (`WicLocations`), WIC assignments
(`WicAgentAssignments`), opening hours, absences (sick leave, vacation, annual-leave balance) and
training. It computes WIC coverage, forecasts, substitutions and coverage gaps, and serves ~27
frontend routes.

**What ShiftKiosk does:** it is the source of truth for *who actually checked in*. Agents run a
fullscreen kiosk client that posts status events (`ON_SHIFT`, `PAUSE`, `OFF`, `AUTO_LOGOUT`,
`EMERGENCY`, `SICK_LEAVE`) to the kiosk server, which stores them in `status_log`.

**How they are connected** (the only three links — `SYSTEM_ARCHITECTURE.md:81-84`):
1. The kiosk client/broker posts attendance events to the kiosk server.
2. Pulse's `KioskProxyService` reads `GET /api/attendance` from the kiosk server over HTTP loopback
   and serves it through Pulse's own `/api/kiosk/attendance` (always-on role gate).
3. The kiosk server makes one outbound call to Pulse for the public-holiday calendar.

Nothing else is shared: not the database, not credentials, not code.

**Key identity facts** (live, read 2026-10-11, cited from `OPEN_ITEMS_AND_RISKS.md` / `DATA_MODEL.md`):
122 active employees in Pulse (`Employees`); 43 WIC locations (41 DE, 2 NL); the kiosk keeps its own
separate agent list.

---

## 2. Architecture

### Pulse layers (`SYSTEM_ARCHITECTURE.md:92-99`)

- **Presentation** — `Frontend/src`, one page file per route (see `Frontend/src/pages/`), TanStack
  Query, Tailwind, i18n EN/DE only.
- **API** — `Backend/Program.cs` composes ~160 routes via ~40 endpoint-mapper extension methods
  called in sequence. Minimal API, not controllers.
- **Domain services** — C# classes in `Backend/Services`, `Backend/Modules`, `Backend/Assistant` and
  top-level `Backend/*Service.cs`.
- **Data access** — EF Core 8 via `Backend/GSDContext.cs`. **No EF migrations**; schema changes are
  idempotent DDL run at startup in `Program.cs`.
- **Database** — SQL Server Express on the local machine, integrated (Windows) authentication,
  database `GSDDashboard`. (Connection string intentionally not reproduced.)

### Request pipeline (`SYSTEM_ARCHITECTURE.md:102-118`, order is load-bearing)

Exception handler → `/swagger` DEV-only gate (404 to everyone else) → CORS → rate limiter →
authentication → authorization → RBAC middleware → default/static files → endpoint mappings →
`/health` → SPA fallback. `Auth:EnforceAuthorization` (`appsettings.json`) controls **only** the
general RBAC middleware and is `false` today; the always-on gates (roster, kiosk proxy, swagger,
manual-edit guard) are independent of it.

### Main services (file → role)

| File | Role |
|---|---|
| `Backend/RosterService.cs` | Roster Generator (plain + WIC mode), preview/generate/batch/delete-with-restore, missing-roster check, coverage walk. |
| `Backend/WicShiftService.cs` | WIC shift board + assignment create/update, move-to-GSD. |
| `Backend/WicCoverageGapService.cs` | Coverage gaps per location/day; `IsOpenOn` and holiday queries. |
| `Backend/Services/SubstitutionService.cs` | Substitute-candidate selection and scoring (4 buckets). |
| `Backend/VacationService.cs` | Vacation CRUD, AL-day counting (`ComputeWorkDaysAsync`), AL-balance sync. |
| `Backend/ShiftService.cs` | Shift grid, task defaults (`AgentTasks`), coverage-per-hour. |
| `Backend/ShiftValidationService.cs` | Legal shift rules R1–R7 and the holiday R8 warning. |
| `Backend/ShiftSyncService.cs` | Cascade writes: sick/vacation create/delete → `ShiftEntries` rows. |
| `Backend/Services/HolidayMatcher.cs` | **Single source of truth** for "is this location/Bundesland closed by a holiday". |
| `Backend/Services/WicHoursResolver.cs` | Opening-hours resolution (date-exception over weekday pattern). |
| `Backend/Services/WicLocationMatcher.cs` | Location/string matching (`SupportLocation` and `AgentTask` → centre). |
| `Backend/Services/WicPresenceResolver.cs` | "Who is covering this WIC today" (unions the two cover signals). |
| `Backend/Services/AvailabilityResolver.cs` | An agent's WIC contribution (`GetWicContribution`). |
| `Backend/Services/CoverageEvaluator.cs` | Headcount coverage classification (`Classify`). |
| `Backend/Services/ForecastService.cs` | Coverage forecast pipeline. |
| `Backend/Services/SubstituteEligibility.cs` | Substitute-candidate eligibility filters. |
| `Backend/Services/WicConflictDetector.cs` | Report-only conflict detection (OVERLAP / SPLIT_SHIFT / CLOSED_LOCATION). |
| `Backend/PlzBundesland.cs` | Postal-code → Bundesland lookup. |
| `Backend/PublicHolidayService.cs` | Public-holiday listing and the Bundesland list. |
| `Backend/SickLeaveService.cs` | Sick-leave CRUD + stats. |
| `Backend/EmployeeService.cs` | Employee CRUD + AL balance edits (`UpdateALBalanceAsync`, `UpdateALEligibleDaysAsync`). |
| `Backend/GSDContext.cs` | EF context + `GuardManualShifts` (protect human edits from automation). |
| `Backend/RbacMiddleware.cs` | Role middleware behind `Auth:EnforceAuthorization`. |
| `Backend/Modules/Auth/KioskEndpoints.cs` | `/api/kiosk/*` proxy routes with always-on role gate. |
| `Backend/Services/KioskProxyService.cs` | Calls the kiosk `GET /api/attendance` on loopback. |
| `Backend/Assistant/*` | In-repo question answering (12 keyword-scored domain handlers; **no external LLM**). |

### Frontend (`Frontend/src/pages/`)

One file per route: `Overview.tsx`, `Shifts.tsx`, `Roster.tsx`, `WICShifts/index.jsx`,
`WicAttendance.tsx`, `RemoteAttendance.tsx`, `WicCoverage.tsx`, `WicLocations.tsx`, `WicSchedule.tsx`,
`Attendance.tsx`, `SickLeave.tsx`, `Vacations.tsx`, `ALBalance.tsx`, `ALCalendar.tsx`,
`WicAnnualLeave.tsx`, `Employees.tsx`, `Pipeline.tsx`, `Rollout.tsx`, `BreakPlanner.tsx`,
`VWICPage.tsx`, `Training.tsx`, `BoList.tsx`, `BulkRtm.tsx`, `Export.tsx`, `AgentHome.tsx` (`/my`),
`Login.tsx`, plus the `overview/` and `attendance/` subfolders.

### ShiftKiosk (high level only)

FastAPI server (port 8000) + a desktop client on each agent laptop, its own DB `ShiftKioskDB`
(14 tables), its own security model with flag profiles (observation → enforce). The detailed
security/credential mechanics are intentionally not reproduced here
(`[izostavljeno: poverljivo]` — see `SHIFTKIOSK.md` / `SECURITY_MODEL.md`). One operational fact worth
knowing: the kiosk client migration from the legacy credential to a per-computer credential was **in
progress** as of 2026-10-11.

---

## 3. Data model

No DB-level foreign keys anywhere; all joins happen in application code. The join map
(`BLUEPRINT_DATA_MODEL.md:543-558`):

```
ShiftEntries.EmployeeId            → Employees.EmployeeId
WicShiftEntries.EmployeeId         → Employees.EmployeeId
WicShiftEntries.SupportLocation    → WicLocations.DisplayName  (via WicLocationMatcher)
WicAgentAssignments.EmployeeName   → Employees.FullName        (string equality)  ← trap
WicAgentAssignments.LocationCode   → WicLocations.LocationCode OR LocationCodeLegacy
WicOpeningHours.LocationCode       → WicLocations.LocationCode OR LocationCodeLegacy
SickLeaves/Vacations/ALBalance.EmployeeId → Employees.EmployeeId
TrainingSchedule.TopicId           → TrainingTopics.Id (only FK-like relationship)
```

**Where assignments bind by `EmployeeName` instead of `EmployeeId`** — this is the single most
important data-model gotcha: `WicAgentAssignments.EmployeeName` is matched to `Employees.FullName`
by string equality (`BLUEPRINT_DATA_MODEL.md:184,190`; `SubstitutionService`). The table has no
`EmployeeId`, no validity period and no manual-change marker (`BLUEPRINT_LOGIC.md:472-473`). A
rename of an employee **silently orphans** their assignment. `WicShiftEntries.SupportLocation` is
likewise matched by string (`WicLocationMatcher`) with no FK.

### Main tables and roles

| Table | Role |
|---|---|
| `Employees` | Agent directory. `EmployeeId` is the unique business key; `FullName` is the denormalised name used for name joins. Soft-delete via `IsActive`. |
| `ShiftEntries` | **The master shift plan.** One row per employee per date per source; 16 `ShiftType` values. Unique indexes on `(EmployeeId, ShiftDate, SourceSheet)` and `(EmployeeId, ShiftDate, ShiftType)`. |
| `WicShiftEntries` | Per-agent per-centre WIC day. `IsOnSite` is the primary coverage flag. |
| `WicLocations` | The 43 centres; `LocationCode` is canonical (e.g. `DE_Essen_BP1`), `LocationCodeLegacy` bridges old codes. |
| `WicAgentAssignments` | MAIN/BACKUP/REGIONAL per centre, joined **by name**. |
| `WicOpeningHours` | Per-location per-day hours, time-versioned by `EffectiveFrom`; supports a second interval and per-day `MinRequired`. |
| `SickLeaves` / `Vacations` | Absences; cascade into `ShiftEntries` via `ShiftSyncService`. `SickLeaves.LastDay = '2099-12-31'` means "still open-ended". |
| `ALBalance` | Annual-leave balance per employee **per year**; 121 rows. `EligibleDays` DECIMAL(10,2), `PlannedTakenAL`/`RemainingAL` DECIMAL(10,1). |
| `PublicHolidays` | National + regional holidays; `Bundesland` holds full state names except `NRW`; `Country` scopes national rows (DE default). |
| `SubstitutionHistory` | 30-day rolling record for substitution fairness scoring. |
| `RtmEntries`, `BoEntries`, `BreakSlots`, `VwicRotationSlots` | Inline-DDL tables (created at startup if absent). |
| `TrainingTopics` / `TrainingSchedule` | Training catalogue + sessions. |
| `DailyAttendance` | **0 rows** — written only by an external job not in this repo. |
| `LeaveQuotas` | Reserved; no code reads/writes it. |

52 physical tables total = 26 application/framework + 26 archive/backup copies (`OPEN_ITEMS_AND_RISKS.md` D10).

---

## 4. Business rules

> ⚠ **The "R" numbers are overloaded** — there are two different R-numbering schemes in this
> codebase, and they collide. Do not quote an "R1" without saying which scheme.

### 4a. Legal/rest shift rules (R1–R7) — `Backend/ShiftValidationService.cs`

| Rule | Check | Hard block? | Code |
|---|---|---|---|
| R1 | Minimum **11 h** rest between shifts | Yes | `ShiftValidationService.cs:115-144` |
| R2 | Maximum **10 h** per day | Yes | `:91-94` |
| R3 | Maximum **48 h** per week | Yes | `:152-164` |
| R4 | Sunday work | No (warning) | `:97-100` |
| R5 | Maximum **6 consecutive** days | Yes | `:167-177` |
| R6 | Students ≤ **20 h**/week | No (warning) | `:180-184` |
| R7 | Work on a **public holiday** | No (warning) | `:44-59` |

Two known limitations (`ROSTER_AND_SCHEDULING.md:241-254`, `OPEN_ITEMS_AND_RISKS.md` F1–F3): the rest
rule only tests the adjacent calendar day; the weekly rule computes the wrong week for a Sunday
shift; the validator is called only from WIC-shift creation and the explicit validate endpoint — the
bulk-assign routes and the Roster Generator do **not** call it. Break deduction (>6 h → −1 h) applies
only to the weekly total.

### 4b. Holiday business rules — owner decision 2026-10-21 (`BLUEPRINT_LOGIC.md:620-631`)

| Rule | Meaning | Enforced by |
|---|---|---|
| R1 **WIC_CLOSED** | A location's holiday (country + Bundesland) closes the WIC — nobody sent, no gap produced. | `RosterService` skips holiday-closed centres; `ShiftValidationService.cs:63-77` (R8) warns on a WIC duty at a closed centre. |
| R2 **EMPLOYEE_HOLIDAY** | An employee's holiday is their own `Employees.Bundesland`; the day is paid. | `HolidayMatcher.Applies(h, "DE", bundesland)` — used in vacation AL-count and validation. |
| R3 | An employee whose Bundesland has a holiday **may** work at an open WIC in a Bundesland without that holiday; paid extra. | Business reference; not yet fully aligned in code (see OPEN_ITEMS). |
| R8 | A WIC duty assigned to a centre closed that day by a public holiday → **warning** (R1 WIC_CLOSED). | `ShiftValidationService.cs:63-77`. |

**Holiday matching — `Backend/Services/HolidayMatcher.cs`** is the single source of truth:
- `Applies(h, country, bundesland)` (`HolidayMatcher.cs:23-29`): national rows are country-scoped
  (`CountryOf(h)` — null/empty `Country` means DE, `:16-17`); regional rows match DE only and compare
  `h.Bundesland` to `bundesland` with `StringComparison.OrdinalIgnoreCase`.
- Location overload `Applies(h, WicLocation)` (`:33-34`) derives Bundesland from the column or
  `PlzBundesland.Get(locationCode, postalCode, country)`.
- `IsHoliday(loc, date, holidays)` (`:37-40`) and the employee overload (`:48-51`).

### 4c. AL day count (owner decision 2026-10-26) — `Backend/VacationService.cs`

`ComputeWorkDaysAsync` (`VacationService.cs:208`) counts **working days (Mon–Fri) minus public
holidays** that apply to the employee, via `HolidayMatcher.Applies(h, country, emp.Bundesland)`
(`:214`). National holidays are country-scoped (DE default; an employee with no Bundesland but an
active NL WIC assignment uses Dutch national holidays); regional holidays match `Employees.Bundesland`
(R2). Half-day (0.5) and zero-day rows are unaffected. The old calendar-day fallback for a NULL
`WorkDaysNet` was removed — it is computed on the fly for display.

### 4d. WIC opening hours — `Backend/Services/WicHoursResolver.cs`

`Resolve(all, locationCode, locationCodeLegacy, dayOfWeek, date)` returns the single effective row: a
dated **exception** row wins over the weekday pattern, and among exceptions the latest `EffectiveFrom`
wins. Null/`IsClosed` means closed. `DutyWindow` returns the first interval's open through the last
interval's close (two-interval centres span the break — the break is never a coverage hole).

### 4e. Substitution rules — `Backend/Services/SubstitutionService.cs`

Four candidate buckets in priority order (`BLUEPRINT_LOGIC.md:418-428`): **(A)** designated
BACKUP/REGIONAL, **(B)** SSP, **(B2)** Voice/VWIC, **(C)** WIC_DONOR (MAIN from a surplus centre,
distance-ordered), **(D)** CALL_IN (OFF agents, needs approval). Scoring
(`BLUEPRINT_LOGIC.md:432-443`): `Score = sourceBonus + tierScore − LoadScore·10`, where `sourceBonus`
BACKUP=10000 / SSP=5000 / WIC_DONOR=0 / CALL_IN=−100, `tierScore` is straight-line distance
(`HaversineKm`) and `LoadScore` is the agent's `SubstitutionHistory` count over 30 days.

### 4f. Other core rules

- **WIC contribution** — `AvailabilityResolver.GetWicContribution` (`BLUEPRINT_LOGIC.md:88-109`): sick →
  0; missing shift → 0; full-absence types → 0; HALF_AL → 0.5; WIC_DUTY (with `AgentTask` not in
  GSD/Backlog) → 1.0; a `WORKING` row counts 0 for WIC even if it has an on-site row.
- **Coverage classification** — `CoverageEvaluator.Classify` (`BLUEPRINT_LOGIC.md:353-362`): CLOSED /
  UNCOVERED / PARTIAL / COVERED against `MinRequired` (per-day `WicOpeningHours.MinRequired` →
  `WicLocations.MinAgentsRequired` → default 1).
- **Manual-change protection** — a human edit stamps `ManualChangeBy`/`At`; `GSDContext.GuardManualShifts`
  throws if any automatic writer tries to overwrite it (see §5).

---

## 5. Conventions and traps

- **Bundesland format**: full German state names, with one exception — **Nordrhein-Westfalen is stored
  as `NRW`** (`Backend/PublicHolidayService.cs:51` hardcodes the list; `Frontend/src/pages/Employees.tsx:13`
  the dropdown). Regional-holiday matching is `OrdinalIgnoreCase` string equality
  (`WicCoverageGapService.cs:434,673`; `OverviewService.cs:82`). Any other spelling matches nothing.
- **Public holidays are entered by hand** — there is no importer. Fill the next year **before
  1 October** (a 1-January holiday enters the 90-day horizon around 3 October).
- **`RawSchedule` is display-only** and never parsed; `WicHoursResolver` reads only
  `OpenTime`/`CloseTime`/`OpenTime2`/`CloseTime2` + `DayOfWeek`/`EffectiveFrom`/`ExceptionDate`/`IsClosed`
  (`BLUEPRINT_LOGIC.md:389-393`).
- **No EF migrations** — schema changes are idempotent DDL run at every startup (`Program.cs`,
  `COL_LENGTH`/`IF NOT EXISTS` guards). The project's migration mechanism, not EF migrations.
- **`ManualChange*` columns** (`ShiftEntries`, `WicOpeningHours`, `ALBalance`): nullable, added
  additively with no backfill; a non-blank `ManualChangeBy` makes the row "manual" and protected.
  The actor is the session KID (`AuthClaims.Kid`) with a fixed fallback tag — never a client-supplied
  value. (Specific KID values and who holds them are `[izostavljeno: poverljivo]`.)
- **UTC timestamps**: `ManualChangeAt`/`LastUpdated` are written with `DateTime.UtcNow`
  (`EmployeeService.cs:157-158`), so stored values are UTC.
- **`RTM` is ambiguous** — "Return to Main" (the `/bulk-rtm` page, `RtmEntries`) vs the access role
  `AppRoles.Rtm` (read/write, like TEAM_LEAD) (`GLOSSARY.md:29`).
- **`BO` and `NIGHT` are not shift types** — BO is a `WORKING` row with `RawValue="BO"` or a `BoEntries`
  row; NIGHT is a shift *pattern* or a Bulk RTM category.
- **WIC vs VWIC substring trap** — never test WIC with `Contains("wic")`; `"VWIC"` also contains it.
  Compare `ShiftType == "WIC_DUTY"` exactly.
- **`IsWicDuty` is a fossil** — the `ShiftEntries` flag predates `ShiftType=WIC_DUTY`; always use
  `ShiftType`, not the flag.
- **Duplicate rows are normal** — most services collapse duplicates by **highest Id**
  (`ShiftDuplicateResolver.BestShiftEntry`); the Shift Plan grid and the roster builder do it
  differently (see OPEN_ITEMS D2).
- **`WicShiftEntries` ghost rows** (`SupportLocation IS NULL`) are a known partial-write artefact;
  cleared by `POST /api/admin/cleanup-wic-orphans`, not by adding `IS NOT NULL` filters.
- **Sentinel value `2099-12-31`** in `SickLeaves.LastDay` means "still open-ended".
- **The roster is generated, not typed** — the Roster Generator is the supported way to create a plan;
  the June Excel import is dead (`CLAUDE.md` / `ROSTER_AND_SCHEDULING.md:74`).

---

## 6. Deploy and environment (descriptive, no secrets)

Summary of `LIVE_DEPLOY_RUNBOOK.md`:

- **Two trees**: build source is the **working tree** `C:\GSDDashboard_secure`, not `git HEAD`; the
  live checkout `C:\GSDDashboard` is a separate older repo and produces a different binary.
- **SDK**: the live DLL was built with **.NET SDK 10.0.204**; the repository targets .NET 8 and cannot
  reproduce the binary 1:1 with SDK 8.
- **Watchdog + sentinel**: the `GSDDashboard-Backend` scheduled task runs a watchdog that starts the
  backend and honours the sentinel `C:\HealthCheck\DEPLOY_IN_PROGRESS` — while it exists the watchdog
  will not start/restart the backend. Deploy = sentinel → stop → copy → verify → remove sentinel.
- **Frontend is served from TWO static roots** — `C:\GSDDashboard\Backend\wwwroot` **and**
  `C:\GSDDashboard\Backend\bin\Release\net8.0\wwwroot`. Copying into one root only does not reach the
  browser.
- **Deploy proof** (what actually counts as "deployed"):
  - SHA-256 of the deployed `GSDDashboard.API.dll` (the "marker" for the backend).
  - The content-hashed bundle filename (e.g. `index-CFzU1rgS.js`) **and its SHA-256**, identical in
    **both** static roots (the "marker" for the frontend).
  - Endpoint behaviour: `/health` 200; `/api/wic/holidays` and `/api/kiosk/attendance` **401** without
    a session (always-on gates); `/swagger` 404 for non-DEV.
- **`/health` returning 200 is not evidence** of a good deploy — it answers before the DB, the proxy
  and the authenticated routes are proven.
- **Backups**: full SQL backups to the SQL Server default backup folder, verified; a database backup is
  not a code snapshot and neither is a rollback.
- **Rollbacks are four separate operations**: kiosk flag profiles, code, schema, and Pulse application.
- Tunnels: local ports are published through Microsoft Dev Tunnels that expire and are restarted by
  scheduled tasks. Exact URLs are `[izostavljeno: poverljivo]` (they are per-project and expire).

---

## 7. Decision history (2026-10-20 → 2026-10-27)

From `git log` of `C:\GSDDashboard_secure` (branch `feature/overview-v21`):

| Date | Commit | Decision / change |
|---|---|---|
| 2026-10-20 | `5e43daa` | Record the 2026-10-20 "Kiosk offline" incident — stale .NET runtime, not config/token. |
| 2026-10-21 | `0ee49e0` | Overview: holiday marking per WIC location + UL/SL reason separation. |
| 2026-10-21 | `0f58a8a` | Holiday marker must not hide coverage gaps (gaps take priority). |
| 2026-10-21 | `f6ffad7` | Canonical Bundesland format (`NRW`), yearly PublicHolidays maintenance. |
| 2026-10-21 | `222d5f5` | Align `PlzBundesland` and `SubstitutionService` to canonical `NRW`. |
| 2026-10-21 | `05aa3c2` | Separate RESIGNED/OL/PH/LPH/OFF from annual leave. |
| 2026-10-21 | `ad8e449` | Country-scoped national holidays; single holiday service. |
| 2026-10-21 | `759af20` | Shift Plan holiday awareness (day marking + coverage minimum). |
| 2026-10-21 | `8b3a908` | Route **all** holiday matching through `HolidayMatcher`. |
| 2026-10-21 | `b87984a` | Enforce R1 — no assignments on holiday-closed WIC (warning + generator). |
| 2026-10-23 | `f93ef7d` | AL balance: decimal taken-days input + dynamic eligible-days limit. |
| 2026-10-23 | `011d553` | Decision: taken-days allow at most 1 decimal place (no fixed 0.5 step). |
| 2026-10-26 | `5e3f7ab`, `60e4360` | "What's new" v4 entries + DE JSON quote fix. |
| 2026-10-26 | `e6c0853` | Backfill `Employees.Bundesland = 'NRW'` for non-WIC employees (19 rows; NL + demo excluded). |
| 2026-10-26 | `69372d2` | AL day count = working days **minus public holidays** (R2-aware). |
| 2026-10-26 | `31e1d94` | Recalculate `Vacations.WorkDaysNet` for all 120 rows. |
| 2026-10-26 | `7fca61a` | Record finding: multiple active MAIN per WIC is allowed (Den Bosch NL ×3). |
| 2026-10-26 | `9bbdfb0` | Add `ALBalance.ManualChangeBy/At` (who, not just when) — idempotent, no backfill. |
| 2026-10-26 | `c761459` | Document the ALBalance audit columns. |

Owner decisions recorded in this window: Bundesland `NRW` for non-WIC employees (26.10), multiple MAIN
per WIC allowed (26.10), Mülheim Thursday-only hours (26.10), AL day-count formula (26.10), AL
1-decimal rule (23.10), holiday rules R1/R2/R3 (21.10).

---

## 8. Open items and risks

Grouped by urgency; full detail (including kiosk security mechanics, which are deliberately not
reproduced here) is in `OPEN_ITEMS_AND_RISKS.md`. Severity labels follow that document.

**High — data / access / board understanding**

- **Supervisor sign-in is NOT implemented in code** (`S14`): documents describe a supervisor-mode
  switch that no `.cs` file reads and no config key selects. Treat those instructions as wrong.
- **Kiosk security rollout incomplete**: several controls remain in observation ("shadow") mode; the
  legacy-credential retirement is blocked on client migration (44 of 63 hosts still legacy as of
  2026-10-11); supervisor accounts are unprovisioned (`S1`–`S4`, `S15`–`S18`).
  `[izostavljeno: poverljivo]` for the credential/signing mechanics.
- **Device-binding configuration drift** (`S17`/`C26`): the live kiosk config and the tracked profiles
  disagree; owner decided 2026-10-11 the live value stays.
- **Legal rules only partly server-enforced** (`F1`): bulk-assign and the Roster Generator skip the
  validator.
- **No retention/deletion** for attendance events, observation log, manual actions (`D11`); historical
  attendance must not be used as individual performance evidence (`D12`).
- **`ShiftEntries` unique index** (`D1`): `UQ_ShiftEntries_EmpDate` (employee, date) **exists** and is
  active — any doc claiming otherwise is wrong.

**Medium — real defects, bounded effect**

- Absence-type lists differ across four code paths (`D3`); opening-hours resolution has a second path
  (`D4`); VWIC assignment can repoint a real centre row (`D5`); substitute acceptance writes an
  unprotected row (`F10`); two raw-SQL admin commands bypass the manual-edit guard (`F12`); EN/DE
  compliance incomplete (`F5`); kiosk client migration in progress (`D13`).

**Low — hygiene / clarity**

- 26 archive tables with no retention (`D10`); dead code and no-op endpoint (`F7`/`F8`); Serbian
  comments/files in the backend (`F13`); `Rollout` feature live but unused — 0 rows (`F15`).

**Deadlines**

- The task brief for this handover references a **"Faza E" with deadline ~26.12**; this term and date
  were **not found anywhere in `C:\GSDDashboard_secure`** — **nepotvrđeno**. No repo document defines a
  "Faza E" or a 26.12 deadline. (The kiosk side uses `dan1`/`dan2`/`dan3` profile names; whether "Faza
  E" maps to one of those is unknown and unconfirmed.)

---

## 9. How to work with this project

The working rules that keep this codebase safe (from the task brief and the repository's own docs):

1. **Narrow tasks.** One small, well-defined change per task. Never bundle an unrelated "while I'm
   here" fix.
2. **Documentation first.** Read the relevant `documentation/` file (and `CLAUDE.md`) before touching
   code. This document and its companions are the entry point.
3. **Proof over assertion.** Every claim about code behaviour carries `file:line`; every number carries
   a date and source. If something cannot be verified, say **nepotvrđeno** rather than guessing.
4. **STOP conditions.** The tasks routinely end with explicit STOP clauses (no push, no build/deploy/
   restart, no DB write, read-only). Respect them literally. When a check fails (e.g. a DLL hash does
   not match), **report it and do not fix it yourself** unless explicitly asked.
5. **User approval gates.** Deploying and writing to the database require the user's explicit,
   task-scoped approval; approval in one context does not extend to the next. Database access in
   verification tasks is **SELECT-only**.
6. **Backup + rollback before every write.** Any DB write is preceded by a backup and a recorded
   rollback path (`ALTER TABLE … DROP COLUMN`, per-row `UPDATE … SET … = NULL`, etc.). Additive
   schema changes are preferred and use idempotent `COL_LENGTH`/`IF NOT EXISTS` guards — no backfill.
7. **Do not fabricate.** No invented functionality, no invented commit hashes, no invented file paths.
   `[izostavljeno: poverljivo]` marks content deliberately left out; `nepotvrđeno` marks an unverified
   claim.
8. **Protect the data.** No secret values, employee names, KID-to-person mappings, tunnel URLs, or
   kiosk-security exploitation detail in any output.
9. **Manual edits are the highest authority.** Any automatic writer must skip rows carrying a manual
   marker; the context guard throws rather than silently overwriting a human decision.
