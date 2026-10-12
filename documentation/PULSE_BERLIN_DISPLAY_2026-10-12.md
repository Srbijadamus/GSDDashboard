# Pulse: every displayed time is Europe/Berlin (ZADATAK 2, Pulse half)

Date: 2026-10-12 · Repo: `C:\GSDDashboard_secure` (branch `security/kiosk-hardening`,
commit on top of `b7e4524`)
Status: **implemented, tested and built — NOT deployed.** Live Pulse was not touched.

The ShiftKiosk half of the same task is committed in `C:\ShiftKiosk_secure`
(`100236a`, `8fb007b`, `85ec140`) and its live deploy is recorded in
`ShiftKiosk_secure/documentation/TIMEZONE_FIX_2026-10-12.md`.

## 1. The rule and the single implementation

**The database keeps UTC; only what the user sees is Berlin.** No OS or SQL Server time
zone was read or changed, and no row, column or stored value is touched.

One helper does the conversion for the whole backend —
`Backend/Services/BerlinTime.cs`, resolving `Europe/Berlin` (IANA) with the Windows id
`W. Europe Standard Time` as fallback, never silently falling back to UTC. It is the
C# counterpart of the kiosk's single `CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME
ZONE 'W. Europe Standard Time' AS datetime2)` expression, so summer time is applied by
the platform instead of a hardcoded `AddHours(2)`.

A SQL Server `datetime2` comes back as `DateTimeKind.Unspecified`; every column this
helper formats is written with `DateTime.UtcNow`, so `Unspecified` is read as UTC (this
is asserted, see §4).

## 2. The defect, measured on live data (read-only)

```
ShiftEntries.ManualChangeAt (latest)   2026-10-12 08:31:28   <- stored UTC
Europe/Berlin wall clock at that time  2026-10-12 10:31:28
what the UI showed                     2026-10-12 08:31      <- two hours early
```

`WicCardsService` formatted the raw column for the "Manual" tooltip ("who and when"),
which is exactly the `ManualChangeAt "ko i kada"` surface named in the task.

## 3. Files changed

| File | Change |
|---|---|
| `Backend/Services/BerlinTime.cs` | **New.** `FromUtc`, `Stamp`, `Now`, `Today`, `StampNow`. |
| `Backend/WicCardsService.cs` | "Manual" tooltip `ManualChangeAt` (2 call sites) → Berlin `yyyy-MM-dd HH:mm`. |
| `Backend/RolloutService.cs` | Rollout provenance `ManualChangeAt` (created/updated args) → Berlin ISO. |
| `Backend/RosterService.cs` | `RosterBatchDto.CreatedAt` is now a Berlin `yyyy-MM-dd HH:mm` string. |
| `Frontend/src/pages/Roster.tsx` | Renders that string verbatim; `new Date(...).toLocaleString()` (the viewer's own zone) removed. |
| `Frontend/src/utils/berlinTime.ts` | **New.** Browser-side `berlinStamp()` for stamps produced in the browser. |
| `Frontend/src/pages/Shifts.tsx` | Printed shift plan footer "Generated:" → `berlinStamp()` instead of `new Date().toLocaleString()`. |
| `Backend/Services/ALPlanningService.cs` | `GeneratedAt` → Berlin (`StampNow("o")`) instead of raw `DateTime.UtcNow`. |
| `Backend/PipelineService.cs` | `CreatedAt` → Berlin ISO instead of the raw UTC column. |
| `Backend/ALBalanceService.cs`, `Backend/EmployeeService.cs`, `Backend/VacationService.cs`, `Backend/OtherModels.cs` | The "current year" default used `DateTime.UtcNow.Year`; it is now the **Berlin** year (`BerlinTime.Today.Year`). Between 00:00 and 01:00 Berlin on 1 January the UTC year is still the previous one. |

Default **date** filters in Pulse already come from `DateTime.Today` on a host whose OS
zone is `W. Europe Standard Time`, and the raw-SQL dates in the app are `DateOnly`; no
default-date filter was found that reads a UTC clock except the year defaults above.

Pulse has no Audit Log and no alarm-acknowledgement surface (checked: no `audit-log`
route, no `EMERGENCY`/ack column anywhere in `Backend`); those two named surfaces belong
to the ShiftKiosk half. Pulse's CSV/XLSX exports contain dates and `HH:mm` shift strings
only — no stored instant is exported, so nothing there needed converting.

## 4. Tests

| Command | Result |
|---|---|
| `cd Frontend && npm run test:berlin-time` | **5/5** (CEST +02, CET +01, the Berlin day boundary at 22:30 UTC, the March DST change, and that the output ignores the viewer's zone — the test process is pinned to `America/New_York`) |
| `dotnet test Backend.Tests --filter "BerlinTimeTests\|WicManualChangeTests"` | **17/17** |
| `dotnet test Backend.Tests` (full) | **205 passed, 5 failed, 1 skipped** — the 5 failures are all `RosterServiceTests`, caused by `RosterService` opening an explicit transaction that the EF **in-memory** provider does not support. They were re-run at clean `HEAD` (`b7e4524`) in a throwaway `git worktree`: **the same 5 fail**, so they are pre-existing and unrelated to this change. |

`WicManualChangeTests.ManualDuty_DoesNotRaiseTheCheckInNotRecordedWarning` now also
asserts the display value: stored `2026-10-01 07:05` (UTC) → `2026-10-01 09:05` (Berlin).

## 5. Builds (offline, no live side effect)

- `Frontend: npm run build` — `tsc -b` + vite OK, output written to `Backend/wwwroot`
  (the repo's configured `outDir`, git-ignored): `index-bW5d_LKg.js`.
- `Backend: dotnet build -c Release` — 0 warnings, 0 errors (SDK 10.0.204, the SDK the
  live DLL was built with).

## 6. Live state — deliberately unchanged

`C:\GSDDashboard` (what the watchdog runs) was not written to. Verified after the work:

```
C:\HealthCheck\DEPLOY_IN_PROGRESS     absent
GET http://localhost:5000/health      200 {"status":"ok"}
live GSDDashboard.API.dll sha256      49FE7B0F…9F4C8F   (== 2026-10-12 08:28 deploy)
live index-BKubz9Is.js sha256         33C8F89B…816CBF   (== 2026-10-12 08:28 deploy)
```

## 7. What remains before this reaches the live UI

The build/clone/5055/runbook sequence was **not** run for this change; the live site still
shows UTC for these surfaces. Remaining, in order:

1. Back up + clone `GSDDashboard` for the test instance, publish the change there and
   exercise the affected surfaces on **5055** (WIC Attendance "Manual" tooltip, Rollout
   provenance, Roster batch "Created", Shifts print footer, AL planning, Pipeline).
2. Deploy by the runbook: `DEPLOY_IN_PROGRESS` sentinel → stop → copy → verify **all**
   routes on localhost and the tunnel → remove sentinel → watchdog.
3. Re-verify the live values against the database (a fresh manual change must show the
   Berlin time of the action).

Rollback for a later deploy is a code/static rollback (`PS1_19_FinalBuildVerify.ps1 -Mode
Rollback`); this change stores nothing new, so no data rollback is involved.
