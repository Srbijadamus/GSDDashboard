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
