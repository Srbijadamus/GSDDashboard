# EON GSD Management Dashboard

Internal read-only workforce management dashboard for the GSD DE and WIC teams (~122 employees).

## Stack

| Layer | Technology |
|-------|-----------|
| Backend | ASP.NET Core 8 Minimal API |
| ORM | Entity Framework Core 8 |
| Database | SQL Server Express 2022 (local) |
| Excel export | ClosedXML 0.105.0 (MIT) |
| Frontend | React 19 + Vite + TypeScript |
| UI | shadcn/ui + Tailwind CSS |
| Charts | Recharts 2.x (MIT) |
| Data fetching | TanStack Query v5 |
| Tables | TanStack Table v8 |

> **Note:** EPPlus is NOT used — it requires a paid commercial license. ClosedXML (MIT) is used for all Excel exports.

---

## Prerequisites

- .NET 8 SDK
- Node.js 20+
- SQL Server Express 2022 running at `localhost\SQLEXPRESS`
- Windows Authentication enabled

---

## Step 1 — Create the database and schema

Open SQL Server Management Studio (SSMS) or Azure Data Studio and run:

```
backend/GSDDashboard.API/Data/schema.sql
```

This script:
- Creates the `GSDDashboard` database (if it doesn't exist)
- Creates all 8 tables with correct indexes
- Seeds all 40 WIC locations (38 DE + 2 NL)

Verify: `SELECT COUNT(*) FROM WicLocations` should return **40**.

---

## Step 2 — Backend setup

```bash
cd backend/GSDDashboard.API
dotnet restore
dotnet run
```

API starts at: `http://localhost:5000`  
Swagger UI at: `http://localhost:5000/swagger`

### Connection string

Located in `appsettings.json`:
```json
"Server=localhost\\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
```

---

## Step 3 — Frontend setup

```bash
cd frontend/gsd-dashboard
npm install
npm run dev
```

Dashboard opens at: `http://localhost:5173`

The Vite dev server proxies all `/api` requests to `http://localhost:5000`.

---

## API Endpoints

All endpoints are read-only (GET). Full documentation at `/swagger`.

### Dashboard
```
GET /api/dashboard/summary?date=2026-05-27
GET /api/dashboard/teamlead-summary?date=2026-05-27
GET /api/dashboard/wic-cards?date=2026-05-27
```

### Shifts
```
GET /api/shifts?from=&to=&teamLead=&role=&engagement=&shiftType=
GET /api/shifts/working-today?date=2026-05-27
GET /api/shifts/download?from=&to=   → .xlsx
```

### WIC
```
GET /api/wic?from=&to=&location=&teamLead=
GET /api/wic/locations
GET /api/wic/coverage?date=2026-05-27
GET /api/wic/download?from=&to=      → .xlsx
```

### Sick Leave
```
GET /api/sickleave?from=&to=&teamLead=&type=&activeOnly=
GET /api/sickleave/active?date=2026-05-27
GET /api/sickleave/stats?from=&to=
GET /api/sickleave/download?from=&to=  → .xlsx
```

### Vacations
```
GET /api/vacations?from=&to=&year=&sheet=&employeeId=
GET /api/vacations/active?date=2026-05-27
GET /api/vacations/upcoming?days=7
GET /api/vacations/download?from=&to=  → .xlsx
```

### Employees
```
GET /api/employees?role=&engagement=&teamLead=&category=&active=
GET /api/employees/{id}
GET /api/employees/{id}/timeline?from=&to=
```

### Other
```
GET /api/albalance
GET /api/albalance/{employeeId}
GET /api/attendance?from=&to=&country=&location=
GET /health
```

---

## Shift type reference

| Code | Meaning | Display color |
|------|---------|---------------|
| WORKING | Working shift | Green |
| WIC_DUTY | WIC on-site duty | Teal |
| AL | Annual leave | Blue |
| HALF_AL | Half-day annual leave | Blue |
| SL | Sick leave | Orange |
| UL | Unpaid leave | Red |
| OFF | Off day | Gray |
| OFF_WEEKEND | Weekend off | Gray |
| PH | Public holiday | Yellow |
| LPH | Local public holiday | Yellow |
| CD | Compensation day | White/outline |
| OL | Other leave | White/outline |
| CO | Compensatory off | White/outline |
| TRAINING | Training day | Purple |
| RESIGNED | Left the company | Dark gray |
| EMPTY | Not filled (future) | — |

### WIC parsing rules
- `"WIC   08:00 - 17:00"` has 3+ spaces between "WIC" and the time — always use `.Contains("WIC")`, never `.Equals("WIC")`
- `"HAL *"` and `"* HAL "` are both valid half-day AL variants → map to `HALF_AL`
- Team Lead names may have trailing `\n` in sick leave data — always `.Trim()` before comparing

---

## Data flow

```
SQL Server Express
       ↓  (EF Core queries)
ASP.NET Core 8 Minimal API  →  JSON
       ↓  (TanStack Query)
React 19 frontend
       ↓  (ClosedXML)
.xlsx downloads
```

**No data entry through the app. No Excel import.** Data flows outbound only (read + download reports).

---

## WIC Locations (40 total)

38 locations in Germany + 2 in the Netherlands.  
All pre-seeded in `WicLocations` table via `schema.sql`.

Special value: `"Global Service Desk"` in `SupportLocation` means the agent worked from the GSD office that day (not a physical WIC location).

---

## Build order (from document Section 10)

1. Run `schema.sql` → all tables + WicLocations seed
2. `dotnet run` backend → verify Swagger loads
3. Test `GET /api/health` → `{"status":"ok"}`
4. Test `GET /api/dashboard/summary?date=today` → verify JSON structure
5. `npm run dev` frontend → verify dashboard loads
6. Test each module tab: Shifts, WIC, Sick Leave, Vacations, AL Balance, Employees
7. Test Excel downloads (Today / 7 Days / 30 Days buttons)
