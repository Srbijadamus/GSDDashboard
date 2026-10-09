# GSDDashboard — Agent Instructions

## Deploy / Verify

**Frontend changes are NOT done until built into `Backend/wwwroot` and verified
on the tunnel (https://d2jn94qg-5000.euw.devtunnels.ms/, port 5000) —
NEVER verify only on localhost:5173.**

Full rules and definition of done: **[DEPLOYMENT_AND_VERIFICATION.md](DEPLOYMENT_AND_VERIFICATION.md)**

Use the existing build/deploy script — do not invent a new pipeline:
```
C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1
```


## Deploy facts learned the hard way (2026-10-06)

- **The build source is the WORKING TREE, not git HEAD** (`C:\GSDDashboard_secure`).
  HEAD can be stale. Building `C:\GSDDashboard` gives a different binary — it is a
  separate repo (`main`) with its own, older sources.
- **The live DLL was built with .NET SDK 10.0.204.** It cannot be reproduced 1:1 with
  SDK 8. Check `dotnet --version` before claiming a rebuild matches the live binary.
- **`/health` returning 200 is NOT proof that a deploy works.** UI, proxied routes and
  the authenticated API must each be exercised.
- **`Invoke-PulsePromotion.ps1 -Mode Promote` fails its pinned-hash check**, so a deploy
  is done by hand: sentinel → stop → copy → verify all routes → remove sentinel →
  watchdog. `-Mode Rollback` does work.
- **Sentinel for deploy: `C:\HealthCheck\DEPLOY_IN_PROGRESS`.** While it exists the
  watchdogs must not start or restart the backend.
- **Never remove the sentinel until ALL routes pass, not just `/health`.**
- **`ExcludedEmployeeIds` in `Backend/Services/TestAccounts.cs` must never be empty.**
  EF Core does not translate `Contains` over an empty collection, so an empty list
  breaks the query at runtime. Keep a sentinel entry such as `"__no_test_account__"`.

## Rollout (added 2026-10-08)

A **Rollout day** is one WIC centre opened on ONE date especially for new workers, with its
own working hours. RTM enters it by hand on the `Rollout` page (Planning menu, immediately
below `Pipeline`). Full document: **[documentation/ROLLOUT.md](documentation/ROLLOUT.md)**.

- It is a **primary manual entry**. Automatic writers (roster generator, imports,
  substitutions, startup reconciliation, closed-day cleanup) must never change, move or
  delete a rollout day or anything created from it.
- Stored in two NEW additive tables, `WicRolloutDays` and `WicRolloutWorkers`. No existing
  table or column was changed; the startup migration only creates these two names.
- The hours are applied by projecting ONE `WicOpeningHours` row with `ExceptionDate` = the
  rollout date and `ChangeNote = 'ROLLOUT'` — the mechanism `WicHoursResolver` already
  prefers over the weekday pattern, so coverage, WIC cards, forecast, the WIC schedule, WIC
  Shifts and the conflict detector all honour it. `ChangeNote = 'ROLLOUT'` is the ownership
  marker: that is the only opening-hours row this feature may rewrite or delete.
- The workers become ordinary assignment rows (`WicShiftEntries` per employee/date/centre,
  plus a manual `ShiftEntries` WIC_DUTY row only when the worker has none for that date).
  A pre-existing row is **never** rewritten; a rollout only adds workers.
- A same-day overlapping assignment elsewhere is a **warning, never a block** (same
  `start1 < end2 && start2 < end1` rule the conflict detector uses).
- **Pipeline stores pipeline EVENTS with free-text agent names, not workers.** There is no
  new-hire table in this repository. The Rollout candidate list therefore flags employees
  whose name matches a Pipeline agent name for that centre and date (`fromPipeline`) and
  requires an explicit confirmation for anyone else. Do not assume a worker source exists.
- User-visible text is EN/DE through i18n, like everything else. The page name is
  "Rollout" in both languages.

## Language rule (permanent — applies to this and every future task)

**Every user-visible string in Pulse is English or German only, according to the selected
UI language (EN/DE), with no mixing in one view.** This covers menus, titles, buttons,
table columns, tabs, validation and error messages, tooltips, confirmation dialogs, CSV /
Excel export headers, empty states and loading text. Serbian, Croatian and Bosnian are
never allowed anywhere in the interface. Instructions may arrive in another language —
that is the language of communication with the requester, not of the application: always
translate the wording, never copy it into the UI. Established product names stay as they
are in both languages (WIC, VWIC, BO, Voice, GSD, RTM, KID, BO List).

Rules of implementation:

- New or changed strings go through i18n (`Frontend/src/i18n/locales/en/common.json` and
  `.../de/common.json`). Never hard-code user-visible text in a component.
- When adding a key, add it to **both** locale files in the same change, and make sure the
  EN view contains no German and the DE view contains no English (except the established
  names above).
- Server messages that the UI shows verbatim must be English or German (English is the
  current default); they must never be Serbian/Croatian/Bosnian.
- Export/CSV headers are user-visible: they must be a single language, not a `EN / DE`
  pair.
- The same rule applies to the ShiftKiosk dashboard (port 8000) and the kiosk program;
  its canonical copy of this rule lives in the ShiftKiosk documentation.
