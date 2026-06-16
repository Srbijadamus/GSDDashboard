# GSD Dashboard — Technical Documentation

## Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Backend runtime | ASP.NET Core (Minimal APIs) | 8.0 |
| ORM | Entity Framework Core | 8.0.0 |
| Database | SQL Server Express | 2022 |
| Excel generation | ClosedXML | 0.102.2 |
| API docs | Swagger / Swashbuckle | 6.5.0 |
| Frontend framework | React | 19.x |
| Build tool | Vite | 8.x |
| Language | TypeScript | ~6.0 |
| Routing | React Router | 7.x |
| Server state | TanStack Query | 5.x |
| Tables | TanStack Table | 8.x |
| Charts | Recharts | 3.x |
| UI / CSS | Tailwind CSS + shadcn/ui | 3.4 |
| Icons | Lucide React | latest |
| Internationalisation | i18next + react-i18next | 26.x |
| Theme | next-themes | 0.4.x |

> EPPlus is explicitly **not** used (commercial licence required). ClosedXML (MIT) handles all `.xlsx` generation.

---

## Repository Layout

```
GSDDashboard/
├── Backend/                        # ASP.NET Core 8 project
│   ├── GSDDashboard.API.csproj
│   ├── Program.cs                  # Application entry point, all route registrations
│   ├── GSDContext.cs               # EF Core DbContext (13 DbSets)
│   ├── appsettings.json            # Connection string, CORS origins
│   ├── appsettings.Development.json
│   ├── schema.sql                  # One-time DB init script (tables + seed data)
│   ├── *Service.cs  (×17)          # Business logic, one file per domain
│   └── Models/                     # EF entity classes and DTOs
│
└── Frontend/                       # React + TypeScript + Vite
    ├── package.json
    ├── vite.config.ts
    ├── tsconfig.json
    ├── .env                        # VITE_API_BASE_URL override
    └── src/
        ├── main.tsx                # Providers, i18n init, QueryClient
        ├── App.tsx                 # Router, sidebar, layout
        ├── index.css               # CSS variables (dark theme palette)
        ├── api/client.ts           # All API calls centralised here
        ├── i18n/                   # i18next config + EN/DE translation JSONs
        ├── pages/  (×18)           # One file per route
        └── components/             # Shared UI components
```

---

## Backend

### Program.cs — Entry Point

`Program.cs` is the single file that:
1. Registers services (EF Core, CORS, Swagger, all 17 service classes)
2. Configures the middleware pipeline
3. Defines every API route using Minimal API syntax (`app.MapGet`, `app.MapPost`, etc.)
4. Serves the React SPA from `/wwwroot` (production)

### GSDContext.cs — Database Context

EF Core `DbContext` with a `DbSet<T>` for each of the 13 database tables. Uses Windows Authentication (`Trusted_Connection=true`) to connect to `localhost\SQLEXPRESS`.

Configured with:
- `EnableRetryOnFailure(3)` for transient SQL errors
- `TrustServerCertificate=true` for local dev

### Services (17 files)

Each service is injected into `Program.cs` and handles one domain area:

| Service | Responsibility |
|---------|---------------|
| `DashboardService` | Top-level metrics: working counts by role, leave counts, WIC coverage summary |
| `ShiftService` | Shift plan queries with date/team-lead/role/engagement filters |
| `WicShiftService` | WIC-specific shift data; classifies on-site vs. GSD-office days |
| `WicCardsService` | Per-location coverage status (COVERED / PARTIAL / UNCOVERED / CLOSED) |
| `SickLeaveService` | Active and historical sick leave records |
| `VacationService` | Vacation requests, approvals, upcoming |
| `ALBalanceService` | Annual leave balance calculations |
| `ALCalendarService` | Calendar-view aggregation of AL across employees |
| `EmployeeService` | Employee master data, filtering, timeline history |
| `AttendanceService` | Daily WIC attendance tracking |
| `PublicHolidayService` | National and regional public holiday calendar |
| `TrainingService` | Training topics and sessions, coverage/impact ranking |
| `PipelineService` | WIC pipeline project tracking |
| `WicScheduleService` | WIC location opening hours |
| `OverviewService` | Aggregated cross-module overview data |
| `ShiftValidationService` | Labour-law compliance rules for shift edits |
| `ShiftSyncService` | Data synchronisation utilities |

### Database Schema (13 tables)

| Table | Key Columns |
|-------|-------------|
| `Employees` | Id, Name, Role, TeamLead, EngagementType |
| `ShiftEntries` | EmployeeId, Date, ShiftType, ShiftCode, StartTime, EndTime |
| `WicShiftEntries` | EmployeeId, Date, LocationCode, WorkingShift, OnSite flag |
| `WicLocations` | Code (PK), Name, Country, Coordinates, MinAgentsRequired |
| `WicAgentAssignments` | LocationCode, EmployeeId, Role (main/backup), DateRange |
| `WicOpeningHours` | LocationCode, DayOfWeek, OpenTime, CloseTime, IsClosed |
| `WicPipeline` | LocationCode, Title, StartDate, EndDate, AgentsNeeded, Status |
| `DailyAttendance` | LocationCode, Date, Status (assigned/WO/closed/PH) |
| `SickLeaves` | EmployeeId, StartDate, EndDate, Type, TeamLead |
| `Vacations` | EmployeeId, StartDate, EndDate, ApprovalStatus |
| `ALBalance` | EmployeeId, Year, EligibleDays, TakenDays, RemainingDays |
| `PublicHolidays` | Date, Name, Bundesland (regional scope) |
| `TrainingTopics` / `TrainingSessions` | Topic metadata and scheduled session assignments |

All date-range columns are indexed. Composite indexes exist on (EmployeeId, Date, SheetType) for shift lookups.

### Shift Type Reference

| Code | Meaning | UI Colour |
|------|---------|-----------|
| `WORKING` | Regular working shift | Green |
| `WIC_DUTY` | On-site WIC duty | Teal |
| `AL` | Annual leave | Blue |
| `HALF_AL` | Half-day annual leave | Light blue |
| `SL` | Sick leave | Orange |
| `UL` | Unpaid leave | Red |
| `OFF` | Off day | Grey |
| `OFF_WEEKEND` | Weekend | Dark grey |
| `PH` | Public holiday | Yellow |
| `LPH` | Local (regional) public holiday | Light yellow |
| `CD` | Compensation day | White outline |
| `OL` | Other leave | White outline |
| `CO` | Compensatory off | White outline |
| `TRAINING` | Training day | Purple |
| `RESIGNED` | Employee left | Dark grey |
| `EMPTY` | Not yet filled | — |

### Parsing Notes

A few non-obvious rules baked into the services:

- WIC shifts are identified with `.Contains("WIC")` not `.Equals()` because the raw data has variable spacing (e.g. `"WIC   08:00 - 17:00"`).
- Half-day AL has two formats: `"HAL *"` (prefix) and `"* HAL "` (suffix) — both map to `HALF_AL`.
- Team lead names in the DB may carry a trailing `\n`; services always call `.Trim()` before comparison.
- Shift start/end times are stored as `varchar` (e.g. `"08:00"`), not SQL `TIME`, for flexibility with non-standard entries.

### API Routes

All routes are defined in `Program.cs` under these groups:

```
GET  /api/dashboard/summary
GET  /api/dashboard/teamlead-summary
GET  /api/dashboard/wic-cards

GET  /api/shifts
GET  /api/shifts/working-today
GET  /api/shifts/download
GET  /api/shifts/download/7days
GET  /api/shifts/download/30days

GET  /api/wic/locations
GET  /api/wic/shifts
GET  /api/wic/coverage
GET  /api/wic/cards
GET  /api/wic/download  (+ 7days / 30days variants)

GET  /api/sickleave
GET  /api/sickleave/active
GET  /api/sickleave/stats
GET  /api/sickleave/download  (+ variants)

GET  /api/vacations
GET  /api/vacations/current
GET  /api/vacations/upcoming
GET  /api/vacations/download  (+ variants)

GET  /api/albalance
GET  /api/albalance/{employeeId}

GET  /api/employees
GET  /api/employees/{id}
GET  /api/employees/{id}/timeline

GET  /api/attendance
GET  /api/attendance/download  (+ variants)

GET  /api/training/topics
GET  /api/training/sessions
GET  /api/pipeline
GET  /api/public-holidays

GET  /health
GET  /swagger  (dev only)
```

In production the backend also serves the frontend SPA via `UseDefaultFiles()` + `UseStaticFiles()` + `MapFallbackToFile("index.html")`.

---

## Frontend

### Providers & Bootstrap (main.tsx)

```
<ThemeProvider>          ← next-themes (dark mode)
  <QueryClientProvider>  ← TanStack Query (staleTime: 30s, retry: 1)
    <App />
  </QueryClientProvider>
</ThemeProvider>
```

i18next is initialised before React mounts. Language detection order: `localStorage` → browser `navigator`.

### Routing & Layout (App.tsx)

React Router v7 `BrowserRouter` with 14 routes. The shell layout is:

```
┌─────────────────────────────────────────┐
│  Sidebar (200 px fixed)  │  Top nav bar │
│  – Navigation links      │──────────────│
│                          │  <Outlet />  │
│                          │  (page)      │
└─────────────────────────────────────────┘
```

### Pages

| Route | Page | Description |
|-------|------|-------------|
| `/` | Overview | Stat cards, team-lead summary, WIC coverage cards |
| `/shifts` | Shifts | Shift plan table with filtering and downloads |
| `/wic-shifts` | WIC Shifts | WIC-specific shift view |
| `/wic-attendance` | WIC Attendance | Daily WIC location attendance |
| `/wic-schedule` | WIC Schedule | Opening hours per location |
| `/wic-locations` | WIC Locations | 40-location master list |
| `/pipeline` | Pipeline | WIC project/workload tracking |
| `/training` | Training | Training sessions and topics |
| `/employees` | Employees | Employee directory with filtering |
| `/sickleave` | Sick Leave | Records and stats |
| `/vacations` | Vacations | Requests, approvals, calendar |
| `/al-balance` | AL Balance | Per-employee annual leave balance |
| `/al-calendar` | AL Calendar | Calendar visualisation of annual leave |
| `/attendance` | Attendance | Daily presence tracking |

### API Client (src/api/client.ts)

Single source of truth for all HTTP calls. Exports:

- `apiFetch<T>(path, options?)` — Generic JSON fetch wrapper; base URL from `VITE_API_BASE_URL` env var (fallback: `http://localhost:5000`).
- `downloadExcel(path)` — Fetches a blob and triggers browser download.
- `api` — Object with typed methods for every endpoint group (`api.dashboard.*`, `api.shifts.*`, `api.wic.*`, etc.).

TanStack Query hooks in each page call these methods as query functions, giving automatic caching, background refetch, and loading/error states.

### Styling

Tailwind CSS 3.4 with a custom dark-mode palette defined as CSS custom properties in `index.css`:

| Variable | Value | Used For |
|----------|-------|----------|
| `--bg` | `#0b0f1a` | Page background |
| `--sidebar` | `#0e1320` | Sidebar background |
| `--card` | `#131928` | Card / panel background |
| `--accent` | `#3b7eff` | Primary blue (links, highlights) |
| `--accent2` | `#00d2a0` | Teal (WIC duty, secondary highlights) |
| `--warn` | `#ff7c3b` | Orange (warnings, sick leave) |
| `--danger` | `#ff3b5c` | Red (errors, uncovered locations) |
| `--green` | `#22d07a` | Working / covered |
| `--text` / `--text2` / `--text3` | Light grey hierarchy | Body text |

Fonts: **IBM Plex Sans** (body) and **IBM Plex Mono** (data cells, times).

### Internationalisation

Located in `src/i18n/`. Translation files at `src/i18n/locales/{en,de}/common.json`. Keys are namespaced: `nav.*`, `status.*`, `download.*`, `table.*`, `overview.*`. The language toggle button in the top nav writes the selection to `localStorage`.

---

## Data Flow

```
SQL Server 2022 (localhost\SQLEXPRESS, Windows Auth)
         │
         │  EF Core 8 queries
         ▼
ASP.NET Core 8 Minimal API  (http://localhost:5000)
         │
         │  JSON over HTTP
         ▼
React Frontend (http://localhost:5173 in dev)
   TanStack Query → page components → tables / charts
         │
         │  user clicks download
         ▼
ClosedXML → .xlsx blob → browser saves file
```

In **production** the React build is placed in `Backend/wwwroot/` and the ASP.NET app serves both the API and the SPA from the same port.

---

## Configuration

### Backend — appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
  },
  "AllowedHosts": "*",
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173", "http://localhost:3000"]
  }
}
```

### Frontend — .env

```
VITE_API_BASE_URL=https://<tunnel-host>-5000.euw.devtunnels.ms
```

For local development, omit this file or leave it empty — the client falls back to `http://localhost:5000`.
