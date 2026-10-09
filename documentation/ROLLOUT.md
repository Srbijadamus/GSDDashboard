# Rollout days

**Status:** implemented in `C:\GSDDashboard_secure` (working tree); page `Rollout`, route
`/rollout`, immediately below `Pipeline` in the **Planning** menu group.

**Language rule:** every string on the page and every message the server returns for it is
English or German through i18n (`Frontend/src/i18n/locales/{en,de}/common.json`). The page
name is **Rollout** in both languages.

---

## 1. What a rollout day is

A **rollout day** is one WIC centre that is opened on **one date especially for new
workers**. It has its own working hours, usually different from the centre's standard
hours for that weekday; when RTM keeps the standard hours, that date simply has a
deliberately enlarged group of workers at that centre.

RTM enters it **by hand** on the `Rollout` page: centre, date, working hours (from-to) and
the new workers who work there that day.

**It is a primary, manual entry — exactly like the other hand-entered rows.** It is the
authority for that centre and date:

* the rollout hours apply **instead of** the weekday pattern for that centre and date in
  coverage, WIC cards, forecast, the WIC schedule, WIC Shifts, WIC Attendance and the
  conflict detector;
* the selected workers appear as **assigned to that centre at that time**;
* **no automatic process may change, move or delete a rollout day or anything created
  from it** (see §5).

For every date and centre **without** a rollout day, everything behaves exactly as before.

---

## 2. Where it is stored

Two **new, additive** tables. No existing table or column was changed to add them; the
startup migration in `Backend/Program.cs` only issues `IF NOT EXISTS ... CREATE TABLE` /
`CREATE INDEX` for these two names.

### `WicRolloutDays` — the rollout day (the source of truth)

| Column | Type | Meaning |
|---|---|---|
| `Id` | INT IDENTITY PK | |
| `LocationCode` | NVARCHAR(50) NOT NULL | the WIC centre |
| `LocationName` | NVARCHAR(200) NULL | centre display name at the time of entry |
| `RolloutDate` | DATE NOT NULL | the one date |
| `StartTime`, `EndTime` | NVARCHAR(5) NOT NULL | rollout working hours, `HH:MM` |
| `Note` | NVARCHAR(1000) NULL | free text by RTM |
| `IsManual` | BIT NOT NULL DEFAULT 1 | always true: a human entered this |
| `ManualChangeBy` | NVARCHAR(100) NULL | author: session KID, or `UI-MANUAL` |
| `ManualChangeAt` | DATETIME2 NULL | when it was entered or last changed |
| `ManualChangeNote` | NVARCHAR(500) NULL | provenance note written by the service |
| `HoursRowId` | INT NULL | the `WicOpeningHours` row this rollout projected |
| `CreatedAt` | DATETIME2 NOT NULL | |

Unique index `UQ_WicRolloutDays_LocDate` on `(LocationCode, RolloutDate)` — one rollout day
per centre and date, so "the hours for that centre and date" can never be ambiguous.
Index `IX_WicRollout_Date` on `(RolloutDate)`.

### `WicRolloutWorkers` — the workers on that day

| Column | Type | Meaning |
|---|---|---|
| `Id` | INT IDENTITY PK | |
| `RolloutDayId` | INT NOT NULL FK → `WicRolloutDays(Id)` ON DELETE CASCADE | |
| `EmployeeId` | NVARCHAR(20) NOT NULL | |
| `EmployeeName` | NVARCHAR(200) NULL | |
| `FromPipeline` | BIT NOT NULL DEFAULT 0 | true = offered by the Pipeline page for this centre/date |
| `PipelineNote` | NVARCHAR(500) NULL | RTM's note, used when the worker is not in Pipeline |
| `ShiftEntryId` | INT NULL | the `ShiftEntries` row the rollout created (null if the worker already had one) |
| `WicShiftEntryId` | INT NULL | the `WicShiftEntries` row the rollout created (null if one already existed) |

Unique index `UQ_WicRolloutWorkers_DayEmp` on `(RolloutDayId, EmployeeId)`.

The two `*Id` columns are what make "an edit or a delete changes **only** what came from
it" true: the service never touches a row it did not create.

---

## 3. How the entry reaches the existing planning surfaces

The rollout tables are the source of truth. Two things are **projected** from them, and
both are removed again when the rollout day is edited away or deleted.

### 3.1 Working hours → a `WicOpeningHours` exception row

`WicHoursResolver` is the single definition of "is this centre open on this date, and
when", and it already gives a row with `ExceptionDate == date` precedence over the weekday
pattern (that mechanism was added 2026-10-01 for manual exception days). A rollout day
therefore writes **one** `WicOpeningHours` row:

* `ExceptionDate` = the rollout date, `DayOfWeek` = that date's weekday,
* `OpenTime`/`CloseTime` = the rollout hours, `IsClosed` = false,
* `RawSchedule` = `Rollout HH:MM - HH:MM`, `ChangeNote` = `ROLLOUT` (the ownership marker),
* `ManualChangeBy`/`ManualChangeAt` = the author and time,
* `EffectiveFrom` = NULL (an exception row is never a pattern),
* `MinRequired` carried forward from the standard row.

Because every surface already resolves hours through `WicHoursResolver`, the rollout hours
are honoured by forecast, briefing, coverage gaps, WIC cards, the overview KPIs, WIC Shifts,
the WIC schedule and the conflict detector without duplicating the resolution rules.

`ChangeNote = 'ROLLOUT'` is the only row this feature will ever rewrite or delete. A
pre-existing manual exception day for the same centre and date is left untouched: while the
rollout exists the rollout row wins (the resolver takes the most recently changed exception
row), and after the rollout is deleted the older row is visible again.

Four date-specific lookups that used to ignore `ExceptionDate` were made exception-aware,
and two pattern views were made to ignore exception rows (they are one-date overrides, not
the weekly pattern):

| File | Change |
|---|---|
| `WicCardsService.GetCardsAsync` | the `DayOfWeek == dow` pre-filter now also keeps `ExceptionDate == date`, or the exception row would never reach the resolver |
| `WicScheduleService.GetOpeningHoursAsync` | weekly pattern view excludes `ExceptionDate != null` rows |
| `DashboardService` open-centre KPI | open = the weekday pattern **for this date's weekday**, or an exception for **exactly** this date |
| `Services/CoverageEvaluator` | prefers this date's exception row over the weekday row |
| `WicShiftService.CreateShiftAsync` | duty-time lookup prefers this date's exception row |
| `WicShiftService.GetAvailableHoursAsync` | available-hours lookup prefers this date's exception row |

### 3.2 Workers → the existing assignment rows

For each selected worker the rollout creates (or keeps in step) two rows, which is exactly
the pair the rest of the application already reads:

* **`WicShiftEntries`** — one row per employee, date and centre
  (`EmployeeId`, `ShiftDate`, `SupportLocation` = centre display name, `LocationCode`,
  `WorkingShift` = `HH:MM - HH:MM`, `WicOpeningHours`, `IsOnSite = 1`, `Task = 'WIC'`).
  This is what `WicPresenceResolver` signal 1, WIC Shifts, WIC Attendance, the WIC cards and
  `WicConflictDetector` read.
* **`ShiftEntries`** — the day-level row (`UQ_ShiftEntries_EmpDate` allows one per employee
  and date), created **only when the worker has none**: `ShiftType = WIC_DUTY`,
  `IsWicDuty = 1`, `AgentTask` = the centre name (20-char column, same truncation as
  everywhere else), `ShiftStart`/`ShiftEnd` = the rollout hours,
  `SourceSheet`/`SourceModule = 'ROLLOUT'`, `SourceId` = the rollout day id, plus the
  manual marker (`ManualChangeBy`/`ManualChangeAt`, `AutoGenerated = 0`). This is what makes
  the worker count as WIC cover at that centre.

Rules this follows:

* **A pre-existing row is never rewritten.** If the worker already holds a `WicShiftEntries`
  row for that centre and date, or already has a `ShiftEntries` row for that date, the
  rollout leaves it exactly as it is and records no ownership of it.
* **Existing agents stay.** A rollout only adds workers; it never moves or replaces anybody
  already assigned to that centre and day.
* **An overlapping assignment elsewhere is a warning, not a block.** The entry form (and the
  save response) report `OVERLAP` with the other centre and its window whenever
  `start1 < end2 && start2 < end1` — the same rule `WicConflictDetector` uses. Unknown times
  never count as a clash. Nothing is blocked.
* **A worker who is genuinely double-booked does not add coverage.** If the worker's
  day-level row belongs to somebody else and is not WIC duty, they appear as present at the
  rollout centre (signal 1) with contribution 0 — which is correct: they cannot be in two
  places — and the `OVERLAP` warning says why.

---

## 4. Pipeline: what is actually there

**Documentation/code mismatch worth knowing.** The requirement is that the new workers are
chosen from the `Pipeline` page. In this code base `Pipeline` is **WIC pipeline events**
(`WicPipeline`): upcoming special activities at a WIC centre that need extra agents. It has
**no employee foreign key** — its agents are free-text names in `PrimaryAgent` /
`BackupAgent` (`NVARCHAR(200)`). There is no new-hire or onboarding table anywhere in the
repository. On 2026-10-09 the live table held **3 rows, all with past dates** (2026-06-05,
2026-06-12, 2026-09-04), so `GET /api/pipeline` returns `[]` in its default window
(today → +30 days) and the page looks empty.

The Rollout page therefore works like this:

* `GET /api/rollout/candidates?locationCode=&date=` returns every active employee with a
  `fromPipeline` flag computed from the **distinct `PrimaryAgent`/`BackupAgent` names of the
  Pipeline events for that centre whose date range covers that date**, matched to an
  employee by name. Matched workers are shown first with a **Pipeline** badge.
* `pipelineNames` lists the Pipeline agent names that could not be matched to an employee,
  so RTM can see them.
* Everyone else is offered too, but the UI marks that choice as **"not in Pipeline"** and
  requires an explicit confirmation before the worker can be added; the flag is stored in
  `WicRolloutWorkers.FromPipeline` with an optional `PipelineNote`.

If a real new-hire source is introduced later, this is the single place to change.

---

## 5. Protection from automatic writers

| Writer | Why it leaves rollout rows alone |
|---|---|
| Roster generator (`RosterService`) | skips manual rows (`PreserveManualShift`, `IsProtectedFromOverwrite`) before replacing a row and before removing its `WicShiftEntries`; the coverage walk refuses to move a manual row |
| Sick-leave / vacation sync and revert (`ShiftSyncService`) | `PreserveManualShift` before every write and before the `WicShiftEntries` removal |
| Startup vacation reconciliation (`Program.cs`, runs before `app.Run()`) | `PreserveManualShift` per row |
| Startup `IsWicDuty` cleanup | raw SQL with `AND ManualChangeBy IS NULL` |
| Substitute accept (`SubstitutionModule`) | refuses the whole request with 409 when a day in range has a manual row |
| Closed-day duty cleanup (`WicScheduleService`) | removes manual days from the delete set before touching anything |
| Bulk import / bulk assign (`ShiftService`) | the bulk path deliberately passes no actor, so `PreserveManualShift` blocks it |
| Offline Python importers | repeat the `ManualChangeBy IS NULL` predicate in SQL |

`GSDContext.GuardManualShifts` is the backstop for every EF write in the application: it
throws on an update or delete of a row whose stored `ManualChangeBy` is non-blank. The
rollout rows go through it like any other manual row. The one deliberate exception is
`GSDContext.AllowRolloutOwnedDelete(row)`, which the Rollout service calls before removing a
`ShiftEntries` row **it created**; it refuses any row whose `SourceModule` is not `ROLLOUT`,
so no other code path can use it. `WicShiftEntries` has no manual-marker column, so the
rollout's on-site rows are protected indirectly: they are only ever removed together with
the rollout's own manual `ShiftEntries` row, by the rollout service itself.

**Only the Rollout feature itself changes rollout rows** — through the page (create, edit,
delete). Deleting a rollout day removes its hours projection, its `WicShiftEntries` rows and
its own `ShiftEntries` rows, and nothing else.

---

## 6. API

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/rollout?from=&to=&locationCode=` | list rollout days in a window, with the derived standard-hours comparison and the overlap warnings |
| GET | `/api/rollout/{id}` | one rollout day |
| GET | `/api/rollout/candidates?locationCode=&date=` | candidate workers, `fromPipeline` flag, unmatched Pipeline names |
| GET | `/api/rollout/standard-hours?locationCode=&date=` | the centre's weekday hours for that date (used to default the form) |
| POST | `/api/rollout/preview` | non-blocking overlap warnings for a draft entry; writes nothing |
| POST | `/api/rollout` | create (author = session KID or `UI-MANUAL`) |
| PUT | `/api/rollout/{id}` | edit |
| DELETE | `/api/rollout/{id}` | delete the day and only what it created |

The actor comes from the same `ResolveManualActor` contract as the other hand-entered rows
(`WicShiftService`): the signed-in user's KID, or the fixed `UI-MANUAL` tag while
`Auth:EnforceAuthorization` is still `false`. `Authorization` behaviour is unchanged.

---

## 7. Tests

`Backend.Tests/RolloutTests.cs` (in-memory, no live database) covers: create with author and
manual flag; the rollout hours winning for that centre and date while other dates keep the
pattern; rollout workers entering presence/coverage without displacing existing agents;
automatic writers being unable to overwrite a rollout row; an overlapping assignment
producing a non-blocking warning and a non-overlapping one producing none; pre-existing rows
never being rewritten; update touching only rollout-owned rows; delete removing only what the
rollout created; the Pipeline candidate flags; and validation.

```
dotnet test C:\GSDDashboard_secure\Backend.Tests\GSDDashboard.Tests.csproj -c Release
```
