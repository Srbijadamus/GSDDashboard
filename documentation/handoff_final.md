# GSD Dashboard — Handoff (Current State)

**Date:** 2026-07-03
**Build status:** VERIFIED PASSING — PS1_19_FinalBuildVerify.ps1 all checks green 2026-06-15
**Stack:** ASP.NET Core 8 · React 19 + Vite 8 · TypeScript 6 · Tailwind 3.4.19 · SQL Server Express

---

## 1. Rules for the Next Agent

Read these before doing anything else.

| Rule | Detail |
|------|--------|
| No git | Never run git add/commit/push/status unless the user explicitly asks |
| No bash | MSYS2 is broken on this machine (exit code 0xC0000142). All shell ops go via PS1 scripts that the **user runs manually** |
| No Unicode in PS1 | No here-strings, no Unicode characters in PowerShell scripts |
| No Invoke-Sqlcmd | Use `System.Data.SqlClient` pattern in PS1 scripts |
| Language | English and German only. No Serbian, no other languages |
| SickLeaves columns | `FirstDay` / `LastDay` (NOT StartDate/EndDate) |
| Vacations columns | `FirstDay` / `LastDay` (NOT StartDate/EndDate) |
| WicOpeningHours codes | Uses new-style LocationCodes (`DE~...`) |
| WicAgentAssignments codes | Old-style (`DE_Dortmund`). Resolved via `WicLocations.LocationCodeLegacy`. Never rewrite existing assignments |
| SSP/Voice base location | Dusseldorf HQ: `lat=51.2154, lon=6.7837` |
| CoverageEvaluator | All COVERED/PARTIAL/UNCOVERED/CLOSED classification must go through this service. No inline chains anywhere else |
| Absence canonical set | `AvailabilityResolver.FullAbsenceTypes` = { SL, AL, UL, PH, LPH, RESIGNED }. TRAINING and HALF_AL are NOT in this set |
| HALF_AL | 0.5 credit via `presentDouble += 0.5` then `(int)Math.Floor(presentDouble)`. Do not exclude HALF_AL agents |
| WicShifts file | The live WIC Shifts component is `WicShifts_old.tsx` — the name contains "old" but it is the active file |
| No duplicate alias maps | `WicLocationMatcher` is the single alias dictionary. Never create another alias dict in any service |

---

## 2. Infrastructure

### Tunnel URLs (expire every 4 days, auto-restart via Task Scheduler)

| Service | URL |
|---------|-----|
| GSD Dashboard | https://d2jn94qg-5000.euw.devtunnels.ms |
| Kiosk | https://ssr7tm2l-8000.euw.devtunnels.ms |
| Kiosk Dashboard | https://ssr7tm2l-8000.euw.devtunnels.ms/dashboard |

### Task Scheduler (auto-starts on reboot)

- `GSDDashboard-Backend` — ASP.NET Core backend, port 5000
- `GSDDashboard-Tunnel` — devtunnel for dashboard
- `ShiftKioskServer` — Python FastAPI kiosk, port 8000
- `ShiftKioskTunnel` — devtunnel for kiosk
- `DevTunnel-AutoStart` — devtunnel CLI auth + start

### Deploy Script

```powershell
powershell -ExecutionPolicy Bypass -File C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1
```

Builds frontend, copies to `Backend/wwwroot/`, builds backend, starts server, runs smoke tests.

---

## 3. Database State

**Server:** `localhost\SQLEXPRESS`
**Database:** `GSDDashboard`
**Auth:** Windows Authentication

| Table / Column | Value |
|----------------|-------|
| `WicLocations` | 43 rows (41 DE + 2 NL), all `IsActive=1` |
| `WicLocations.Coordinates` | 43/43 geocoded (5 city-only precision, acceptable) |
| `WicLocations.MinAgentsRequired` | All = 1 (default) |
| `WicLocations.Bundesland` | 41 DE populated, 2 NL = NULL (correct) |
| `WicLocations.LocationCodeLegacy` | 42/43 populated (RENDSBURG = NULL, direct match, no legacy needed) |
| `WicLocations.PostalCode` | Extracted from LocationCode for all 41 DE rows; NL rows NULL |
| `WicLocations.OpeningDay` | Added at startup if absent. Seeded from WicCoverageImport |
| `WicLocations.Comment` | Added at startup if absent |
| `Employees` | ~130 rows |
| `Employees.PrimaryKid` | Added at startup if absent. Seeded from WicCoverageImport (70 agents) |
| `Employees.SecondaryKid` | Added at startup if absent |
| `Employees.InfosysEmail` | Added at startup if absent |
| `Employees.EonEmail` | Added at startup if absent |
| `Employees.HasCar` | Added at startup if absent. Editable via /api/wic-coverage/agents/{kid} |
| `Employees.GroupRegion` | Added at startup if absent. Editable via /api/wic-coverage/agents/{kid} |
| `WicAgentAssignments.AssignmentType` | MAIN, BACKUP, or REGIONAL |
| `WicAgentAssignments.IsActive` | bool, allows soft-disable of assignments |
| `WicAgentAssignments.Notes` | string, free text |
| `SickLeaves.LeaveType` | Values: "SL", "Self", "Child" |
| `SubstitutionHistory` | Table exists; grows at runtime when substitutes are accepted |
| `BreakSlots` | Created at startup if absent. Populated by BreakService.AutoDistributeAsync |
| `VwicRotationSlots` | Created at startup if absent. Populated by VwicService.SaveRotationSlotsAsync |
| `AgentReachableCities` | Created at startup if absent. Seeded once by WicCoverageImport if empty |
| `DayOfWeek` convention | 0=Sun ... 6=Sat (.NET convention throughout) |

### WicCoverageImport seeder (runs once at startup if AgentReachableCities is empty)

- Seeds PrimaryKid, SecondaryKid, InfosysEmail, EonEmail on ~70 Employees matched by FullName
- Creates AgentReachableCities rows (agent-to-city reachability)
- Creates WicAgentAssignment rows (MAIN/BACKUP/REGIONAL) from 44-WIC seed table
- Seeds WicLocations.OpeningDay and .Comment
- City normalization (e.g. "Munich" -> "Munchen")
- Agent name aliases (e.g. "Aman Kedo" -> "Amani Kedo") for fuzzy name matching

---

## 4. Backend API Endpoints

### WIC Core

| Method | URL | Service |
|--------|-----|---------|
| GET | `/health` | Liveness probe |
| GET | `/api/wic/locations` | All WIC locations |
| GET | `/api/wic?from=&to=&locationCode=&employeeId=&teamLead=` | WIC shift entries |
| GET | `/api/wic/cards?date=&country=` | Coverage status cards |
| GET | `/api/wic/coverage?date=` | Per-location agent coverage |
| GET | `/api/wic/available-hours?date=` | Free hours per agent |
| GET | `/api/wic/open?date=&horizon=` | Open/coverage status N days |
| PATCH | `/api/wic/shifts/{id}` | Update WicShiftEntry |
| POST | `/api/wic/shifts` | Create WicShiftEntry |
| POST | `/api/wic/assign` | Assign agent to WIC for a date |
| GET | `/api/wic/download?from=&to=` | WIC shifts Excel |
| GET | `/api/wic/backup?locationCode=&date=&horizon=` | Older backup candidates (BackupService) |
| GET | `/api/wic/substitutes?locationCode=&date=&horizon=&absentIds=` | Ranked substitutes |
| POST | `/api/wic/substitutes/accept` | Accept substitute |
| GET | `/api/wic/forecast?horizon=14&locationCode=` | 14-day forecast (clamped 1-30) |
| GET | `/api/wic/whatif?absentEmployeeId=&date=&horizon=` | What-if simulation |
| GET | `/api/wic/briefing` | Today's absences + gaps + next at-risk |
| GET | `/api/wic/briefing/export` | Excel export (Absences / Gaps / AT_RISK) |
| GET | `/api/wic/reachability?from=&to=` | Haversine matrix |
| GET | `/api/wic/reachability/sanity` | Berlin to Munich ~504 km check |
| GET | `/api/wic/schedule` | WIC opening hours |
| POST | `/api/wic/al-planning` | AL planning for a WIC and date range |

### Overview

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/overview/wic-status?date=&horizon=` | Per-location per-day status (horizon 1-7) |
| GET | `/api/overview/detail?type=&date=` | Agents by type (VOICE/CHAT/BACKLOG/AL/SL/TRAINING/WIC) |

### VWIC

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/vwic/daily?date=` | 24h VWIC timeline |
| POST | `/api/vwic/assign` | Assign agent to VWIC slot |
| GET | `/api/vwic/candidates?date=` | Voice agents eligible for VWIC |
| PUT | `/api/vwic/agents/add` | Set SecondaryRole = VWIC |
| PUT | `/api/vwic/agents/remove` | Clear SecondaryRole |
| POST | `/api/vwic/rotation-plan` | Generate daily rotation plan |
| POST | `/api/vwic/rotation-plan/save` | Persist to VwicRotationSlots |
| POST | `/api/vwic/rotation-plan-week` | Generate week rotation plan |
| POST | `/api/vwic/rotation-plan-week/export` | Excel week plan export |

### Break Planner

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/breaks?date=` | Break slots for a date |
| POST | `/api/breaks/auto-distribute` | Auto-assign 30-min breaks |
| POST | `/api/breaks/{id}/start` | Mark break as started |
| POST | `/api/breaks/{id}/end` | Mark break as done |
| POST | `/api/breaks/{id}/cancel` | Cancel a break slot |
| POST | `/api/breaks/manual` | Create/replace manual break |

### WIC Coverage

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/wic-coverage/agents?search=` | Agents with KIDs, emails, cities, roles |
| GET | `/api/wic-coverage/agents/{kid}` | Single agent by PrimaryKid |
| PATCH | `/api/wic-coverage/agents/{kid}` | Update HasCar and/or GroupRegion |
| GET | `/api/wic-coverage/wics?search=` | Active WICs with MAIN/BACKUP counts |
| GET | `/api/wic-coverage/wics/{locationCode}` | Full WIC plan (Main/BackupA/BackupB/BackupC) |
| GET | `/api/wic-coverage/wics/{locationCode}/reachable-agents` | Reachable agents by city |
| POST | `/api/wic-coverage/wics/{locationCode}/backup-b` | Pin BackupB to BACKUP assignment |

### Employees + Leave

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/employees?...` | Employee list |
| GET | `/api/employees/{id}` | Single employee |
| GET | `/api/employees/{id}/timeline` | Employee shift timeline |
| POST | `/api/employees` | Create employee |
| DELETE | `/api/employees/{id}` | Delete employee |
| PATCH | `/api/employees/{id}/albalance` | Update AL balance |
| GET | `/api/sickleave?from=&to=&teamLead=&type=&activeOnly=` | Sick leave records |
| GET | `/api/sickleave/active?date=` | Active sick leaves on a date |
| GET | `/api/sickleave/stats?from=&to=` | Aggregate stats |
| POST | `/api/sickleave` | Add sick leave (triggers ShiftSync) |
| PATCH | `/api/sickleave/{id}` | Update sick leave (reverts + re-syncs) |
| DELETE | `/api/sickleave/{id}` | Delete sick leave |
| GET | `/api/sickleave/download?from=&to=` | Excel export |
| GET | `/api/vacations?...` | Vacation records |
| GET | `/api/vacations/active?date=` | Active vacations on a date |
| GET | `/api/vacations/upcoming?days=` | Upcoming vacations |
| DELETE | `/api/vacations/{id}` | Delete vacation |
| GET | `/api/vacations/download?from=&to=` | Excel export |
| GET | `/api/albalance` | AL balance per employee |
| GET | `/api/albalance/{id}` | Single AL balance |
| PATCH | `/api/albalance/{id}` | Update AL balance |

### Shifts + Pipeline + Other

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/shifts?from=&to=&teamLead=&role=&engagement=&shiftType=` | Shift plan |
| GET | `/api/shifts/working-today?date=` | Agents working today |
| GET | `/api/shifts/download?from=&to=` | Excel export |
| PATCH | `/api/shifts/{id}` | Update ShiftType/times/task |
| POST | `/api/shifts/validate` | Validate shift change |
| GET | `/api/pipeline` | Pipeline events |
| POST | `/api/pipeline` | Create event |
| PATCH | `/api/pipeline/{id}` | Update event |
| DELETE | `/api/pipeline/{id}` | Delete event |
| GET | `/api/alcalendar?from=&to=` | AL calendar view |
| GET | `/api/attendance?...` | Daily attendance |
| GET | `/api/attendance/download?...` | Excel export |
| GET | `/api/training` | Training records |
| GET | `/api/public-holidays` | Public holiday calendar |
| GET | `/api/dashboard/summary?date=` | KPI counts |
| GET | `/api/dashboard/teamlead-summary?date=` | Per-TeamLead breakdown |
| GET | `/api/dashboard/wic-cards?date=` | Dashboard WIC cards |

---

## 5. Frontend Pages

| Route | Component | Write ops |
|-------|-----------|-----------|
| `/` | Overview.tsx | None |
| `/shifts` | Shifts.tsx | None |
| `/wic-shifts` | WicShifts_old.tsx | Reassign agents, new shift modal |
| `/vwic` | VWICPage.tsx | Add/remove agents, rotation plan, save |
| `/breaks` | BreakPlanner.tsx | Auto-distribute, start/end/cancel, manual override |
| `/wic-attendance` | WicAttendance.tsx | Accept substitute, assign agent, manual check-in, AL planning |
| `/wic-schedule` | WicSchedule.tsx | None |
| `/pipeline` | Pipeline.tsx | Create/edit/delete events |
| `/training` | Training.tsx | None |
| `/wic` | WicLocations.tsx | None |
| `/attendance` | Attendance.tsx | None |
| `/sickleave` | SickLeave.tsx | Add/patch/delete sick leave |
| `/vacations` | Vacations.tsx | None |
| `/albalance` | ALBalance.tsx | None |
| `/alcalendar` | ALCalendar.tsx | None |
| `/employees` | Employees.tsx | Create/delete employees, edit AL balance |
| `/wic-coverage` | WicCoverage.tsx | Patch agent (HasCar/GroupRegion), pin BackupB |

---

## 6. Substitution Engine

### Tiers (higher score = preferred)

| Tier | Score | Condition |
|------|-------|-----------|
| BACKUP | 10,000 | Designated backup in `WicAgentAssignments` |
| SSP | 5,000 | `PrimaryRole = "SSP"` |
| WIC_DONOR | 0 + reachability (0-500) | Nearby WIC with surplus (`surplus = effectiveCoverage - minRequired > 0`) |
| CALL_IN | -100 + reachability | All other reachable agents |

Fairness penalty: `-LoadScore * 10` (30-day rolling SubstitutionHistory).
HALF_AL agents: 0.5 coverage credit, NOT excluded.

### Known nulls (always null in SubstituteCandidate)

- `contactEmail` — no email field on Employees table (Employees now has InfosysEmail/EonEmail from WicCoverageImport, but SubstituteDto was not updated to include it)
- `travelMinutes` — haversine only, no routing API
- `hasDirectTrain` — no transit data
- `fairnessScore` raw value — computed internally as LoadScore

---

## 7. Key Design Decisions

### LocationCodeLegacy

`WicAgentAssignments.LocationCode` stores old codes (`DE_Dortmund`).
`WicLocations.LocationCode` stores new tilde codes (`DE~44139~Dortmund~Str.`).
`WicLocations.LocationCodeLegacy` bridges them.
**Never write new assignments with old-style codes.**
All services that filter by location check both codes via WicLocationMatcher.

### WicLocationMatcher (static)

30-entry alias dictionary. Two static methods:
- `MatchesSupportLocation(sl, loc)` — checks DisplayName, City, aliases vs. both LocationCode and LocationCodeLegacy
- `MatchesAssignmentCode(code, loc)` — checks LocationCode OR LocationCodeLegacy

**Never duplicate the alias map** — all callers use this static class.

### ReachabilityService (singleton)

Haversine matrix. 4h TTL cache. Double-checked locking with `SemaphoreSlim`. Uses `IServiceScopeFactory` to resolve scoped `GSDContext` inside a Singleton.

### HALF_AL

`presentDouble += 0.5`, then `effectiveCoverage = (int)Math.Floor(presentDouble)`. Consistent across all 6 coverage-computing services. HALF_AL is NOT in `AvailabilityResolver.FullAbsenceTypes`.

### CoverageEvaluator.EvaluateAsync — known inconsistency (not yet fixed)

`CoverageEvaluator.EvaluateAsync` (the DB-backed instance method) includes HALF_AL in its internal absence set, treating it as fully absent. This is inconsistent with all other services that give HALF_AL 0.5 credit. Only `SubstitutionService` calls `EvaluateAsync` directly. A fix would align the instance method with the static `Classify` + `ClassifyByMinutes` behaviour, but would change what `SubstitutionService` reports for PARTIAL vs. COVERED. Awaiting user decision before applying.

### BreakService.AutoDistributeAsync

Seeded random (`new Random(date.DayOfYear * 100 + date.Year % 100)`) ensures the same agent order on repeated runs for the same date. Agents on a VWIC rotation slot are skipped from breaks that overlap that slot. The `maxConcurrent` limit is computed from `voiceMinPct` (fraction of pool that must stay on the line).

### ShiftSyncService

When a sick leave or vacation is created/updated: writes ShiftType=SL or AL to ShiftEntries for all weekdays in the range, saving `PreviousStatus` in the `PreviousStatus` column. When reverted: restores `PreviousStatus` or deletes the synthetic entry if it was auto-generated.

### WicCoverageService — excluded agents

Three agents are hardcoded as excluded from coverage plans: `Ferenc Koreh`, `Tunde Szabo`, `Zsolt Fulop`. These are excluded from `GetAgentsAsync` results.

### REGIONAL assignment type (BackupC tier)

`WicAgentAssignments.AssignmentType = "REGIONAL"` is a third type (in addition to MAIN and BACKUP). These appear as the BackupC tier in WicCoverageService and are not counted in the BACKUP tier of SubstitutionService.

---

## 8. Overview Command Center

```
Overview.tsx
+-- KpiCard x5  (Open Today, At-Risk, Closure Risk, Absent, Coverage %)
|     data: /api/wic/forecast + /api/wic/briefing
+-- Coverage Heatmap
|     rows=WIC locations, cols=next 14 days
|     click -> SubstituteDrawer (Sheet)
+-- WicMapView  (react-leaflet + OpenStreetMap)
|     CircleMarkers coloured by today's status
|     STATUS_HEX hardcoded (Leaflet can't use CSS vars) -- intentional
|     ThemedTileLayer: light=OSM / dark=CartoDB dark_all
+-- Recommendations Panel
|     today's gaps from briefing + best substitute name
|     click -> SubstituteDrawer
+-- CommandPalette (Ctrl+K)
      searches /api/wic/locations, navigates to /wic-attendance?location=
```

---

## 9. Build Status (last confirmed)

**2026-06-15 — PS1_19_FinalBuildVerify.ps1 — all green**

- Frontend: 0 TypeScript errors, 1852 modules, 2.37s
- Backend: 0 errors, 0 warnings
- `/health` -> HTTP 200
- `/api/wic/locations` -> HTTP 200, 43 locations
- `/api/wic/forecast?horizon=7` -> HTTP 200
- `/api/wic/briefing` -> HTTP 200
- `/api/wic/substitutes?locationCode=DE~86150~Augsburg~Schaezlerstr.%203&date=2026-06-15` -> HTTP 200

---

## 10. What Was NOT Implemented

### Map connector lines

SVG lines from AT_RISK pins to nearest backup agents were spec'd but not built. `SubstituteDto` does not include `homeCoordinates`. AT_RISK pins use larger radius + thicker border instead.
**Fix:** Add `homeLocationCode` + `homeCoordinates` to `SubstituteDto`, draw `<Polyline>` in `Overview.tsx`.

### Team lead grouping in absence feed

`GET /api/wic/briefing` absence objects have no `teamLeadId`. A flat list is shown with a WarningBanner.
**Fix:** Add `teamLeadName` to `BriefingAbsenceDto`.

### WIC map when all coordinates are null

Map hidden, WarningBanner shown. Intentional — empty Leaflet map is useless.
**Fix:** Geocode any locations with null coordinates.

### SubstituteDto email fields

`Employees` now has `InfosysEmail` and `EonEmail` (from WicCoverageImport). `SubstituteDto.contactEmail` is still null because `SubstitutionService` was not updated to include the new columns. If email contact of substitutes is needed, add `InfosysEmail` to `SubstituteDto` and populate it in `SubstitutionService.GetSubstitutesAsync`.

### ShiftReorderService

`PATCH /api/shiftplan/reorder` accepts a list of employee IDs but performs no action (returns `{success:true, count:N}`). Order is managed entirely on the frontend side. The endpoint is vestigial; no frontend client call currently targets it.
