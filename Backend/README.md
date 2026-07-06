# EON GSD Management Dashboard

Internal workforce management dashboard for the GSD DE and WIC teams (~122 employees). Supports read, write, and download operations.

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
- Creates 8 core tables (Employees, ShiftEntries, WicShiftEntries, WicLocations, DailyAttendance, SickLeaves, Vacations, ALBalance)
- Seeds all 40 WIC locations (38 DE + 2 NL)

The application creates 3 additional tables at startup: `BreakSlots`, `VwicRotationSlots`, `AgentReachableCities`.  
The remaining entity tables (WicAgentAssignments, WicOpeningHours, WicPipeline, PublicHolidays, TrainingTopics, TrainingSchedule, LeaveQuotas, SubstitutionHistory) must exist in the live DB — create them via separate SQL scripts before first use.

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

Full documentation at `/swagger`. Endpoints support GET, POST, PATCH, and DELETE operations.

### Dashboard
```
GET /api/dashboard/summary?date=
GET /api/dashboard/teamlead-summary?date=
GET /api/dashboard/wic-cards?date=
```

### Shifts
```
GET    /api/shifts?from=&to=&teamLead=&role=&engagement=&shiftType=
GET    /api/shifts/working-today?date=
GET    /api/shifts/download?from=&to=       → .xlsx
POST   /api/shifts                          create shift entry
PATCH  /api/shifts/{id}                     update shift entry
DELETE /api/shifts/{id}                     delete shift entry
GET    /api/shifts/reorder                  shift reorder helpers
```

### WIC Shifts
```
GET  /api/wic?from=&to=&location=&teamLead=
GET  /api/wic/locations
GET  /api/wic/open?date=
GET  /api/wic/cards?date=
GET  /api/wic/coverage?date=
GET  /api/wic/forecast?from=&to=
GET  /api/wic/briefing?date=
GET  /api/wic/download?from=&to=            → .xlsx
POST /api/wic/substitutes/accept            accept substitute assignment
```

### WIC Coverage (agent/WIC master data)
```
GET    /api/wic-coverage/agents?search=
GET    /api/wic-coverage/agents/{kid}
PATCH  /api/wic-coverage/agents/{kid}
GET    /api/wic-coverage/wics?search=
GET    /api/wic-coverage/wics/{locationCode}
GET    /api/wic-coverage/wics/{locationCode}/reachable-agents
POST   /api/wic-coverage/wics/{locationCode}/backup-b
```

### V-WIC
```
GET  /api/vwic?date=
GET  /api/vwic/rotation?from=&to=
```

### Sick Leave
```
GET    /api/sickleave?from=&to=&teamLead=&type=&activeOnly=
GET    /api/sickleave/active?date=
GET    /api/sickleave/stats?from=&to=
GET    /api/sickleave/download?from=&to=    → .xlsx
POST   /api/sickleave                       create sick leave record
PATCH  /api/sickleave/{id}                  update sick leave record
DELETE /api/sickleave/{id}                  delete sick leave record
```

### Vacations
```
GET    /api/vacations?from=&to=&year=&sheet=&employeeId=
GET    /api/vacations/active?date=
GET    /api/vacations/upcoming?days=
GET    /api/vacations/download?from=&to=    → .xlsx
POST   /api/vacations
PATCH  /api/vacations/{id}
DELETE /api/vacations/{id}
```

### Employees
```
GET    /api/employees?role=&engagement=&teamLead=&category=&active=
GET    /api/employees/{id}
GET    /api/employees/{id}/timeline?from=&to=
GET    /api/employees/{id}/future-shifts
POST   /api/employees                       create employee
PATCH  /api/employees/{id}                  update employee (soft-delete via IsActive=false)
DELETE /api/employees/{id}                  soft-delete (sets IsActive=false)
PATCH  /api/employees/{id}/albalance        update AL balance
```

### AL Balance
```
GET /api/albalance
GET /api/albalance/{employeeId}
```

### AL Calendar
```
GET /api/alcalendar?from=&to=&teamLead=
```

### AL Planning
```
GET  /api/alplanning?year=&teamLead=
POST /api/alplanning
```

### Attendance
```
GET /api/attendance?from=&to=&country=&location=
```

### Overview
```
GET /api/overview?date=&teamLead=
GET /api/overview/{employeeId}?date=
```

### Breaks
```
GET    /api/breaks?date=&teamLead=
POST   /api/breaks
PATCH  /api/breaks/{id}
DELETE /api/breaks/{id}
```

### Public Holidays
```
GET    /api/publicholidays?year=&bundesland=
POST   /api/publicholidays
DELETE /api/publicholidays/{id}
```

### Pipeline
```
GET    /api/pipeline?locationCode=&date=
POST   /api/pipeline
PATCH  /api/pipeline/{id}
DELETE /api/pipeline/{id}
```

### WIC Schedule (opening hours)
```
GET    /api/wic-schedule?locationCode=
POST   /api/wic-schedule
PATCH  /api/wic-schedule/{id}
DELETE /api/wic-schedule/{id}
```

### Training
```
GET    /api/training/topics
POST   /api/training/topics
GET    /api/training/sessions?from=&to=
POST   /api/training/sessions
PATCH  /api/training/sessions/{id}
DELETE /api/training/sessions/{id}
```

### Substitution
```
GET  /api/substitution?locationCode=&date=
POST /api/substitution
```

### Backup
```
GET /api/backup?date=&locationCode=
```

### Reachability
```
GET /api/reachability?city=
```

### What-If
```
GET /api/whatif?date=&locationCode=&employeeId=
```

### Health
```
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

Data flows in both directions: the API supports full CRUD for shifts, sick leave, vacations, employees, training sessions, breaks, pipeline entries, and WIC assignments. Excel import is not supported (data is entered via API or direct DB). Excel export is available for shifts, sick leave, and vacations via `/download` endpoints.

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
