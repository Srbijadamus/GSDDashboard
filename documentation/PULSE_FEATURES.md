# PULSE_FEATURES — every page and function

**Verification basis:** the `C:\GSDDashboard_secure` working tree and the live `GSDDashboard`
database, read **read-only** on **2026-10-11**. Every page states its route, its source file, whether
it reads or writes, and the endpoints it calls. Where the requester's page name does not exist as a
page, that is stated explicitly and the real feature is named.

**Related documents:** [SYSTEM_ARCHITECTURE.md](./SYSTEM_ARCHITECTURE.md) ·
[ROSTER_AND_SCHEDULING.md](./ROSTER_AND_SCHEDULING.md) · [GLOSSARY.md](./GLOSSARY.md) ·
[INTEGRATION_AND_DATA_FLOWS.md](./INTEGRATION_AND_DATA_FLOWS.md)

---

## 1. What Pulse is

WorkForce Pulse is the internal workforce-management and visibility application for the GSD team. It
is a React single-page application served by an ASP.NET Core backend, backed by the `GSDDashboard`
SQL Server database on the same machine. It owns the shift plan, WIC assignments and coverage,
sickness and leave, attendance visibility from the kiosks, people data, pipeline events, training and
an internal question-answering assistant.

| Fact | Value on 2026-10-11 |
|---|---|
| SPA routes registered | **27** (`Frontend/src/App.tsx:42-70`) |
| Backend HTTP routes registered | **161** (plus the single-page-app fallback = 162 endpoints), all Minimal-API `Map*`; **no** WebSocket, SignalR or hub surface exists |
| Backend route methods | GET 86 · POST 48 · PATCH 13 · PUT 3 · DELETE 11 |
| Sidebar entries | **26** in 6 labelled groups plus Overview and the AGENT-only view (`Frontend/src/layout/navItems.ts:24-92`) |
| Active employees | **122** (128 rows, 6 inactive) |
| WIC locations | **43** (41 DE, 2 NL), all active |
| Shift plan rows | **41,931** covering 2026-01-01 → 2027-02-26 |
| WIC assignment rows | **23,945**, of which 9,137 on-site |
| Languages | EN and DE only |

---

## 2. Roles, permissions and the agent view

Four roles exist. They are stored in the `AppUserRoles` table (never on `Employees`), and non-employees
such as the developer and the RTMs exist only there with a null employee id.

| Role | Read | Write | Live accounts |
|---|---|---|---|
| `AGENT` | **own data only**, via a server-side allow-list | **none** — every write is refused | 0 rows exist today |
| `TEAM_LEAD` | everything | everything | 6 |
| `RTM` | everything | everything | 2 |
| `DEV` | everything | everything plus `/api/admin/*` | 1 |

**Sign-in** is KID-only today: the user enters the identifier, `POST /api/auth/login` resolves it,
and the server sets an HttpOnly `gsd_auth` cookie with a 12-hour sliding expiry. A prepared but
**not provisioned** supervisor path (username + password + TOTP, accounts in the kiosk database) is
described in [SECURITY_MODEL.md](./SECURITY_MODEL.md).

**Two independent gates exist in code:**

1. **`Auth:EnforceAuthorization`** — the global role middleware. It is currently **false**, so that
   middleware is a no-op. Allow-lists and the route gates below still apply.
2. **Route-level gates that are always enforced regardless of that flag:**
   - `/api/roster/*` — RTM, TEAM_LEAD, DEV only (`RosterAccess`). An agent session gets 403 even with
     the global flag off. The page-level role check in `Roster.tsx` is cosmetic.
   - `/api/kiosk/*` — anonymous 401, DEV/RTM/TEAM_LEAD allowed, everything else 403.
   - `/swagger` — DEV only, otherwise 404, so the endpoint map is not leaked over the tunnel.

**Agent self-service** is the single page `/my` (`Frontend/src/pages/AgentHome.tsx`): read-only
profile, annual-leave card, upcoming shifts, sickness and vacation lists, all scoped server-side to
the session's own employee id. When the AGENT role is active the sidebar collapses to that one item
and the command palette and chat widget are hidden.

**Client-side role gates exist in exactly four places** and none of them is a security control on its
own: `Roster.tsx`, `Overview.tsx` (twice), and `WICShifts/index.jsx`. The server gate is the real one.

---

## 3. Pages, one by one

### Overview — `/` (`pages/Overview.tsx`)

The operations command centre, read-only apart from substitution acceptance.

- KPI cards: open locations today, at-risk count, closure risk, absences, coverage percentage.
- **Coverage forecast map** — a Leaflet map (`WicMapView`, `Overview.tsx:569`) with one marker per WIC
  centre coloured by today's coverage status, using CARTO basemaps with a key held in
  `VITE_CARTO_KEY`. Clicking a marker opens a substitute drawer that calls
  `GET /api/wic/substitutes` and can accept a substitute via `POST /api/wic/substitutes/accept`.
- **28-day forecast strip** and an exception list of today's absences and gaps.
- **Missing-roster warning**: active employees with no shift in the next 14 days, and shift rows whose
  employee no longer has an active employee record. Each entry deep-links to the Roster Generator or
  the Employees page. Shown to RTM/TEAM_LEAD/DEV only, and enforced server-side.
- Data: `GET /api/wic/forecast?horizon=28` (refreshed every 10 minutes),
  `GET /api/wic/briefing`, `GET /api/wic/cards?date=`, `GET /api/roster/missing?days=14`,
  `GET /api/wic/conflicts`, `GET /api/wic/coverage-gaps`.

> **Requester's "mapa" and "Hours Report" items:** the map is **not** a separate page — it is this
> pane inside Overview. There is **no Hours Report page** in Pulse at all; the closest surfaces are
> `/export` (HR Export) and the per-page download buttons. See §4.

### Shift Plan — `/shifts` (`pages/Shifts.tsx`)

The full shift grid: all agents × all dates, colour-coded by shift type. Read **and** write.

- Filters: text search, team lead, role, shift type, 7/14/30-day range, start date, Today.
- **Shift cell menu**: change type, edit the time, delete, swap with another agent. Overriding a
  system-generated `OFF_WEEKEND` or `PH` cell requires an explicit confirmation dialog.
- **Legal validation**: a shift change is checked server-side first; a violation opens a dialog that
  the user must explicitly override before the save is re-sent.
- Task badge per row (WIC, VWIC, Voice, Backlog, Dispatcher, SME, SSP). Setting Task = WIC opens a
  location picker populated from the WIC location master data.
- **Coverage per hour** bar, showing the projected mix of Voice / vWIC / WIC / Backlog / Other /
  AL / Sick / Training / Off by hour, with the below-minimum warning applying only to the Voice
  bucket.
- Row reordering (buttons and drag-and-drop), Excel download, and a print/PDF view.
- **Add AL** directly from the grid.
- Endpoints: `GET /api/shifts`, `GET /api/shifts/coverage`, `POST /api/shifts/validate`,
  `PATCH /api/shifts/{id}/manual`, `POST /api/shifts/assign/manual`, `POST /api/shifts/{id}/swap`,
  `GET /api/shifts/{id}/wic-entries`, `PATCH /api/shiftplan/reorder`, `POST /api/vacations`,
  `GET /api/shifts/download`.

### Roster Generator — `/roster` (`pages/Roster.tsx`)

Creates one employee's roster in a single batch. RTM/TEAM_LEAD/DEV only. Full rules in
[ROSTER_AND_SCHEDULING.md](./ROSTER_AND_SCHEDULING.md).

- **Two modes**: plain (dates + times + working days → `WORKING` rows) and WIC (an employee plus a WIC
  centre → WIC duty on the centre's open days and back-office work on the other working days).
- **Preview before write** is the intended workflow; the preview lists the exact rows, the skipped
  weekends and holidays, collisions, and what an overwrite would replace.
- **Overwrite** is off by default, never touches absences, and snapshots the originals so deleting the
  batch restores them.
- Endpoints: `POST /api/roster/preview`, `POST /api/roster/generate`, `GET /api/roster/locations`,
  `GET /api/roster/batches`, `DELETE /api/roster/batches/{id}`, `GET /api/roster/missing`.

### WIC Shifts — `/wic-shifts` (`pages/WICShifts/index.jsx`)

The daily WIC view in card form, one card per centre. Read **and** write.

- Polls every 30 seconds and shows covered / partial / uncovered counts plus a "connection lost"
  state and an "updated N seconds ago" counter.
- **New Shift** modal: pick an employee, a centre and a date; the server returns blocked or warning
  reasons which are shown before the save.
- Click a card or drag an agent onto another card to reassign; an uncovered-location warning dialog
  appears when the move would break coverage.
- A **GSD Backlog** branch moves an agent back to internal work for the day (see the move-to-GSD
  action below).
- Gap-filling list with the candidates the server proposes for a centre and date.
- Available-hours panel with a CSV download.
- Endpoints: `GET /api/wic/cards`, `GET /api/wic`, `GET /api/wic/assign-candidates`,
  `POST /api/wic/shifts`, `PATCH /api/wic/{agentId}`, `GET /api/wic/availableHours`,
  `GET /api/wic/availableHours/download`.

> The requester asked for "WIC" and "WIC Shifts" as separate items. `/wic` is **WIC Locations**, a
> read-only master-data table — it is not a shifts page. The shifts page is `/wic-shifts`.

### VWIC — `/vwic` (`pages/VWICPage.tsx`)

Virtual WIC coverage for the day, in two tabs.

- **Coverage**: an hourly timeline for the window with the agents on it, gaps, and candidates for
  filling them; add an agent to virtual WIC or assign a slot.
- **Rotation**: a rotation-plan generator with date, window, interval, maximum continuous hours and
  handover buffer; save a plan, generate a week, and export it.
- Endpoints: `GET /api/vwic/daily`, `GET /api/vwic/candidates`, `POST /api/vwic/agents/add`,
  `POST /api/vwic/assign`, `POST /api/vwic/rotation-plan/save`, `GET /api/vwic/rotation-plan`,
  `GET`/`POST /api/vwic/rotation-plan-week`, `.../rotation-plan-week/export`.

### Break Planner — `/breaks` (`pages/BreakPlanner.tsx`)

Voice and VWIC break scheduling for a date. Read **and** write. Refreshes every 30 seconds.

- **Auto-distribute** spreads breaks across a configurable window while respecting rotation slots and
  a maximum number of concurrent breaks, with a daily fairness shuffle.
- Start, end or cancel an individual break; add a manual slot.
- Endpoints: `GET /api/breaks`, `POST /api/breaks/auto-distribute`, `POST /api/breaks/{id}/start`,
  `.../end`, `.../cancel`, `POST /api/breaks/manual`.

### Training — `/training` (`pages/Training.tsx`)

Three tabs: scheduler, sessions, topics.

- Create training topics (duration, minimum and maximum group size, mandatory).
- Rank candidate slots for a topic, see why agents are missing, and confirm a session — confirming
  creates the TRAINING shift rows.
- Session detail cross-references the shift plan.
- Endpoints: `GET`/`POST /api/training/topics`, `GET /api/training/sessions`,
  `POST /api/training/suggest`, `POST /api/training/confirm`, `DELETE /api/training/sessions/{id}`.
- **Note:** the attendee list is stored as a comma-separated string, not a join table.

### Pipeline — `/pipeline` (`pages/Pipeline.tsx`)

Create, edit and delete pipeline events — one-off activities at a centre that need extra agents
(dates, times, handling type, agents required, primary and backup agent, status, notes).

- Endpoints: `GET /api/pipeline`, `POST /api/pipeline`, `PATCH /api/pipeline/{id}`,
  `DELETE /api/pipeline/{id}`.
- Important for scheduling: **pipeline events carry free-text agent names, not employee records.**
  There is no new-hire table anywhere in the repository. The Rollout page relies on this: it flags a
  candidate whose name matches a pipeline agent name and requires explicit confirmation for anyone
  else.

### Rollout — `/rollout` (`pages/Rollout.tsx`)

A **rollout day** is one WIC centre opened on one date especially for new workers, with its own hours.
RTM enters it by hand. This is a **primary manual entry** — automatic writers must never change, move
or delete a rollout day or anything created from it.

- Filters by centre and date range; reads the standard hours for the chosen centre and date and the
  candidate worker list; the preview shows warnings before the save.
- Endpoints: `GET /api/rollout`, `POST /api/rollout`, `PUT /api/rollout/{id}`,
  `DELETE /api/rollout/{id}`, `GET /api/rollout/standard-hours`, `GET /api/rollout/candidates`,
  `POST /api/rollout/preview`.
- Stored in the additive tables `WicRolloutDays` and `WicRolloutWorkers`. **(Both are empty on
  2026-10-11 — the feature is live but has not been used yet.)**

### WIC Attendance — `/wic-attendance` (`pages/WicAttendance.tsx`)

The main RTM screen: who is on site, who is checked in, and who is missing. Read **and** write. It is
the most feature-dense page in the application.

**Header counts (the definition matters and is stated once in
`pages/attendance/checkinStatus.ts`):**

```
CheckedIn = every distinct employee with a kiosk ACTIVE or DONE row
Expected  = today's WIC roster at OPEN centres (absences and back-office excluded)
            PLUS everyone who checked in and is not on that roster
NotYetIn  = Expected minus CheckedIn

  ⇒  Expected = CheckedIn + NotYetIn, unconditionally
```

The invariant is deliberate: anyone at work today counts as expected, so the three numbers can never
describe different populations. A kiosk row with a blank employee id is excluded from every count and
surfaced once as a note. The work-mode badge (`WIC` or the "Virtual WIC Support" home-office value)
is shown next to every checked-in person so a virtual check-in can never be read as on-site presence.
The kiosk's free-text assigned-location field **is displayed but never parsed, matched or used to
classify anyone**.

**Functions on the page:**

| Function | What it does | Endpoint |
|---|---|---|
| AL planning | Plan annual leave in the coverage context | `POST /api/wic/al-planning` |
| Assign agent | Assign an agent to a centre for a date | `POST /api/wic/assignments` |
| Manual check-in | Check in an agent when the kiosk is unavailable | `POST /api/kiosk/manual-checkin` |
| Manual log-out | Log off an active or sick agent | `POST /api/kiosk/manual-logout` |
| Find substitute | Ranked candidates with a reason | `GET /api/wic/substitutes` |
| Accept substitute | Record the decision | `POST /api/wic/substitutes/accept` |
| Move to GSD Backlog | Move the agent back to internal work (single day or a date range) | `POST /api/wic/move-to-gsd` |
| Opening-hours editor | Edit the centre's hours and its effective-from version | `GET`/`POST /api/wicschedule/opening-hours`, `.../version` |
| Minimum-required editor | Override the staffing minimum per weekday | `PATCH /api/wicschedule/opening-hours/{code}/{dow}/min-required` |

**"WIC attendance" vs the requester's "WIC attendance" and "Remote Attendance"** — see the next
entry; these were deliberately separated on 2026-10-06 so an on-site view and a work-from-anywhere
view can never be confused.

### Remote Attendance — `/remote-attendance` (`pages/RemoteAttendance.tsx`)

Read-only. Shows the same authenticated kiosk feed filtered by group (`ALL` / `Voice` / `VWIC` / `BO`)
with name, group, team lead, status, check-in, check-out and hours. Refreshes every 60 seconds.
Single endpoint: `GET /api/kiosk/attendance`. **No writes at all** — that is its purpose.

### WIC Schedule — `/wic-schedule` (`pages/WicSchedule.tsx`)

Read-only multi-view schedule: a 14-day view, a weekly report, and the location-hours view.

- Shows per-day cells coloured by status, including the "GSD Backlog" case.
- Team-lead colours are hardcoded in the component (six colours); adding a team lead requires a code
  change.
- Endpoints: `GET /api/wic/locations`, `GET /api/wicschedule/agents`,
  `GET /api/wicschedule/opening-hours`, `GET /api/wicschedule/export/agents/csv`.

### WIC Locations — `/wic` (`pages/WicLocations.tsx`)

Read-only master table of the 43 WIC centres: name, city, country, address, opening schedule string,
NPP badge. Stat tiles for total / DE / NL, country filter and search.
Endpoint: `GET /api/wic/locations`.

### WIC Assignments — `/wic-coverage` (`pages/WicCoverage.tsx`)

The **standing** assignment directory, not a daily coverage page. Two tabs (centres and agents).

- Per centre: MAIN, Backup A (assigned backups), Backup B (the reachable-city pool) and Backup C
  (regional assignments), with a detail sheet and reachable-agent list.
- Per agent: KID pair, e-mails, car flag, group region, reachable cities and WIC roles.
- Write actions: patch an agent's attributes, and **pin** a Backup B candidate into a formal BACKUP
  assignment.
- Endpoints: `GET /api/wic-coverage/wics`, `GET /api/wic-coverage/agents`,
  `GET /api/wic-coverage/agents/{kid}`, `GET /api/wic-coverage/wics/{code}`,
  `.../reachable-agents`, `PATCH /api/wic-coverage/agents/{kid}`,
  `POST /api/wic-coverage/wics/{code}/backup-b`.
- The page was renamed from "WIC Coverage" to "WIC Assignments" in 2026-09-03; the route was
  deliberately kept.

### WIC Annual Leave — `/wic-al` (`pages/WicAnnualLeave.tsx`)

Read-only cross-reference: annual leave for WIC-assigned agents over a 14-day window, with KPI cards
and per-day absence counts. Joins `GET /api/wic-coverage/agents` and `GET /api/vacations` client-side.

### Daily Attendance — `/attendance` (`pages/Attendance.tsx`)

Read-only. Historical per-location attendance with date, team-lead, location and status filters, plus
download buttons.

- Data: `GET /api/attendance/agents`, `GET /api/kiosk/attendance` (proxied kiosk feed, refresh every
  60 seconds), `GET /api/attendance/teamleads`, `GET /api/attendance/download/today|7d|30d`.
- **The underlying `DailyAttendance` table has 0 rows and is written only by an external job that is
  not in this repository.** The page therefore shows shift data and the live kiosk feed rather than
  imported history. This is an honest gap.

### Sick Leave — `/sickleave` (`pages/SickLeave.tsx`)

Sick-leave registry. Read **and** write.

- Views for today, 7 days and 14 days; filters by team lead, name, type and active-only; statistics
  panel and a day-grid grouped view.
- Add, edit (dates, type, notes), delete, and an **End** action for an active record.
- Creating or editing a record automatically writes or reverts the corresponding shift rows through
  the sync service. Weekdays only, capped at 90 days so an open-ended record cannot flood the table.
- The open-ended end date is a sentinel that the UI displays as "Open". Sick leave of 21 calendar days
  or more is flagged as long-term.
- Endpoints: `GET /api/sickleave`, `/stats`, `/active`, `POST /api/sickleave`,
  `PATCH`/`DELETE /api/sickleave/{id}`, `POST /api/sickleave/end-active/{employeeId}`,
  `GET /api/sickleave/download`.

### Vacations — `/vacations` (`pages/Vacations.tsx`)

Annual-leave bookings. Read **and** write, plus a bulk paste importer.

- Tabs for the list and the importer; grouping, sorting and search; an AL history drawer per employee.
- **Bulk import** parses pasted lines in European date format and resolves names fuzzily; unlike Bulk
  RTM it **does** auto-correct near matches.
- The server **rejects a vacation that overlaps an existing sick leave** for the same employee.
- Endpoints: `GET /api/vacations`, `/active`, `/upcoming`, `/download`, `POST /api/vacations`,
  `DELETE /api/vacations/{id}`, `GET /api/vacations/availability`.

### AL Balance — `/albalance` (`pages/ALBalance.tsx`)

Annual-leave balance per employee **and per year**. Read plus inline editing.

- KPI tiles for total employees, low balance, critical and negative balance.
- Click a value to edit: TAKEN via `PATCH /api/employees/{id}/albalance`, ELIGIBLE via
  `PATCH /api/employees/{id}/albalance/eligible` (validated client- and server-side, up to 2 decimal
  places). Remaining, the progress bar and the KPI cards recompute from the new value.
- **There is no history for an eligible-days change** — only `LastUpdated`. The previous value is
  recoverable only from a database backup. (See [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md).)

### AL Calendar — `/alcalendar` (`pages/ALCalendar.tsx`)

Read-only calendar of annual leave across the team in 7-day, 14-day and 3-month views, with a
team-lead filter, a leave-availability bar, a legend and a per-day expansion. The 3-month view also
reads the WIC forecast.
Endpoints: `GET /api/alcalendar`, `GET /api/vacations/availability`, `GET /api/wic/forecast`.

### Employees — `/employees` (`pages/Employees.tsx`)

Employee directory with create, edit and delete.

- Filters by team, type and role; full create/edit form (role, team lead, Bundesland, shift pattern,
  NPP qualification, KIDs, e-mails, car flag, group region).
- **Delete shows the employee's future shifts first** (via
  `GET /api/employees/{id}/future-shifts`) so the impact is visible before the delete is confirmed.
- Endpoints: `GET /api/employees`, `POST /api/employees`, `PUT`/`PATCH /api/employees/{id}`,
  `DELETE /api/employees/{id}`, `GET /api/employees/{id}/future-shifts`.
- The team-lead, role, engagement and Bundesland option lists are hardcoded in the component.

### Pulse Assistant — `/assistant` (`pages/WicAssistant.tsx`)

An internal question-answering chat. **No external AI service is called** — the backend is a
keyword-scoring router over 12 in-repository domain handlers. It answers in English or German and
returns an optional data table and a follow-up hint. The chat widget is also mounted globally and is
hidden for agent sessions.
Endpoint: `POST /api/assistant/ask` (15-second client timeout). Diagnostics:
`POST /api/assistant/score`.

### BO List — `/bo-list` (`pages/BoList.tsx`)

The daily list of agents working in the GSD back office rather than travelling to a centre. Add, edit
(with times and a note), delete, and drag to reorder.
Endpoints: `GET /api/bo-list`, `POST /api/bo-list`, `PATCH /api/bo-list/{id}`,
`DELETE /api/bo-list/{id}`, `DELETE /api/bo-list/by-date`.

### Bulk RTM Entry — `/bulk-rtm` (`pages/BulkRtm.tsx`)

Bulk shift management for one day, in two tabs.

- **Paste & Parse** — one agent name per line under a chosen category
  (`AL`, `OFF`, `CD`, `NIGHT`, `SL`, `BO`, `WIC`). The entry always applies to the entry day as a
  whole day, and **no time is stored**; a stray time or date on a pasted line is ignored with a note,
  not treated as an error. Name matching is case- and whitespace-insensitive, and **a typo is never
  corrected automatically** — the row says "Name not found", offers the nearest employees and waits
  for an explicit pick, while the other rows save normally.
- Each category writes exactly what its own manual-entry surface writes for that day: AL/OFF/CD/NIGHT
  through the manual shift-assign endpoint (so the row is protected from automatic writers), SL
  through the sickness endpoint, BO by replacing the day's list, WIC through the WIC assignment
  endpoint using the centre's own hours.
- **Manual** tab — list today's RTM rows, add a row, edit inline, delete.
- Endpoints: `GET /api/rtm`, `GET /api/rtm/home-wic`, `POST /api/rtm/rows`,
  `PATCH`/`DELETE /api/rtm/rows/{id}`, `DELETE /api/bo-list/by-date`, plus the per-category writes
  above.

> The improved Bulk RTM implementation is **uncommitted** in the working tree as of 2026-10-11 (see
> [CHANGE_HISTORY.md](./CHANGE_HISTORY.md) §7). The rule above is the one recorded in `CLAUDE.md`;
> verify against the working tree before quoting specifics to the board.

### HR Export — `/export` (`pages/Export.tsx`)

The central export surface: choose employee (or all), a date range, one or more data types (shift
plan, sick leave, annual leave, attendance, AL balance, training) and a format (xlsx or csv), then
open `GET /api/hr-export` with those parameters.

> **Requester's "Hours Report" and "izveštaji":** the Pulse "Hours Report" existed in the embedded
> kiosk dashboard (see [SHIFTKIOSK.md](./SHIFTKIOSK.md)), **not** in Pulse. In Pulse the reporting
> surfaces are **HR Export** plus the per-page download buttons on Shift Plan, WIC Shifts, WIC
> Schedule, Sick Leave, Vacations and Daily Attendance. **NIJE POTVRĐENO:** whether a Pulse "Hours
> Report" is still expected; no route or file with that name exists on 2026-10-11.

### Login — `/login` (`pages/Login.tsx`)

The KID sign-in form, outside the application shell. Calls `POST /api/auth/login`; on load it probes
`GET /api/auth/me`; sign-out is `POST /api/auth/logout`.

---

## 4. Cross-cutting behaviour

| Topic | Behaviour |
|---|---|
| **Refresh** | No WebSocket or server-sent events anywhere in the working tree. Live data is polled: the kiosk attendance feed every 60 s (three pages), WIC Shifts every 30 s, breaks every 30 s, the coverage forecast every 10 minutes. |
| **Excel / CSV** | Every major view has a download button. Export headers are single-language, never a bilingual pair. |
| **Exports that are server-generated** | `/api/hr-export`, `/api/shifts/download`, `/api/wic/download`, `/api/sickleave/download`, `/api/vacations/download`, `/api/attendance/download/{today,7d,30d}`, `/api/wicschedule/export/agents/csv`, `/api/wic/availableHours/download`. |
| **Theme** | Dark and light mode, switchable in the topbar, persisted per browser. |
| **Command palette** | Ctrl+K search. It searches **WIC locations only** — it does not search pages, agents or employees. Hidden for agent sessions. |
| **Attribution strip** | A fixed line in the shell. The text is hardcoded, not translated. It names individuals, so it is deliberately **not** reproduced in this documentation set. |

---

## 5. Language (EN/DE) — verified status

The permanent rule is that every user-visible string is English or German according to the selected
language, with no mixing inside one view. Strings live in
`Frontend/src/i18n/locales/{en,de}/common.json` and are resolved with `t()`.

**This rule is not yet fully met in the working tree.** The honest status on 2026-10-11:

| Page | Status |
|---|---|
| `BoList.tsx` | **Entirely hardcoded German**, while the sidebar label is English "BO List" |
| `SickLeave.tsx` | German modal copy mixed with English table copy **in the same view** |
| `WicSchedule.tsx` | Hardcoded English title and tabs, plus German empty-state and table headers **in the same view** |
| `Pipeline.tsx`, `Training.tsx`, `Roster.tsx`, `Employees.tsx`, `WicLocations.tsx`, `BreakPlanner.tsx`, `Export.tsx`, `VWICPage.tsx`, `ALBalance.tsx`, `ALCalendar.tsx`, `Attendance.tsx` | Fully or largely hardcoded English; the DE view therefore shows English |
| `Shifts.tsx` | Mostly `t()`, with a bilingual German/English warning string and several hardcoded English labels |
| `Overview.tsx`, `WicAttendance.tsx`, `RemoteAttendance.tsx`, `BulkRtm.tsx`, `Rollout.tsx`, `AgentHome.tsx`, `Login.tsx`, `WicAssistant.tsx` | Largely compliant |
| Shell layout | Attributions line is a knowingly hardcoded English exception |

The two files that violate the rule most seriously — `BoList.tsx` (German-only) and `WicSchedule.tsx`
(German text inside the English view) — should not be shown on screen during a board demo without a
caveat, or the mixed language will be visible.

---

## 6. Things that look like pages but are not

| Requested name | Reality on 2026-10-11 |
|---|---|
| "ShiftPlan" | No file. The page is `/shifts` → `pages/Shifts.tsx`, labelled "Shift Plan" |
| "GSD Backlog" | No page and no route. It is an **action** (`components/MoveToGsdBacklogAction.tsx`) available from four places, posting `POST /api/wic/move-to-gsd` |
| "mapa" | Not a route. The map is a pane inside Overview |
| "Hours Report" | Not a route in Pulse. It exists in the kiosk dashboard |
| "Manual Entry" | Not a page. Manual entry is spread across the Shifts grid, the Bulk RTM manual tab, the manual check-in modal and the BO List |
| "DailyAttendance" | No page of that name. The nav label "Daily Attendance" points at `/attendance` |
| "WIC" (as a shifts page) | `/wic` is the read-only WIC Locations table; the shifts page is `/wic-shifts` |
| "WIC coverage" | Renamed to "WIC Assignments" (display name only; the route is unchanged) |

**Other verified gaps worth knowing:** there is no catch-all/404 route; `Roster.tsx.bak-wic` is a
stale backup file sitting in the pages folder with no route; `WICShifts/mockData.js` is dead code
containing hardcoded centre data; and the "Recharts" library is a declared dependency with no chart
usage found.
