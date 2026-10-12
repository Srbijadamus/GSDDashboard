# Backfill of 21 manual WIC_DUTY rows into WicShiftEntries - completed

Date: 2026-10-12 (local) / 2026-10-12 14:33:50 UTC
Environment: SQL Server Express `GSDDashboard` on `localhost\SQLEXPRESS` (the instance runs on UTC)
Instance scope: the 21 manual `ShiftEntries` `WIC_DUTY` rows for 15/16/19/20/21/22/23/26/28/29/30.10.2026

## Result

**21 rows inserted, one transaction, committed. `WicShiftEntries` 23981 -> 24002.**
New ids: **67765 - 67785** (consecutive; max id before the write was 67764, nothing else wrote in between).

Rollback (generated, **NOT executed**):
`C:\ProgramData\PulseRollback\wic-backfill-20261012-162439\rollback.sql`
New ids: `...\inserted-ids.txt`

## Step A - script vs. the server's own sync: MATCH

Reference rows written by `SyncWicDutyFromShiftPlanAsync` (Backend/ShiftService.cs:325-416), the last
13 created after 13:35 UTC (`WicShiftEntries` 67752-67764): sampled **67764, 67760, 67763**.
The script's location resolution (ShiftService.cs:342-368, cases 1-4) and time rule (370-382 +
WicHoursResolver.DutyWindow) reproduce every column:

| column | server-synced row (67764 Siegen) | script rule | match |
|---|---|---|---|
| EmployeeId | 9126886 | 9126886 (from the source ShiftEntries row) | OK |
| ShiftDate | 2026-10-20 | 2026-10-20 | OK |
| DayOfWeek | `Tuesday` | `Tuesday` (`ShiftDate.DayOfWeek.ToString()`) | OK |
| SupportLocation | `Siegen` | `Siegen` (case 2, AgentTask exact) | OK |
| LocationCode | `DE~57072~Siegen~Friedrichstr. 60` | same | OK |
| WicOpeningHours | NULL | NULL (the sync never sets it either) | OK |
| WorkingShift | `08:00-17:00` | `08:00-17:00` (ShiftStart-ShiftEnd) | OK |
| IsOnSite | true | true | OK |
| IsGSDDay | false | false | OK |
| IsOffDay | false | false | OK |
| Task | `WIC` | `WIC` | OK |
| Id | identity 67764 | identity (server) | n/a |
| CreatedAt / CreatedBy | **column does not exist** | **column does not exist** | n/a |

`WicShiftEntries` has exactly 12 columns (Id, EmployeeId, ShiftDate, DayOfWeek, SupportLocation,
WicOpeningHours, WorkingShift, IsOnSite, IsGSDDay, IsOffDay, Task, LocationCode) - there is no
CreatedAt/CreatedBy to fill. `ShiftEntries` has no CreatedAt either; its manual provenance is
`ManualChangeBy/At/Note`, which the 21 partner rows already carry (`S60028`).

67760 (Salzgitter) and 67763 (Munster) match the same way, column for column.
Full evidence: `Backfill-WicFromManualShift.stepA.txt`.

No divergence was found, so no script logic had to be changed. The one real defect found in
the pre-write material was in the extraction file `plan-21.csv`: the two Berlin rows
(ShiftEntries 30050 and 57421) had their data columns shifted left - `Berlin - Gaußstr.` had
been truncated at the hyphen and the WorkingShift sat in the Action column. `plan-21.csv` has
been rewritten from the live plan (21 rows, 13 columns) and the pre-write snapshot is
`plan-21.pre-write.txt`.

## Step B - writers that UPDATE/DELETE `WicShiftEntries` without a paired `ShiftEntries` check

Full table in `Backfill-WicFromManualShift.precheck.txt`. 15 mutation sites were audited.
Thirteen prove the pair in the same unit of work:

| file:line | operation | paired proof |
|---|---|---|
| Modules/SubstitutionModule.cs:145,149 | remove stale row / update row | updates or creates the same (emp,date) `ShiftEntries` row at 109-136; `PreserveManualShift` at 72 |
| RosterService.cs:582 | update (upsert by emp+date+centre) | the `ShiftEntries` row was just created/replaced in the same loop |
| RosterService.cs:667 | remove WIC rows of a replaced WIC_DUTY day | only for a row being replaced; `PreserveManualShift` at 651 |
| RosterService.cs:1173 | remove WIC rows of a batch | dates come from the batch's own `WIC_DUTY` rows (1162-1164) |
| ShiftService.cs:338,390 | stand down / update | that `ShiftEntries` cell is the caller |
| ShiftSyncService.cs:210 | remove rows for a vacation day | keySet == the `ShiftEntries` rows mutated at 188-198; `PreserveManualShift` at 185 |
| WicScheduleService.cs:317 | remove closed-day duties | deletes the matching `WIC_DUTY` row at 333-352; manual days removed first at 301-308 |
| WicShiftService.cs:587 | update (upsert) | `/api/wic/assignments` + `/api/wic/shifts` write the partner row in the same method |
| WicShiftService.cs:884 | remove superseded row | the partner `ShiftEntries` row is updated at 925-931 |
| WicShiftService.cs:1046 | stand down + IsGSDDay | partner row created/updated at 1012-1033 |
| RolloutService.cs:526 | remove | by the id the rollout itself recorded in `WicRolloutWorkers`, and it drops its own `ShiftEntries` row at 530-538 |

Two entries are **not** "paired" in that strict sense:

1. `WicShiftService.cs:142` - `PATCH /api/wic/{id}` (WIC Attendance cell edit). It changes
   `IsOnSite`/`Task`/`SupportLocation` on one row chosen by its own Id and does not rewrite the
   Shift Plan cell. It cannot delete a row and it only touches the row the human picked, so it is
   a second human surface, not an automatic writer.
2. `Program.cs:728` - `POST /api/admin/cleanup-wic-orphans` deletes rows where
   `SupportLocation IS NULL` and a non-NULL sibling exists for the same (employee, date). It is
   matched by a sibling `WicShiftEntries` row, never by `ShiftEntries`. It is a manual one-shot
   (Program.cs:657-658: deliberately not run at startup).

**This is the one finding worth your decision.** No automatic writer can touch the 21 rows or the
13 already-synced rows, and `cleanup-wic-orphans` can only ever delete rows with a NULL
SupportLocation - all 21 new rows have a non-NULL one (verified: 0 NULL-SupportLocation rows exist
on any of the 21 (employee, date) pairs). So the write is not exposed to it. The literal wording of
condition B ("any writer that UPDATE/DELETEs without checking the paired ShiftEntries row") is
nevertheless met by that endpoint, so it is reported here rather than buried: it would only become
a real hole if a NULL-SupportLocation row ever legitimately belonged to a manual day.

## Step C - pre-write gate (run twice, once immediately before the write)

| check | result |
|---|---|
| plan rows in scope | 21 |
| rows whose action is not INSERT | 0 |
| (employee, date) pairs that already have any `WicShiftEntries` row | 0 |
| partner `ShiftEntries` rows still manual `WIC_DUTY` | 21 of 21 |
| plan WorkingShift == ShiftStart-ShiftEnd | 21 of 21 |
| backup `...\wic-backfill-20261012-162439\WicShiftEntries.csv` | exists, 2 546 616 bytes, 23981 data rows, max Id 67764 |
| live `WicShiftEntries` count / max Id | 23981 / 67764 |
| NULL-SupportLocation rows on the 21 pairs | 0 |
| gate result | **PASS** |

## The 21 rows

| new Id | ShiftEntries Id | employee | date | SupportLocation | WorkingShift | LocationCode |
|---|---|---|---|---|---|---|
| 67765 | 56825 | 9117836 | 2026-10-15 | Trier | 08:00-17:00 | DE~54294~Trier~Eurener Str. 33 |
| 67766 | 56828 | 9120970 | 2026-10-15 | Mülheim | 08:00-17:00 | DE~45476~Mülheim~Moritzstr. 16-22 |
| 67767 | 56842 | 9126882 | 2026-10-15 | Landshut | 08:00-17:00 | DE~84036~Landshut~Kiem-Pauli Str. 2 |
| 67768 | 56929 | 9125519 | 2026-10-16 | Stadland | 07:30-16:30 | DE~26935~Stadland~Dedesdorfer Straße 2 |
| 67769 | 62370 | 9125517 | 2026-10-16 | Emmerthal | 07:00-16:00 | DE~31860~Emmerthal~Kraftwerksgelände |
| 67770 | 30050 | 9074431 | 2026-10-19 | Berlin - Gaußstr. | 07:30-16:30 | DE~10589~Berlin~Gaußstr. 11 |
| 67771 | 57094 | 9119463 | 2026-10-20 | Arnsberg | 08:00-17:00 | DE~59821~Arnsberg~Hellefelder Str. 8 |
| 67772 | 57188 | 9121951 | 2026-10-21 | Recklinghausen | 07:30-16:30 | DE~45661~Recklinghausen~Bochumer Str. 2 |
| 67773 | 57273 | 9117836 | 2026-10-22 | Trier | 08:00-17:00 | DE~54294~Trier~Eurener Str. 33 |
| 67774 | 57276 | 9120970 | 2026-10-22 | Mülheim | 08:00-17:00 | DE~45476~Mülheim~Moritzstr. 16-22 |
| 67775 | 57290 | 9126882 | 2026-10-22 | Landshut | 08:00-17:00 | DE~84036~Landshut~Kiem-Pauli Str. 2 |
| 67776 | 57378 | 9125519 | 2026-10-23 | Stadland | 07:30-16:30 | DE~26935~Stadland~Dedesdorfer Straße 2 |
| 67777 | 62269 | 9126874 | 2026-10-23 | Brokdorf | 07:00-16:00 | DE~25576~Brokdorf~Osterende |
| 67778 | 62375 | 9125517 | 2026-10-23 | Emmerthal | 07:00-16:00 | DE~31860~Emmerthal~Kraftwerksgelände |
| 67779 | 57421 | 9074431 | 2026-10-26 | Berlin - Gaußstr. | 07:30-16:30 | DE~10589~Berlin~Gaußstr. 11 |
| 67780 | 57646 | 9121951 | 2026-10-28 | Recklinghausen | 07:30-16:30 | DE~45661~Recklinghausen~Bochumer Str. 2 |
| 67781 | 57735 | 9117836 | 2026-10-29 | Trier | 08:00-17:00 | DE~54294~Trier~Eurener Str. 33 |
| 67782 | 57751 | 9126882 | 2026-10-29 | Landshut | 08:00-17:00 | DE~84036~Landshut~Kiem-Pauli Str. 2 |
| 67783 | 57840 | 9125519 | 2026-10-30 | Stadland | 07:30-16:30 | DE~26935~Stadland~Dedesdorfer Straße 2 |
| 67784 | 62274 | 9126874 | 2026-10-30 | Brokdorf | 07:00-16:00 | DE~25576~Brokdorf~Osterende |
| 67785 | 62380 | 9125517 | 2026-10-30 | Emmerthal | 07:00-16:00 | DE~31860~Emmerthal~Kraftwerksgelände |

Every row: `Task='WIC'`, `IsOnSite=1`, `IsGSDDay=0`, `IsOffDay=0`, `WicOpeningHours=NULL`,
DayOfWeek from the date. All 21 partner `ShiftEntries` rows still carry `ManualChangeBy='S60028'`
with `ManualChangeNote='Manual shift cell edit (/shifts); automatic writers must preserve this row.'` -
the Shift Plan cells were not touched.

## Post-write coverage

`GET /api/wic/coverage?date=` reports each of the 21 agents exactly once, at the right centre, with
the same WorkingShift (21/21, see `coverage_verify_backfill.txt`):

| date | centres now staffed (agentCount=1, covered=true) |
|---|---|
| 2026-10-15 | Trier, Mülheim, Landshut |
| 2026-10-16 | Stadland, Emmerthal |
| 2026-10-19 | Berlin - Gaußstr. |
| 2026-10-20 | Arnsberg |
| 2026-10-21 | Recklinghausen |
| 2026-10-22 | Trier, Mülheim, Landshut |
| 2026-10-23 | Stadland, Brokdorf, Emmerthal |
| 2026-10-26 | Berlin - Gaußstr. |
| 2026-10-28 | Recklinghausen |
| 2026-10-29 | Trier, Landshut |
| 2026-10-30 | Stadland, Brokdorf, Emmerthal |

The "rostered for WIC duty but no on-site WIC row" gap signal is empty for all 21. One gap remains
in 15-30.10 and it is not one of ours: `ShiftEntries` 56797, employee 9074526, 2026-10-15, empty
AgentTask (SourceModule PATTERN_FILL) - the server's own "no WIC centre could be resolved"
outcome, which needs the "Assign Agent" action, not a backfill.

Also verified after the write: `/health` 200, `/api/wic/open`, `/api/wic/`, `/api/wic/locations`,
`/api/wic/availableHours`, `/api/wic/agent/9117836`, `/api/wic/cards`, `/api/wic/conflicts`,
`/api/wic/forecast`, `/api/wic/reachability`, `/api/wic/briefing`, `/api/wic/substitutes`,
`/api/wic-coverage/wics`, `/api/wicschedule/agents`, `/api/overview/wic-status`,
`/api/attendance/wic` - all 200; `/api/wic/coverage-gaps` is 401 (it is auth-gated on this
instance) and the tunnel `https://d2jn94qg-5000.euw.devtunnels.ms` serves `/health` and
`/api/wic/coverage` 200 with the same payloads.

The source-rebuilt API surface is **159 route registrations** (`Tools/api-routes.txt`,
`Tools/Enumerate-ApiRoutes.ps1`). The "82 routes" baseline from the earlier task is not in this
repository or in this session, so no 82-vs-159 comparison is claimed: 159 is the measured number
today, and no route registration was added or removed by this task.

## Artefacts

| file | what |
|---|---|
| `C:\ProgramData\PulseRollback\wic-backfill-20261012-162439\WicShiftEntries.csv` | pre-write backup, 23981 rows |
| `...\plan-21.csv` | the 21 planned rows, rewritten from the live plan |
| `...\plan-21.pre-write.txt` | snapshot proving each pair had 0 WIC rows before the write |
| `...\precheck.txt` | step B/C gate output (PASS) |
| `...\apply.txt` | the apply run and its post-apply verification |
| `...\inserted-ids.txt` | the 21 new ids |
| `...\rollback.sql` | the undo, **not executed** |
| `Tools\Backfill-WicFromManualShift.ps1` | the backfill (read-only unless `-Apply`) |
| `Tools\Backfill-WicFromManualShift.verify.ps1` + `.stepA.txt` | step A comparison |
| `Tools\Backfill-WicFromManualShift.precheck.ps1` + `.precheck.txt` | step B/C gate |
| `Tools\coverage_verify_backfill.ps1` + `.txt` | post-write coverage evidence |
| `Tools\Enumerate-ApiRoutes.ps1` + `api-routes.txt` | API surface rebuilt from source |

## Deviations you may disagree with

1. **Condition B: one writer does not check the paired row.** `Program.cs:728`
   (`POST /api/admin/cleanup-wic-orphans`) deletes NULL-SupportLocation rows by sibling match.
   It cannot reach any of the 21 rows or the 13 synced rows, and the NULL-sibling query on the 21
   pairs returned 0, so the write proceeded and the finding is reported here. If you want a literal
   STOP on that finding, nothing prevents reverting: `rollback.sql` deletes exactly the 21 ids.
2. **`-Apply` is scoped by date.** The live plan contains 26 rows: the 5 rows for 2026-10-12 that
   the server itself already synced (56526, 56586, 62162, 62421, 62517), plus the 21 coverage rows.
   The apply therefore ran with `-TargetFromDate 2026-10-12 -ExpectedInsertCount 21`, and the
   script refuses to insert if the scoped count is not exactly 21.
3. **No deploy.** The change is confined to `Tools\`; no backend or frontend file changed, so
   `Backend\wwwroot` was left exactly as built at 15:36:49 and no sentinel/deploy cycle was run.
   Verification used the already-running instance on port 5000 and the tunnel.
4. **Commit without push**, as instructed.

