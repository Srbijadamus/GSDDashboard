# WorkForce Pulse — Technical Documentation

---

## Naming

The product name is **WorkForce Pulse**. Infrastructure names remain `GSDDashboard` — path `C:\GSDDashboard`, database `GSDDashboard`, scheduled task `GSDDashboard-Backend`, namespace `GSDDashboard.API`, repository, tunnel URLs. **This mismatch is intentional.** Renaming the infrastructure breaks the deploy chain and tunnels. Do not "align" them.

The assistant is named **Pulse Assistant**. The route remains `/assistant`.

`GSD` as a business domain term remains untouched: `GSD backlog`, `GSD duty`, `Global Service Desk`, `IsGSDDay`, `AgentTask = 'GSD'`, column names and enum values. This is a domain term, not a brand.

---

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

### Execution Strategy — Required for Manual Transactions

`EnableRetryOnFailure(3)` makes EF Core refuse to open a manual transaction unless the call is wrapped in `CreateExecutionStrategy().ExecuteAsync()`. Calling `BeginTransactionAsync()` directly throws `InvalidOperationException` at the strategy layer — before the transaction opens and before any `catch (DbUpdateException)` runs. The caller gets HTTP 500 with no useful error surfaced.

**Rule:** Every endpoint that calls `BeginTransactionAsync()` must use this pattern:

```csharp
IResult? errorResult = null;
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () => {
    await using var tx = await db.Database.BeginTransactionAsync();
    try {
        // all DB mutations + SaveChangesAsync()
        await tx.CommitAsync();
    } catch (Exception ex) {          // catch Exception, not DbUpdateException
        await tx.RollbackAsync();
        logger.LogError(ex, "...");
        errorResult = Results.Conflict(new { error = "..." });
    }
});
if (errorResult != null) return errorResult;
```

Catch `Exception`, not `DbUpdateException` — the strategy conflict exception is not a `DbUpdateException`. Current implementation: `SubstitutionModule.cs` (Accept Substitute endpoint).

### Services

| Service | DI Lifetime | Responsibility |
|---------|-------------|---------------|
| `DashboardService` | Scoped | Top-level KPI metrics; per-TeamLead and per-WIC breakdowns |
| `ShiftService` | Scoped | Shift plan queries, Excel export, update |
| `ShiftSyncService` | Scoped | Bidirectional SickLeave/Vacation to ShiftEntries propagation with revert |
| `ShiftValidationService` | Scoped | Validates shift changes against German labour law rules |
| `AvailabilityResolver` | Scoped | Canonical absence resolver; SickLeaves take priority over ShiftEntries |
| `WicShiftService` | Scoped | WIC-specific shifts, on-site vs. office, assignment management |
| `WicCardsService` | Scoped | Per-location coverage status cards via CoverageCalculator. Uses `Vacations` table for AL detection (not `ShiftEntries.ShiftType`). See **Vacations / AL ShiftEntries divergence** note below. |
| `CoverageEvaluator` | Scoped | Canonical COVERED/PARTIAL/UNCOVERED/CLOSED classifier |
| `ReachabilityService` | **Singleton** | Haversine matrix, 4h TTL cache, `IServiceScopeFactory` |
| `SubstitutionService` | Scoped | 4-tier ranked substitute engine. Section (A) includes both `BACKUP` and `REGIONAL` assignment types from `WicAgentAssignments`. `sourceType` field in candidates reflects which type the agent holds. |
| `BackupService` | Scoped | Older substitute engine at `/api/wic/backup` |
| `ForecastService` | Scoped | 14-day coverage forecast |
| `WhatIfService` | Scoped | What-if absence simulation |
| `BriefingService` | Scoped | Daily briefing JSON + 3-sheet Excel export |
| `ALPlanningService` | Scoped | AL planning per WIC location |
| `VwicService` | Scoped | Virtual WIC coverage 07-18; rotation plan generator |
| `BreakService` | Scoped | Voice-agent break scheduling, auto-distribution, VWIC-aware |
| `WicCoverageService` | Scoped | Agent and WIC coverage plan management — serves `/api/wic-coverage/*`; static MAIN/BACKUP/REGIONAL directory only, **no date parameter, no COVERED/PARTIAL/UNCOVERED computation** |
| `WicCoverageImport` | Static | One-time startup seeder (skips if AgentReachableCities non-empty) |
| `WicConflictDetector` | Scoped | Detects agents assigned to multiple WIC locations on the same day. Reports-only (never writes). Three conflict types: `OVERLAP` (hours overlap → real conflict), `SPLIT_SHIFT` (non-overlapping hours, e.g. Demmin dual-location — intentional design, hidden by default), `CLOSED_LOCATION` (agent on-site at a location closed that day). |
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

### Three WIC Coverage Sources

Three distinct services compute WIC coverage. They use different absence signals and serve different purposes:

| Service | Absence signal | Date scope | COVERED/PARTIAL/UNCOVERED? | Notes |
|---------|---------------|-----------|---------------------------|-------|
| `ForecastService` | `ShiftEntries.ShiftType` (AL, SL, etc.) via `GetWicContribution` | Multi-day forecast | Yes — via `CoverageEvaluator` | Authoritative for risk forecasting. Reads `WicOpeningHours` at load (L62) — closed days return `IsAtRisk=false`, skipped entirely. Verified correct 2026-09-03: 190 at-risk on open days, 0 on closed days. |
| `OverviewService` | `ShiftEntries.ShiftType` via `GetAbsentIdsAsync` pre-filter | Today snapshot | Yes — via `CoverageEvaluator` | Feeds the Overview page daily KPIs. |
| `WicCardsService` | `Vacations` table (FirstDay ≤ date ≤ LastDay, no status filter) + `SickLeaves` | Single date | Yes — via `CoverageCalculator` (minute-based) | Feeds `/api/dashboard/wic-cards`. Uses Vacations for AL detection — diverges from ShiftEntries. 72 vacation rows in 2026-06-01–2026-12-31 have no matching AL ShiftEntry; 8 AL ShiftEntries have no matching Vacation. Impact on WIC on-site agents for specific dates is zero for 2026-09-08/2026-09-15. |
| `WicCoverageService` | None | No date | Static roles only | Serves `/api/wic-coverage/*`. Returns MAIN/BACKUP/REGIONAL assignment directory. **Never** computes COVERED/PARTIAL/UNCOVERED. |

### Vacations / AL ShiftEntries divergence

`Vacations` and AL `ShiftEntries` are separate imports and are not auto-synced. Checked 2026-09-03 for Jun–Dec 2026: 72 `Vacations` rows without a matching AL `ShiftEntry`, 8 in the reverse direction. `WicCardsService` reads `Vacations`; `OverviewService` and `ForecastService` do not. Operational impact on WIC coverage verified as zero for the tested dates — no affected agent had `IsOnSite=1` on 2026-09-08 or 2026-09-15. Accepted as known divergence, not fixed. Re-check if the WIC roster grows.

`Vacations.ApprovedDenied` (actual column name — **not** `ApprovalStatus`) is always `NULL` in current data. No approval filtering is applied anywhere. `WicCardsService` loads all `Vacations` rows unconditionally.

---

## Data Model: ShiftEntries and WicShiftEntries

These two tables are separate and have no foreign key or cascade relationship. They are joined in code by `(EmployeeId, ShiftDate)`.

| Table | Source of truth for | Populated by |
|-------|--------------------|-----------| 
| `ShiftEntries` | Shift type (WORKING, AL, SL, WIC_DUTY, HALF_AL, etc.) and shift times | Excel import via `ShiftService`; SickLeave/Vacation creation via `ShiftSyncService` |
| `WicShiftEntries` | WIC duty details: which location (`SupportLocation`), on-site vs. remote, agent task | Excel import; `SubstitutionModule` (when a substitute is accepted) |

**Source of truth for "where is who today":** `ShiftEntries.ShiftType` determines if an agent is present, absent, or on WIC duty. `WicShiftEntries.SupportLocation` determines which WIC location they cover. Both tables are read in parallel for coverage calculations — mismatches produce artifacts.

### WicShiftEntries Field Reference

| Field | Type | Meaning | Who writes it |
|-------|------|---------|---------------|
| `IsOnSite` | bool | Agent is physically on-site at the WIC location | SubstitutionModule (Accept), WicShiftService (Assign Agent, GetOpenAsync) |
| `IsGSDDay` | bool | Legacy import flag: agent worked a GSD shift instead of WIC that day | **Excel import only.** Current code always writes `false`. No coverage service reads this field. Exported to Excel column "GSD Day" in the WIC shift download. |
| `IsOffDay` | bool | Legacy import flag: agent had a day off | **Excel import only.** Current code always writes `false`. Read by `WicScheduleService` (renders "OFF" in CSV) and `GetAvailableHoursAsync` (excludes rows where `IsOffDay=true`). |
| `Task` | string? | Default `"WIC"`. Also `"VWIC"`, `"SL"` (garbage from import), `null` | Set to `"WIC"` by SubstitutionModule and Assign Agent |
| `SupportLocation` | string? | Free-text location name. Resolved to `WicLocation` via `WicLocationMatcher.MatchesSupportLocation` | Excel import; SubstitutionModule; Assign Agent |

**IsGSDDay vs. IsOnSite:** Both can be non-zero in historical data from the Excel WIC plan. `IsGSDDay=1` means the row was scheduled but the agent ended up doing GSD work; `IsOnSite=0` means not scheduled at all. In current code these are always written as `false`/`false` — historical values are read-only artifacts from the import.

**Coverage query filter pattern (all services):** `w.IsOnSite == true` — `IsGSDDay` is intentionally NOT used in coverage queries because the Excel import is no longer the authoritative source for GSD vs. WIC. `ShiftEntries.ShiftType == "WIC_DUTY"` is the authoritative signal (via `AvailabilityResolver.GetWicContribution`).

### Known Data Patterns (not bugs)

**`SupportLocation = 'Global Service Desk'`** — WIC agents have a `WicShiftEntries` row for every day of their schedule. On days they work in the GSD office (not at a WIC site), the Excel import writes `SupportLocation='Global Service Desk'` with `IsOnSite=0`. These rows are harmless — all coverage queries filter `w.IsOnSite == true`, so they are never evaluated. Currently ~1 264 rows.

**Multiple rows per `(EmployeeId, ShiftDate)` is legitimate** — an agent may be the scheduled backup for several WIC locations on the same day. Do not treat these as duplicates without first verifying that the `SupportLocation` values are identical (after normalising encoding — see below). Currently ~102 such agent+date combinations; 0 true duplicates confirmed after encoding normalisation.

**Duplicate-check caveat:** comparing `SupportLocation` as raw strings treats `Fürstenwalde` and `FÃ¼rstenwalde` as two distinct locations and will produce a false "no duplicates" result. Always normalise via `WicLocationMatcher.RepairDoubleEncoding()` (or SQL `COLLATE`) before comparing. During the June 2026 encoding cleanup, 41 rows appeared to be legitimate multi-location entries until the constraint-level repair revealed they were encoding duplicates.

**Double-encoding artifacts in `SupportLocation`** — historical import rows sometimes contain Windows-1252 mis-decoded UTF-8 sequences (e.g. `MÃ¼nchen` instead of `München`). `WicLocationMatcher.RepairDoubleEncoding()` corrects these at query time; they have no effect on coverage. The June 2026 batch cleanup removed 41 duplicate rows and renamed the remaining 50 oştećene values in place.

**Demmin dual-location (intentional design):** Demmin has two physical locations — *Am Hanseufer* (Mon/Wed 08:00-15:00, Tue/Thu 10:00-12:00) and *Woldeforster Str.* (Tue/Thu 13:00-15:00). A single MAIN agent covers both sequentially on Tue/Thu with no overlap. This creates two `WicShiftEntries` rows for the same `(EmployeeId, ShiftDate)` resolving to two distinct `LocationCode` values. `WicConflictDetector` classifies this as `SPLIT_SHIFT` (hidden by default) — not `OVERLAP`. Do not treat as a data quality issue.

**`SupportLocation = 'VWIC'`** — agent was assigned to Virtual WIC (remote phone coverage) on that day, not a physical WIC site. `IsOnSite=1` is correct. `WicLocationMatcher` intentionally does not map this value, so these rows never enter coverage calculations. Do not delete — they are valid historical schedule records. Currently 8 rows. Note: these rows are not synchronised with `VwicRotationSlots`; the Excel-import VWIC schedule and the in-app VWIC rotation plan are separate systems with no shared data.

### Known Artifacts

1. **`IsWicDuty=1` on non-WIC_DUTY rows.** Any code path that changes `ShiftType` on an existing row (sick leave sync, vacation sync, `AssignShiftAsync`, PATCH endpoint) used to leave `IsWicDuty=true` if the row previously had `ShiftType=WIC_DUTY`. Fixed at the four write sites in `ShiftSyncService.cs` and `ShiftService.cs` — these now clear `IsWicDuty` whenever `ShiftType` is set to anything other than `WIC_DUTY`. Startup safety net: `UPDATE ShiftEntries SET IsWicDuty=0 WHERE ShiftType != 'WIC_DUTY' AND IsWicDuty=1` (catches any residual rows from before the fix, or from external writes).

2. **Orphan `WicShiftEntries` with `NULL SupportLocation`.** A partial write (e.g. failed Accept Substitute transaction pre-B1 fix) can leave a `WicShiftEntry` with `SupportLocation=NULL`. This row satisfies no coverage query but bypasses the `UNIQUE` constraint on `(EmployeeId, ShiftDate, SupportLocation)` because SQL Server treats NULL values as distinct in uniqueness checks. One-shot cleanup: `POST /api/admin/cleanup-wic-orphans` (deletes rows where NULL-location entry coexists with a non-NULL entry for the same agent+date).

3. **Duplicate entries from partial writes.** The current `SubstitutionModule.cs` cleans up NULL-location `WicShiftEntries` for the same `(EmployeeId, ShiftDate)` before inserting the new targeted row, preventing future duplicates.

---

## Database Schema

**Server:** `localhost\SQLEXPRESS`
**Database:** `GSDDashboard`
**Auth:** Windows Authentication

### Tables

| Table | Key Columns | Notes |
|-------|-------------|-------|
| `Employees` | EmployeeId, FullName, FirstName, LastName, PrimaryRole, SecondaryRole, TeamLeadName, Engagement, Bundesland, IsActive, **PrimaryKid, SecondaryKid, InfosysEmail, EonEmail, HasCar, GroupRegion, ShiftPattern** | ~130 rows. New columns added at startup if absent. `ShiftPattern`: EARLY/MORNING/AFTERNOON/NIGHT/BACKUP (agent's default daily shift slot) |
| `ShiftEntries` | EmployeeId, ShiftDate, ShiftType, ShiftCode, ShiftStart, ShiftEnd, AgentTask, LocationId, IsWicDuty, **AutoGenerated, SourceModule, SourceId, PreviousStatus** | Authoritative for absence types |
| `WicShiftEntries` | EmployeeId, ShiftDate, SupportLocation, IsOnSite, Task | WIC duty records |
| `WicLocations` | Id, LocationCode, DisplayName, City, Country, Lat, Lon, Bundesland, MinAgentsRequired, LocationCodeLegacy, PostalCode, IsActive, **OpeningDay, Comment** | 43 rows (41 DE + 2 NL). New columns added at startup |
| `WicAgentAssignments` | LocationCode, EmployeeName, AssignmentType, **IsActive, Notes** | MAIN, BACKUP, or REGIONAL. Join: `e.FullName = waa.EmployeeName` |
| `WicOpeningHours` | LocationCode, DayOfWeek, OpenTime, CloseTime, IsClosed, **OpenTime2, CloseTime2** | DayOfWeek: .NET convention 0=Sun...6=Sat. OpenTime2/CloseTime2 for split-hours locations |
| `WicPipelineItems` | Id, PipelineDate, PipelineDateEnd, Title, Description, PrimaryAgent, BackupAgent, AdditionalAgentsNeeded, HandledBy, CreatedBy, CreatedAt, Status, StartTime, EndTime, AgentsRequired | Pipeline events |
| `DailyAttendance` | LocationCode, Date, Status | assigned / WO / closed / PH |
| `SickLeaves` | EmployeeId, FirstDay, LastDay, LeaveType, DurationDays, ChildName, Comments, SourceSheet | **FirstDay/LastDay** (NOT StartDate/EndDate) |
| `Vacations` | EmployeeId, FirstDay, LastDay, WorkDaysNet, ApprovedDenied, ApproverName, ApproverDate, Comments, SourceYear, SourceSheet | **FirstDay/LastDay** (NOT StartDate/EndDate). `ApprovedDenied` is always NULL in current data — no approval filtering is applied. |
| `ALBalance` | EmployeeId, EmployeeName, EligibleDays, PlannedTakenAL, RemainingAL, CountSL, CountUL, CountWorkingSundays, CountFreeSundays | `PlannedTakenAL` and `RemainingAL` are `DECIMAL(10,1)` — allows half-day values (e.g. 14.5). ALTERed from `int` at startup if the column is still `int`. |
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
- `Vacations.ApprovedDenied` is always NULL — do NOT filter on it; use it only to confirm no approval gate exists
- `WicOpeningHours.DayOfWeek` = .NET convention (0=Sun, 1=Mon ... 6=Sat)
- `WicAgentAssignments.LocationCode` uses old-style codes (`DE_Dortmund`); `WicLocations.LocationCodeLegacy` maps to them
- `WicLocations.LocationCode` uses new tilde-style codes (`DE~44139~Dortmund~Str.`)
- `WicAgentAssignments` join is by **FullName** string, not EmployeeId
- `SickLeaves.LeaveType` values: `"SL"` (system), `"Self"` (employee-reported), `"Child"` (child illness)
- Absence detection canonical set: `SL, AL, UL, PH, LPH, RESIGNED` (defined in `AvailabilityResolver.FullAbsenceTypes`)
- `HALF_AL` = 0.5 coverage contribution (not in full-absence set; fractional credit)
- `TRAINING` is NOT in `FullAbsenceTypes` (agent is present at training, not absent from WIC)
- 3 tables created at startup via `ExecuteSqlRaw` if not exists: BreakSlots, VwicRotationSlots, AgentReachableCities
- 9 new columns added to Employees at startup if not exists: PrimaryKid, SecondaryKid, InfosysEmail, EonEmail, HasCar, GroupRegion, ShiftPattern
- 2 new columns added to WicLocations at startup if not exists: OpeningDay, Comment
- `Vacations.WorkDaysNet` is `DECIMAL(10,1)` — ALTERed from `int` at startup. Half-day AL entries use `WorkDaysNet=0.5`.
- `ALBalance.PlannedTakenAL` and `ALBalance.RemainingAL` are `DECIMAL(10,1)` — ALTERed from `int` at startup (requires dynamically dropping the auto-named DEFAULT constraint before ALTER, then re-adding it). Threshold checks `≤5` and `≤10` work correctly with decimals.

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
| GET | `/api/wic/conflicts?from=&to=&includeSplitShifts=` | Agents with conflicting WIC assignments. `includeSplitShifts=false` (default) returns only `OVERLAP` and `CLOSED_LOCATION`. `includeSplitShifts=true` also returns `SPLIT_SHIFT` (Demmin-style complementary schedules). Reports only — no data is mutated. |

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
| POST | `/api/vacations` | Create vacation; body includes `isHalfDay: bool` — sets `WorkDaysNet=0.5`, `ShiftType=HALF_AL`, forces `lastDay=firstDay` |
| PATCH | `/api/vacations/{id}` | Update vacation |
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

### Admin

| Method | URL | Description |
|--------|-----|-------------|
| POST | `/api/admin/cleanup-wic-orphans` | One-shot: deletes `WicShiftEntries` rows where `SupportLocation IS NULL` and a non-NULL row exists for the same `(EmployeeId, ShiftDate)`. Not in startup path — call manually. |
| GET | `/api/admin/wic-migration/dry-run` | Reports how many `WicShiftEntries` (IsOnSite=1, IsGSDDay=0) have a paired `ShiftEntry` with `ShiftType≠WIC_DUTY` and would be candidates for migration to `WIC_DUTY`. Returns counts by disposition and the list of unmatched `SupportLocation` values. Read-only — no data changes. |

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
| `/employees` | Employees.tsx | Create/edit/delete employees (incl. shift pattern), edit AL balance |
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

`favicon.svg` uses the hardcoded hex `#007CC3` (Infosys blue). This is intentional — SVG files cannot reference CSS custom properties. This hex appears only in `Frontend/public/favicon.svg` and nowhere in the app UI.

**`--brand-accent` token rule:** `--brand-accent` (`#007CC3`, Infosys blue) is for brand identity only — the sidebar logo mark and the favicon. Never for status indicators, interactive controls, or data visualisation. It remains separate from `--st-info-*` even though the hues are close: `info` semantically means AL/informational status; `--brand-accent` means only "this is the application". If they ever diverge in value, that divergence is intentional.

### API Client (src/api/client.ts)

Single source of truth for all HTTP calls. Base URL from `VITE_API_BASE_URL` (fallback: `http://localhost:5000`). Exports `apiFetch<T>`, `downloadExcel`, and a typed `api` object.

TanStack Query stale times: locations 10 min, forecast/briefing 5 min, static data 10 min.

---

## Build & Deploy

The backend is a framework-dependent .NET build (`dotnet build -c Release`, not `dotnet publish`) launched by the watchdog script and supervised by Windows Task Scheduler (`GSDDashboard-Backend`) — see `PROJECT_BLUEPRINT.md` § Windows Scheduled Task for the watchdog/health-check/tunnel layering. The frontend is a Vite SPA whose build output lives inside the backend's `wwwroot/`. Both must be built before a restart will pick up any changes. No admin rights are available on this Cloud PC, so the task itself is never Disabled — only its processes are stopped by exact PID.

### Mandatory procedure — no exceptions

```
1.  git add <explicit files> && git commit           (never `.` or `-A`)
2.  git worktree add <path> <exact commit sha>        (pin the build to what's being deployed)
3.  cd <worktree>/Frontend && npm ci && npm run build (tsc -b, vite build, copies dist into worktree Backend/wwwroot + bin/.../wwwroot)
4.  cd <worktree>/Backend  && dotnet build -c Release
5.  cd <worktree>/Backend.Tests && dotnet test        → must pass before continuing
6.  robocopy live Backend/bin/Release/net8.0 and Backend/wwwroot to a timestamped backup folder (/E, exit code 8+ = failure)
7.  Identify current watchdog + API PIDs by exact CommandLine match (`-File *watchdog_gsd_backend.ps1*`, exclude `-Command` matches on your own shell)
8.  Stop-Process the watchdog PID, then the GSDDashboard.API.exe PID (exact PID only, never by name)
9.  robocopy worktree Backend/bin/Release/net8.0 → live, and worktree Backend/wwwroot → live (/E, no /MIR, no /PURGE, /XF client-errors.log so the live log survives)
10. Hash-check every copied file (SHA-256) against the worktree source — 0 mismatches required
11. Start-ScheduledTask GSDDashboard-Backend                     (Disable/Enable are not used — see limitation above)
12. Wait ~45s (30s watchdog startup delay + API boot), then verify: exactly 1 watchdog + 1 API process, GET /health = 200, smoke-test the API/page endpoints, GET the tunnel URL = 200
13. git worktree remove --force <path>
```

**Rollback** (any failure in steps 6-12): stop watchdog + API by exact PID, restore all files from the step-6 backup, `Start-ScheduledTask GSDDashboard-Backend`, verify `/health` = 200, then stop and report — do not retry with changes.

**Step 10 hash check**: confirms the live files are byte-identical to the tested worktree build before restarting — catches a partial/corrupt robocopy before it's ever served.

**Step 12 verification**: check the tunnel (`https://d2jn94qg-5000.euw.devtunnels.ms/`) as well as `localhost:5000`, since the tunnel is the production-facing surface.

### Verification (from tunnel, not localhost)

All verification runs against `https://d2jn94qg-5000.euw.devtunnels.ms/`, never `localhost:5173`. The tunnel is the production surface; the dev server bypasses the backend.

### Tunnel URL scope

Each project has its own tunnel. Never cross URLs between projects.

| Project | Tunnel |
|---------|--------|
| GSDDashboard | `https://d2jn94qg-5000.euw.devtunnels.ms/` |
| LaptopTracker | `https://puzzled-plane-1cfdm0z-5016.euw.devtunnels.ms/` |
| ShiftKiosk | `https://ssr7tm2l-8000.euw.devtunnels.ms/` |

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

Canonical full-absence set (AvailabilityResolver.FullAbsenceTypes): `SL, AL, UL, OL, PH, LPH, RESIGNED`.
`OL` (Other leave) is unpaid, same as `UL` (see `ShiftTypes.UnpaidTypes` / `ShiftTypes.IsPaidType`); `AL` remains paid.
`HALF_AL` = 0.5 coverage contribution (not in full-absence set; fractional credit via `Math.Floor`).
`TRAINING` is NOT in the full-absence set (agent is present at training, not absent from WIC).

---

---

## Operational Workflows

These are the common day-to-day operational changes and their system effects, for anyone who needs to make such changes without reading the full service code.

---

### Q1 — Moving a WIC agent to GSD backlog

**Scenario:** An agent who is normally assigned to a WIC location needs to work in GSD (Voice, Backlog/SSP) for a day or a longer period.

**Steps in the application:**

1. Open the **Shifts** page and find the agent's row for the target date(s).
2. Click the shift cell and change `ShiftType` from `WIC_DUTY` to `WORKING`.
3. Set `AgentTask` to `Voice` or `Backlog` as appropriate (shown as a badge in the shift cell).
4. Save. `ShiftSyncService` clears `IsWicDuty` automatically when `ShiftType` changes away from `WIC_DUTY`.

**What changes in the database:**
- `ShiftEntries.ShiftType` → `WORKING`; `IsWicDuty` → `0`; `AgentTask` updated.
- `WicShiftEntries` is **not** automatically updated. The row remains with whatever `IsOnSite` value it had.

**Effect on WIC coverage:**
- `AvailabilityResolver.GetWicContribution(false, sh)` returns `0.0` because `ShiftType ≠ WIC_DUTY`.
- The WIC card for the agent's location will immediately show one fewer active agent.
- If coverage drops below `MinAgentsRequired`, the location status changes to `PARTIAL` or `UNCOVERED`.
- The **Backup** and **Substitution** endpoints will now list this location as at-risk.

**Note:** If the agent should be absent from WIC for an extended period, also update the WicShiftEntries row (`IsOnSite=false`) via `PATCH /api/wic/shifts/{id}` to keep the two tables consistent.

**UI action (added 2026-09):** The manual steps above are no longer required for a same-day move to GSD backlog — a **"Move to GSD Backlog" / "Zu GSD-Backlog verschieben"** action is available in three places:
- `/wic-shifts` — the "⋮" kebab menu on each agent row in a location card.
- `/wic-attendance` — the icon button on each agent chip in the location detail's "Agent chips" section.
- `AssignAgentModal` — as an alternative action below the normal Assign form.

All three call the shared `MoveToGsdBacklogAction` component (`Frontend/src/components/MoveToGsdBacklogAction.tsx`), which posts to `POST /api/wic/move-to-gsd` (`WicShiftService.cs`). That endpoint runs inside a `CreateExecutionStrategy` transaction and sets, in one operation: `ShiftEntries.ShiftType='WORKING'`, `AgentTask='GSD'`, `IsWicDuty=0`, and — on every `WicShiftEntries` row where the agent was on-site that day — `IsOnSite=0`, `IsGSDDay=1`. No row is ever deleted, so the historical WIC assignment stays visible. If the move would leave the agent's WIC location with no other available agent, the action shows a warning dialog (`Cancel` primary / `Move anyway` secondary) before proceeding — this is a warning, not a hard block.

The reverse action (`POST /api/wic/assignments`, used by `AssignAgentModal`'s main Assign form) explicitly sets `IsOnSite=true`, `IsGSDDay=false`, `Task='WIC'` on `WicShiftEntries` and `ShiftType='WIC_DUTY'` on `ShiftEntries`.

---

### Q2 — Planned WIC agent needs to work BO instead

**Scenario:** An agent is scheduled for WIC duty but on a specific day must handle a BO (back-office) task instead. This is a task change, not an agent swap.

**Correct approach — change the task, do not swap agents:**

1. Open the **Shifts** page and change the agent's `ShiftType` from `WIC_DUTY` to `WORKING`, set `AgentTask` to the appropriate task (e.g. `Backlog`).
2. Alternatively, use `PATCH /api/shifts/{id}` directly: `{ "shiftType": "WORKING", "agentTask": "Backlog" }`.

**Incorrect approach — swapping out the agent:**
Swapping agents means assigning a substitute, which creates a `SubstitutionHistory` record and triggers the 30-day fairness penalty. This inflates load scores for agents used as emergency substitutes when in reality it was a planned task reassignment, not an absence.

**What the system shows:**
- The agent disappears from the WIC coverage count for that day (`GetWicContribution` returns `0.0`).
- The gap is visible in `/api/wic/open` and in the **Briefing** export as a coverage gap for the affected location.
- `SubstitutionService` and `BackupService` will surface candidates to fill the gap — this is the signal for a team lead to arrange actual backup coverage if needed.

**Note on the `WicShiftEntries` row:** The agent's `WicShiftEntry` for that date still has `IsOnSite=true`. This is acceptable for a single day — the coverage calculation ignores it because it reads `ShiftEntries.ShiftType` first. For multi-day reassignments, patch `IsOnSite=false` to keep the data clean.

---

### Q3 — How WIC and GSD data is joined

**Two-table design:**

`ShiftEntries` and `WicShiftEntries` are independent tables with no foreign key relationship. They are joined in code on `(EmployeeId, ShiftDate)` wherever both are needed. There is no cascade, no trigger, and no referential constraint between them.

```
ShiftEntries    ←── joined in code by (EmployeeId, ShiftDate) ───→    WicShiftEntries
ShiftType       determines presence/absence/WIC duty                  SupportLocation  determines which WIC site
IsWicDuty       legacy flag (now always derived from ShiftType)       IsOnSite         agent is physically there
AgentTask       what kind of work the agent is doing                  Task             WIC / VWIC / null
```

**Why they can get out of sync:**

Both tables are written by different code paths:
- `ShiftEntries` is written by: Excel import via `ShiftService`, `ShiftSyncService` (sick leave / vacation propagation), the Shifts-page PATCH endpoint, `SubstitutionModule` (Accept Substitute), and the Assign Agent endpoint.
- `WicShiftEntries` is written by: a separate Excel import, `SubstitutionModule`, and the Assign Agent endpoint.

If one path writes only one table (e.g. a shift import runs after Assign Agent), the two rows for the same `(EmployeeId, ShiftDate)` will disagree. The coverage logic resolves this by treating `ShiftEntries.ShiftType` as **authoritative**: `GetWicContribution` returns `1.0` only when `ShiftType = WIC_DUTY`. A `WicShiftEntry` with `IsOnSite=true` but a paired `ShiftEntry` with `ShiftType = WORKING` counts as `0.0`.

**Known artefacts of this design:**

1. A `WicShiftEntry` can exist with `IsOnSite=true` while `ShiftEntries` says `WORKING` — happens when a shift import overwrites the `ShiftEntry` after Assign Agent set it to `WIC_DUTY`.
2. Agents with no `ShiftEntry` at all (5 505 WIC agents in the historical import) correctly receive `GetWicContribution = 1.0` — the null case is treated as "no evidence of absence, assume on duty."
3. `IsWicDuty` on `ShiftEntries` is a stale legacy flag. It was previously used to detect WIC duty but could remain `true` after a `ShiftType` change. All write paths now reset it. Coverage no longer reads `IsWicDuty` — it reads `ShiftType` exclusively.
4. **Flagged, not yet fixed (found 2026-09):** of 881 `(EmployeeId, ShiftDate)` groups with more than one `WicShiftEntries` row, 87 are legitimate multi-location coverage (`COUNT(DISTINCT SupportLocation) > 1`), but 831 are a same-key contradiction — one row `IsOffDay=1`, another `IsOnSite=1` for the same employee/date. Coverage code is not affected today (it filters per-location `IsOnSite=true` rows rather than assuming one row per key), but this is stale/duplicate data that should be cleaned up in a future pass.

---

## Parsing Notes

- WIC shifts matched with `.Contains("WIC")` — raw data has variable spacing.
- HALF_AL has two raw formats: `"HAL *"` (prefix) and `"* HAL "` (suffix) — both map to `HALF_AL`.
- Team lead names may carry a trailing `\n`; services always `.Trim()` before comparison.
- Shift times stored as `varchar` (e.g. `"08:00"`), not SQL `TIME`.
- `WicAgentAssignments` join is string-based: `e.FullName = waa.EmployeeName`.

---

## Assistant Router

`AssistantService` routes each question to the `IDomainHandler` with the highest non-zero `Score()`. If two handlers tie, a clarification prompt is returned.

Input is always `normalizedQ`: lowercase, umlauts converted (ä→ae, ö→oe, ü→ue, ß→ss), trimmed.

### Handler priority table

| Score | Handler | Winning keywords |
|-------|---------|-----------------|
| 100 | `SickLeaveHandler` | "sick", "krank", "sick leave", `\bsl\b` |
| 100 | `PipelineHandler` | "pipeline" |
| 100 | `TrainingHandler` | "training", "schulung", "session" |
| 95 | `WicForecastHandler` | "wic coverage" |
| 90 | `ALBalanceHandler` | "balance", "remaining al", "al balance", "remaining leave", "remaining days", "how many days left" |
| 90 | `WicForecastHandler` | "at risk", "forecast", "coverage risk" |
| 90 | `WicCoverageDetailHandler` | "who covers", "main agent", "who is responsible", "who is the agent for" |
| 80 | `AgentAvailabilityHandler` | "available" — **requires a person name; returns error for list queries** |
| 80 | `DashboardHandler` | "dashboard", "summary", "working today", "on duty", "wic duty", "absent today", "agents today", "headcount" |
| +90 | `WicLeaveHandler` | `\bwic\b` (whole-word — see warning below) |
| +60 | `WicLeaveHandler` | "leave", "urlaub", "annual", "frei", "off" |
| +40 | `VacationsHandler` | "leave", "urlaub", "absent", "vacation" |

### ⚠️ WIC substring trap

`WicLeaveHandler` scores **+90** for the whole-word regex `\bwic\b`. Before the August 2026 fix this was `q.Contains("wic")`, which caused "vwic", "rwic", and other compound identifiers to incorrectly route to WicLeaveHandler.

**Rule**: Never write `q.Contains("wic")` in any handler. Always use `Regex.IsMatch(q, @"\bwic\b")`.

Handlers fixed (August 2026): `WicLeaveHandler`, `WicOpeningHoursHandler`, `VacationsHandler`.

### WicLeaveHandler yield conditions

WicLeaveHandler returns 0 (does not compete) for:
- "sick", "krank" → SickLeaveHandler
- "pipeline" → PipelineHandler
- "training", "schulung" → TrainingHandler
- "balance", "urlaubskonto" → ALBalanceHandler
- "forecast", "at risk", "prognose", "vorhersage" → WicForecastHandler
- **"wic duty", "wic dienst"** → DashboardHandler
- "employee list", "mitarbeiterliste" → VacationsHandler

Absence keywords ("absent", "absence", "away", "abwesend") only score for WicLeaveHandler when `\bwic\b` is also present. Without "wic" these questions route to DashboardHandler (counts) or VacationsHandler (names).

### SickLeaveHandler — long-term sick filter

`HandleAsync` detects long-sick queries via: "longer than", "more than", "21 day", "langzeitkrank", "long-term", "laenger als". When detected, it filters to employees sick for ≥21 calendar days (or the number parsed from the question). The Langzeitkrank threshold (21 days) matches the `crit` tone on the Sick Leave page.
- Raw "OFFWE" from the upstream sheet maps to `OFF_WEEKEND` in ShiftTypes.Parse().

---

## Frontend Resilience

### TanStack Query — cache key convention

**Rule:** A `queryKey` must uniquely identify a data **shape**, not just an endpoint. Two components MAY share a key only if their `queryFn` returns the identical runtime object structure (same fields, same types). If one `queryFn` unwraps a nested property, transforms the response, or strips fields before returning, it MUST use a distinct key.

**Why:** TanStack Query stores one value per key in a shared in-process cache. When component A populates key `K` with shape `X`, component B reading key `K` gets shape `X` regardless of what type B declared for that key. TypeScript generics are compile-time only; they do not protect against runtime shape mismatches.

**Canonical failure (2026-09-04):** `Overview.tsx` and `WicAttendance.tsx` both queried `["wic-forecast", N]`. Overview's queryFn unwrapped `.locations` → cached `LocationForecast[]`. WicAttendance's queryFn returned the full response object → cached `ForecastResponse`. After WicAttendance ran a stale-data refetch, the cache held `ForecastResponse`. On navigate-back to Overview, `forecast.filter(...)` threw `TypeError: forecast.filter is not a function`.

**Fixed 2026-09-04:** WicAttendance now uses key `["wic-attendance-forecast", N]`.

**Audit result (2026-09-04):** every `useQuery` in `Frontend/src` was inspected. One additional collision found and fixed:

| queryKey | File | queryFn shape | Collision type |
|----------|------|--------------|----------------|
| `["wic-forecast", N]` | `Overview.tsx` | `LocationForecast[]` (unwrapped) | **FIXED** — WicAttendance moved to `["wic-attendance-forecast", N]` |
| `["wic-locations"]` | `AssignAgentModal.tsx` | stripped `{locationCode, displayName}[]` only | **FIXED** — strip removed; full response cached |
| `["wic-locations"]` | 5 other consumers | full API response | all compatible once strip removed |
| `["employees-active"]` | 3 consumers | identical runtime shape; TS annotations differ only | no risk — not fixed |

---

### TanStack Query cache key collision — `["wic-forecast", N]`

**Symptom:** Navigate Overview → WicAttendance → Overview → blank white screen (error card after boundary was added). Same white screen on WicAttendance when navigating after Overview populated the cache.

**Root cause:** `Overview` and `WicAttendance` both registered a `useQuery` with key `["wic-forecast", horizonDays]` against the same `/api/wic/forecast?horizon=N` endpoint, but their `queryFn` returned different shapes:

- Overview: `return r.locations ?? []` → cached value is `LocationForecast[]`
- WicAttendance: `return r.json()` → cached value is `ForecastResponse` (object with `.locations`, `.generatedAt`, `.locationCount`, `.totalAtRiskDays`)

When WicAttendance ran its `queryFn` (on stale-data refetch after the first 5-minute `staleTime` window), it overwrote the shared cache entry with `ForecastResponse`. On navigate-back to Overview, TanStack Query returned `ForecastResponse` from cache. Overview's render immediately hit:

```tsx
const totalOpen = forecast != null ? forecast.filter(lf => ...).length : null
//                                           ^^^^^^
// TypeError: forecast.filter is not a function
// ForecastResponse is an object, not an array
```

React error boundary caught this synchronous render throw. Without the boundary, the full tree unmounts silently (white screen).

**Fix:** Gave WicAttendance a distinct cache key (`"wic-attendance-forecast"`) so it never writes into Overview's cache slot:

```tsx
// WicAttendance.tsx — line 706
queryKey: ["wic-attendance-forecast", horizonDays],
```

Overview keeps `["wic-forecast", horizonDays]` unchanged. The two queries are now independent; no cache poisoning possible.

**Verification:** 5 consecutive Overview → WicAttendance → Overview SPA navigations, then Ctrl+F5 — zero `[Overview crash]` entries, zero page errors, boundary never triggered.

### Overview Error Boundary (`OverviewErrorBoundary`)

**File:** `Frontend/src/components/OverviewErrorBoundary.tsx`

**Why it exists:** Safety net for any future synchronous render throw on Overview or WicAttendance. Without a boundary, a render-time throw unmounts the full React tree silently (white screen). TanStack Query catches async throws in `queryFn` but cannot catch synchronous throws inside JSX evaluation.

**Scope:** Wraps `<Overview />` and `<WicAttendance />` at route level in `App.tsx`:

```tsx
<Route index element={<OverviewErrorBoundary><Overview /></OverviewErrorBoundary>} />
<Route path="/wic-attendance" element={<OverviewErrorBoundary><WicAttendance /></OverviewErrorBoundary>} />
```

Must be placed as a **true parent at route level** — a boundary inside the component's own return block does not catch throws from that component's render function (JSX expressions are evaluated before being passed as `children`).

**Implementation pattern:** React error boundaries require class components; `useTranslation()` cannot be used in class components. Solution: the functional wrapper `OverviewErrorBoundary` calls the hook and passes `t` as a prop to `OverviewErrorBoundaryCore` (class component).

**Error logging:** On catch, `componentDidCatch` logs `[Overview crash]` + component stack (first 600 chars) to `console.error` and POSTs the full stack to `/api/debug/client-error` (appends to `client-errors.log` in the backend output directory).

**Error UI:** Uses crit-tone CSS tokens (`--st-crit-bg`, `--st-crit-bd`, `--st-crit-solid`) — no hardcoded hex. The message is i18n-keyed (`overview.error.title`, `overview.error.detail`) in both EN and DE.

### Debug endpoint — `/api/debug/client-error`

**Purpose:** Receives frontend crash reports from `OverviewErrorBoundary.componentDidCatch` and appends them to `client-errors.log` in the backend output directory.

**Authentication:** None. Intended exclusively for the internal network and dev tunnel.

**Constraints (added 2026-09-04):**
- Request body read limited to first **8 KB** — larger payloads are silently truncated, not rejected.
- Log file capped at **5 MB** — new writes are silently discarded once the limit is reached.

**Log location:** `C:\GSDDashboard\Backend\bin\Release\net8.0\client-errors.log`

**To reset the log:** delete or truncate the file; the endpoint creates it on first write.

---

## Display Name Changes

Changes to visible UI text only — routes, API paths, and i18n keys are never renamed.

| Date | Route | Old display name | New display name (EN) | New display name (DE) | Notes |
|------|-------|------------------|-----------------------|-----------------------|-------|
| 2026-09-03 | `/wic-coverage` | WIC Coverage | WIC Assignments | WIC-Zuweisungen | Route and all `wicCoverage.*` i18n keys preserved. Added subtitle: "Static assignment directory — not a daily coverage status." / "Statisches Zuweisungsverzeichnis — kein täglicher Abdeckungsstatus." Changed keys: `nav.wicCoverage`, `wicCoverage.title`. Added key: `wicCoverage.subtitle`. |
