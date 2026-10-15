# WIC shift-plan sync — analysis (2026-10-15)

Read-only analysis of the defect "a ShiftEntries row carries a WIC DUTY but the day has no
on-site `WicShiftEntries` row", for the window **2026-10-15 … 2027-02-28**.

`Program.cs` is a console app that references the **production** rule classes
(`WicHoursResolver`, `WicWorkingShift`, `WicLocationMatcher`, `WicBackOfficePriority`,
`AvailabilityResolver`), so the analysis cannot drift from what the application reads. It
issues SELECTs only.

Scope of "WIC duty" (exactly what `AvailabilityResolver.IsWicDutyAt` reads):

* `ShiftType = WIC_DUTY` / `IsWicDuty = 1`;
* the flipped encoding — `ShiftType = WORKING` with a WIC centre name in `AgentTask`;
* a **plan** back-office day (`RawValue = 'BO'`, no `ManualChangeBy`) whose agent's priority
  centre is open (the live WIC-over-BO rule);
* a BO day RTM authored (`ManualChangeBy` set) is **excluded** — BO stands;
* absences (AL/SL/UL/OL/PH/LPH), HALF_AL, OFF, TRAINING are never WIC duty rows;
* a WIC duty row at a **closed** centre is reported separately and never written.

## Files

| File | Meaning |
|---|---|
| `missing-onsite-wic-rows-BEFORE.csv` | **The 22 rows before the repair** (the repair tool's plan, kept in `C:\ProgramData\PulseRollback\wicsync-20261015-203749\plan.csv`). Either no `WicShiftEntries` row existed for the centre, or one existed with `IsOnSite = 0`. |
| `missing-onsite-wic-rows-AFTER.csv` | The same check **after** the repair: empty (0 rows). |
| `analysis-AFTER.txt` | After the repair: 3185 WIC duty rows with an on-site row, **0 without**, 505 without a resolvable centre (not WIC duty at any centre — see below), 1 manual RTM BO day. |
| `analysis.txt`, `summary.json`, `missing-onsite-wic-rows.csv` | The live re-runnable outputs of `Program.cs` (post-repair state). |

`*_BEFORE` files were produced by the repair tool's plan because the analysis tool ran against
the live database before the repair and its outputs are re-derived on each run; the numbers
are reproduced in `../IZVESTAJ-WIC-SYNC-20261015.md` §1.

## The 505 "centre could not be resolved" rows

These are WIC-duty rows whose `AgentTask` is empty, so no centre can be matched. They are
**not** part of the defect: every one of them already has an on-site `WicShiftEntries` row
(verified 2026-10-15: 23 such rows had `wicOn >= 1`; the rest are plan BO rows with no open
assigned centre, which stay BO). Inventing a centre for them would create a new mismatch
instead of removing one, so neither the code path nor the repair touches them.

## Running it

```powershell
dotnet run -c Release --project C:\GSDDashboard_secure\Tools\WicSyncFix20261015\Analysis `
    -- "C:\GSDDashboard_secure\Tools\WicSyncFix20261015\Analysis"
```

It needs `localhost\SQLEXPRESS` / `GSDDashboard` (integrated auth) and writes only the three
files above.
