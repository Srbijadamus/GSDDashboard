# GSD Dashboard — Technical Documentation

## Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Backend runtime | ASP.NET Core (Minimal APIs) | 8.0 |
| ORM | Entity Framework Core | 8.0 |
| Database | SQL Server Express | 2022, `localhost\SQLEXPRESS` |
| Excel generation | ClosedXML (MIT) | 0.102.x |
| API docs | Swagger / Swashbuckle | dev only |
| Frontend framework | React | 19.x |
| Build tool | Vite | 8.x |
| Language | TypeScript | ~6.0 |
| Routing | React Router | 7.x |
| Server state | TanStack Query | 5.x |
| Tables | TanStack Table | 8.x |
| Charts | Recharts | 3.x |
| Map | react-leaflet + leaflet | 5.0 / 1.9.4 |
| UI / CSS | Tailwind CSS | 3.4.19 |
| Icons | Lucide React | latest |
| Fonts | IBM Plex Sans + IBM Plex Mono | — |
| Internationalisation | i18next + react-i18next | 26.x |
| Theme | next-themes | 0.4.x |
| Kiosk | Python FastAPI | 3.11+ |

EPPlus is explicitly **not** used (commercial licence). ClosedXML handles all `.xlsx` generation.

---

## Repository Layout

```
C:\GSDDashboard\
+-- Backend/
|   +-- GSDDashboard.API.csproj
|   +-- Program.cs                   # Entry point, all route registrations
|   +-- GSDContext.cs                # EF Core DbContext (19 DbSets)
|   +-- appsettings.json             # Connection string + CORS
|   +-- schema.sql                   # One-time DB init (core tables + seed)
|   +-- Services/                    # Domain services
|   |   +-- AvailabilityResolver.cs
|   |   +-- CoverageEvaluator.cs
|   |   +-- WicLocationMatcher.cs
|   |   +-- ReachabilityService.cs
|   |   +-- SubstitutionService.cs
|   |   +-- ForecastService.cs
|   |   +-- WhatIfService.cs
|   |   +-- BriefingService.cs
|   |   +-- ALPlanningService.cs
|   |   +-- WicCoverageService.cs
|   |   +-- WicCoverageImport.cs
|   |   +-- ... (other services)
|   +-- (service files in root Backend/)
|       +-- ShiftService.cs, SickLeaveService.cs, VwicService.cs,
|           BreakService.cs, WicShiftService.cs, WicCardsService.cs,
|           ShiftSyncService.cs, ShiftValidationService.cs, BackupService.cs,
|           VacationService.cs, ALBalanceService.cs, EmployeeService.cs,
|           AttendanceService.cs, PublicHolidayService.cs, TrainingService.cs,
|           PipelineService.cs, WicScheduleService.cs, OverviewService.cs,
|           ALCalendarService.cs, DashboardService.cs, ShiftReorderService.cs
|
+-- Frontend/
|   +-- package.json
|   +-- vite.config.ts
|   +-- tailwind.config.js
|   +-- src/
|       +-- main.tsx                 # Providers, i18n init, QueryClient
|       +-- App.tsx                  # Router, sidebar, layout, topbar
|       +-- index.css                # CSS custom properties (light + dark)
|       +-- api/client.ts            # All API calls in one place
|       +-- i18n/                    # i18next config + EN/DE JSONs
|       +-- pages/                   # One component per route
|       +-- components/              # Shared UI (Sheet, CoverageBadge, etc.)
|
+-- documentation/
+-- PS1_19_FinalBuildVerify.ps1      # Primary build + deploy script

C:\ShiftKiosk\
+-- server\
    +-- server.py                    # Python FastAPI kiosk, port 8000
```

---

## Backend Architecture

### Program.cs

Single file. Registers all services via DI, configures middleware (Swagger, CORS, static files), runs idempotent startup SQL to create new tables (BreakSlots, VwicRotationSlots, AgentReachableCities) and add new columns, runs WicCoverageImport seeder, then maps all Minimal API route groups.

### GSDContext.cs

EF Core `DbContext` with Windows Authentication (`Trusted_Connection=true`). `EnableRetryOnFailure(3)` for transient errors. 19 DbSets including SubstitutionHistory, BreakSlots, VwicRotationSlots, AgentReachableCities.

### Services

| Service | DI Lifetime | Responsibility |
|---------|-------------|---------------|
| `DashboardService` | Scoped | Top-level KPI metrics; per-TeamLead and per-WIC breakdowns |
| `ShiftService` | Scoped | Shift plan queries, Excel export, update |
| `ShiftSyncService` | Scoped | Bidirectional SickLeave/Vacation to ShiftEntries propagation with revert |
| `ShiftValidationService` | Scoped | Validates shift changes against German labour law rules |
| `AvailabilityResolver` | Scoped | Canonical absence resolver; SickLeaves take priority over ShiftEntries |
| `WicShiftService` | Scoped | WIC-specific shifts, on-site vs. office, assignment management |
| `WicCardsService` | Scoped | Per-location coverage status cards via CoverageCalculator |
| `CoverageEvaluator` | Scoped | Canonical COVERED/PARTIAL/UNCOVERED/CLOSED classifier |
| `ReachabilityService` | **Singleton** | Haversine matrix, 4h TTL cache, `IServiceScopeFactory` |
| `SubstitutionService` | Scoped | 4-tier ranked substitute engine |
| `BackupService` | Scoped | Older substitute engine at `/api/wic/backup` |
| `ForecastService` | Scoped | 14-day coverage forecast |
| `WhatIfService` | Scoped | What-if absence simulation |
| `BriefingService` | Scoped | Daily briefing JSON + 3-sheet Excel export |
| `ALPlanningService` | Scoped | AL planning per WIC location |
| `VwicService` | Scoped | Virtual WIC coverage 07-18; rotation plan generator |
| `BreakService` | Scoped | Voice-agent break scheduling, auto-distribution, VWIC-aware |
| `WicCoverageService` | Scoped | Agent and WIC coverage plan management |
| `WicCoverageImport` | Static | One-time startup seeder (skips if AgentReachableCities non-empty) |
| `SickLeaveService` | Scoped | Sick leave records, stats, create/patch/delete, ShiftSync, Excel |
| `VacationService` | Scoped | Vacation records, Excel export |
| `ALBalanceService` | Scoped | AL balance |
| `EmployeeService` | Scoped | Employee master data |
| `AttendanceService` | Scoped | Daily WIC attendance |
| `PublicHolidayService` | Scoped | Holiday calendar |
| `TrainingService` | Scoped | Training records |
| `PipelineService` | Scoped | Pipeline event CRUD |
| `WicScheduleService` | Scoped | WIC opening hours |
| `OverviewService` | Scoped | Cross-module overview aggregation |
| `ALCalendarService` | Scoped | Annual leave calendar view |
| `WicLocationMatcher` | Static | 30-entry alias dict, legacy code matching |
| `PlzBundesland` | Static | PLZ to Bundesland fallback (38-entry map) |
| `CoverageCalculator` | Static | Minute-based coverage overlap (dual open blocks) |

**Rule:** No service may contain its own `COVERED/PARTIAL/UNCOVERED/CLOSED` if-else chain. All coverage classification goes through `CoverageEvaluator`.

---

## Database Schema

**Server:** `localhost\SQLEXPRESS`
**Database:** `GSDDashboard`
**Auth:** Windows Authentication

### Tables

| Table | Key Columns | Notes |
|-------|-------------|-------|
| `Employees` | EmployeeId, FullName, FirstName, LastName, PrimaryRole, SecondaryRole, TeamLeadName, Engagement, Bundesland, IsActive, **PrimaryKid, SecondaryKid, InfosysEmail, EonEmail, HasCar, GroupRegion** | ~130 rows. New columns added at startup if absent |
| `ShiftEntries` | EmployeeId, ShiftDate, ShiftType, ShiftCode, ShiftStart, ShiftEnd, AgentTask, LocationId, IsWicDuty, **AutoGenerated, SourceModule, SourceId, PreviousStatus** | Authoritative for absence types |
| `WicShiftEntries` | EmployeeId, ShiftDate, SupportLocation, IsOnSite, Task | WIC duty records |
| `WicLocations` | Id, LocationCode, DisplayName, City, Country, Lat, Lon, Bundesland, MinAgentsRequired, LocationCodeLegacy, PostalCode, IsActive, **OpeningDay, Comment** | 43 rows (41 DE + 2 NL). New columns added at startup |
| `WicAgentAssignments` | LocationCode, EmployeeName, AssignmentType, **IsActive, Notes** | MAIN, BACKUP, or REGIONAL. Join: `e.FullName = waa.EmployeeName` |
| `WicOpeningHours` | LocationCode, DayOfWeek, OpenTime, CloseTime, IsClosed, **OpenTime2, CloseTime2** | DayOfWeek: .NET convention 0=Sun...6=Sat. OpenTime2/CloseTime2 for split-hours locations |
| `WicPipelineItems` | Id, PipelineDate, PipelineDateEnd, Title, Description, PrimaryAgent, BackupAgent, AdditionalAgentsNeeded, HandledBy, CreatedBy, CreatedAt, Status, StartTime, EndTime, AgentsRequired | Pipeline events |
| `DailyAttendance` | LocationCode, Date, Status | assigned / WO / closed / PH |
| `SickLeaves` | EmployeeId, FirstDay, LastDay, LeaveType, DurationDays, ChildName, Comments, SourceSheet | **FirstDay/LastDay** (NOT StartDate/EndDate) |
| `Vacations` | EmployeeId, FirstDay, LastDay, ApprovalStatus, DurationDays | **FirstDay/LastDay** (NOT StartDate/EndDate) |
| `ALBalance` | EmployeeId, Year, EligibleDays, TakenDays, RemainingDays | — |
| `PublicHolidays` | HolidayDate, Name, Bundesland, IsNational | IsNational=true for federal holidays |
| `TrainingTopics` / `TrainingSessions` | Topic metadata and session assignments | — |
| `SubstitutionHistory` | EmployeeId, LocationCode, Date, SourceType, AssignedAt, LoadScore | Populated at runtime; 30-day window for fairness penalty |
| `BreakSlots` | Id, EmployeeId, BreakDate, BreakStart, BreakEnd, ActualStart, ActualEnd, DurationMinutes, Status, AgentRole | Created at startup if absent. Status: SCHEDULED/ON_BREAK/DONE/CANCELLED |
| `VwicRotationSlots` | Id, EmployeeId, RotationDate, SlotStart, SlotEnd | Created at startup if absent. Persisted rotation plan |
| `AgentReachableCities` | Id, EmployeeId, EmployeeName, City, Source | Created at startup if absent. Seeded by WicCoverageImport |
| `LeaveQuota` | EmployeeId, Year, QuotaDays | Annual leave quota override |

### Critical Schema Notes

- `SickLeaves` uses `FirstDay` / `LastDay` — do NOT use `StartDate` / `EndDate`
- `Vacations` uses `FirstDay` / `LastDay` — do NOT use `StartDate` / `EndDate`
- `WicOpeningHours.DayOfWeek` = .NET convention (0=Sun, 1=Mon ... 6=Sat)
- `WicAgentAssignments.LocationCode` uses old-style codes (`DE_Dortmund`); `WicLocations.LocationCodeLegacy` maps to them
- `WicLocations.LocationCode` uses new tilde-style codes (`DE~44139~Dortmund~Str.`)
- `WicAgentAssignments` join is by **FullName** string, not EmployeeId
- `SickLeaves.LeaveType` values: `"SL"` (system), `"Self"` (employee-reported), `"Child"` (child illness)
- Absence detection canonical set: `SL, AL, UL, PH, LPH, RESIGNED` (defined in `AvailabilityResolver.FullAbsenceTypes`)
- `HALF_AL` = 0.5 coverage contribution (not in full-absence set; fractional credit)
- `TRAINING` is NOT in `FullAbsenceTypes` (agent is present at training, not absent from WIC)
- 3 tables created at startup via `ExecuteSqlRaw` if not exists: BreakSlots, VwicRotationSlots, AgentReachableCities
- 8 new columns added to Employees at startup if not exists: PrimaryKid, SecondaryKid, InfosysEmail, EonEmail, HasCar, GroupRegion
- 2 new columns added to WicLocations at startup if not exists: OpeningDay, Comment

### SSP / Voice Agent Base Location

SSP and Voice agents without a WIC assignment use **Dusseldorf HQ** as their base for distance calculations: `lat=51.2154, lon=6.7837`.

---

## API Endpoints

All routes registered in `Program.cs` (Minimal API syntax).

### Dashboard

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/dashboard/summary?date=` | KPI counts by shift type |
| GET | `/api/dashboard/teamlead-summary?date=` | Per-TeamLead breakdown |
| GET | `/api/dashboard/wic-cards?date=` | WIC coverage cards (older format) |

### WIC Core

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/wic/locations` | All 43 WIC locations |
| GET | `/api/wic?from=&to=&locationCode=&employeeId=&teamLead=` | WIC shift entries |
| GET | `/api/wic/cards?date=&country=` | Per-location coverage status cards |
| GET | `/api/wic/coverage?date=` | Per-location agent coverage |
| GET | `/api/wic/available-hours?date=` | Free hours per agent at their WIC |
| GET | `/api/wic/open?date=&horizon=` | Open/coverage status, N days |
| PATCH | `/api/wic/shifts/{id}` | Update Task/SupportLocation/IsOnSite on a WicShiftEntry |
| POST | `/api/wic/shifts` | Create new WicShiftEntry |
| POST | `/api/wic/assign` | Assign agent to WIC location for a date |
| GET | `/api/wic/download?from=&to=` | Excel export of WIC shifts |
| GET | `/api/wic/backup?locationCode=&date=&horizon=` | Older substitute candidates (BackupService) |
| GET | `/api/wic/substitutes?locationCode=&date=&horizon=&absentIds=` | Ranked substitutes (SubstitutionService) |
| POST | `/api/wic/substitutes/accept` | Accept substitute (writes SubstitutionHistory + ShiftEntry) |
| GET | `/api/wic/forecast?horizon=14&locationCode=` | Coverage forecast (horizon clamped 1-30) |
| GET | `/api/wic/whatif?absentEmployeeId=&date=&horizon=` | What-if simulation |
| GET | `/api/wic/briefing` | Today's absences + gaps + next at-risk |
| GET | `/api/wic/briefing/export` | Excel export (3 sheets: Absences, Gaps, AT_RISK) |
| GET | `/api/wic/reachability?from=&to=` | Haversine distance matrix |
| GET | `/api/wic/reachability/sanity` | Berlin to Munich ~504 km sanity check |
| GET | `/api/wic/schedule` | WIC opening hours |
| POST | `/api/wic/al-planning` | AL planning for a WIC location and date range |

### Overview

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/overview/wic-status?date=&horizon=` | Per-location per-day status for N days |
| GET | `/api/overview/detail?type=&date=` | Agents by type (VOICE/CHAT/BACKLOG/AL/SL/TRAINING/WIC) |

### VWIC

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/vwic/daily?date=` | 24h VWIC coverage timeline |
| POST | `/api/vwic/assign` | Assign agent to VWIC slot |
| GET | `/api/vwic/candidates?date=` | Voice agents eligible for VWIC |
| PUT | `/api/vwic/agents/add` | Set agent SecondaryRole = VWIC |
| PUT | `/api/vwic/agents/remove` | Clear agent SecondaryRole |
| POST | `/api/vwic/rotation-plan` | Generate daily rotation plan |
| POST | `/api/vwic/rotation-plan/save` | Persist rotation plan to VwicRotationSlots |
| POST | `/api/vwic/rotation-plan-week` | Generate weekly rotation plan (Mon-Fri) |
| POST | `/api/vwic/rotation-plan-week/export` | Excel export of week plan |

### Break Planner

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/breaks?date=` | All break slots for a date |
| POST | `/api/breaks/auto-distribute` | Auto-assign 30-min breaks |
| POST | `/api/breaks/{id}/start` | Mark break as started |
| POST | `/api/breaks/{id}/end` | Mark break as done |
| POST | `/api/breaks/{id}/cancel` | Cancel a break slot |
| POST | `/api/breaks/manual` | Create/replace manual break for an agent |

### WIC Coverage

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/wic-coverage/agents?search=` | Agents with KIDs, emails, reachable cities, WIC roles |
| GET | `/api/wic-coverage/agents/{kid}` | Single agent by PrimaryKid |
| PATCH | `/api/wic-coverage/agents/{kid}` | Update HasCar and/or GroupRegion |
| GET | `/api/wic-coverage/wics?search=` | Active WICs with MAIN/BACKUP counts |
| GET | `/api/wic-coverage/wics/{locationCode}` | Full WIC coverage plan (Main/BackupA/BackupB/BackupC) |
| GET | `/api/wic-coverage/wics/{locationCode}/reachable-agents` | Agents reachable by city |
| POST | `/api/wic-coverage/wics/{locationCode}/backup-b` | Pin a BackupB agent to BACKUP assignment |

### Shifts

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/shifts?from=&to=&teamLead=&role=&engagement=&shiftType=` | Full shift plan |
| GET | `/api/shifts/working-today?date=` | Agents working today |
| GET | `/api/shifts/download?from=&to=` | Excel export |
| PATCH | `/api/shifts/{id}` | Update ShiftType/times/task |
| POST | `/api/shifts/validate` | Validate shift change against labour law rules |

### Employees

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/employees?...` | Employee list |
| GET | `/api/employees/{id}` | Single employee |
| GET | `/api/employees/{id}/timeline` | Employee shift timeline |
| POST | `/api/employees` | Create employee |
| DELETE | `/api/employees/{id}` | Delete employee |
| PATCH | `/api/employees/{id}/albalance` | Update AL balance |

### Leave

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/sickleave?from=&to=&teamLead=&type=&activeOnly=` | Sick leave records |
| GET | `/api/sickleave/active?date=` | Active sick leaves on a date |
| GET | `/api/sickleave/stats?from=&to=` | Aggregate stats |
| POST | `/api/sickleave` | Add sick leave (triggers ShiftSync) |
| PATCH | `/api/sickleave/{id}` | Update sick leave (reverts + re-syncs ShiftEntries) |
| DELETE | `/api/sickleave/{id}` | Delete sick leave record |
| GET | `/api/sickleave/download?from=&to=` | Excel export |
| GET | `/api/vacations?...` | Vacation records |
| GET | `/api/vacations/active?date=` | Active vacations on a date |
| GET | `/api/vacations/upcoming?days=` | Upcoming vacations |
| DELETE | `/api/vacations/{id}` | Delete vacation |
| GET | `/api/vacations/download?from=&to=` | Excel export |
| GET | `/api/albalance` | AL balance per employee |
| GET | `/api/albalance/{id}` | Single AL balance |
| PATCH | `/api/albalance/{id}` | Update AL balance |

### Pipeline + Calendar + Other

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/pipeline` | Pipeline events |
| POST | `/api/pipeline` | Create event |
| PATCH | `/api/pipeline/{id}` | Update event |
| DELETE | `/api/pipeline/{id}` | Delete event |
| GET | `/api/alcalendar?from=&to=` | Annual leave calendar view |
| GET | `/api/attendance?...` | Daily attendance records |
| GET | `/api/attendance/download?...` | Excel export |
| GET | `/api/training` | Training records |
| GET | `/api/wic/schedule` | WIC opening hours |
| GET | `/api/public-holidays` | Public holiday calendar |
| GET | `/health` | Liveness probe |
| GET | `/swagger` | Swagger UI (dev only) |

`MapFallbackToFile("index.html")` serves the React SPA for all unmatched routes in production.

---

## Frontend Architecture

### Bootstrap (main.tsx)

```
<ThemeProvider attribute="class" defaultTheme="system" enableSystem>
  <QueryClientProvider client={queryClient}>
    <App />
  </QueryClientProvider>
</ThemeProvider>
```

i18next initialised before React mounts. Language detection: `localStorage` then browser `navigator`.

### Layout (App.tsx)

```
+--------------------------------------------------+
|  Top bar: app name | date/horizon | Ctrl+K       |
|           ThemeToggle | DE/EN toggle              |
+--------------+-----------------------------------+
|  Sidebar     |                                   |
|  (nav links) |     <page component>              |
|              |                                   |
+--------------+-----------------------------------+
```

Horizon selector (7 or 14 days) only visible on the Overview page.

### Routes

| Route | Component file | Write ops |
|-------|---------------|-----------|
| `/` | Overview.tsx | None |
| `/shifts` | Shifts.tsx | None |
| `/wic-shifts` | WicShifts_old.tsx | Reassign agents, new shift modal |
| `/vwic` | VWICPage.tsx | Assign/manage agents, rotation plan, save |
| `/breaks` | BreakPlanner.tsx | Auto-distribute, start/end/cancel, manual |
| `/wic-attendance` | WicAttendance.tsx | Accept substitute, assign agent, check-in |
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

### Theme System

CSS custom properties in `index.css`. `:root` = light, `.dark` = dark (navy palette).

| Variable | Light | Dark |
|----------|-------|------|
| `--bg` | white | dark navy `#0b0f1a` |
| `--card` | light card | `#131928` |
| `--accent` | blue | blue |
| `--green` | green | green |
| `--warn` | orange | orange |
| `--danger` | red | red |
| `--status-covered` | green | green |
| `--status-partial` | orange | orange |
| `--status-uncovered` | red | red |
| `--status-closed` | slate | slate |

Leaflet cannot use CSS variables in canvas/SVG — `STATUS_HEX` in `Overview.tsx` holds hardcoded hex values for map pins. This is intentional.

### API Client (src/api/client.ts)

Single source of truth for all HTTP calls. Base URL from `VITE_API_BASE_URL` (fallback: `http://localhost:5000`). Exports `apiFetch<T>`, `downloadExcel`, and a typed `api` object.

TanStack Query stale times: locations 10 min, forecast/briefing 5 min, static data 10 min.

---

## Kiosk Server

| Property | Value |
|----------|-------|
| Language | Python + FastAPI |
| Port | 8000 |
| Entry point | `C:\ShiftKiosk\server\server.py` |
| Tunnel | https://ssr7tm2l-8000.euw.devtunnels.ms |
| Dashboard | https://ssr7tm2l-8000.euw.devtunnels.ms/dashboard |

Physical check-in/check-out data for WIC agents. Separate from the ASP.NET Core backend.

---

## Shift Type Reference

| Code | Meaning |
|------|---------|
| `WORKING` | Regular working shift |
| `WIC_DUTY` | On-site WIC duty |
| `AL` | Annual leave |
| `HALF_AL` | Half-day annual leave (0.5 coverage credit) |
| `SL` | Sick leave |
| `UL` | Unpaid leave |
| `OFF` | Off day |
| `OFF_WEEKEND` | Weekend (raw "OFFWE" maps to this) |
| `PH` | Public holiday |
| `LPH` | Local (regional) public holiday |
| `TRAINING` | Training day |
| `RESIGNED` | Employee left |
| `CD` | Compensation day |
| `OL` | Other leave |
| `CO` | Comp off |

Canonical full-absence set (AvailabilityResolver.FullAbsenceTypes): `SL, AL, UL, PH, LPH, RESIGNED`.
`HALF_AL` = 0.5 coverage contribution (not in full-absence set; fractional credit via `Math.Floor`).
`TRAINING` is NOT in the full-absence set (agent is present at training, not absent from WIC).

---

## Parsing Notes

- WIC shifts matched with `.Contains("WIC")` — raw data has variable spacing.
- HALF_AL has two raw formats: `"HAL *"` (prefix) and `"* HAL "` (suffix) — both map to `HALF_AL`.
- Team lead names may carry a trailing `\n`; services always `.Trim()` before comparison.
- Shift times stored as `varchar` (e.g. `"08:00"`), not SQL `TIME`.
- `WicAgentAssignments` join is string-based: `e.FullName = waa.EmployeeName`.
- Raw "OFFWE" from the upstream sheet maps to `OFF_WEEKEND` in ShiftTypes.Parse().
