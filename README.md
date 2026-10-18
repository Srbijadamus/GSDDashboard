# WorkForce Pulse (GSDDashboard)

Internal workforce management dashboard for the **GSD DE (Global Service Desk
Germany)** and **WIC (Workforce In Contact)** teams: shift schedules, WIC
location coverage, attendance, sick leave, vacations, AL balances, Excel export
and an AI-assisted substitution engine.

## Stack

- Backend: ASP.NET Core 8 Minimal API (`Backend/`)
- Frontend: React 19 + Vite + TypeScript (`Frontend/`)
- Database: SQL Server Express (`localhost\SQLEXPRESS`)

## Run

```powershell
# Backend — http://localhost:5000 (swagger at /swagger, DEV role only)
cd Backend
dotnet run

# Frontend (dev only) — http://localhost:5173
cd Frontend
npm install
npm run dev
```

## Build & test

```powershell
# Backend tests (xUnit, in-memory; live acceptance tests skip without env vars)
dotnet test Backend.Tests

# Frontend production build (writes into Backend/wwwroot)
cd Frontend
npm run build

# Frontend unit tests (each script is standalone)
npm run test:attendance
npm run test:bulk-rtm
# ... and the other test:* scripts in package.json
```

## Deploy

Frontend changes are **not done** until built into `Backend/wwwroot` and verified
on the tunnel (port 5000), never only on localhost:5173. Canonical build+verify
script: `C:\GSDDashboard_secure\PS1_19_FinalBuildVerify.ps1`. Full definition of
done: [DEPLOYMENT_AND_VERIFICATION.md](DEPLOYMENT_AND_VERIFICATION.md).

## Documentation

- [documentation/README.md](documentation/README.md) — overview, tunnel URLs, task scheduler
- [DEPLOYMENT_AND_VERIFICATION.md](DEPLOYMENT_AND_VERIFICATION.md) — definition of done, deploy rules
- [documentation/KIOSK_HARDENING.md](documentation/KIOSK_HARDENING.md) — actual kiosk hardening state
- [documentation/auth_rbac.md](documentation/auth_rbac.md) — authentication & RBAC
- [documentation/ROLLOUT.md](documentation/ROLLOUT.md), [documentation/BULK_RTM.md](documentation/BULK_RTM.md) — feature references
- [documentation/PROJECT_DOCUMENTATION_INDEX.md](documentation/PROJECT_DOCUMENTATION_INDEX.md) — full index
