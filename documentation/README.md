# GSD Dashboard

Internal workforce management dashboard for the **GSD DE (Global Service Desk Germany)** and **WIC (Workforce In Contact)** teams. Provides real-time visibility into shift schedules, WIC location coverage, attendance, sick leave, vacations, annual leave balances — with Excel export and an AI-assisted substitution engine.

**Stack:** ASP.NET Core 8 · React 19 · TypeScript · SQL Server Express · TanStack Query · Tailwind CSS · IBM Plex fonts

---

## Live Tunnel URLs

Tunnels expire every 4 days and auto-restart via Task Scheduler.

| Service | URL |
|---------|-----|
| GSD Dashboard | https://d2jn94qg-5000.euw.devtunnels.ms |
| Kiosk | https://ssr7tm2l-8000.euw.devtunnels.ms |
| Kiosk Dashboard | https://ssr7tm2l-8000.euw.devtunnels.ms/dashboard |

---

## Task Scheduler (auto-starts on reboot)

| Task Name | Purpose |
|-----------|---------|
| `GSDDashboard-Backend` | Starts the ASP.NET Core backend on port 5000 |
| `GSDDashboard-Tunnel` | Opens the devtunnel for the dashboard |
| `ShiftKioskServer` | Starts the Python FastAPI kiosk server on port 8000 |
| `ShiftKioskTunnel` | Opens the devtunnel for the kiosk |
| `DevTunnel-AutoStart` | Ensures devtunnel CLI is authenticated and running |

---

## Prerequisites

| Requirement | Version | Notes |
|-------------|---------|-------|
| .NET SDK | 8.0+ | Backend runtime |
| Node.js | 20+ | Frontend build |
| SQL Server Express | 2022 | Must run at `localhost\SQLEXPRESS` |
| Windows Authentication | — | Required for SQL Server connection |
| Python | 3.11+ | Kiosk server only |

---

## Deploy (Build + Verify)

```powershell
powershell -ExecutionPolicy Bypass -File C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1
```

This script builds the React frontend, copies the output to `Backend/wwwroot/`, builds the backend, starts the server, and runs smoke tests on all key endpoints.

---

## Manual Start

### Backend

```powershell
cd C:\GSDDashboard\Backend
dotnet run --configuration Release
```

API available at http://localhost:5000. Swagger at http://localhost:5000/swagger.

### Frontend (dev mode only)

```powershell
cd C:\GSDDashboard\Frontend
npm install
npm run dev
```

Dev server at http://localhost:5173. In production the frontend is served from `Backend/wwwroot/` on port 5000.

### Kiosk Server

```powershell
cd C:\ShiftKiosk\server
python server.py
```

FastAPI kiosk server at http://localhost:8000.

---

## Configuration

### Backend (`Backend/appsettings.json`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173", "http://localhost:3000"]
  }
}
```

### Frontend (`Frontend/.env`)

```env
VITE_API_BASE_URL=https://8nh5k5g1-5000.euw.devtunnels.ms
```

Set this to the current tunnel URL when accessing via tunnel. Omit for local development (defaults to `http://localhost:5000`).

---

## Project Structure

```
C:\GSDDashboard\
├── Backend/
│   ├── Program.cs              # Entry point + all API route registrations (Minimal API)
│   ├── GSDContext.cs           # EF Core DbContext
│   ├── appsettings.json        # Connection string and CORS config
│   ├── schema.sql              # One-time DB init script
│   ├── Services/               # Domain service files
│   └── Models/                 # EF entities and DTOs
│
├── Frontend/
│   └── src/
│       ├── main.tsx            # React entry, providers, i18n
│       ├── App.tsx             # Router and shell layout
│       ├── api/client.ts       # All API calls in one place
│       ├── pages/              # Page components (one per route)
│       ├── components/         # Shared UI components
│       └── i18n/               # EN/DE translation files
│
├── documentation/              # This folder
└── PS1_19_FinalBuildVerify.ps1 # Primary build + deploy script

C:\ShiftKiosk\
└── server\
    └── server.py               # Python FastAPI kiosk server
```

---

## Troubleshooting

| Symptom | Check |
|---------|-------|
| API returns 500 | SQL Server running, Windows Auth enabled |
| Frontend shows no data | Backend on port 5000; check browser console for CORS errors |
| Excel download fails | Backend reachable; check network tab |
| Wrong language showing | Clear `localStorage` key `i18nextLng` and reload |
| Tunnel not accessible | Re-run `GSDDashboard-Tunnel` task in Task Scheduler |
