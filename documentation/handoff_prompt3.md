# Handoff — Prompt 3
Date: 2026-06-15
Session status: COMPLETE (build SUCCESS, all 5 endpoints confirmed)

---

## What was done

### Step 0A — HALF_AL consistency
All 6 coverage-computing services now handle HALF_AL identically:
- `presentDouble += 0.5` for HALF_AL shift type
- `effectiveCoverage = (int)Math.Floor(presentDouble)`
- Services fixed: WicShiftService, BackupService, OverviewService (SubstitutionService was already correct)

### Step 0B — DayOfWeek bug fix
`WicShiftService.GetAvailableHoursAsync` was converting to SQL-style DOW (1=Mon...7=Sun).
DB confirmed (PS1_13): uses .NET convention 0=Sun...6=Sat.
Fixed: `int dow = (int)x.Wic.ShiftDate.DayOfWeek;` — no conversion.

### Step 0C — WicLocationMatcher extraction
New file: `Backend/Services/WicLocationMatcher.cs`
- Static class, 30-entry alias dictionary
- `MatchesSupportLocation(string? sl, WicLocation loc)` — checks DisplayName, City, and aliases against both LocationCode AND LocationCodeLegacy
- `MatchesAssignmentCode(string code, WicLocation loc)` — checks LocationCode OR LocationCodeLegacy
Removed duplicate alias dictionaries from: WicShiftService, SubstitutionService, BackupService, CoverageEvaluator, OverviewService, WicCardsService (5 copies eliminated).

### Step 0D — MinAgentsRequired
PS1_13 confirmed all 43 WicLocations had MinAgentsRequired = NULL.
Auto-UPDATE set all to 1. Field is now queryable and non-null.

### Step 1 — ForecastService.cs
File: `Backend/Services/ForecastService.cs`
Endpoint: `GET /api/wic/forecast?horizon=14&locationCode=optional`
- horizon clamped 1-30
- Per location per day: checks open/closed (holidays included), fractional HALF_AL coverage, CoverageEvaluator.Classify
- coverageBuffer = effectiveCoverage - minRequired
- isAtRisk = status is UNCOVERED or PARTIAL
DTOs: ForecastDayDto, ForecastLocationDto, ForecastResponse

### Step 2 — WhatIfService.cs
File: `Backend/Services/WhatIfService.cs`
Endpoint: `GET /api/wic/whatif?absentEmployeeId=X&date=Y&horizon=5`
- horizon clamped 1-14
- Finds MAIN assignments by FullName match; resolves via locByCode OR locByLegacy
- Calls SubstitutionService.GetSubstitutesAsync with [employeeId] as explicitAbsentIds
- No DB writes — pure simulation
DTOs: WhatIfLocationResult, WhatIfResponse

### Step 3 — BriefingService.cs
File: `Backend/Services/BriefingService.cs`
Endpoints:
- `GET /api/wic/briefing` — JSON summary for today: absences, gaps, next AT_RISK days
- `GET /api/wic/briefing/export` — 3-sheet ClosedXML Excel export
Sheet colors: Absences (#dc2626), Coverage Gaps (#d97706), Next AT_RISK Days (#7c3aed)
- For each gap location, calls SubstitutionService in try/catch to get best substitute name
DTOs: BriefingAbsenceDto, BriefingGapDto, BriefingAtRiskDto, BriefingResponse

### Step 4 — SubstitutionHistory + fairness penalty
New table: SubstitutionHistory (created via PS1_14)
- Id, EmployeeId, LocationCode, Date, SourceType, AssignedAt
- Indexes: IX_SubHist_Emp, IX_SubHist_Date
New field: `int LoadScore` on SubstituteCandidate record
Score formula: `sourceBonus + tierScore - LoadScore * 10.0`
30-day rolling history loaded in SubstitutionService; graceful try/catch if table missing.
New model: `OtherModels.cs` → SubstitutionHistory class
New DbSet: `GSDContext.cs` → `DbSet<SubstitutionHistory>`

### Step 5 — Build confirmed SUCCESS
PS1_15 output (2026-06-15 15:14:45):
- Build: SUCCESS, 0 errors, 2 warnings
- Reachability sanity: PASSED (503.5 km Berlin to Munich)
- GET /api/wic/forecast: OK — 43 locations, 6 at-risk days
- GET /api/wic/briefing: OK — 8 absences, 1 gap, best sub = Baschir Mahrufi
- GET /api/wic/whatif: OK — 2 affected locations
- GET /api/wic/substitutes regression: OK

---

## Files changed

### New files
- `Backend/Services/WicLocationMatcher.cs`
- `Backend/Services/ForecastService.cs`
- `Backend/Services/WhatIfService.cs`
- `Backend/Services/BriefingService.cs`
- `PS1_13_Step0_Verify.ps1`
- `PS1_14_SubstitutionHistoryTable.ps1`
- `PS1_15_BuildTest3.ps1`
- `PS1_16_TunnelTest.ps1`

### Modified files
- `Backend/Services/CoverageEvaluator.cs` — removed duplicate alias map, uses WicLocationMatcher
- `Backend/WicShiftService.cs` — HALF_AL fix, DayOfWeek fix, WicLocationMatcher
- `Backend/BackupService.cs` — HALF_AL fix, WicLocationMatcher
- `Backend/OverviewService.cs` — HALF_AL fix, WicLocationMatcher
- `Backend/WicCardsService.cs` — WicLocationMatcher
- `Backend/Services/SubstitutionService.cs` — WicLocationMatcher, LoadScore, 30-day fairness
- `Backend/OtherModels.cs` — SubstitutionHistory model
- `Backend/GSDContext.cs` — SubstitutionHistory DbSet + index config
- `Backend/Program.cs` — registered ForecastService, WhatIfService, BriefingService; mapped all 3 endpoints

---

## DB state (as of Prompt 3)
- WicLocations: 43 rows, all IsActive=1, all MinAgentsRequired=1
- DayOfWeek in WicOpeningHours: 0=Sun...6=Sat (.NET convention)
- SubstitutionHistory table: exists, 0 rows (populated at runtime by substitution assignments)

---

## Known issues / deferred
- Tunnel test (PS1_16) was written but output not captured before context limit; user moved to Prompt 4
- SubstitutionHistory is written by BriefingService when it records confirmed assignments, but there is no POST endpoint yet to record substitutions from the UI — history will grow only from briefing usage
- Frontend not yet started (all 5 new backend endpoints have no UI)

---

## Prompt 4 starting state
- Backend: fully built and running at http://localhost:5000
- Tunnel: https://n8jlr9dr-5000.euw.devtunnels.ms
- Frontend: React 19 + Vite + Tailwind CSS 3.4.19 + TanStack Query + next-themes (all installed)
- Missing frontend deps: react-leaflet, leaflet (to be installed in Prompt 4 Step 1)
- Target page: /wic-attendance — coverage status, agent chips, substitute finder drawer
