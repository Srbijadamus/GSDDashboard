# GSD Dashboard — Technical Documentation

## Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Backend runtime | ASP.NET Core (Minimal APIs) | 8.0 |
| ORM | Entity Framework Core | 8.0 |
| Database | SQL Server Express | 2022, `localhost\SQLEXPRESS` |
| Excel generation | ClosedXML (MIT) | 0.102.x |
| API docs | Swagger / Swashbuckle | dev only |
| Frontend framework | React | 19.x |
| Build tool | Vite | 8.x |
| Language | TypeScript | ~6.0 |
| Routing | React Router | 7.x |
| Server state | TanStack Query | 5.x |
| Tables | TanStack Table | 8.x |
| Charts | Recharts | 3.x |
| Map | react-leaflet + leaflet | 5.0 / 1.9.4 |
| UI / CSS | Tailwind CSS | 3.4.19 |
| Icons | Lucide React | latest |
| Fonts | IBM Plex Sans + IBM Plex Mono | — |
| Internationalisation | i18next + react-i18next | 26.x |
| Theme | next-themes | 0.4.x |
| Kiosk | Python FastAPI | 3.11+ |

EPPlus is explicitly **not** used (commercial licence). ClosedXML handles all `.xlsx` generation.

---

## Repository Layout

```
C:\GSDDashboard\
├── Backend/
│   ├── GSDDashboard.API.csproj
│   ├── Program.cs                   # Entry point, all route registrations
│   ├── GSDContext.cs                # EF Core DbContext
│   ├── appsettings.json             # Connection string + CORS
│   ├── schema.sql                   # One-time DB init (tables + seed)
│   ├── Services/                    # Domain services
│   │   ├── CoverageEvaluator.cs
│   │   ├── SubstitutionService.cs
│   │   ├── ReachabilityService.cs
│   │   ├── ForecastService.cs
│   │   ├── WhatIfService.cs
│   │   ├── BriefingService.cs
│   │   ├── ALPlanningService.cs
│   │   ├── VwicService.cs
│   │   ├── WicLocationMatcher.cs    # Static alias/legacy code helper
│   │   └── ... (other services)
│   └── Models/                      # EF entities and DTOs
│
├── Frontend/
│   ├── package.json
│   ├── vite.config.ts
│   ├── tailwind.config.js
│   └── src/
│       ├── main.tsx                 # Providers, i18n init, QueryClient
│       ├── App.tsx                  # Router, sidebar, layout, topbar
│       ├── index.css                # CSS custom properties (light + dark)
│       ├── api/client.ts            # All API calls in one place
│       ├── i18n/                    # i18next config + EN/DE JSONs
│       ├── pages/                   # One component per route
│       └── components/              # Shared UI (Sheet, CoverageBadge, etc.)
│
├── documentation/
└── PS1_19_FinalBuildVerify.ps1      # Primary build + deploy script

C:\ShiftKiosk\
└── server\
    └── server.py                    # Python FastAPI kiosk, port 8000
```

---

## Backend Architecture

### Program.cs

Single file. Registers all services via DI, configures middleware, defines all Minimal API routes, serves the React SPA from `/wwwroot` in production.

### GSDContext.cs

EF Core `DbContext` with Windows Authentication (`Trusted_Connection=true`). `EnableRetryOnFailure(3)` for transient errors.

### Services

| Service | DI Lifetime | Responsibility |
|---------|-------------|---------------|
| `DashboardService` | Scoped | Top-level KPI metrics |
| `ShiftService` | Scoped | Shift plan queries |
| `WicShiftService` | Scoped | WIC-specific shifts, on-site vs. office |
| `WicCardsService` | Scoped | Per-location coverage status cards |
| `CoverageEvaluator` | Scoped | Canonical COVERED/PARTIAL/UNCOVERED/CLOSED classifier |
| `SubstitutionService` | Scoped | 4-tier ranked substitute engine |
| `ReachabilityService` | **Singleton** | Haversine matrix, 4h TTL cache, `IServiceScopeFactory` |
| `ForecastService` | Scoped | 14-day coverage forecast |
| `WhatIfService` | Scoped | What-if absence simulation |
| `BriefingService` | Scoped | Daily briefing JSON + Excel export |
| `ALPlanningService` | Scoped | AL planning per WIC location |
| `VwicService` | Scoped | Virtual WIC coverage 07-18 |
| `SickLeaveService` | Scoped | Sick leave records and stats |
| `VacationService` | Scoped | Vacation records |
| `ALBalanceService` | Scoped | AL balance calculations |
| `EmployeeService` | Scoped | Employee master data |
| `AttendanceService` | Scoped | Daily WIC attendance |
| `PublicHolidayService` | Scoped | National + regional holiday calendar |
| `TrainingService` | Scoped | Training topics and sessions |
| `PipelineService` | Scoped | Pipeline event CRUD |
| `WicScheduleService` | Scoped | WIC opening hours |
| `OverviewService` | Scoped | Cross-module overview aggregation |
| `WicLocationMatcher` | Static | Alias dict + legacy code matching |
| `PlzBundesland` | Static | PLZ → Bundesland fallback lookup |

**Rule:** No service may contain its own `COVERED/PARTIAL/UNCOVERED/CLOSED` if-else chain. All coverage classification goes through `CoverageEvaluator`.

---

## Database Schema

**Server:** `localhost\SQLEXPRESS`  
**Database:** `GSDDashboard`  
**Auth:** Windows Authentication

### Tables

| Table | Key Columns | Notes |
|-------|-------------|-------|
| `Employees` | Id, FullName, PrimaryRole, TeamLead, EngagementType, Bundesland | 130 rows. `PrimaryRole="SSP"` = backlog agents |
| `ShiftEntries` | EmployeeId, ShiftDate, ShiftType, ShiftCode, StartTime, EndTime | Authoritative for absence types |
| `WicShiftEntries` | EmployeeId, ShiftDate, LocationCode, WorkingShift, OnSite | WIC duty records |
| `WicLocations` | Id, LocationCode, DisplayName, City, Country, Coordinates, Bundesland, MinAgentsRequired, LocationCodeLegacy, PostalCode | 43 rows (41 DE + 2 NL) |
| `WicAgentAssignments` | LocationCode (old-style), EmployeeName, AssignmentType | MAIN or BACKUP. Join: `e.FullName = waa.EmployeeName` |
| `WicOpeningHours` | LocationCode, DayOfWeek, OpenTime, CloseTime, IsClosed | DayOfWeek: .NET convention 0=Sun…6=Sat |
| `WicPipeline` | LocationCode, Title, StartDate, EndDate, AgentsNeeded, Status | Pipeline events |
| `DailyAttendance` | LocationCode, Date, Status | assigned / WO / closed / PH |
| `SickLeaves` | EmployeeId, FirstDay, LastDay, LeaveType | **FirstDay/LastDay** (NOT StartDate/EndDate) |
| `Vacations` | EmployeeId, StartDate, EndDate, ApprovalStatus | — |
| `ALBalance` | EmployeeId, Year, EligibleDays, TakenDays, RemainingDays | — |
| `PublicHolidays` | Date, Name, Bundesland | Regional scope |
| `TrainingTopics` / `TrainingSessions` | Topic metadata and session assignments | — |
| `SubstitutionHistory` | EmployeeId, LocationCode, Date, SourceType, AssignedAt | Populated at runtime; used for fairness penalty |

### Critical Schema Notes

- `SickLeaves` uses `FirstDay` / `LastDay` — do NOT use `StartDate` / `EndDate`
- `WicOpeningHours.DayOfWeek` = .NET convention (0=Sun, 1=Mon … 6=Sat)
- `WicAgentAssignments.LocationCode` uses old-style codes (`DE_Dortmund`); `WicLocations.LocationCodeLegacy` maps to them
- `WicLocations.LocationCode` uses new tilde-style codes (`DE~44139~Dortmund~Str.`)
- `WicAgentAssignments` join is by **FullName** string, not EmployeeId
- `SickLeaves.LeaveType` has only value `"Self"` — not used for absence detection
- Absence detection uses `ShiftEntries.ShiftType` values: `SL, AL, UL, PH, LPH, RESIGNED, TRAINING`
- `HALF_AL` = 0.5 contribution (not excluded, fractional coverage)

### SSP / Voice Agent Base Location

SSP and Voice agents without a WIC assignment use **Dusseldorf HQ** as their base for distance calculations: `lat=51.2154, lon=6.7837`.

---

## API Endpoints

All routes registered in `Program.cs` (Minimal API syntax).

### WIC Core

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/wic/locations` | All 43 WIC locations |
| GET | `/api/wic/cards?date=` | Per-location coverage status cards |
| GET | `/api/wic/shifts?from=&to=&locationCode=` | WIC shift entries |
| GET | `/api/wic/open?date=&horizon=` | Open/coverage status, N days |
| GET | `/api/wic/forecast?horizon=14&locationCode=` | Coverage forecast (horizon clamped 1-30) |
| GET | `/api/wic/substitutes?locationCode=&date=&horizon=&absentIds=` | Ranked substitute candidates |
| POST | `/api/wic/substitutes/accept` | Accept a substitute (writes to SubstitutionHistory) |
| GET | `/api/wic/whatif?absentEmployeeId=&date=&horizon=` | What-if simulation |
| GET | `/api/wic/briefing` | Today's absences + gaps + next at-risk |
| GET | `/api/wic/briefing/export` | Excel export (3 sheets) |
| GET | `/api/wic/reachability?from=&to=` | Haversine distance matrix |
| GET | `/api/wic/reachability/sanity` | Berlin→Munich ~504 km sanity check |

### Shifts

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/shifts` | Full shift plan |
| GET | `/api/shifts/working-today` | Agents working today |
| GET | `/api/shifts/download` | Excel today |
| GET | `/api/shifts/download/7days` | Excel 7-day |
| GET | `/api/shifts/download/30days` | Excel 30-day |

### Employees

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/employees` | Employee list |
| GET | `/api/employees/{id}` | Single employee |
| POST | `/api/employees` | Create employee |
| DELETE | `/api/employees/{id}` | Delete employee |
| PATCH | `/api/employees/{id}/albalance` | Update AL balance |

### Leave

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/sickleave` | Sick leave records |
| POST | `/api/sickleave` | Add sick leave |
| GET | `/api/sickleave/stats` | Aggregate stats |
| GET | `/api/vacations` | Vacation records |
| DELETE | `/api/vacations/{id}` | Delete vacation |
| GET | `/api/albalance` | AL balance per employee |
| PATCH | `/api/albalance/{id}` | Update AL balance |

### Pipeline

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/pipeline` | Pipeline events |
| POST | `/api/pipeline` | Create event |
| PATCH | `/api/pipeline/{id}` | Update event |
| DELETE | `/api/pipeline/{id}` | Delete event |

### Other

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/attendance` | Daily attendance records |
| GET | `/api/training` | Training records |
| GET | `/api/wic/schedule` | WIC opening hours |
| GET | `/api/public-holidays` | Public holiday calendar |
| GET | `/health` | Liveness probe |
| GET | `/swagger` | Swagger UI (dev only) |

In production, `MapFallbackToFile("index.html")` serves the React SPA for all unmatched routes.

---

## Frontend Architecture

### Bootstrap (main.tsx)

```
<ThemeProvider attribute="class" defaultTheme="system" enableSystem>
  <QueryClientProvider client={queryClient}>
    <App />
  </QueryClientProvider>
</ThemeProvider>
```

i18next initialised before React mounts. Language detection: `localStorage` → browser `navigator`.

### Layout (App.tsx)

```
┌──────────────────────────────────────────────┐
│  Top bar: app name | date/horizon | Ctrl+K   │
│           ThemeToggle | DE/EN toggle          │
├──────────────┬───────────────────────────────┤
│  Sidebar     │                               │
│  (nav links) │     <page component>          │
│              │                               │
└──────────────┴───────────────────────────────┘
```

### Theme System

CSS custom properties in `index.css`. `:root` = light, `.dark` = dark (navy palette).

| Variable | Light | Dark |
|----------|-------|------|
| `--bg` | white | dark navy `#0b0f1a` |
| `--card` | light card | `#131928` |
| `--accent` | blue | blue |
| `--green` | green | green |
| `--warn` | orange | orange |
| `--danger` | red | red |
| `--status-covered` | green | green |
| `--status-partial` | orange | orange |
| `--status-uncovered` | red | red |
| `--status-closed` | slate | slate |

Leaflet cannot use CSS variables in canvas/SVG — `STATUS_HEX` in `Overview.tsx` holds hardcoded hex values for map pins. This is intentional.

### API Client (src/api/client.ts)

Single source of truth for all HTTP calls. Base URL from `VITE_API_BASE_URL` (fallback: `http://localhost:5000`). Exports `apiFetch<T>`, `downloadExcel`, and a typed `api` object.

TanStack Query stale times: locations 10 min, forecast/briefing 5 min, static data 10 min.

---

## Kiosk Server

| Property | Value |
|----------|-------|
| Language | Python + FastAPI |
| Port | 8000 |
| Entry point | `C:\ShiftKiosk\server\server.py` |
| Tunnel | https://ssr7tm2l-8000.euw.devtunnels.ms |
| Dashboard | https://ssr7tm2l-8000.euw.devtunnels.ms/dashboard |

Physical check-in/check-out data for WIC agents. Separate from the ASP.NET Core backend.

---

## Shift Type Reference

| Code | Meaning |
|------|---------|
| `WORKING` | Regular working shift |
| `WIC_DUTY` | On-site WIC duty |
| `AL` | Annual leave |
| `HALF_AL` | Half-day annual leave (0.5 coverage credit) |
| `SL` | Sick leave |
| `UL` | Unpaid leave |
| `OFF` | Off day |
| `OFF_WEEKEND` | Weekend |
| `PH` | Public holiday |
| `LPH` | Local (regional) public holiday |
| `TRAINING` | Training day |
| `RESIGNED` | Employee left |

Absence types (excluded from coverage): `SL, AL, UL, PH, LPH, RESIGNED, TRAINING`. `HALF_AL` = 0.5 contribution (not excluded).

---

## Parsing Notes

- WIC shifts matched with `.Contains("WIC")` — raw data has variable spacing.
- HALF_AL has two formats: `"HAL *"` (prefix) and `"* HAL "` (suffix) — both map to `HALF_AL`.
- Team lead names may carry a trailing `\n`; services always `.Trim()` before comparison.
- Shift times stored as `varchar` (e.g. `"08:00"`), not SQL `TIME`.
- `WicAgentAssignments` join is string-based: `e.FullName = waa.EmployeeName`.
