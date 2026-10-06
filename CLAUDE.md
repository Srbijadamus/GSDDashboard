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