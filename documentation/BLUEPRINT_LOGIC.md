# GSDDashboard — Business Logic and API Reference (Sections 4 and 7)

**Part of the Project Blueprint.** See [PROJECT_BLUEPRINT.md](./PROJECT_BLUEPRINT.md) for the index.

---

# Section 4 — Business Logic

---

## 4.0 Shift Plan — Coverage per Hour (`GET /api/shifts/coverage`)

**File:** `Backend/ShiftService.cs` → `GetCoverageAsync(date)`. This was previously undocumented
(not listed in FUNCTIONS.md's ShiftService method table) — added here as part of the vWIC /
Task-defaults fix (2026-09).

Powers the "Coverage per Hour" chart on the Shift Plan page (`Frontend/src/pages/CoverageBar.jsx`).
For each hour 07:00–18:00 on the given date:

1. Load all `ShiftEntries` for the date joined to `Employees`, excluding inactive agents and
   `PrimaryRole = "2nd Level"`.
2. Per agent, classify by `ShiftType`:
   - `AL` / `HALF_AL` → **AL** bucket
   - `SL` → **Sick** bucket
   - `TRAINING` → **Training** bucket
   - `OFF` / `OFF_WEEKEND` / `PH` → **Off** bucket
   - `WORKING` / `WIC_DUTY` → only counted if the hour falls inside `[ShiftStart, ShiftEnd)`
     (agents outside their shift hours are not counted at all)
   - Any other `ShiftType` (e.g. `UL`, `CD`, `EMPTY`) is not counted in any bucket.
3. For `WORKING`/`WIC_DUTY` rows inside their shift window:
   - `ShiftType = WIC_DUTY` (or legacy `IsWicDuty = 1`) → **WIC** bucket (physical WIC duty).
   - Otherwise (a `WORKING` row) → bucketed by **effective Task** (`AgentTasks.Resolve`, see
     BLUEPRINT_DATA_MODEL.md "Task defaults"): `Voice`→**Voice**, `VWIC`→**vWIC**, `WIC`→**WIC**,
     `Backlog`→**Backlog**, anything else (`Dispatcher`/`SME`/`SSP`, or no task/role match) →
     **Other**.
4. `MinRequired(hour)`: <08:00 → 1, 08:00–17:00 → 3, ≥17:00 → 1 (undocumented magic numbers,
   unchanged by this fix — no source found defining these thresholds beyond this function).
5. `BelowThreshold = Voice < MinRequired(hour)`. **Only the Voice bucket counts toward the
   threshold** — vWIC/WIC/Backlog/Other/AL/Sick/Training/Off agents do not, because they are not
   taking Voice calls.

**Bug fixed 2026-09:** previously every `WORKING` row counted as "Voice" regardless of the
agent's Task or Role (only `WIC_DUTY` was excluded), and `BelowThreshold` used `(Voice + WIC) <
Min`. This made the chart show ~44–46 apparent "Voice" agents at 09:00 on 2026-09-16 (all
WORKING agents: Dispatcher, SME, SSP, Backlog, VWIC, Chat, Chat CRO, Trainer, Booking Tool, Bulk
PWs, and real Voice agents, combined) versus the corrected 14 real Voice agents for the same
slot — see PART 1d numbers in the handoff for the full breakdown.

---

## 4.1 WIC Coverage Calculation

The coverage pipeline runs in `ForecastService.GetForecastAsync()`. The same pipeline is used (with bulk-loaded data) in `WicShiftService.GetOpenAsync()` and `SubstitutionService.GetSubstitutesAsync()`.

### Step-by-step: `ForecastService.GetForecastAsync(horizon, locationCode, fromDate)`

```
1. Clamp horizon to [1, 62]
2. Load all active WicLocations
3. Load all WicOpeningHours (flat list)
4. Load PublicHolidays in the window
5. Load WicShiftEntries in the window WHERE IsOnSite=1, JOIN to active Employees
6. Load ShiftEntries in the window → ShiftDuplicateResolver.BestShiftEntry per (EmployeeId, Date)
7. Load SickLeaves overlapping the window

For each location × date:
  a. WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy, dow, date)
     → picks the row with latest EffectiveFrom <= date for this location+dow
  b. Determine isClosed:
     - hours == null OR hours.IsClosed → CLOSED_DAY
     - OR publicHolidays.Any(ph => ph.HolidayDate == date && ph.IsNational) → PUBLIC_HOLIDAY
     - OR bundesland != null AND publicHolidays.Any(ph => ph.HolidayDate == date
         && ph.Bundesland == bundesland) → PUBLIC_HOLIDAY (regional)
     *** Closed check happens FIRST. If closed, skip all agent calculations.
         This avoids false UNCOVERED signals for days the WIC is not operating.
         Verified correct 2026-09-03: ForecastService loads WicOpeningHours at step 3 (line 62);
         closed days return IsAtRisk=false and are skipped — 190 at-risk on open days, 0 on closed days. ***
  c. If open:
     presence = WicPresenceResolver.Resolve(loc, date, wicEntries, shiftByEmpDate,
                                            candidateDutyRows, isSick)
     presentDouble = presence.PresentDouble        // both signals, deduplicated
     scheduledCount = presence.OnSiteCount + presence.CheckInNotRecordedCount
     effectiveCoverage = (int)Math.Floor(presentDouble)
     minReq = hours.MinRequired ?? loc.MinAgentsRequired ?? 1
     evalResult = CoverageEvaluator.Classify(false, effectiveCoverage, minReq)
```

### `AvailabilityResolver.GetWicContribution(isSick, sh)`

This is the single canonical function for computing an agent's WIC contribution. All services call this — no service maintains its own if-else chain.

```
if (isSick)                                          → return 0.0
if (sh == null)                                      → return 0.0   (unknown is not present)
if (sh.ShiftType IN FullAbsenceTypes)                → return 0.0
if (sh.ShiftType == "HALF_AL")                       → return 0.5
if (sh.ShiftType == "WIC_DUTY")
    AND sh.AgentTask IN ("GSD", "Backlog")           → return 0.0   (agent doing non-WIC work despite shift type)
if (sh.ShiftType == "WIC_DUTY")                      → return 1.0
else (WORKING, TRAINING, or other non-WIC type)     → return 0.0   (agent is doing other work, not WIC duty)
```

`FullAbsenceTypes = { "SL", "AL", "UL", "OL", "PH", "LPH", "RESIGNED" }`

**Rule changed 2026-09-30:** `sh == null` returns `0.0`, not `1.0`. A missing `ShiftEntry` cannot rule out an absence that was never imported, so unknown is not counted as coverage — same rule as `RosterService.IsAvailable`.

**Rule introduced 2026-09-03:** `WIC_DUTY` with `AgentTask = "GSD"` or `"Backlog"` returns `0.0`. Historical impact: 223 rows (March–June 2026, all in `AgentTask = 'BACKLOG'`), 0 current rows (July 2026 onward). Change is intentionally retroactive — those 223 rows were always miscounted as full coverage. Comparison is case-insensitive (`"BACKLOG"` and `"Backlog"` are equivalent). The check reads `ShiftEntries.AgentTask` — **not** `WicShiftEntries.Task` (which holds "WIC"/"VWIC"/null and never "GSD"/"Backlog").

**Key rule:** An agent scheduled as `WORKING` (doing GSD voice/chat work) counts as `0.0` for WIC coverage — they are not covering the WIC even if they have a `WicShiftEntry` with `IsOnSite=1`. Only `WIC_DUTY` counts as full contribution.

### `AvailabilityResolver.IsWicDutyAt(sh, loc)` / `GetWicContributionAt(isSick, sh, loc)`

The **location-aware** reading of the same row, and the single definition of "this
`ShiftEntries` row means the agent is on WIC duty at this centre". Every reader of the
`ShiftEntries` cover signal calls it instead of testing `ShiftType == "WIC_DUTY"` itself.

```
IsWicDutyAt(sh, loc):
  if (sh == null)                                         → false
  if (!WicLocationMatcher.MatchesAgentTask(sh.AgentTask, loc)) → false
  if (sh.ShiftType == "WIC_DUTY")                         → true
  if (sh.ShiftType IN NotOnWicDutyTypes)                  → false
  if (sh.ShiftType == "WORKING")                          → true    (WIC_DUTY encoding, ShiftType flipped)
  else                                                    → false   (unknown is not present)

NotOnWicDutyTypes = FullAbsenceTypes + { "HALF_AL", "OFF", "OFF_WEEKEND", "CD", "CO", "TRAINING" }

GetWicContributionAt(isSick, sh, loc) =
  isSick                  → 0.0
  IsWicDutyAt(sh, loc)    → 1.0
  otherwise               → GetWicContribution(isSick, sh)
```

**Why a `WORKING` row can count.** `AgentTask` carries a centre's `DisplayName` *only* on
the `WIC_DUTY` encoding; on a plain `WORKING` row the column holds one of the seven task
badges or `"GSD"`, never a centre name (see `AgentTasks.NonLocationValues`). A row whose
`AgentTask` names a centre is therefore a WIC-duty row even when its `ShiftType` reads
`WORKING`. Reading `ShiftType` alone turns one wrong field into a phantom `UNSTAFFED` day
that no RTM can explain from the UI, because the task cell shows the centre name while the
day is reported uncovered — the Berlin - Brückenstrasse 2026-10-13 false positive.

Such rows exist in the data: `RosterBatch` 155/156/170 (`CreatedByKid=SYSTEM_WICFIX`,
2026-09-30 06:09, overwrite mode) left rows with `RawValue=NULL` + `AgentTask=<centre>` but
`ShiftType=WORKING`, after which the `Program.cs` startup cleanup zeroed their `IsWicDuty`
flag. Corroborated by data that admits no other reading: Merlin Voss 2026-10-01/02 carries
exactly this shape **and** a `WicShiftEntries` `IsOnSite=1` row at the same centre.

**Absence rows are never reinterpreted.** A leave import overwrites `ShiftType` and leaves
the previous `AgentTask` behind — the DB holds e.g. 14 `AL` rows still tagged `"Hamburg"`.
`NotOnWicDutyTypes` is what keeps those out; reading them as coverage would hide exactly
the gaps the coverage-gaps endpoint exists to find. Both directions are pinned by
`Backend.Tests/WicDutyRecognitionTests.cs`.

A report-only startup check in `Program.cs` logs the count of these flipped-`ShiftType`
rows. It never mutates them: `ShiftType` is roster truth and only an RTM may change it.

### `WicPresenceResolver.Resolve(loc, date, onSiteRows, shiftByEmpDate, candidateDutyRows, isSick)`

**The single definition of "who is covering this WIC location on this date, and how do we
know".** Both cover signals are unioned here, once, and every surface reads coverage
through it.

```
Signal 1 — on-site WicShiftEntries rows:
    w.ShiftDate == date AND w.IsOnSite
    AND WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, loc)
    contribution = AvailabilityResolver.GetWicContributionAt(isSick, shiftByEmpDate[emp,date], loc)

Signal 2 — ShiftEntries WIC duty with no on-site row:
    s.ShiftDate == date AND AvailabilityResolver.IsWicDutyAt(s, loc)
    AND the employee was not already counted by signal 1
    contribution = AvailabilityResolver.GetWicContributionAt(isSick, s, loc)

EffectiveCoverage        = floor(Σ contribution)        ← headcount, vs MinRequired
CheckInNotRecordedCount  = # contributing agents with no on-site row
Evidence                 = NONE        if nothing contributes
                           ON_SITE     if ≥1 contributing agent has an on-site row
                           ROSTER_ONLY otherwise (rostered, nothing confirmed on site)
```

Agents found by both signals are **counted once**, by employee id.

### `WicCoverageEvidence`

| Value | Meaning | How a surface must present it |
|---|---|---|
`NONE` | Nobody is contributing coverage. | A real gap if the centre is open and `present < MinRequired`. Offer a substitute. |
`ON_SITE` | At least one contributing agent has an on-site row. | Covered. |
`ROSTER_ONLY` | Coverage exists, but only from `ShiftEntries` WIC duty — no contributing agent has an on-site row. | **Staffed.** Never a gap, never offered a substitute. Surfaced as *"Staffed — check-in not recorded"* on the WIC Locations card. |

`ROSTER_ONLY` is the SYSTEM_WICFIX import defect (521 date+location rows as of
2026-09-30: `RosterBatch` writes created the `ShiftEntries` side and never wrote the
`WicShiftEntries` side). It is a **record-keeping** state, not a staffing gap — the same
distinction the coverage-gaps banner already draws for `NOT_RECORDED`.

`CheckInNotRecordedCount` is deliberately independent of `Evidence`: a centre where 2 of
3 required agents are confirmed on site reports `ON_SITE` but still has one missing
check-in, so the card can badge it without the status changing.

**Why this type exists.** The union used to live inline in `ForecastService` only.
`BriefingService` (Overview → Gaps & Recommendations) and `WicCardsService` (WIC
Locations page) built presence from signal 1 **alone**. On 2026-10-01 that made them
report 19 open, staffed centres as `0/1 UNCOVERED` — with a Find Substitute button —
while the banner, the Coverage Risk strip and the KPI cards all said the same 19 were
covered. Five surfaces, five readings, two of them missing a signal. **Any new surface
reads coverage through `WicPresenceResolver` or it will drift the same way.**

Pure and allocation-light: callers bulk-load rows for whatever window they need and pass
them in, so it is callable per location per date inside an existing loop with no extra
queries. `CandidateDutyRows(shiftEntries)` is the signal-2 pre-filter (drops task badges
and `"GSD"`, which can never name a centre).

### Headcount vs. minutes — a legitimate difference, not drift

`EffectiveCoverage` answers *"are enough heads rostered"* (`CoverageEvaluator.Classify`,
used by the banner, the forecast and the briefing). The WIC Locations card additionally
runs `CoverageCalculator` over each agent's **minutes** against the centre's opening
intervals (`ClassifyByMinutes`). A card can therefore read `PARTIAL` while the forecast
reads `COVERED`: one head is rostered, but their hours do not span every open interval.
That is a finer metric, not a disagreement — do not "fix" it by forcing the card to the
headcount status.

Five active centres have a second interval: Augsburg, Essenbach, Hannover, München,
Quickborn.

### Two-interval centres — the break is only a break

**RTM rule (2026-10-01):** at a centre with `OpenTime2`/`CloseTime2`, the gap between the
intervals is a break. WIC duty spans it, and the break is never a coverage hole.

`WicHoursResolver.DutyWindow(h)` is the single definition of the window a WIC duty row
must carry: **the first interval's open through the LAST interval's close** (Augsburg
Thursday 07:30–12:00 + 12:30–16:00 → `07:30–16:00`). Returns `(null, null)` for a closed
or time-less row so the caller keeps its own fallback. All three WIC row writers use it:

| Writer | Path |
|---|---|
`RosterService.BuildPlanAsync` | the WIC `PlanRow` for an open day |
`WicShiftService` WIC_DUTY upsert | `POST /api/wic/shifts` |
`WicShiftService.CreateAssignmentAsync` | centre-hours fallback only — the request-time and prior-`WorkingShift` priorities above it are unchanged |

**One spanning row, not two rows.** `ShiftEntries` carries a single `ShiftStart`/`ShiftEnd`
pair and has three unique indexes that each cap it at one row per agent-day
(`UQ_ShiftEntries_EmpDate` on `(EmployeeId, ShiftDate)`, plus `...EmpDateType` and
`...EmpDateSheet`), so a second row for the same agent and date **cannot be inserted at
all**. The codebase already used this convention for the mirror-image read:
`WicShiftService.SplitWorkingShift` collapses a two-block `WorkingShift` string to
first-start..last-end.

**Why the break can never become a hole.** `CoverageCalculator` measures required time as
the sum of the opening intervals only (`CalcOpenMinutes`: Augsburg = 270 + 210 = 480, the
30-minute break excluded) and credits an agent against each interval separately
(`CalcAgentCoverage`, and `CoveredByUnion` for the pooled figure). The gap belongs to
neither interval, so a spanning agent scores 480/480 = 100% FULL/COVERED automatically —
no special-casing was needed in `WicCardsService`, and an agent who really covers only the
first interval still scores 270/480 = 56% PARTIAL.

**Existing rows were not rewritten** (RTM decision: no DB writes). As of 2026-10-01 there
are **575 under-covering WIC_DUTY rows** across the five centres, **426 of them dated
2026-10-01 or later**, running to 2027-02-26. Until those are regenerated, Augsburg and
Hannover keep reading PARTIAL on the card while every headcount surface reads COVERED —
correctly, on both axes.


## Manual changes (RTM principle, 2026-10-01)

2026-10-05 secure-only `/shifts` cell fix: WORKING/WIC_DUTY selections now open
a viewport-level time-confirmation dialog, outside the clipped scrolling table.
The former Save control sat below the full status menu; an offline browser
fixture placed it at y1020..1050 in a 720px viewport, and selecting WORKING alone
sent no update. Times still require explicit Save; cancelling makes no change.
Validation/save failures are visible, without a silent validation bypass.
Existing working-time hard blocks remain unchanged.

Both the live-compatible `/api/shifts/{id}` PATCH and `/api/shifts/assign` POST
and the candidate's dedicated `/manual` aliases require DEV/RTM/TEAM_LEAD sessions,
independent of the general RBAC flag; ManualChangeBy comes from the session KID,
never the request body. Only a successful future cell edit/create stamps
ManualChangeBy/At/Note; another authorized human cell edit replaces those markers.
IsWicDuty follows the explicitly edited type. Business task/location/assignment
links are not implicitly cleared. **Provenance decision:** set AutoGenerated=false
on an authorized human cell edit; retain SourceModule, SourceId, SourceSheet and
PreviousStatus unchanged. The explicit
manual marker outranks that historical automatic provenance; no classification
is inferred from `"MANUAL"` strings or an unmarked historical row.

**Authorized manual-master protections (live since 2026-10-05):** roster preview,
overwrite and batch delete/restore, location coverage generation, sick/vacation/
other-leave sync and reverts (including deletes), future vacation reconciliation,
WIC shift/assignment upserts and closed-day cleanup, substitution, swap,
move-to-GSD and training writes preserve manually marked ShiftEntries. Paired WIC
links are retained when the guarded operation is skipped. Each skip logs operation,
row, actor and date; endpoint refusals return a conflict or an explicit
`success=false, skipped=true`. Roster collision/detached/restore-skipped counts
remain visible, and location-coverage counts only successful writes.
An EF SaveChanges safety net rejects unapproved changes/deletes even if a caller
clears the marker; marker concurrency tokens reject stale automatic writers loaded
before a human edit. SQL imports hold update/range locks until commit, check for
any manual row on employee/date (even a different SourceSheet), skip it and report
preserved counts. This does not constrain arbitrary external administrative SQL.
Automatic behavior on unmarked rows and working-time validation remain unchanged.

The candidate Bulk RTM uses authenticated `/api/shifts/assign/bulk` for its distinct
import operation; it does not stamp manual markers and cannot override one.
Old bulk clients using `/assign` are indistinguishable from live cell requests:
they get cell marking, never a client-supplied actor or a heuristic bypass.
Generic `/assign` keeps its duplicate conflict; the dedicated alias still allows
same-type time edits. No database access, backfill (including the unmarked Vincent
example), live change or deployment was performed. Deployment verification remains
the parent's responsibility; live static hotfix drift must be reconciled first.

**A manual change is a legitimate decision, never an error and never a gap.** It covers
manual exception days, ad-hoc deployments, hand-edited shift rows, manual assignments and
manual check-ins.

| Marker | Where | Meaning |
|---|---|---|
| `ShiftEntries.ManualChangeBy` / `ManualChangeAt` / `ManualChangeNote` | nullable, added 2026-10-01 | a human deliberately set this row. `ShiftEntry.IsManualChange` is the `[NotMapped]` predicate |
| `WicOpeningHours.ExceptionDate` | nullable DATE, added 2026-10-01 | this row applies to that ONE date only and overrides the weekday pattern |
| `WicOpeningHours.ManualChangeBy` / `ManualChangeAt` | nullable | who recorded the exception |

Added because the existing fields cannot tell manual from automatic: `SourceSheet`/
`SourceModule` can hold `"MANUAL"` but the shift-edit path does not write it, and
`AutoGenerated` only means "written by ShiftSyncService". On 2026-10-01 zero rows carried a
MANUAL marker while hand-edited rows demonstrably existed. All columns are additive and
nullable, created by idempotent `COL_LENGTH` guards in the `Program.cs` startup block (the
project’s existing mechanism) — no backfill, no existing row rewritten.

**Consequences, each covered by a test in `Backend.Tests/WicManualChangeTests.cs`:**
- `WicHoursResolver` prefers an `ExceptionDate` row over the weekday pattern, in **both**
  overloads. Since every surface resolves hours through it, an exception-open centre is
  open everywhere — Forecast, Briefing, CoverageGap, Cards, Locations, Overview KPIs and
  WIC Attendance — with no further change. An exception row never acts as a pattern row,
  so it cannot leak into the following week.
- `RosterService.IsProtectedFromOverwrite` returns true for any manual row, whatever its
  ShiftType. `ProtectedAbsenceTypes` alone did not cover a hand-edited `WORKING`/`BO` row.
  Automatic rows are still replaced normally.
- The `Program.cs` startup `IsWicDuty` cleanup and the flipped-ShiftType data-quality
  warning both skip manual rows (`AND ManualChangeBy IS NULL`).
- `WicPresentAgent.CheckInNotRecorded` is false for a manual duty: an ad-hoc deployment has
  no paired on-site row by design, so warning about it would turn a decision into a
  complaint. `WicPresence.ManualCount` and a neutral **"Manual"** label (who/when in the
  tooltip, never warning colours) are shown instead.
- Real gaps stay detectable: an exception-open day with nobody rostered and nobody checked
  in is still `UNCOVERED`/at-risk.

**Ad-hoc deployment with no centre link.** A kiosk check-in with work mode `WIC` by someone
not on today’s roster is presence, matched by `employee_id`. It is **not** guessed into a
centre: the kiosk’s `location` is free text (never compared) and the reliable link it does
hold, `status_log.wic_center_id`, is **not returned** by `GET /api/attendance`. Such agents
appear as **"Checked in, no centre assigned"** with an *Assign to centre* action.

### `CoverageEvaluator.Classify(isClosed, presentAgents, minRequired)`

The canonical headcount classifier. Every other service calls this static method.

```
if (isClosed)        → CLOSED
if (present <= 0)    → UNCOVERED  "0/N required agents present"
if (present < min)   → PARTIAL    "P/N required agents present"
else                 → COVERED    "P/N agents present"
```

A minute-ratio variant `ClassifyByMinutes(isClosed, coveredMinutes, totalMinutes)` is used by `CoverageCalculator` for the per-agent schedule view in WicCards.

---

## 4.2 WIC Hours Resolution

`WicHoursResolver.Resolve(all, locationCode, locationCodeLegacy, dayOfWeek, date)` returns the single effective opening-hour row for a location + day + date.

```
Filter: LocationCode == locationCode
        OR (locationCodeLegacy != null AND LocationCode == locationCodeLegacy)
        AND DayOfWeek == dayOfWeek
        AND (EffectiveFrom == null OR EffectiveFrom <= date)
Sort:   EffectiveFrom DESC (null treated as DateOnly.MinValue)
Take:   first
```

Result: The most-recently-effective row. Null means no hours are defined — treated as closed.

---

## 4.3 Substitution Candidate Selection

`SubstitutionService.GetSubstitutesAsync(locationCode, date, horizon, explicitAbsentIds)` returns substitute candidates for a WIC when it is under- or not covered.

### Closed-day handling

If `WicHoursResolver` returns null/closed, or a public holiday applies, the day is returned as `CLOSED` with zero candidates. No substitution is needed.

### Present-count calculation

Among the MAIN agents for the WIC:
- Agent is absent if: in `explicitAbsentIds`, OR has an active SickLeave, OR ShiftType is in `_fullAbsenceTypes`
- Agent is additionally absent if they have no WicShiftEntry with `IsOnSite=1` at **this** location today (they may be physically at a different WIC)
- HALF_AL agents count as 0.5 present

### Candidate pool (four buckets, evaluated in priority order)

**(A) Designated BACKUP / REGIONAL agents** — agents listed in `WicAgentAssignments` for this location with `AssignmentType = "BACKUP"` or `"REGIONAL"`. Not absent, not already on a WIC today.

**(B) SSP agents** — `PrimaryRole = "SSP"`. SSP agents work the backlog and can be reassigned without affecting WIC coverage elsewhere ("zero donor risk"). Not absent, not already on a WIC today.

**(B2) Voice / VWIC agents** — `PrimaryRole = "Voice"` or `"VWIC"`. Currently working (ShiftType = WORKING or null). These are office-based and redeployable. Not absent, not already on a WIC.

**(C) WIC_DONOR** — MAIN agents from nearby WICs that have surplus coverage (presentCount > minRequired). Ordered by distance ascending. Each donated agent reduces the donor WIC's count; a `donorImpact` string is computed (e.g. "Home WIC: 3→2, min=2 → PARTIAL after removal").

**(D) CALL_IN (last resort)** — Any active agent with ShiftType = OFF or OFF_WEEKEND. Requires explicit management approval.

### Scoring

```csharp
Score(c) = sourceBonus + tierScore - c.LoadScore * 10.0

sourceBonus:  BACKUP = 10000, SSP = 5000, WIC_DONOR = 0, CALL_IN = -100
tierScore (straight-line distance):
  VERY_EASY (< 30 km)  = 500
  EASY      (< 75 km)  = 400
  MODERATE  (< 150 km) = 300
  HARD      (< 300 km) = 200
  IMPRACTICAL (≥ 300)  = 50
LoadScore = count of SubstitutionHistory entries for this agent in the past 30 days
            (fairness balancing — agents used recently are ranked lower)
```

`LoadScore` is read from `SubstitutionHistory` in a `try/catch` — graceful fallback to 0 if table does not exist.

### Distance calculation

`HaversineKm(from, to)` — straight-line distance from "lat,lon" coordinate strings. Travel time and public transit are **not** calculated (always null in the response). `ReachabilityService` provides a cached distance matrix for known WIC-to-WIC pairs.

### Base location resolution

For each candidate:
1. Find their MAIN assignment at a WIC other than the target → use that WIC's coordinates
2. If HQ-based role (SSP, Voice, VWIC, Chat, Dispatcher, SME, Booking Tool) → `"51.2154,6.7837"` (Düsseldorf HQ)
3. If `Bundesland` is set → use `_bundeslandCentroids` (approximate regional centroid)
4. Otherwise → null (no distance calculated)

---

## 4.4 Location Matching

`WicLocationMatcher` is the single source of truth for all location string comparisons. No other file may contain its own alias dictionary.

### `MatchesSupportLocation(sl, loc)`

Used by every service that needs to check if a `WicShiftEntry.SupportLocation` belongs to a `WicLocation`.

```
1. If sl == null → false
2. Apply RepairDoubleEncoding(sl) — fixes Windows-1252/UTF-8 double-encoded values
   Guard: no work done if 'Ã' (U+00C3) is absent (clean values return immediately)
3. If sl == loc.DisplayName → true
4. If sl == loc.City → true
5. Lookup _aliases[sl] → if aliased code matches loc.LocationCode or loc.LocationCodeLegacy → true
6. Otherwise → false
```

The `_aliases` dictionary uses a culture-invariant, `IgnoreNonSpace` comparer, so "ü" and "u" resolve to the same key at lookup time. This handles DB values where the Excel import stored ASCII-romanised names ("Munchen") alongside Unicode names ("München").

### `MatchesAgentTask(agentTask, loc)`

Used by every service that needs to check if a `ShiftEntries.AgentTask` value names a
given `WicLocation`. The one definition of that rule — it previously existed as four
byte-for-byte copies (`WicCoverageGapService`, `ForecastService`, `RosterService`,
`WicScheduleService`).

```
1. If agentTask is null/blank → false
2. wasTruncated = agentTask.Trim().Length >= AgentTaskMaxLength (20)
   — judged on the STORED length, BEFORE repair: repairing a double-encoded value
     shortens it, so a repaired string is no longer a reliable witness of a cut
3. Apply RepairDoubleEncoding; drop a dangling trailing "Ã" left by a mid-pair cut
4. Exact match against DisplayName, LocationCode or LocationCodeLegacy → true
5. If wasTruncated: prefix match against any of those three → true
6. Otherwise → false
```

`AgentTaskMaxLength = 20` is the width of `ShiftEntries.AgentTask`. A centre's
`DisplayName` is stored there truncated to that width on `WIC_DUTY` rows, so
`"Berlin - Brückenstrasse"` (23 chars) persists as `"Berlin - Brückenstra"` (20).
**Requiring the column limit before accepting a prefix is load-bearing** — it is what
stops a short value from prefix-matching an unrelated centre.

### `RepairDoubleEncoding(s)`

Repairs Windows-1252 mis-decoded UTF-8 two-byte sequences. The pattern `Ã¼` is the result of reading UTF-8 bytes `0xC3 0xBC` as Windows-1252 characters (Atilde + `¼`). The method replaces seven such pairs to restore correct Unicode.

Guard: if the string does not contain `Ã` (U+00C3), the method returns immediately with no allocation — zero cost on clean values.

### `MatchesQuery(userTerm, displayName, city, locationCode)`

Used by the assistant handlers for NL query → location matching. Applies `StripUmlauts()` on both sides (ä→ae, ö→oe, ü→ue, ß→ss) so "muenchen" matches "München".

---

## 4.5 ShiftSyncService — Cascade Writes

When a `SickLeave` or `Vacation` record is created, `ShiftSyncService` writes corresponding `ShiftEntry` rows so the Shifts page reflects the leave.

### `SyncSickLeaveAsync(employeeId, dateFrom, dateTo, sourceId)`

- Caps `dateTo` at `today + 90 days` (prevents flooding from open-ended SL records with `LastDay = 2099-12-31`)
- Skips weekends (Saturday, Sunday)
- For existing ShiftEntry: saves `PreviousStatus = existing.ShiftType`, sets `ShiftType = "SL"`, `AutoGenerated = true`, `SourceModule = "SickLeave"`, `SourceId = sourceId`
- For missing ShiftEntry: creates a new one

### `RevertSickLeaveAsync(employeeId, dateFrom, dateTo, sourceId)`

- Finds rows where `SourceModule == "SickLeave"` and `SourceId == sourceId`
- If `PreviousStatus != null` → restore it (undo the override)
- If `PreviousStatus == null` → delete the row (it was newly created by the sync)

Same pattern applies for `SyncVacationAsync` / `RevertVacationAsync` but uses `shiftType = "AL"` (or `"HALF_AL"` for half-day bookings).

---

## 4.6 Duplicate ShiftEntry Resolution

`ShiftDuplicateResolver.BestShiftEntry(IGrouping)` — picks the entry with the highest `Id` from a group of entries for the same (EmployeeId, ShiftDate). This is the tie-breaker when an Excel import row and an AL_IMPORT row both exist for the same agent+date.

The method is called in every bulk-load path: `DashboardService`, `ForecastService`, `SubstitutionService`, `WicShiftService`.

---

## 4.7 Public Holiday and Regional Holiday Logic

```
isNational = publicHolidays.Any(ph => ph.HolidayDate == date && ph.IsNational)
bundesland = loc.Bundesland ?? PlzBundesland.Get(loc.LocationCode, loc.PostalCode, loc.Country)
isRegional = bundesland != null
             && publicHolidays.Any(ph => ph.HolidayDate == date
                                        && ph.Bundesland == bundesland  // case-insensitive)
isClosed = isNational || isRegional || hours == null || hours.IsClosed
```

`PlzBundesland.Get()` is a lookup function that derives the Bundesland from the postal code when `WicLocations.Bundesland` is not set.

---

## 4.8 Assistant Router

`AssistantService.AskAsync(rawQuestion)` is the entry point.

```
1. Trim and cap question at 500 characters
2. IsUnsupportedLanguage() check:
   - Reject non-Latin scripts (Greek, Cyrillic, Arabic, CJK, Korean)
   - Reject Spanish punctuation (¿ ¡)
   - Reject any non-ASCII character outside the German allowed set
     (Ä Ö Ü ä ö ü ß — code points 0x00C4, 0x00D6, etc.)
   - Reject known Romance-language keywords
3. SharedParser.NormalizeUmlauts(q.ToLowerInvariant()) → normalizedQ
   NormalizeUmlauts: ä→ae, ö→oe, ü→ue, ß→ss
4. Score all handlers: handler.Score(normalizedQ) for each IDomainHandler
5. Sort descending by score; filter to nonzero scores
6. If nonzero.Count == 0 → generic help message
7. If top two scores are equal (tie) → disambiguation message naming both domains
8. Otherwise → call winner.HandleAsync(SharedParser.Parse(rawQ))
```

`SharedParser.Parse(rawQ)` returns an `AssistantParsedQuery` with:
- `NormalizedQ` — umlaut-normalised lowercase
- `From`, `To` — date range (from ISO, EU, named month, or relative keywords like "next week")
- `DateWasExplicit` — whether a date was found in the question
- `PersonHint` — extracted person name (regex patterns)
- `LocationHint` — extracted location name
- `TeamLeadHint` — extracted team lead name
- `IsCountQuery` — true if question contains "how many", "wie viele", "count", "anzahl"
- `IsLowestQuery` — true if question contains "lowest", "worst day", "welcher tag", etc.

**Default date range** (when no date is mentioned): today through today+14 days.

### 12 Registered Domain Handlers

Registered in `Program.cs` DI container in this order:

| Handler | DomainKey | What it answers |
|---|---|---|
| WicLeaveHandler | not documented | Who is on WIC leave |
| SickLeaveHandler | not documented | Sick leave queries |
| VacationsHandler | not documented | Annual leave / vacation queries |
| ALBalanceHandler | not documented | AL balance queries |
| DashboardHandler | not documented | Today's dashboard summary |
| WicForecastHandler | not documented | WIC coverage forecast |
| WicCoverageDetailHandler | not documented | WIC coverage detail per location |
| PipelineHandler | not documented | Pipeline events |
| TrainingHandler | not documented | Training sessions |
| EmployeesHandler | not documented | Employee queries |
| WicOpeningHoursHandler | not documented | WIC opening hours |
| AgentAvailabilityHandler | not documented | Agent availability |

Handler implementation files are in `Backend/Assistant/` — they were not read during this blueprint exercise. The `Score(normalizedQ)` method in each handler does keyword matching on the normalised query. Each handler's `DomainKey` and `DomainLabel` values are used in the disambiguation message and the `/api/assistant/score` debug endpoint.

---

## 4.9 WicConflictDetector — Conflict Classification

`WicConflictDetector.GetConflictsAsync(from, to, includeSplitShifts)` finds agents with `IsOnSite=1` entries resolving to two or more distinct `LocationCode` values on the same date.

### Three conflict types

| Type | Condition | Default visible | Meaning |
|------|-----------|----------------|---------|
| `OVERLAP` | ≥2 open entries whose opening hours overlap (`s1 < e2 AND s2 < e1`) | Yes | Real conflict — agent cannot be at two places simultaneously |
| `SPLIT_SHIFT` | ≥2 open entries with no pairwise overlap | No (requires `includeSplitShifts=true`) | Complementary schedule, e.g. Demmin Am Hanseufer (10:00-12:00) + Woldeforster (13:00-15:00) — intentional design |
| `CLOSED_LOCATION` | ≥1 entry whose location is closed on that day per `WicHoursResolver` | Yes | Agent scheduled on-site at a location that does not operate that day |

One agent-day can produce both `OVERLAP` and `CLOSED_LOCATION` records (open locations overlap + at least one closed location entry).

### Hours resolution

Uses `WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy, dow, date)` — same path as `ForecastService` line 62. DayOfWeek follows .NET convention (0=Sun…6=Sat).

### Reports only

`WicConflictDetector` never mutates `WicShiftEntries.IsOnSite`, `WicShiftEntries.SupportLocation`, or any coverage state. It reports what is in the database; resolving conflicts requires manual data correction.

**Endpoint:** `GET /api/wic/conflicts?from=&to=&includeSplitShifts=`

**Verified count (2026-06-01 to 2026-09-30, 2026-09-03):** 13 OVERLAP, 1 CLOSED_LOCATION (Angelika Weber — Wesel, closed Sunday), 14 SPLIT_SHIFT (all Demmin, hidden by default).

---

## 4.10 RetryOnFailure Configuration

The EF Core SQL Server provider is configured with `EnableRetryOnFailure(3)` (maximum 3 retries). This uses the built-in `SqlServerRetryingExecutionStrategy`. No explicit `CreateExecutionStrategy` / transaction wrapper pattern is used in the current codebase.

---

# Section 7 — API Reference

The full API is documented via Swagger at `http://localhost:5000/swagger` on the running server. The list below covers all known endpoints from reading service and endpoint mapper files.

---

## 7.1 Dashboard

Base path: `/api/dashboard`

| Method | Path | Query params | Response | Notes |
|---|---|---|---|---|
| GET | `/api/dashboard/summary` | `date` (yyyy-MM-dd, optional) | `DashboardSummaryDto` | Agent counts by role and shift type for the date; WicUnoccupiedCount |
| GET | `/api/dashboard/teamlead-summary` | `date` (optional) | `TeamLeadSummaryDto[]` | Per team-lead breakdown: working, AL, SL, training, WIC assigned |
| GET | `/api/dashboard/wic-cards` | `date` (optional) | `WicCardDto[]` (simple version) | Per-WIC location agent list; status = OCCUPIED or UNOCCUPIED |

---

## 7.2 WIC Shifts

Base path: `/api/wic`

| Method | Path | Query/Body | Response | Notes |
|---|---|---|---|---|
| GET | `/api/wic/` | `from`, `to`, `location`, `employeeId`, `teamLead` (all optional) | `WicShiftDto[]` | All WIC shift entries in range |
| PATCH | `/api/wic/{id}` | Body: `{ task?, supportLocation?, isOnSite? }` | `WicShiftDto` | Edit a single WicShiftEntry |
| GET | `/api/wic/locations` | — | location list with openingSchedule string | All active WicLocations |
| GET | `/api/wic/coverage` | `date` (optional) | `WicCoverageDto[]` | Per-location coverage with agent list |
| GET | `/api/wic/open` | `date`, `horizon` (default 3) | `WicOpenDayDto[]` | Per-day, per-location open/closed + coverage status for the horizon |
| GET | `/api/wic/availableHours` | `from`, `to`, `teamLead` | `AvailableHoursDto[]` | Agents with free hours after WIC close |
| GET | `/api/wic/availableHours/download` | same as above | CSV file | Download available hours as CSV |
| GET | `/api/wic/agent/{employeeId}` | `from`, `to` | `WicShiftDto[]` | WIC shifts for a single agent |
| GET | `/api/wic/download` | `from`, `to` | .xlsx file | Download WIC shifts as Excel |
| POST | `/api/wic/shifts` | Body: `{ employeeId, date, shiftType, shiftStart?, shiftEnd?, agentTask?, locationCode? }` | `{ success, employeeName, shiftDate, shiftType }` | Create or update a ShiftEntry; if ShiftType=WIC_DUTY also upserts WicShiftEntry |
| POST | `/api/wic/assignments` | Body: `{ employeeId, locationCode, date, shiftStart?, shiftEnd? }` | `{ success, skipped?, reason?, nppWarning? }` | Full WIC assignment: creates/updates ShiftEntry + WicShiftEntry; checks NPP qualification, time conflicts, and blocking shift types |

### `POST /api/wic/assignments` — which rows count as a time conflict

`WicShiftService.CreateAssignmentAsync` blocks the new assignment on overlapping hours
only against the agent's existing `WicShiftEntries` rows for that date where
**`IsOnSite == true`**. `IsOnSite == false` rows for the same agent+date are never
compared, because they don't represent a live commitment:

- **Move-to-GSD history** (`POST /api/wic/move-to-gsd`) sets `IsOnSite=0`,
  `IsGSDDay=1` on the agent's prior on-site row and keeps it as a record — see
  Q1 in `technical.md`.
- **Manual reassignment to Voice/Backlog** (`PATCH /api/wic/{id}` with
  `task="Voice"` or `"Backlog"`) sets `IsOnSite=0` because the agent is doing
  other GSD work that day, not because they're unavailable.
- **Excel-import `SupportLocation='Global Service Desk'` rows** are written with
  `IsOnSite=0` for days an imported WIC agent worked the GSD office instead of a
  WIC site — harmless history, per `technical.md` "Known Data Patterns".

Absences (AL/SL/PH/etc.) are never represented by an `IsOnSite=false`
`WicShiftEntries` row in the first place — they are blocked earlier in the same
handler by a separate check against `ShiftEntries.ShiftType` (the
"blocking shift types" set: `AL, HALF_AL, SL, UL, PH, LPH, OFF, OFF_WEEKEND, OL,
RESIGNED`), before the overlap loop runs at all.

---

## OL (Other Leave) date-range entry

`Vacations.tsx` → "Add Vacation" modal has an AL/OL toggle that reuses the same
From/To range picker (`VacationService.CreateAsync`, `POST /api/vacations` with
`leaveType: "OL"`). Unlike AL:
- No `Vacations` row is created and AL balance is untouched — OL is purely a
  `ShiftEntries.ShiftType = "OL"` marker, one row per working day (Mon–Fri) in
  the range (`ShiftSyncService.SyncVacationAsync(..., "OL", "OtherLeave")`).
- Past `firstDay` values are allowed (no "future only" restriction), so OL can
  be logged retroactively.
- `Shifts.tsx` treats `OL` as an absence type (grayed-out row) the same as
  `AL`/`HALF_AL`/`SL`/`UL`.

---

## 7.3 WIC Cards

| Method | Path | Query | Response |
|---|---|---|---|
| GET | `/api/wic/cards` | `date` (optional), `country` (optional, "DE"/"NL"/"ALL") | `WicCardDto2[]` |

`WicCardDto2` includes: location metadata, `TodaySchedule` (open/close times, total minutes), `AssignedAgentDto[]` with per-agent coverage match (FULL/PARTIAL/NONE), main and backup agent name lists, overall CoverageStatus and CoveragePercent.

---

## 7.4 WIC Forecast

| Method | Path | Query | Response |
|---|---|---|---|
| GET | `/api/wic/forecast` | `horizon` (default 28, max 62), `locationCode` (optional), `startDate` (optional) | `ForecastResponse` |

`ForecastResponse`: `{ generatedAt, horizon, locationCount, totalAtRiskDays, locations[] }` — each location has `forecast[]` array with per-day status and atRiskDays count.

---

## 7.5 WIC Substitutes

| Method | Path | Query | Response |
|---|---|---|---|
| GET | `/api/wic/substitutes` | `locationCode` (required), `date` (optional), `horizon` (1-7, default 1), `absentIds` (comma-separated, optional) | `SubstitutionResponse` |

`SubstitutionResponse`: `{ locationCode, displayName, horizon, days[], knownNulls[] }`. Each day has `candidates[]` with scoring, distance, source type, upcoming absences, and `donorImpact` for WIC_DONOR candidates. `knownNulls` lists always-null fields (contactEmail, travelMinutes, hasDirectTrain).

Returns 404 if locationCode not found or inactive.

---

## 7.6 Sick Leave

Base path: `/api/sickleave`

| Method | Path | Query/Body | Response |
|---|---|---|---|
| GET | `/api/sickleave` | `from`, `to`, `teamLead`, `type`, `activeOnly` | `SickLeaveDto[]` |
| GET | `/api/sickleave/stats` | `teamLead` (optional) | `SickLeaveStatsDto` |
| POST | `/api/sickleave` | `{ employeeId?, firstName?, lastName?, startDate, endDate, type?, childName?, notes? }` | `CreateSickLeaveResult` |
| PATCH | `/api/sickleave/{id}` | `{ startDate?, endDate?, type?, notes? }` | `PatchSickLeaveResult` |
| DELETE | `/api/sickleave/{id}` | — | `{ deleted: true }` |
| GET | `/api/sickleave/download` | `from`, `to` | .xlsx file |

On create: writes SL ShiftEntries via ShiftSyncService. On delete: reverts ShiftEntries.
On patch with a new endDate: rewrites the ShiftEntry range.

---

## 7.7 Vacations

Base path: `/api/vacations`

| Method | Path | Query/Body | Response |
|---|---|---|---|
| GET | `/api/vacations/` | `from`, `to`, `year`, `sheet`, `employeeId` | `VacationDto[]` |
| GET | `/api/vacations/active` | `date` (optional) | `VacationDto[]` |
| GET | `/api/vacations/upcoming` | `days` (default 7) | `VacationDto[]` |
| GET | `/api/vacations/availability` | `from`, `to`, `maxLeave` (default 8) | `DailyLeaveCountDto[]` |
| POST | `/api/vacations/` | `{ employeeId, firstDay, lastDay, comments?, isHalfDay? }` | `VacationDto` or 400 |
| DELETE | `/api/vacations/{id}` | — | `{ deleted: true }` |
| GET | `/api/vacations/download` | `from`, `to` | .xlsx file |

Post rejects if: invalid dates, lastDay < firstDay, employee not found, exceeds `ScheduleLimits.MaxFutureDays`, or overlaps a SickLeave for the same employee.

---

## 7.8 AL Balance

Base path: `/api/albalance`

| Method | Path | Params | Response |
|---|---|---|---|
| GET | `/api/albalance/` | `year` (optional, default = current year) | `ALBalanceDto[]` |
| GET | `/api/albalance/{employeeId}` | `year` (optional, default = current year) | `ALBalanceDto` or 404 |

Manual balance adjustment uses `PATCH /api/employees/{employeeId}/albalance` (taken days) and
`PATCH /api/employees/{employeeId}/albalance/eligible` (eligible days, added 2026 — see Employees).
`ALBalance` rows are now scoped per employee **and** per calendar year (`ALBalance.Year`, unique
constraint `(EmployeeId, Year)`); both endpoints operate on the current year's row.

---

## 7.9 Employees

Base path: `/api/employees`

| Method | Path | Query/Body | Response |
|---|---|---|---|
| GET | `/api/employees/` | `active` (bool, optional) | `Employee[]` |
| POST | `/api/employees/` | Employee body | `Employee` |
| PUT | `/api/employees/{id}` | Employee body | `Employee` |
| DELETE | `/api/employees/{id}` | — | `{ deleted: true }` |
| PATCH | `/api/employees/{employeeId}/albalance` | `{ alUsed: int }` | updated balance |
| PATCH | `/api/employees/{employeeId}/albalance/eligible` | `{ eligibleDays: decimal }` | updated balance, or 400 if `< 0` / more than 2 decimals |

---

## 7.10 Attendance

Base path: `/api/attendance`

| Method | Path | Query | Response |
|---|---|---|---|
| GET | `/api/attendance` | `from`, `to`, `country` | `DailyAttendance[]` |
| GET | `/api/attendance/download/today` | — | .xlsx file |
| GET | `/api/attendance/download/7d` | — | .xlsx file |
| GET | `/api/attendance/download/30d` | — | .xlsx file |

---

## 7.11 Public Holidays

| Method | Path | Response |
|---|---|---|
| GET | `/api/publicholidays` | `PublicHoliday[]` |
| POST | `/api/publicholidays` | Created `PublicHoliday` |
| DELETE | `/api/publicholidays/{id}` | `{ deleted: true }` |

---

## 7.12 Pipeline

Base path: `/api/pipeline`

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/pipeline` | — | `WicPipelineItem[]` |
| POST | `/api/pipeline` | `WicPipelineItem` body | Created item |
| PUT | `/api/pipeline/{id}` | Updated body | Updated item |
| DELETE | `/api/pipeline/{id}` | — | `{ deleted: true }` |

---

## 7.13 WIC Schedule

Base path: `/api/wic-schedule`

| Method | Path | Query | Response |
|---|---|---|---|
| GET | `/api/wic-schedule` | `from`, `to`, `employeeId` | WIC schedule rows |

---

## 7.14 Training

Base path: `/api/training`

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/training/topics` | — | `TrainingTopic[]` |
| POST | `/api/training/topics` | `TrainingTopic` body | Created topic |
| GET | `/api/training/sessions` | — | `TrainingSession[]` |
| POST | `/api/training/sessions` | `TrainingSession` body | Created session |
| PATCH | `/api/training/sessions/{id}` | status update | Updated session |
| DELETE | `/api/training/sessions/{id}` | — | `{ deleted: true }` |

---

## 7.15 AL Calendar

Base path: `/api/alcalendar`

| Method | Path | Query | Response |
|---|---|---|---|
| GET | `/api/alcalendar` | `from`, `to`, `teamLead` | AL calendar rows |

---

## 7.16 VWIC

Base path: `/api/vwic`

| Method | Path | Query | Response |
|---|---|---|---|
| GET | `/api/vwic/daily` | `date` | `VwicDailyResponse` |
| GET | `/api/vwic/candidates` | `date` | `VwicCandidate[]` |
| GET | `/api/vwic/rotation` | `date` | rotation schedule |
| POST | `/api/vwic/rotation` | slot body | created slot |
| DELETE | `/api/vwic/rotation/{id}` | — | deleted |

---

## 7.17 Breaks

Base path: `/api/breaks`

| Method | Path | Query/Body | Response |
|---|---|---|---|
| GET | `/api/breaks` | `date` | `BreakSlotDto[]` |
| POST | `/api/breaks` | break slot body | Created `BreakSlotDto` |
| PATCH | `/api/breaks/{id}` | `{ status?, actualStart?, actualEnd? }` | Updated `BreakSlotDto` |
| DELETE | `/api/breaks/{id}` | — | `{ deleted: true }` |
| POST | `/api/breaks/distribute` | `{ date }` | `BreakDistributeResult` |

---

## 7.18 WIC Coverage

Base path: `/api/wic-coverage`

| Method | Path | Response |
|---|---|---|
| GET | `/api/wic-coverage` | Per-location agent tier data (`WicCoverageDto[]` with main/backupA/backupB/backupC tiers) |

---

## 7.19 BO List

Base path: `/api/bo-list`

| Method | Path | Query/Body | Response |
|---|---|---|---|
| GET | `/api/bo-list` | `date` | `BoEntry[]` |
| POST | `/api/bo-list` | `{ date, employeeName, shiftStart, shiftEnd, note? }` | Created `BoEntry` |
| PUT | `/api/bo-list/{id}` | Updated fields | Updated `BoEntry` |
| DELETE | `/api/bo-list/{id}` | — | `{ deleted: true }` |

---

## 7.20 Bulk RTM

Base path: `/api/bulk-rtm`

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/bulk-rtm` | Parsed batch body | Results per entry |

---

## 7.21 WIC Assistant (legacy)

Base path: `/api/wic-assistant`

The WicAssistant module is a narrower per-WIC assistant, separate from the full Pulse Assistant. Endpoints not documented here.

---

## 7.22 Pulse Assistant

Base path: `/api/assistant`

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/assistant/ask` | `{ question: string }` | `AssistantResponse` |
| POST | `/api/assistant/score` | `{ question: string }` | Array of `{ domain, label, score }` sorted by score desc |

`AssistantResponse`:
```json
{
  "answerText": "...",
  "dateRangeChecked": "2026-09-01 to 2026-09-15",
  "table": [{ "employee", "employeeId", "start", "end", "workDays", "wicLocation", "role" }],
  "error": null,
  "hint": "optional follow-up suggestion"
}
```

`table` and `hint` are omitted when null.

---

## 7.23 WIC Migration

| Method | Path | Response |
|---|---|---|
| GET | `/api/wic-migration/dry-run` | Preview of migration changes |
| POST | `/api/wic-migration/apply` | Apply migration |

---

## 7.24 Reachability

| Method | Path | Response |
|---|---|---|
| GET | `/api/reachability` | Reachability matrix rows |
| POST | `/api/reachability` | Create/update entry |

---

## 7.25 Substitute Accept

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/substitute-accept` | `{ locationCode, employeeId, date, sourceType }` | Records to SubstitutionHistory; also writes assignment |

---

## 7.26 Backup

Endpoints for managing the backup agent assignments. Not fully documented.

Base path: `/api/backup`

---

## 7.27 Briefing

| Method | Path | Query | Response |
|---|---|---|---|
| GET | `/api/wic/briefing` | `date` (optional) | Briefing with absences, gaps, next at-risk days |

---

## 7.28 What-If

| Method | Path | Query | Response |
|---|---|---|---|
| GET | `/api/wic/whatif` | `locationCode`, `date`, `absentIds` (comma-separated) | What-If coverage result |

---

## 7.29 AL Planning

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/al-planning` | `employeeId`, `from`, `to` | AL planning data |
| POST | `/api/al-planning` | planning body | Created plan |

---

## 7.30 Health + Admin

| Method | Path | Response |
|---|---|---|
| GET | `/health` | `{ status: "ok", timestamp: "..." }` |
| POST | `/api/admin/cleanup-wic-orphans` | `{ deleted: N, message: "..." }` — deletes ghost WicShiftEntry rows |
| GET | `/swagger` | Swagger UI |
| GET | `/swagger/v1/swagger.json` | OpenAPI JSON spec |
