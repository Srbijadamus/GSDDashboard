# WIC duty — BO / left-WIC rows are no longer WIC cover (2026-10-15)

Task: `/api/wic/cards` (signal 2) counted a `ShiftEntries` WORKING row with a WIC centre's
name in `AgentTask` as WIC duty. When RTM changed a cell `WIC_DUTY` → `WORKING`/BO the
centre name stayed in `AgentTask`, so the agent kept being listed as covering the centre
(9129428, Pfaffenhofen, 16.–27.11.2026).

Rule applied everywhere: **a row with `RawValue='BO'`, or a row that AROSE from a
`WIC_DUTY` → non-WIC transition, is never read as WIC duty** — and the transition clears
the stale centre name at the source.

---

## 1. Readers of signal 2 (`ShiftEntries.AgentTask` = a WIC centre) and how they were aligned

The reading is centralised, so the fix is in the shared definition + the shared pre-filter;
every surface below then inherits it. Nothing re-declares the rule.

| # | Surface / reader | File:line | What it does | Alignment |
|---|---|---|---|---|
| 1 | **Definition** — `AvailabilityResolver.IsWicDutyAt` | `Backend/Services/AvailabilityResolver.cs:132-146` (guard at `:139`) | The single "this row is WIC duty at this centre" test every reader uses | Added the `RawValue='BO'` guard before the centre match (new helper `IsBackOfficeRawValue`, `:122-134`) |
| 1b | `AvailabilityResolver.GetWicContributionAt` / `GetOnSiteWicContribution(…, loc)` | `Backend/Services/AvailabilityResolver.cs:152`, `:187` | Wrap `IsWicDutyAt` | Inherit automatically |
| 2 | **Signal-2 pre-filter** — `WicPresenceResolver.CandidateDutyRows` | `Backend/Services/WicPresenceResolver.cs:97-106` | SQL-side candidate set (drops task badges) | Added the BO drop; doc updated at `:68-90` |
| 3 | `WicPresenceResolver.Resolve` (signal 2 loop) | `Backend/Services/WicPresenceResolver.cs:149-157` | Calls `IsWicDutyAt` per centre/date | Inherits |
| 4 | **`/api/wic/cards`** — `WicCardsService.GetCardsAsync` | `Backend/WicCardsService.cs:124-125` (candidates), `:254-256` (resolve) | WIC Locations cards | Inherits |
| 5 | **`/api/wic/coverage-gaps`** — `WicCoverageGapService.GetGapsAsync` | `Backend/WicCoverageGapService.cs:279-287` (candidates), `:347-353` (resolve) | Coverage gaps / banner / Coverage Risk | SQL pre-filter now also drops `RawValue='BO'`; resolver inherits |
| 6 | `WicCoverageGapService.ComputeCoveredDates` + `GetStillGapCountAsync` | `Backend/WicCoverageGapService.cs:127-158`, `:491-500` | Roster-verification ground truth | Same resolver; SQL pre-filter updated |
| 7 | Gap-row label — `WicCoverageGapService.ComputeTaskLabel` | `Backend/WicCoverageGapService.cs:199-232` (BO first at `:213`) | "Where is this agent" label on gap rows | A BO row now labels **BO**, never `WIC <centre>` |
| 8 | **`/api/wic/forecast`** — `ForecastService.GetForecastAsync` | `Backend/Services/ForecastService.cs:98-99` (candidates), `:138-141` (resolve) | Coverage Risk strip / KPI | Inherits |
| 9 | **`/api/wic/briefing`** — `BriefingService.GetBriefingAsync` | `Backend/Services/BriefingService.cs:92-96` (candidates) | Gaps & Recommendations | Inherits |
| 10 | **Overview WIC detail** — `OverviewService.GetDetailAsync("WIC")` | `Backend/OverviewService.cs:214-220` | Overview "WIC" people list (`IsWicDuty \|\| WIC_DUTY`) | Checked: reads neither `RawValue` nor a centre name, so a stale name never entered it; the BO rule cannot make it drop a real WIC_DUTY row. **No code change needed.** |

**Checked and NOT signal-2 (no change possible or needed):**

| Surface | File:line | Why not |
|---|---|---|
| `/api/wic/open` | `Backend/WicShiftService.cs:223-328` (`GetOpenAsync`) | Reads signal 1 only (`WicShiftEntries.IsOnSite`) |
| `/api/wic/coverage` | `Backend/WicShiftService.cs:330-351` | Signal 1 only |
| `/api/wic` schedule / availableHours | `Backend/WicShiftService.cs:83-128`, `:353+` | `WicShiftEntries` only |
| Overview day summary | `Backend/OverviewService.cs:35-145` | Signal 1 only |
| Closed-day duties | `Backend/WicScheduleService.cs:189-250` | Only `ShiftType == WIC_DUTY` with a matching `AgentTask` (a BO row is WORKING, never selected) |
| Dashboard | `Backend/DashboardService.cs:105-174` | `WicShiftEntries` only |
| Startup integrity report | `Backend/Program.cs:617-655` | Report-only; a BO row that no longer reads as WIC duty is correctly not reported as a data defect |

**Rule 2 lives in the central hook** `GSDContext.LinkWicRowsToShiftTypeChanges`
(`Backend/GSDContext.cs:94-190`): on every `WIC_DUTY` → non-WIC transition it now clears
`AgentTask` when it names an active WIC centre, in the same `SaveChanges` as the stand-down
of the on-site `WicShiftEntries` row. The decision is a pure, unit-tested helper:
`WicAttendanceLink.ClearsWicCentreAgentTask` (`Backend/Services/WicAttendanceLink.cs:47-70`).
A task badge (`GSD`, `Voice`, …) is never cleared; a row with no transition is untouched.

---

## 2. Existing data fixed (requirement 3)

- **29 rows cleared** (`ShiftEntries.AgentTask = NULL`) — every `ShiftDate >= 2026-10-15`,
  `ShiftType='WORKING'` row whose `AgentTask` names an active WIC centre and which has **no
  on-site `WicShiftEntries` row** that day.
  - 9129428 Pfaffenhofen: **10 rows** (2026-11-16 … 2026-11-27, incl. 16.11)
  - 9124697 Helmstedt 9, 9135517 Neu-Isenburg 5, 9121951 Osnabrück/Münster 4, 9119463 Mülheim 1
- **42 leave rows** (AL/SL/RESIGNED) with a stale centre name were **listed and deliberately
  NOT touched**: they are not WORKING/BO rows and `SubstitutionModule` reads `AgentTask` as
  the substitute's duty (`Backup/untouched-leave-rows.csv`).
- **`ManualChangeBy` / `ManualChangeAt` / `ManualChangeNote` were never written** (verified
  row-by-row against the pre-image). No KID of the executor was stamped.
- Only `AgentTask` changed; `ShiftType`, times, `IsWicDuty`, `SourceModule` untouched.
- Remaining WORKING-with-centre-name rows from 2026-10-15 onward: **0**.

### Backup and rollback

```
C:\ProgramData\PulseRollback\bo-agenttask-20261015-164944\
    ShiftEntries-pre.csv          29 full rows, before the change
    cleared-rows.csv              the 29 rows with what was cleared
    untouched-leave-rows.csv      the 42 leave rows, listed only
    rollback.sql                  UNDO (not executed) — restores AgentTask, guarded by Id + cleared state
    SHA256SUMS.txt
    REPORT.txt
```

Tool: `Tools/BoAgentTaskFix20261015/Clear-BoAgentTask.ps1` (DryRun by default; `-Apply`
requires the expected count, writes the backup first, then one guarded transaction).

---

## 3. Deploy (per `documentation/LIVE_DEPLOY_RUNBOOK.md`)

| Step | Evidence |
|---|---|
| Precondition | live DLL `98AB98C526170B9B5DA2E91226208E375B89C47991AC1A7091E9C4D3480868E6` — asserted OK |
| Snapshot | `C:\ProgramData\PulseRollback\bo-agenttask-deploy-20261015-165222` — 221 files, every copied file hash-verified against the live originals + `backend.json` |
| Sentinel | `C:\HealthCheck\DEPLOY_IN_PROGRESS` created before the stop, removed only after 82/82 |
| Stop | port 5000 owner **23492** `GSDDashboard.API.exe` = the live Release exe; stopped |
| Build | working tree `C:\GSDDashboard_secure`, SDK **10.0.204**; `PS1_19_FinalBuildVerify.ps1 -Mode Build` incl. preservation assert (261 candidate inputs) |
| Copy | built binary + static into live Release and BOTH static roots; `appsettings*.json` preserved (hash unchanged) |
| **New DLL hash (live and candidate)** | `0D096412ACD10E5EC5AC3346575CBF1719DD7FAEB29203EAE9D34570DF120380` |
| Static | `index-DBHFkHxs.js` / `index-Ded7oazT.css` (unchanged content) |
| **82/82 — manual new build** | **82/82 x200 local + 82/82 x200 tunnel**, login 200/DEV (`.tmp-bo-agenttask-deploy/routes-final.json`) |
| **82/82 — watchdog-owned process** | **82/82 x200 local + 82/82 x200 tunnel** (`.tmp-bo-agenttask-deploy/routes-watchdog.json`) |
| Watchdog | sentinel removed → task `GSDDashboard-Backend` **Running**; port 5000 owner PID 9884, parent 10612 = `watchdog_gsd_backend.ps1`; `/health` 200 |
| KID used | DEV `S69307` only. **RTM KID `S60028` was never used** for any write or login |

---

## 4. Post-deploy acceptance (requirement 6)

`/api/wic/cards` for Pfaffenhofen on the BO days in November → **only 9135516** on all ten
days (16,17,18,19,20,23,24,25,26,27.11), `coverageStatus=COVERED`, 9129428 absent;
**Helmstedt 2026-10-16 still COVERED** (agent 9129429); `/health` **200**.
Receipt: `.tmp-bo-agenttask-deploy/wic-permission-check.json` — `PASS: true`.
Verified twice: on the manually started new build **and** on the watchdog-started process.

## 5. Tests

`Backend.Tests/BoAgentTaskWicTests.cs` — 16 new tests for rules 1 and 2 (BO never WIC duty in
the definition, the candidate pre-filter, the resolver and `/api/wic/cards`; the hook clearing
a stale centre name on WORKING/BO/OFF/AL, keeping a task badge, not touching a
non-transition, and keeping the name when moving INTO WIC_DUTY; the helper in isolation; the
BO label).

**Whole suite: 387 passed, 0 failed, 2 skipped** (`dotnet test`, Release, net8.0).

## 6. Files changed

```
Backend/Services/AvailabilityResolver.cs      IsBackOfficeRawValue + BO guard in IsWicDutyAt
Backend/Services/WicPresenceResolver.cs       CandidateDutyRows drops BO rows
Backend/Services/WicAttendanceLink.cs         ClearsWicCentreAgentTask helper
Backend/GSDContext.cs                         hook clears the stale centre name on the transition
Backend/WicCoverageGapService.cs              SQL pre-filter drops BO; ComputeTaskLabel labels BO
Backend.Tests/BoAgentTaskWicTests.cs          new tests
Tools/BoAgentTaskFix20261015/                 data-fix tool + deploy scripts + this report
Tools/pulse-preservation-manifest.json        candidate half regenerated for the intentional change
```
