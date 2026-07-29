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
