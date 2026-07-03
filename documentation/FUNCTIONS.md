# GSDDashboard — Function & Endpoint Catalog

Complete reference for every backend service method, API endpoint, and significant frontend component.

**Last updated:** 2026-07-03

---

## Table of Contents

1. [Backend Services](#backend-services)
   - [DashboardService](#dashboardservice)
   - [ShiftService](#shiftservice)
   - [ShiftValidationService](#shiftvalidationservice)
   - [ShiftSyncService](#shiftsyncservice)
   - [AvailabilityResolver](#availabilityresolver)
   - [WicShiftService](#wicshiftservice)
   - [WicCardsService](#wiccardsservice)
   - [CoverageCalculator](#coveragecalculator)
   - [CoverageEvaluator](#coverageevaluator)
   - [ReachabilityService](#reachabilityservice)
   - [SubstitutionService](#substitutionservice)
   - [SubstituteAccept](#substituteaccept)
   - [BackupService](#backupservice)
   - [ForecastService](#forecastservice)
   - [WhatIfService](#whatifservice)
   - [BriefingService](#briefingservice)
   - [WicScheduleService](#wicscheduleservice)
   - [WicLocationMatcher](#wiclocationmatcher)
   - [ALPlanningService](#alplanningservice)
   - [VwicService](#vwicservice)
   - [BreakService](#breakservice)
   - [WicCoverageService](#wiccoverageservice)
   - [WicCoverageImport](#wiccoverageimport)
   - [SickLeaveService](#sickleaveservice)
   - [VacationService](#vacationservice)
   - [ALBalanceService](#albalanceservice)
   - [EmployeeService](#employeeservice)
   - [AttendanceService](#attendanceservice)
   - [PublicHolidayService](#publicholidayservice)
   - [TrainingService](#trainingservice)
   - [PipelineService](#pipelineservice)
   - [ALCalendarService](#alcalendarservice)
   - [OverviewService](#overviewservice)
   - [ShiftReorderService](#shiftreorderservice)
   - [PlzBundesland](#plzbundesland)
2. [API Endpoints](#api-endpoints)
3. [Frontend Pages and Components](#frontend-pages-and-components)
4. [Known Issues and Flags](#known-issues-and-flags)

---

## Backend Services

---

### DashboardService

**DI Lifetime:** Scoped
**File:** `Backend/DashboardService.cs`
**Namespace:** `Modules.Dashboard`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetSummaryAsync(date)` | Counts agents by ShiftType for a given date (VOICE, CHAT, SSP, AL, SL, Training, WicDuty, PH, OFF) | `DateOnly date` | `DashboardSummaryDto` | ShiftEntries, Employees, WicShiftEntries |
| `GetTeamLeadSummaryAsync(date)` | Per-TeamLead breakdown of working / AL / SL / Training / WIC agent counts | `DateOnly date` | `List<TeamLeadSummaryDto>` | ShiftEntries, Employees, WicAgentAssignments |
| `GetWicCardsAsync(date)` | Per-WIC location coverage cards | `DateOnly date` | `List<WicCardDto>` | WicLocations, WicShiftEntries, Employees, WicAgentAssignments |

---

### ShiftService

**DI Lifetime:** Scoped
**File:** `Backend/ShiftService.cs`
**Namespace:** `Modules.Shifts`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetShiftsAsync(params)` | Shift plan query with from/to/teamlead/role/engagement/shifttype filters | `ShiftFilterParams` | `List<ShiftRowDto>` | ShiftEntries, Employees |
| `GetWorkingTodayAsync(date)` | Returns agents whose ShiftType is WORKING on the given date | `DateOnly date` | `List<ShiftRowDto>` | ShiftEntries, Employees |
| `ExportToExcelAsync(from, to)` | ClosedXML Excel export of shift plan for the given date range | `DateOnly from, DateOnly to` | `byte[]` | ShiftEntries, Employees |
| `UpdateShiftAsync(id, dto)` | Updates ShiftType, times, and/or task on an existing ShiftEntry | `int id, ShiftUpdateDto dto` | `ShiftRowDto?` | ShiftEntries |
| `ValidateShiftAsync(request)` | Delegates working-time rule validation to ShiftValidationService | `ShiftValidateRequest` | `ValidationResult` | ShiftValidationService |

---

### ShiftValidationService

**DI Lifetime:** Scoped
**File:** `Backend/ShiftValidationService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `ValidateAsync(shiftId, newShiftType, newStart, newEnd)` | Checks working-time rules: student hour limits, Bundesland public holidays, shift length constraints | `int shiftId, string newShiftType, TimeOnly? newStart, TimeOnly? newEnd` | `ValidationResult` | ShiftEntries, Employees, PublicHolidays |

---

### ShiftSyncService

**DI Lifetime:** Scoped
**File:** `Backend/ShiftSyncService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `SyncSickLeaveAsync(employeeId, from, to, sourceId)` | Creates or updates ShiftEntries with ShiftType=SL for all weekdays in the sick leave range; saves PreviousStatus | `string employeeId, DateOnly from, DateOnly to, int sourceId` | `void` | ShiftEntries |
| `SyncVacationAsync(employeeId, from, to, sourceId)` | Creates or updates ShiftEntries with ShiftType=AL for all weekdays in the vacation range | `string employeeId, DateOnly from, DateOnly to, int sourceId` | `void` | ShiftEntries |
| `RevertSickLeaveAsync(employeeId, from, to, sourceId)` | Restores PreviousStatus or removes auto-generated SL entries | `string employeeId, DateOnly from, DateOnly to, int sourceId` | `void` | ShiftEntries |
| `RevertVacationAsync(employeeId, from, to, sourceId)` | Restores PreviousStatus or removes auto-generated AL entries | `string employeeId, DateOnly from, DateOnly to, int sourceId` | `void` | ShiftEntries |

---

### AvailabilityResolver

**DI Lifetime:** Scoped
**File:** `Backend/Services/AvailabilityResolver.cs`

**Canonical absence set (`FullAbsenceTypes`):** `{ SL, AL, UL, PH, LPH, RESIGNED }` — TRAINING and HALF_AL are intentionally excluded from this set.

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetStatusAsync(employeeId, date)` | Single-employee absence check. SickLeave records take priority over ShiftEntries | `string employeeId, DateOnly date` | `AbsenceStatus` | SickLeaves, ShiftEntries |
| `GetAbsentIdsAsync(ids, date)` | Bulk absence check: returns the subset of provided employee IDs that are absent on the given date | `IEnumerable<string> ids, DateOnly date` | `HashSet<string>` | SickLeaves, ShiftEntries |

---

### WicShiftService

**DI Lifetime:** Scoped
**File:** `Backend/WicShiftService.cs`
**Namespace:** `Modules.WicShifts`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetWicShiftsAsync(from, to, location, employeeId, teamLead)` | WIC shift entries with optional date range and identity filters | `DateOnly from, DateOnly to, string? location, string? employeeId, string? teamLead` | `List<WicShiftDto>` | WicShiftEntries, Employees, ShiftEntries |
| `GetCoverageAsync(date)` | Per-location agent coverage for a given date | `DateOnly date` | `List<WicCoverageDto>` | WicLocations, WicShiftEntries, Employees, WicAgentAssignments |
| `GetAvailableHoursAsync(date)` | Free hours per agent at their assigned WIC | `DateOnly date` | `List<AvailableHoursDto>` | WicShiftEntries, Employees, WicOpeningHours |
| `GetOpenAsync(date, horizon)` | Open/coverage status for up to 7 days. Uses AvailabilityResolver + CoverageEvaluator.Classify() | `DateOnly date, int horizon` | `List<WicOpenDayDto>` | WicLocations, WicOpeningHours, WicShiftEntries, ShiftEntries, SickLeaves, PublicHolidays |
| `PatchWicShiftAsync(id, request)` | Updates Task, SupportLocation, and/or IsOnSite on a WicShiftEntry | `int id, PatchWicShiftRequest request` | `WicShiftDto?` | WicShiftEntries |
| `CreateShiftAsync(request)` | Creates a new WicShiftEntry and corresponding ShiftEntry | `CreateShiftRequest request` | `WicShiftDto` | WicShiftEntries, ShiftEntries, Employees |
| `CreateAssignmentAsync(request)` | Assigns an agent to a WIC location for a specific date | `CreateAssignmentRequest request` | `object` | WicShiftEntries |
| `ExportToExcelAsync(from, to)` | ClosedXML Excel export of WIC shifts for the given date range | `DateOnly from, DateOnly to` | `byte[]` | WicShiftEntries, Employees |

---

### WicCardsService

**DI Lifetime:** Scoped
**File:** `Backend/WicCardsService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetCardsAsync(date, country?)` | Per-location detailed coverage cards including opening hours, assigned agents, and shift overlap analysis via CoverageCalculator | `DateOnly date, string? country` | `List<WicCardDto2>` | WicLocations, WicOpeningHours, SickLeaves, Vacations, WicShiftEntries, Employees, WicAgentAssignments |

---

### CoverageCalculator

**DI Lifetime:** Static
**File:** `Backend/CoverageCalculator.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `Calculate(isClosed, open1, close1, open2, close2, agents)` | Minute-overlap based coverage calculation. Calls CoverageEvaluator.ClassifyByMinutes() | Closure flag, two opening windows (TimeOnly pairs), agent list | `CoverageResult` | None (pure logic) |
| `CalcOpenMinutes(...)` | Helper: total open minutes for a location's schedule | Opening window parameters | `int` | None (pure logic) |
| `CalcAgentCoverage(...)` | Helper: total covered minutes from agent shifts overlapping the opening window | Agent shift list, opening window | `int` | None (pure logic) |

---

### CoverageEvaluator

**DI Lifetime:** Scoped (instance methods); Static (classifier methods)
**File:** `Backend/Services/CoverageEvaluator.cs`

> **Note:** `EvaluateAsync` treats HALF_AL as fully absent. All other services give HALF_AL 0.5 coverage credit. Only SubstitutionService uses `EvaluateAsync` directly — see [Known Issues](#known-issues-and-flags).

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `Classify(isClosed, presentAgents, minRequired, closedReason?)` | **Static.** Canonical headcount classifier. Returns COVERED / PARTIAL / UNCOVERED / CLOSED | `bool isClosed, int presentAgents, int minRequired, string? closedReason` | `CoverageStatus` | None (pure logic) |
| `ClassifyByMinutes(isClosed, coveredMinutes, totalMinutes)` | **Static.** Minute-ratio classifier used by CoverageCalculator | `bool isClosed, int coveredMinutes, int totalMinutes` | `CoverageStatus` | None (pure logic) |
| `EvaluateAsync(locationCode, date, absentIds?)` | **Instance (DB-backed).** Full evaluation including opening hours check, holiday check, SickLeave check, ShiftEntries absence check | `string locationCode, DateOnly date, HashSet<string>? absentIds` | `CoverageStatus` | WicLocations, WicOpeningHours, PublicHolidays, WicShiftEntries, SickLeaves, ShiftEntries |

---

### ReachabilityService

**DI Lifetime:** Singleton
**File:** `Backend/Services/ReachabilityService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetMatrixAsync()` | Haversine distances between all WIC pairs that have coordinates. 4-hour TTL cache with double-checked locking (SemaphoreSlim) | None | Distance matrix | WicLocations (via IServiceScopeFactory) |
| `GetDistanceAsync(from, to)` | Single-pair haversine distance lookup | `string from, string to` | `double?` | WicLocations (via matrix cache) |
| `GetSanityAsync()` | Sanity check: verifies Berlin→Munich distance is approximately 504 km | None | Sanity result | WicLocations (via matrix cache) |
| `InvalidateCache()` | Clears the cached distance matrix (call after coordinate update) | None | `void` | None |
| `GetMissingCoordsAsync()` | Returns list of LocationCodes that have no latitude/longitude set | None | `List<string>` | WicLocations |

---

### SubstitutionService

**DI Lifetime:** Scoped
**File:** `Backend/Services/SubstitutionService.cs`

> **Note:** This is the canonical substitute-ranking engine. See [BackupService](#backupservice) for the older parallel implementation.

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetSubstitutesAsync(locationCode, date, horizon, explicitAbsentIds?)` | 4-tier ranked substitute list. Scores: BACKUP=10,000; SSP=5,000; WIC_DONOR=0 + reachability bonus (0–500); CALL_IN=−100 + reachability. Donor guard skips if surplus ≤ 0. Fairness penalty = −LoadScore×10 from 30-day SubstitutionHistory. HALF_AL = 0.5 coverage credit | `string locationCode, DateOnly date, int horizon, IEnumerable<string>? explicitAbsentIds` | Substitution response | WicLocations, WicAgentAssignments, Employees, WicShiftEntries, ShiftEntries, SickLeaves, SubstitutionHistory, ReachabilityService |

---

### SubstituteAccept

**DI Lifetime:** Static handler
**File:** `Backend/Modules/SubstitutionModule.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `POST /api/wic/substitutes/accept` | Accepts a substitute assignment. Creates/updates ShiftEntry (ShiftType=WIC_DUTY), creates/updates WicShiftEntry (IsOnSite=true), writes a SubstitutionHistory row | Accept request body | Confirmation object | WicLocations, WicOpeningHours, Employees, ShiftEntries, WicShiftEntries, SubstitutionHistory |

---

### BackupService

**DI Lifetime:** Scoped
**File:** `Backend/BackupService.cs`
**Namespace:** `Modules.Backup`

> **Note:** Pre-dates SubstitutionService. Uses simpler scoring. SubstitutionService is the canonical engine — see [Known Issues](#known-issues-and-flags).

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetBackupAsync(locationCode, date, horizon)` | Older substitute candidate engine. Returns ranked BACKUP / BACKLOG / WIC:* candidates with haversine distance | `string locationCode, DateOnly date, int horizon` | `BackupResponseDto?` | WicLocations, WicAgentAssignments, Employees, WicOpeningHours, WicShiftEntries, ShiftEntries, SickLeaves |

---

### ForecastService

**DI Lifetime:** Scoped
**File:** `Backend/Services/ForecastService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetForecastAsync(horizon, locationCode?)` | 14-day (1–30, clamped) coverage forecast per WIC location. For each location/day: checks open/closed (holidays included), computes fractional HALF_AL coverage, calls CoverageEvaluator.Classify() | `int horizon, string? locationCode` | Forecast response | WicLocations, WicOpeningHours, WicShiftEntries, ShiftEntries, SickLeaves, PublicHolidays |

---

### WhatIfService

**DI Lifetime:** Scoped
**File:** `Backend/Services/WhatIfService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetWhatIfAsync(absentEmployeeId, date, horizon)` | Simulates the impact of a single agent's absence. Finds MAIN assignments by FullName, then calls SubstitutionService with the explicit absent ID. No DB writes | `string absentEmployeeId, DateOnly date, int horizon` | What-if response | WicAgentAssignments, WicLocations, SubstitutionService |

---

### BriefingService

**DI Lifetime:** Scoped
**File:** `Backend/Services/BriefingService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetBriefingAsync()` | Today's absences + coverage gaps + next AT_RISK days. For each gap calls SubstitutionService to get best substitute name (wrapped in try/catch) | None | `BriefingResponse` | Employees, ShiftEntries, SickLeaves, WicLocations, WicOpeningHours, WicShiftEntries, PublicHolidays, SubstitutionService |
| `ExportToExcelAsync()` | 3-sheet ClosedXML workbook: Absences (red header), Coverage Gaps (amber header), Next AT_RISK Days (purple header) | None | `byte[]` | Employees, ShiftEntries, SickLeaves, WicLocations, WicOpeningHours, WicShiftEntries, PublicHolidays, SubstitutionService |

---

### WicScheduleService

**DI Lifetime:** Scoped
**File:** `Backend/WicScheduleService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetScheduleAsync()` | WIC opening hours per location per day of the week | None | Schedule data | WicLocations, WicOpeningHours |

---

### WicLocationMatcher

**DI Lifetime:** Static
**File:** `Backend/Services/WicLocationMatcher.cs`

Single source of truth for resolving free-text location strings to WicLocation records. Contains a 30-entry alias dictionary.

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `MatchesSupportLocation(sl, loc)` | Checks whether a WicShiftEntry.SupportLocation string matches a given WicLocation (by DisplayName, City, alias map, LocationCode, or LocationCodeLegacy) | `string sl, WicLocation loc` | `bool` | None (pure logic) |
| `MatchesAssignmentCode(code, loc)` | Checks whether a WicAgentAssignment.LocationCode matches a given WicLocation (by LocationCode or LocationCodeLegacy) | `string code, WicLocation loc` | `bool` | None (pure logic) |

---

### ALPlanningService

**DI Lifetime:** Scoped
**File:** `Backend/Services/ALPlanningService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetALPlanningAsync(locationCode, dateRanges)` | For each requested date range: computes per-day coverage with the requesting employee absent, surfaces conflicts, and finds the best substitute | `string locationCode, List<DateRange> dateRanges` | Planning response | WicLocations, WicShiftEntries, Employees, ShiftEntries, SickLeaves, PublicHolidays, SubstitutionService |

---

### VwicService

**DI Lifetime:** Scoped
**File:** `Backend/VwicService.cs`
**Namespace:** `Modules.Vwic`

**MinRequired by hour:** 00–07 = 1 agent, 07–17 = 3 agents, 17–24 = 1 agent.

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetDailyAsync(date?)` | 24-hour VWIC coverage timeline with break slot deductions per hour | `DateOnly? date` | `VwicDailyResponseDto` | Employees, ShiftEntries, SickLeaves, BreakSlots, WicShiftEntries, VwicRotationSlots |
| `AssignAgentAsync(request)` | Creates or updates a WicShiftEntry with SupportLocation=VWIC | `VwicAssignRequest request` | `object` | WicShiftEntries, Employees |
| `GetCandidatesAsync(date?)` | Voice agents eligible for VWIC duty on a given date | `DateOnly? date` | `List<VwicCandidateDto>` | Employees, ShiftEntries, SickLeaves |
| `AddToVwicAsync(request)` | Sets Employee.SecondaryRole = VWIC | `VwicManageRequest request` | `object` | Employees |
| `RemoveFromVwicAsync(request)` | Clears Employee.SecondaryRole if it is currently VWIC | `VwicManageRequest request` | `object` | Employees |
| `GenerateRotationPlanAsync(request)` | Round-robin rotation schedule for a given date/time range. Includes coverage proof and fairness statistics | `VwicRotationRequest request` | `VwicRotationResponse` | Employees, ShiftEntries, SickLeaves |
| `GenerateWeekRotationPlanAsync(request)` | Week-long (Mon–Fri) rotation plan using cumulative hours for fairness | `VwicWeekRequest request` | `VwicWeekResponse` | Employees, ShiftEntries, SickLeaves |
| `ExportWeekPlanAsync(request)` | ClosedXML Excel export of the week plan plus a summary sheet | `VwicWeekRequest request` | `byte[]` | Employees, ShiftEntries, SickLeaves |
| `SaveRotationSlotsAsync(request)` | Persists rotation slots to VwicRotationSlots (replaces all existing slots for the date) | `SaveRotationRequest request` | `object` | VwicRotationSlots |

---

### BreakService

**DI Lifetime:** Scoped
**File:** `Backend/BreakService.cs`
**Namespace:** `Modules.Breaks`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetBreaksAsync(date?)` | All break slots for a date, filtering out agents who are absent | `DateOnly? date` | `List<BreakSlotDto>` | BreakSlots, SickLeaves, Vacations, ShiftEntries, Employees |
| `AutoDistributeAsync(request)` | Auto-assigns 30-minute break slots across the window. Respects VWIC rotation (no break during VWIC slot), concurrent agent limit (voiceMinPct), agent shift window, and daily seeded fairness shuffle | `AutoDistributeRequest request` | `BreakDistributeResult` | Employees, ShiftEntries, SickLeaves, Vacations, VwicRotationSlots, BreakSlots |
| `StartBreakAsync(id)` | Sets ActualStart, recalculates BreakEnd, sets status = ON_BREAK | `int id` | `object` | BreakSlots |
| `EndBreakAsync(id)` | Sets ActualEnd and sets status = DONE | `int id` | `object` | BreakSlots |
| `CancelBreakAsync(id)` | Sets status = CANCELLED | `int id` | `object` | BreakSlots |
| `CreateManualBreakAsync(request)` | Replaces any SCHEDULED slot for the agent+date with the manually specified slot | `ManualBreakRequest request` | `object` | BreakSlots, Employees |
| `BreakCoverHour(breakStart, breakEnd, h)` | **Static helper.** Returns true if [breakStart, breakEnd) overlaps hour h. Used internally by VwicService | `TimeOnly breakStart, TimeOnly breakEnd, int h` | `bool` | None (pure logic) |

---

### WicCoverageService

**DI Lifetime:** Scoped
**File:** `Backend/Services/WicCoverageService.cs`

> **Note:** Hardcoded excluded agents: Ferenc Koreh, Tunde Szabo, Zsolt Fulop.

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetAgentsAsync(search?)` | Agents with PrimaryKid, emails, HasCar, GroupRegion, ReachableCities, WicRoles. Filters to agents that have a PrimaryKid or appear in AgentReachableCities | `string? search` | `List<AgentCoverageDto>` | Employees, AgentReachableCities, WicAgentAssignments, WicLocations |
| `GetAgentByKidAsync(kid)` | Single agent by PrimaryKid or EmployeeId | `string kid` | `AgentCoverageDto?` | Employees, AgentReachableCities, WicAgentAssignments, WicLocations |
| `PatchAgentAsync(kid, dto)` | Updates HasCar and/or GroupRegion on the Employee record | `string kid, PatchAgentDto dto` | `bool` | Employees |
| `GetWicsAsync(search?)` | Active WIC locations with counts of MAIN and BACKUP assignments | `string? search` | `List<WicListItemDto>` | WicLocations, WicAgentAssignments |
| `GetWicByCodeAsync(locationCode)` | Full WIC coverage plan: Main agents, BackupA (BACKUP assignments), BackupB (reachable via AgentReachableCities), BackupC (REGIONAL assignments) | `string locationCode` | `WicCoverageDto?` | WicLocations, WicAgentAssignments, AgentReachableCities, Employees |
| `GetReachableAgentsAsync(locationCode)` | Agents reachable for a WIC location via city reachability | `string locationCode` | `List<AgentTierDto>` | WicLocations, WicAgentAssignments, AgentReachableCities, Employees |
| `PinBackupBAsync(locationCode, dto)` | Promotes an agent from the BackupB pool to a BACKUP WicAgentAssignment | `string locationCode, PinBackupBDto dto` | `bool` | WicLocations, WicAgentAssignments |

---

### WicCoverageImport

**DI Lifetime:** Static (startup seeder)
**File:** `Backend/Services/WicCoverageImport.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `RunAsync(db)` | One-time startup seeder. Skips entirely if AgentReachableCities already has rows. Seeds Employee KIDs/emails (70-agent table), AgentReachableCities (agent→city reachability), WicAgentAssignment rows with MAIN/BACKUP/REGIONAL tiers (44-WIC table), and WicLocation.OpeningDay/.Comment fields | `AppDbContext db` | `void` | Employees, AgentReachableCities, WicAgentAssignments, WicLocations |

---

### SickLeaveService

**DI Lifetime:** Scoped
**File:** `Backend/SickLeaveService.cs`
**Namespace:** `Modules.SickLeave`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetSickLeavesAsync(from, to, teamLead, type, activeOnly)` | Filtered sick leave records. `activeOnly=true` filters to today-active records with LeaveType "SL" or "Self" | `DateOnly from, DateOnly to, string? teamLead, string? type, bool activeOnly` | `List<SickLeaveDto>` | SickLeaves, Employees |
| `GetActiveOnDateAsync(date)` | Active sick leaves on a specific date (LeaveType "SL" or "Self") | `DateOnly date` | `List<SickLeaveDto>` | SickLeaves, Employees |
| `GetStatsAsync(from, to)` | Active count, average duration, breakdown by TeamLead | `DateOnly from, DateOnly to` | `SickLeaveStatsDto` | SickLeaves, Employees |
| `CreateAsync(request)` | Creates a SickLeave record and calls ShiftSyncService.SyncSickLeaveAsync | `CreateSickLeaveRequest request` | `SickLeaveDto` | SickLeaves, Employees, ShiftEntries (via ShiftSyncService) |
| `PatchAsync(id, request)` | Updates SickLeave dates/type/notes, reverts old ShiftEntry sync, re-syncs with new dates | `int id, PatchSickLeaveRequest request` | `SickLeaveDto?` | SickLeaves, ShiftEntries (via ShiftSyncService) |
| `DeleteAsync(id)` | Removes a SickLeave record | `int id` | `bool` | SickLeaves |
| `ExportToExcelAsync(from, to)` | ClosedXML export with duration-based row color coding | `DateOnly from, DateOnly to` | `byte[]` | SickLeaves, Employees |

---

### VacationService

**DI Lifetime:** Scoped
**File:** `Backend/VacationService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetVacationsAsync(...)` | Filtered vacation records with optional date range, employee, and team-lead filters | Filter params | Vacation list | Vacations, Employees |

---

### ALBalanceService

**DI Lifetime:** Scoped
**File:** `Backend/ALBalanceService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| *(methods not documented)* | Manages annual-leave balance records | — | — | — |

---

### EmployeeService

**DI Lifetime:** Scoped
**File:** `Backend/EmployeeService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| *(methods not documented)* | CRUD operations on Employee records; timeline view; AL balance patch | — | — | — |

---

### AttendanceService

**DI Lifetime:** Scoped
**File:** `Backend/AttendanceService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| *(methods not documented)* | Attendance data query and export | — | — | — |

---

### PublicHolidayService

**DI Lifetime:** Scoped
**File:** `Backend/PublicHolidayService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| *(methods not documented)* | Returns public holiday records | — | — | PublicHolidays |

---

### TrainingService

**DI Lifetime:** Scoped
**File:** `Backend/TrainingService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| *(methods not documented)* | Returns training schedule data | — | — | — |

---

### PipelineService

**DI Lifetime:** Scoped
**File:** `Backend/PipelineService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| *(methods not documented)* | CRUD operations on pipeline events | — | — | — |

---

### ALCalendarService

**DI Lifetime:** Scoped
**File:** `Backend/ALCalendarService.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| *(methods not documented)* | Builds ALCalendarDto: days with ALDayEntry agents and team-lead warnings for the requested range | `DateOnly from, DateOnly to` | `ALCalendarDto` | SickLeaves, Vacations, ShiftEntries, Employees |

---

### OverviewService

**DI Lifetime:** Scoped
**File:** `Backend/OverviewService.cs`
**Namespace:** `Modules.Overview`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `GetWicSummaryAsync(date, horizon)` | Per-location, per-day coverage status for up to 7 days. Uses CoverageEvaluator.Classify(), WicLocationMatcher, AvailabilityResolver | `DateOnly date, int horizon` | WIC status summary | WicLocations, WicOpeningHours, PublicHolidays, WicAgentAssignments, Employees, WicShiftEntries, ShiftEntries, SickLeaves |
| `GetDetailAsync(type, date)` | Agents grouped by shift type for a given date. Supported types: VOICE, CHAT, BACKLOG, AL, SL, TRAINING, WIC | `string type, DateOnly date` | `OverviewDetailDto` | ShiftEntries, Employees |

---

### ShiftReorderService

**DI Lifetime:** Static handler
**File:** `Backend/ShiftReorderService.cs`

> **VESTIGIAL:** This endpoint accepts a reorder payload but performs no action. Frontend `client.ts` does not call it. See [Known Issues](#known-issues-and-flags).

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `PATCH /api/shiftplan/reorder` | Accepts an ordered employee ID list but does nothing with it. Returns success | Ordered ID list | Success response | None |

---

### PlzBundesland

**DI Lifetime:** Static
**File:** `Backend/PlzBundesland.cs`

| Method | Purpose | Inputs | Outputs | Tables/Services |
|--------|---------|--------|---------|-----------------|
| `Get(locationCode, postalCode, country)` | Maps PLZ / LocationCode pattern to Bundesland name. Runtime fallback when WicLocations.Bundesland is null. Contains 38-entry PLZ mapping | `string locationCode, string postalCode, string country` | `string?` | None (pure logic) |

---

## API Endpoints

### Dashboard

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/dashboard/summary?date=` | DashboardService | Agent counts by ShiftType for a date |
| GET | `/api/dashboard/teamlead-summary?date=` | DashboardService | Per-TeamLead working/absence counts |
| GET | `/api/dashboard/wic-cards?date=` | DashboardService | Per-WIC coverage card summary |

### Shifts

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/shifts?from=&to=&teamLead=&role=&engagement=&shiftType=` | ShiftService | Shift plan with filters |
| GET | `/api/shifts/working-today?date=` | ShiftService | Agents on WORKING shift |
| GET | `/api/shifts/download?from=&to=` | ShiftService | Excel export of shift plan |
| PATCH | `/api/shifts/{id}` | ShiftService | Update shift type/times/task |
| POST | `/api/shifts/validate` | ShiftService → ShiftValidationService | Validate working-time rules |
| PATCH | `/api/shiftplan/reorder` | ShiftReorderService | **VESTIGIAL — no-op** |

### WIC Shifts

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/wic/locations` | WicShiftService | All WIC locations |
| GET | `/api/wic?from=&to=&locationCode=&employeeId=&teamLead=` | WicShiftService | WIC shift entries with filters |
| GET | `/api/wic/coverage?date=` | WicShiftService | Per-location agent coverage |
| GET | `/api/wic/available-hours?date=` | WicShiftService | Free hours per agent at WIC |
| GET | `/api/wic/open?date=&horizon=` | WicShiftService | Open/coverage status (up to 7 days) |
| PATCH | `/api/wic/shifts/{id}` | WicShiftService | Update Task/SupportLocation/IsOnSite |
| POST | `/api/wic/shifts` | WicShiftService | Create new WIC shift entry |
| POST | `/api/wic/assign` | WicShiftService | Assign agent to WIC for a date |
| GET | `/api/wic/download?from=&to=` | WicShiftService | Excel export of WIC shifts |
| GET | `/api/wic/cards?date=&country=` | WicCardsService | Detailed per-location coverage cards |
| GET | `/api/wic/schedule` | WicScheduleService | Opening hours per location per day |

### Reachability

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/wic/reachability?from=&to=` | ReachabilityService | Haversine distance between two WIC locations |
| GET | `/api/wic/reachability/sanity` | ReachabilityService | Berlin→Munich ~504 km sanity check |

### Substitution

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/wic/substitutes?locationCode=&date=&horizon=&absentIds=` | SubstitutionService | Ranked substitute list |
| POST | `/api/wic/substitutes/accept` | SubstituteAccept | Accept and persist a substitute assignment |
| GET | `/api/wic/backup?locationCode=&date=&horizon=` | BackupService | **Older engine** — ranked backup candidates |

### Forecast & Planning

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/wic/forecast?horizon=14&locationCode=` | ForecastService | 14-day coverage forecast per location |
| GET | `/api/wic/whatif?absentEmployeeId=&date=&horizon=` | WhatIfService | Impact simulation for one absent agent |
| POST | `/api/wic/al-planning` | ALPlanningService | AL planning: coverage impact + best substitute per date range |

### Briefing

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/wic/briefing` | BriefingService | Today's absences, coverage gaps, AT_RISK days |
| GET | `/api/wic/briefing/export` | BriefingService | 3-sheet Excel briefing export |

### VWIC

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/vwic/daily?date=` | VwicService | 24-hour VWIC coverage timeline |
| POST | `/api/vwic/assign` | VwicService | Assign agent to VWIC |
| GET | `/api/vwic/candidates?date=` | VwicService | Voice agents eligible for VWIC duty |
| PUT | `/api/vwic/agents/add` | VwicService | Add agent to VWIC pool (set SecondaryRole=VWIC) |
| PUT | `/api/vwic/agents/remove` | VwicService | Remove agent from VWIC pool |
| POST | `/api/vwic/rotation-plan` | VwicService | Generate single-day rotation plan |
| POST | `/api/vwic/rotation-plan/save` | VwicService | Persist rotation slots to DB |
| POST | `/api/vwic/rotation-plan-week` | VwicService | Generate week-long rotation plan |
| POST | `/api/vwic/rotation-plan-week/export` | VwicService | Excel export of week rotation plan |

### Breaks

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/breaks?date=` | BreakService | Break slots for a date |
| GET | `/api/breaks/diag` | BreakService | **DEAD — diagnostic endpoint, not a production feature** |
| POST | `/api/breaks/auto-distribute` | BreakService | Auto-assign break slots |
| POST | `/api/breaks/{id}/start` | BreakService | Mark break as started |
| POST | `/api/breaks/{id}/end` | BreakService | Mark break as ended |
| POST | `/api/breaks/{id}/cancel` | BreakService | Cancel a break slot |
| POST | `/api/breaks/manual` | BreakService | Create a manual break slot |

### WIC Coverage

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/wic-coverage/agents?search=` | WicCoverageService | Agents with coverage roles and reachability |
| GET | `/api/wic-coverage/agents/{kid}` | WicCoverageService | Single agent by KID |
| PATCH | `/api/wic-coverage/agents/{kid}` | WicCoverageService | Update agent HasCar / GroupRegion |
| GET | `/api/wic-coverage/wics?search=` | WicCoverageService | Active WICs with MAIN/BACKUP counts |
| GET | `/api/wic-coverage/wics/{locationCode}` | WicCoverageService | Full WIC coverage plan (Main/BackupA/B/C) |
| GET | `/api/wic-coverage/wics/{locationCode}/reachable-agents` | WicCoverageService | Agents reachable for a WIC via city |
| POST | `/api/wic-coverage/wics/{locationCode}/backup-b` | WicCoverageService | Pin a BackupB agent to a BACKUP assignment |

### Sick Leave

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/sickleave?from=&to=&teamLead=&type=&activeOnly=` | SickLeaveService | Filtered sick leave records |
| GET | `/api/sickleave/active?date=` | SickLeaveService | Active sick leaves on a date |
| GET | `/api/sickleave/stats` | SickLeaveService | Count, avg duration, by-TeamLead breakdown |
| POST | `/api/sickleave` | SickLeaveService | Create sick leave + sync ShiftEntries |
| PATCH | `/api/sickleave/{id}` | SickLeaveService | Update sick leave + re-sync ShiftEntries |
| DELETE | `/api/sickleave/{id}` | SickLeaveService | Remove sick leave record |
| GET | `/api/sickleave/download?from=&to=` | SickLeaveService | Excel export with duration-based row colors |

### Vacations

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/vacations?...` | VacationService | Filtered vacation records |
| GET | `/api/vacations/active?date=` | VacationService | Active vacations on a date |
| GET | `/api/vacations/upcoming?days=` | VacationService | Upcoming vacations within N days |
| DELETE | `/api/vacations/{id}` | VacationService | Remove a vacation record |
| GET | `/api/vacations/download?from=&to=` | VacationService | Excel export |

### AL Balance

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/albalance` | ALBalanceService | All AL balance records |
| GET | `/api/albalance/{id}` | ALBalanceService | Single AL balance record |
| PATCH | `/api/albalance/{id}` | ALBalanceService | Update AL balance |

### Employees

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/employees?...` | EmployeeService | Filtered employee list |
| GET | `/api/employees/{id}` | EmployeeService | Single employee |
| GET | `/api/employees/{id}/timeline` | EmployeeService | Employee shift/absence timeline |
| POST | `/api/employees` | EmployeeService | Create employee |
| DELETE | `/api/employees/{id}` | EmployeeService | Remove employee |
| PATCH | `/api/employees/{id}/albalance` | EmployeeService | Update employee's AL balance |

### Attendance

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/attendance?...` | AttendanceService | Attendance data with filters |
| GET | `/api/attendance/download?...` | AttendanceService | Excel export |

### Miscellaneous

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/public-holidays` | PublicHolidayService | All public holiday records |
| GET | `/api/training` | TrainingService | Training schedule data |
| GET | `/api/pipeline` | PipelineService | All pipeline events |
| POST | `/api/pipeline` | PipelineService | Create pipeline event |
| PATCH | `/api/pipeline/{id}` | PipelineService | Update pipeline event |
| DELETE | `/api/pipeline/{id}` | PipelineService | Delete pipeline event |
| GET | `/api/alcalendar?from=&to=` | ALCalendarService | AL calendar with team-lead warnings |

### Overview

| Method | Route | Handler | Description |
|--------|-------|---------|-------------|
| GET | `/api/overview/wic-status?date=&horizon=` | OverviewService | Per-location per-day coverage status (up to 7 days) |
| GET | `/api/overview/detail?type=&date=` | OverviewService | Agents by shift type for a date |

---

## Frontend Pages and Components

### Pages

| Route | Component | File | Write Operations | Key API Calls |
|-------|-----------|------|-----------------|---------------|
| `/` | Overview | `pages/Overview.tsx` | None | `/api/wic/forecast`, `/api/wic/briefing`, `/api/wic/substitutes` |
| `/shifts` | Shifts | `pages/Shifts.tsx` | None | `/api/shifts` |
| `/wic-shifts` | WicShifts | `pages/WicShifts_old.tsx` | Reassign agents, create new shift | `/api/wic`, `/api/wic/shifts` |
| `/vwic` | VWICPage | `pages/VWICPage.tsx` | Assign/manage agents, generate and save rotation plan | `/api/vwic/*` |
| `/breaks` | BreakPlanner | `pages/BreakPlanner.tsx` | Auto-distribute, start/end/cancel breaks | `/api/breaks/*` |
| `/wic-attendance` | WicAttendance | `pages/WicAttendance.tsx` | Accept substitute, assign agent, manual check-in | `/api/wic/forecast`, `/api/wic/cards`, `/api/wic/substitutes`, `/api/wic/substitutes/accept` |
| `/wic-schedule` | WicSchedule | `pages/WicSchedule.tsx` | None | `/api/wic/schedule` |
| `/pipeline` | Pipeline | `pages/Pipeline.tsx` | Create / edit / delete events | `/api/pipeline` |
| `/training` | Training | `pages/Training.tsx` | None | `/api/training` |
| `/wic` | WicLocations | `pages/WicLocations.tsx` | None | `/api/wic/locations` |
| `/attendance` | Attendance | `pages/Attendance.tsx` | None | `/api/attendance` |
| `/sickleave` | SickLeave | `pages/SickLeave.tsx` | Add / patch / delete sick leave | `/api/sickleave/*` |
| `/vacations` | Vacations | `pages/Vacations.tsx` | None | `/api/vacations` |
| `/albalance` | ALBalance | `pages/ALBalance.tsx` | None | `/api/albalance` |
| `/alcalendar` | ALCalendar | `pages/ALCalendar.tsx` | None | `/api/alcalendar` |
| `/employees` | Employees | `pages/Employees.tsx` | Create / delete employees, edit AL balance | `/api/employees`, `/api/albalance` |
| `/wic-coverage` | WicCoverage | `pages/WicCoverage.tsx` | Patch agent (HasCar / GroupRegion), pin BackupB | `/api/wic-coverage/*` |

### Components

| Component | File | Purpose |
|-----------|------|---------|
| CoverageBadge | `components/CoverageBadge.tsx` | Status badge rendering COVERED / PARTIAL / UNCOVERED / CLOSED. Props: `status`, `compact?` |
| Sheet | `components/Sheet.tsx` | Right-side slide-in drawer used for substitute and detail panels |
| ShiftBadge | `components/ShiftBadge.tsx` | Color-coded badge for shift type codes (VOICE, CHAT, AL, SL, etc.) |
| ThemeToggle | `components/ThemeToggle.tsx` | Sun / Moon icon button. Uses `next-themes` `useTheme()` hook |
| CommandPalette | `components/CommandPalette.tsx` | Ctrl+K global search overlay. Searches `/api/wic/locations` and navigates to `/wic-attendance?location=` |
| DownloadButtons | `components/DownloadButtons.tsx` | Reusable Excel download button row |
| ALPlanningModal | `pages/ALPlanningModal.tsx` | Modal for AL planning requests. Embedded in WicAttendance |
| AssignAgentModal | `pages/AssignAgentModal.tsx` | Modal for manually assigning an agent to a WIC location |
| ManualCheckinModal | `pages/ManualCheckinModal.tsx` | Modal for manually checking in an agent |
| AppLayout | `App.tsx` (inline) | Top-level shell: sidebar nav, topbar with date/horizon picker, Ctrl+K trigger, LangToggle, ThemeToggle, and React Router `<Routes>` |

---

## Known Issues and Flags

| Flag | Location | Description |
|------|----------|-------------|
| **DEAD** | `GET /api/breaks/diag` | Diagnostic endpoint. Not a production feature and should not be relied upon. |
| **VESTIGIAL** | `PATCH /api/shiftplan/reorder` — `Backend/ShiftReorderService.cs` | Accepts an ordered employee ID list but performs no action. Returns success immediately. Frontend `client.ts` does not call this endpoint. Order is managed frontend-side. |
| **INCONSISTENCY** | `CoverageEvaluator.EvaluateAsync` — `Backend/Services/CoverageEvaluator.cs` | Treats HALF_AL as fully absent (0.0 coverage credit). All other services — including SubstitutionService, ForecastService, and VwicService — give HALF_AL a 0.5 coverage credit. Only SubstitutionService calls EvaluateAsync directly, making it the one consumer affected by this inconsistency. |
| **DUPLICATE_LOGIC** | `BackupService` vs `SubstitutionService` | Both services rank substitute candidates for WIC coverage gaps. `BackupService` (`Backend/BackupService.cs`) uses simpler scoring and predates `SubstitutionService` (`Backend/Services/SubstitutionService.cs`). `SubstitutionService` is the canonical engine. `BackupService` is retained but should be treated as legacy. |
| **MISLEADING_NAME** | `pages/WicShifts_old.tsx` | The filename contains "old" but this is the live, actively used WicShifts page component served at `/wic-shifts`. It is not an archived or deprecated file. |
