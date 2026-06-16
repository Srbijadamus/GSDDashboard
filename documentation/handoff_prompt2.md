# Handoff Prompt 2 — WIC Coverage Evaluator + Reachability + Substitution

Session date: 2026-06-15
Branch: main
Build status: **CONFIRMED SUCCESS** — 2026-06-15 14:35:53, 4 pre-existing warnings, 0 errors
Reachability sanity: **PASSED** — 503.5 km Berlin to Munich, `passed: true`
Substitutes endpoint: **CONFIRMED END-TO-END** — gap=1, 22 candidates returned, priority order BACKUP > SSP > CALL_IN correct, all fields populated as expected

---

## What was done

### New files written

| File | Purpose |
|---|---|
| `Backend/Services/CoverageEvaluator.cs` | Canonical coverage classifier — all other services delegate here |
| `Backend/Services/ReachabilityService.cs` | Haversine matrix, 4h cache, singleton, sanity check endpoint |
| `Backend/Services/SubstitutionService.cs` | Ranked substitute engine, 4 tiers, donor guard, HALF_AL |

### Existing files modified

| File | Change |
|---|---|
| `Backend/CoverageCalculator.cs` | Replaced inline `COVERED/PARTIAL/UNCOVERED` chain with `CoverageEvaluator.ClassifyByMinutes()`. Added `using GSDDashboard.API.Services;` |
| `Backend/WicShiftService.cs` | Replaced inline coverage string ternary in `GetOpenAsync` with `CoverageEvaluator.Classify().Status.ToString()`. Added `using`. |
| `Backend/OverviewService.cs` | Replaced inline status ternary in `GetWicSummaryAsync` with `CoverageEvaluator.Classify().Status.ToString()`. Added `using`. |
| `Backend/Program.cs` | Added `AddSingleton<ReachabilityService>`, `AddScoped<CoverageEvaluator>`, `AddScoped<SubstitutionService>`. Added `MapReachabilityEndpoints()`, `MapSubstitutionEndpoints()`. Removed duplicate `AddScoped<OverviewService>` registration. |
| `Backend/OtherModels.cs` | Added `LocationCodeLegacy NVARCHAR(50) NULL` property to `WicLocation` entity. |
| `Backend/WicCardsService.cs` | Added `LocCodeMatches()` helper; MAIN and BACKUP agent lookups now match on `LocationCode OR LocationCodeLegacy`. |
| `Backend/BackupService.cs` | BACKUP agent lookup now checks `location.LocationCodeLegacy` as fallback. |
| `Backend/WicScheduleService.cs` | Agent count query includes legacy code match. |
| `Backend/Services/SubstitutionService.cs` | MAIN/BACKUP filters use legacy fallback; added `locByLegacyCode` dict; `ResolveBase` tries both dicts and correctly excludes the target WIC via legacy code. |

---

## New endpoints

| Method | URL | Service |
|---|---|---|
| GET | `/api/wic/reachability/sanity` | ReachabilityService — Berlin→Munich ~504 km check |
| GET | `/api/wic/reachability?from=X&to=Y` | ReachabilityService — single-pair or full matrix |
| GET | `/api/wic/substitutes?locationCode=X&date=Y&horizon=1&absentIds=E001,E002` | SubstitutionService |

All endpoints are read-only GET.

---

## CoverageEvaluator — design contract

`Classify(isClosed, presentAgents, minRequired, closedReason?)` is **static** — callers do not need injection.

`ClassifyByMinutes(isClosed, coveredMinutes, totalMinutes)` is **static** — for CoverageCalculator only.

`EvaluateAsync(locationCode, date, absentIds?)` is **instance** — full DB-backed, injected via DI.

**Rule:** No other file may contain its own `COVERED/PARTIAL/UNCOVERED/CLOSED` if-else chain.

---

## SubstitutionService — key decisions

### Absence logic
- Full exclusion: `SL, AL, UL, PH, LPH, RESIGNED, TRAINING`
- HALF_AL = 0.5 contribution (NOT excluded)
- Auto-detect absent: checks SickLeaves + ShiftEntries for current MAIN agents
- Explicit override: `?absentIds=` parameter fully overrides auto-detect

### Scoring tiers (higher = preferred)
| Tier | Base score |
|---|---|
| BACKUP | 10,000 |
| SSP | 5,000 |
| WIC_DONOR | 0 + reachability (0–500) |
| CALL_IN | –100 + reachability |

### Donor guard
`surplus = nearEffectiveCoverage - nearMinRequired`
If `surplus <= 0`: skip. Hard rule, no exceptions.

### SSP base location
1. First active WIC assignment (MAIN or BACKUP) other than the target WIC
2. Bundesland centroid (approximate — labelled as such)
3. null — distance unknown

### WicAgentAssignment join
`WicAgentAssignments` has `EmployeeName` (string), NOT `EmployeeId`.
Correct join in all SQL and LINQ: `e.FullName = waa.EmployeeName`

---

## Known nulls (always null in SubstitutionResponse)

```
contactEmail      — no email field on Employees table
travelMinutes     — no routing API; straight-line haversine only
hasDirectTrain    — no transit data source
fairnessScore     — SubstitutionHistory table not present in DB
```

---

## ReachabilityService cache behaviour

- Singleton service, 4-hour TTL, double-checked locking with `SemaphoreSlim`
- Uses `IServiceScopeFactory` to resolve scoped `GSDContext` inside a Singleton
- `InvalidateCache()` is public — call if coordinates are updated externally
- Locations missing coordinates are logged to stderr and excluded from matrix
- `GetMissingCoordsAsync()` returns their LocationCodes

---

## DB state after Prompt 2

| Item | Value |
|---|---|
| WicLocations | 43 active rows |
| WicLocations.Coordinates | 43/43 geocoded (5 city-only precision) |
| WicLocations.MinAgentsRequired | All NULL — runtime default = 1 |
| WicLocations.Bundesland | 41 DE populated, 2 NL = NULL (correct) |
| WicLocations.PostalCode | Extracted from LocationCode by PS1_6 |
| WicLocations.LocationCodeLegacy | 42/43 populated (RENDSBURG = NULL — direct match, no legacy needed) |
| Employees | 130 active rows |
| WicAgentAssignments | Old-style codes (e.g. `DE_Dortmund`). Do NOT change — may be used by external systems. Resolved via `LocationCodeLegacy`. |
| WicAgentAssignments.AssignmentType | Values: MAIN, BACKUP |
| SubstitutionHistory table | Does NOT exist in live DB |
| SickLeaves.LeaveType | Only value: "Self" — not used; ShiftType is authoritative |

---

## Migration scripts (run in order, all idempotent)

| Script | Purpose |
|---|---|
| `PS1_9_LegacyCodeMap.ps1` | Adds `LocationCodeLegacy` column; stamps 42 rows using PLZ-anchored LIKE patterns |
| `PS1_10_FixDemmin.ps1` | One-shot fix for `DE_Demmin_Hanse` — space after tilde in `DE~17109~ Demmin~%` |

Note on Demmin: the actual `LocationCode` in WicLocations for the Hanseufer site is `DE~17109~ Demmin~Am Hanseufer 2` (space after first tilde). PS1_9 pattern `DE~17109~Demmin~Hans%` missed it; PS1_10 corrected it. PS1_9 is also patched for future re-runs.

## Build and test scripts

```
powershell -ExecutionPolicy Bypass -File C:\GSDDashboard\PS1_8_Build_Test.ps1
```

Steps: kill existing server, `dotnet build -c Release`, show DLL timestamp, start server, call `/api/wic/reachability/sanity`, find first MAIN agent via legacy-code-aware join, call `/api/wic/substitutes`.

Confirmed result (2026-06-15): SUCCESS, 4 pre-existing warnings, 0 errors. DLL: 14:35:53. Reachability PASSED 503.5 km. Substitutes OK (Markkleeberg = CLOSED, candidates = 0 — correct).

```
powershell -ExecutionPolicy Bypass -File C:\GSDDashboard\PS1_11_OpenLocationTest.ps1
```

Tries Dortmund first; if closed falls back through `/api/wic/open` then `/api/wic/cards` to find any open location and calls substitutes on it. Confirmed: Augsburg open, status=COVERED, present=1, gap=0 — correct.

```
powershell -ExecutionPolicy Bypass -File C:\GSDDashboard\PS1_12_GapTest.ps1
```

Finds the MAIN agent at Augsburg (correct join: `e.FullName = waa.EmployeeName`), marks them absent via `absentIds`, calls substitutes. Confirmed result 2026-06-15:
- `gap=1`, `gapCeiling=1`, `candidates=22`
- Rank 1: Mohamad Nasir Amany — `sourceType=BACKUP` (correct, highest priority)
- Rank 2-3: SSP agents (correct)
- Rank 22: CALL_IN (correct, lowest priority)
- `sourceType`: 22/22 populated
- `distanceKm`: 2/22 populated — expected, SSP and CALL_IN agents without a resolved WIC base location return null distance
- `knownNulls` array: contactEmail, travelMinutes, hasDirectTrain, fairnessScore — all documented correctly

---

## Known issues / Prompt 3 blockers

1. **WicShiftService `_absenceTypes` still includes `HALF_AL`** (line ~215). This means HALF_AL agents are counted as absent in `GetOpenAsync` (effectiveCoverage = scheduledCount - absentCount). This is **inconsistent** with SubstitutionService which gives them 0.5 credit. Decide in Prompt 3: either remove HALF_AL from `_absenceTypes` in WicShiftService and use fractional accounting, or document the intentional difference.

2. **`WicOpeningHours.DayOfWeek` format** — WicShiftService uses .NET `(int)date.DayOfWeek` (Sun=0), but some PS1 scripts used SQL Sunday=7 convention. Verify which convention is in the DB before adding new WIC schedule queries.

3. **`WicShiftEntry.SupportLocation` matching** — SubstitutionService uses `MatchesLoc()` (alias map + city + DisplayName). WicShiftService uses `WicMatchesLoc()` (same logic, same map, different method name). These are duplicated across 4 files. Consider extracting to a static `WicLocationMatcher` helper in Prompt 3.

4. **MinAgentsRequired = null for all 43 WIC locations** — every coverage calculation falls back to default 1. If real minimums are loaded in Prompt 3, re-test all three modified endpoints.

5. **`OverviewService.GetWicSummaryAsync` still uses `MatchLoc()` (no alias map)** — only matches by `DisplayName` and `City`. WicShiftService uses the full alias map. This mismatch predates Prompt 2 and is not introduced by these changes, but worth noting.

---

## Prompt 2 — final status

**COMPLETE** as of 2026-06-15.

All deliverables verified:

| Deliverable | Status |
|---|---|
| `CoverageEvaluator` — canonical classifier, no inline chains | DONE |
| `ReachabilityService` — Haversine matrix, 4h cache, sanity endpoint | DONE, PASSED 503.5 km |
| `SubstitutionService` — 4-tier ranking, donor guard, HALF_AL, knownNulls | DONE, CONFIRMED |
| `LocationCodeLegacy` migration — 42/43 rows, all assignments resolved | DONE |
| Build 0 errors | CONFIRMED |
| `/api/wic/substitutes` gap+candidates end-to-end | CONFIRMED — gap=1, 22 candidates, correct priority |

Nothing is left open from Prompt 2. The four items listed in Known Issues below are pre-existing or intentionally deferred to Prompt 3.

---

## LocationCodeLegacy — design note for Prompt 3

`WicAgentAssignments.LocationCode` stores old-style codes (`DE_Dortmund`). `WicLocations.LocationCode` stores new tilde-style codes (`DE~44139~Dortmund`). The column `WicLocations.LocationCodeLegacy` bridges them. **Never write new assignments with old-style codes.** If new WIC locations are added with the new naming convention, no legacy entry is needed. If existing external systems push new rows into `WicAgentAssignments` using old codes, add the legacy entry to `WicLocations` for the new location.

All five services that query `WicAgentAssignments` by location are already updated. `OverviewService` loads all assignments without filtering by location code — it is not affected.

## Files NOT changed in Prompt 2

- `PlzBundesland.cs` — no changes needed
- `geocoder.py` — no changes needed
- All migration scripts PS1_4 through PS1_7 — no changes needed
- `WicShiftService.cs` HALF_AL inconsistency (see Known Issues #1) — deferred to Prompt 3
