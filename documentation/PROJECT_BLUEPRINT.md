# GSDDashboard — Project Blueprint

This document is the master index for reconstructing the GSDDashboard project from scratch.
It covers Sections 1, 2, 8, 9, and 10. Detailed sections are in sibling files.

**Split documents:**
- [BLUEPRINT_DATA_MODEL.md](./BLUEPRINT_DATA_MODEL.md) — Section 3: All database tables
- [BLUEPRINT_DESIGN.md](./BLUEPRINT_DESIGN.md) — Section 5: Design system tokens, typography, z-index
- [BLUEPRINT_SCREENS.md](./BLUEPRINT_SCREENS.md) — Section 6: All pages/routes
- [BLUEPRINT_LOGIC.md](./BLUEPRINT_LOGIC.md) — Sections 4 & 7: Business logic + API reference

---

## Naming

The product name is **WorkForce Pulse**. Infrastructure names remain `GSDDashboard` — path `C:\GSDDashboard`, database `GSDDashboard`, scheduled task `GSDDashboard-Backend`, namespace `GSDDashboard.API`, repository, tunnel URLs. **This mismatch is intentional.** Renaming the infrastructure breaks the deploy chain and tunnels. Do not "align" them.

The assistant is named **Pulse Assistant**. The route remains `/assistant`.

`GSD` as a business domain term remains untouched: `GSD backlog`, `GSD duty`, `Global Service Desk`, `IsGSDDay`, `AgentTask = 'GSD'`, column names and enum values. This is a domain term, not a brand.

---

# Section 1 — Purpose and Context

## What GSDDashboard Is

GSDDashboard is an internal operational dashboard for the **E.ON Group Service Desk (GSD)** based in Düsseldorf, Germany. It is a single-page web application backed by an ASP.NET Core REST API. The system provides real-time visibility into agent shift plans, WIC coverage, sick leave, annual leave, attendance from kiosk terminals, pipeline events, training, and an AI-powered assistant.

## Who Uses It

- **~57 GSD agents** — Voice, SSP (backlog), Chat, Dispatcher, WIC (on-site), VWIC (virtual), SME
- **Team leads** (6 active) — daily attendance, sick leave entry, coverage review
- **Managers and planners** — forecast view, substitution planning, AL quota management

The tool is deployed on a Windows 11 laptop at the GSD office. It is an internal tool, not exposed to customers or external systems.

## What Problem It Solves

Before GSDDashboard, the team relied on Excel files and email to track who was where and whether WIC locations were covered. The dashboard solves:

1. **Shift visibility** — who is working, on leave, sick, or on WIC duty today and in coming weeks
2. **WIC coverage tracking** — which of the 43 WIC (Kundenzentrum) locations are at risk of being understaffed or uncovered, and who can substitute
3. **Sick leave management** — entry, tracking, automatic propagation to the shift grid
4. **Attendance** — live check-in data from ShiftKiosk terminals at WIC locations
5. **Annual leave balance** — planned vs. eligible days per agent
6. **Pipeline events** — upcoming special activities at WIC locations that need extra agents
7. **Training scheduling** — topic catalogue and session scheduling
8. **AI assistant** — natural-language queries over the operational data (no external LLM)

## Acronym Glossary

| Acronym | Expansion | Meaning |
|---|---|---|
| GSD | Group Service Desk | E.ON's internal customer service desk for energy customers |
| WIC | Kundenzentrum | Customer service centre — a physical walk-in location where agents serve customers in person. Agents travel to the WIC from the GSD office. |
| VWIC | Virtual WIC | Agent works from the GSD office in Düsseldorf but handles customer calls for a WIC location remotely (no travel). |
| BO | Back Office | Internal GSD work — not WIC travel. Agents on BO handle backlog tickets, quality work, etc. |
| NPP | Kernkraftwerk (Nuclear Power Plant) | A special WIC category located at a nuclear power plant site. Agents need `NppQualified = true` to be assigned. |
| AL | Annual Leave / Urlaub | Paid time off. Tracked in `Vacations` and `ALBalance` tables. |
| HALF_AL | Half-day annual leave | 0.5 working day of AL. |
| SL | Sick Leave / Krankmeldung | Tracked in `SickLeaves` table. |
| UL | Unpaid Leave | Not paid; tracked in ShiftEntries. |
| KID | Kundenzentrum-ID | Internal location identifier. Stored as `WicLocations.LocationCode`. |
| SSP | Special Support Position | Backlog-focused role. SSP agents can be substituted into WIC duty with zero donor risk. |
| TL | Team Lead | Manager for a group of 8–12 agents |
| PH | Public Holiday | National holiday. All WICs closed. |
| LPH | Local Public Holiday | Regional holiday (Bundesland-specific). The relevant WIC is closed. |
| CD | Compensation Day | Day off earned by working a holiday or weekend. |
| CO | Compensation Off | Similar to CD; legacy terminology. |
| RTM | Return to Main | Agent returning from WIC or special duty back to the main GSD workflow. The BulkRtm page processes RTM batches. |

---

# Section 2 — Stack and Infrastructure

## Technology Stack

### Backend

| Component | Version |
|---|---|
| .NET SDK | 8.0 |
| ASP.NET Core Minimal API | 8.0 |
| Entity Framework Core | 8.0.0 |
| EF Core SQL Server provider | 8.0.0 |
| EF Core Tools | 8.0.0 |
| Swashbuckle.AspNetCore (Swagger) | 6.5.0 |
| ClosedXML (Excel export) | 0.102.2 |

No ORM migrations — schema changes are applied as idempotent `ExecuteSqlRaw()` blocks in `Program.cs` on startup.

### Frontend

| Package | Version |
|---|---|
| React | ^19.2.6 |
| TypeScript | ~6.0.2 |
| Vite | ^8.0.12 |
| Tailwind CSS | ^3.4.19 |
| TanStack React Query | ^5.100.14 |
| TanStack React Table | ^8.21.3 |
| react-router-dom | ^7.15.1 |
| i18next | ^26.3.0 |
| i18next-browser-languagedetector | ^8.2.1 |
| react-i18next | ^17.0.8 |
| leaflet | ^1.9.4 |
| react-leaflet | ^5.0.0 |
| next-themes | ^0.4.6 |
| lucide-react | ^1.16.0 |
| recharts | ^3.8.1 |

### Database

SQL Server Express 2022 (or compatible). Connection string:
```
Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;
```
Windows Authentication (no SQL login). No connection pool tuning; default EF Core pool is used.

---

## Port Layout

| Service | Port | Description |
|---|---|---|
| ASP.NET Core backend | **5000** | HTTP only (no HTTPS). Serves the React SPA from `wwwroot/` and all REST endpoints. |
| Vite dev server | **5173** | Frontend development only. Proxies `/api` to port 5000. |
| ShiftKiosk (FastAPI) | **8000** | Separate Python service on the same machine. Provides kiosk attendance data. |

**devtunnel:** The GSDDashboard backend is exposed externally via Microsoft devtunnel on port 5000. The tunnel URL changes if the tunnel is recreated — run `devtunnel show` in the project context to get the current URL.

---

## Build and Deploy Pipeline

### Frontend Build

```bash
cd Frontend
npm run build
```

The `build` script in `package.json`:
1. Runs `tsc -b` (TypeScript type-check and compile)
2. Runs `vite build` (outputs content-hashed bundles to `Backend/wwwroot/`)
3. Copies `Backend/wwwroot/` to `Backend/bin/Release/net8.0/wwwroot/` for the release binary

The backend serves `wwwroot/` as static files via `app.UseStaticFiles()` + `app.MapFallbackToFile("index.html")`.

**Cache headers (set in Program.cs):**
- `index.html` → `no-cache, no-store, must-revalidate` (never cache the SPA shell)
- `/assets/*.js|css` → `public, max-age=31536000, immutable` (content-hashed filenames, cache forever)

### Backend Run

Development: `cd Backend && dotnet run` (uses `dotnet watch run` for hot reload).
Production: the framework-dependent build at `Backend/bin/Release/net8.0/GSDDashboard.API.exe`, produced by `dotnet build -c Release` (not `dotnet publish` — publish would generate a `web.config` the app never uses, since it runs standalone, not under IIS). Launched and supervised by the watchdog described below, not run directly.

### Windows Scheduled Task (Production Deploy)

Task name: `GSDDashboard-Backend`. Backup XML at `C:\GSDDashboard\task-backups\GSDDashboard-Backend.xml`.

- **Trigger:** Logon
- **Action:** `powershell.exe -NonInteractive -WindowStyle Hidden -File "C:\HealthCheck\watchdog_gsd_backend.ps1"` — not `dotnet run`, not a self-contained exe launched directly by the task.
- **RunLevel:** Highest (requires admin to Disable/Enable the task itself; not available on this Cloud PC — see below).
- **MultipleInstancesPolicy:** IgnoreNew (prevents double-start)

The task runs as the interactive user with highest available privileges.

**Watchdog** (`C:\HealthCheck\watchdog_gsd_backend.ps1`): infinite loop. On start, waits 30s before first launch. Sets its working directory to `C:\GSDDashboard\Backend` and starts `Backend\bin\Release\net8.0\GSDDashboard.API.exe`. If the exe exits for any reason, the watchdog logs it and relaunches 10s later.

**Independent health-check layer** (`C:\HealthCheck\health_check.ps1`, task `ServiceHealthCheck`, every 5 minutes): separately polls `GSDDashboard` on port 5000 (alongside ShiftKiosk:8000 and LaptopTracker:5016, each scoped to its own port/task). After 3 consecutive failed checks it stops the port-5000 listener and calls `Start-ScheduledTask` on `GSDDashboard-Backend` to recover.

**Dev tunnel**: separate task `GSDDashboard-Tunnel-v2`, its own watchdog `watchdog_gsd_tunnel.ps1`, running `devtunnel host gsd-dashboard-v2`. Independent of the backend task — survives backend restarts and is never touched by a backend deploy.

**Admin limitation on this Cloud PC**: no admin rights are available. `Disable-ScheduledTask`/`Enable-ScheduledTask` fail with Access Denied (the task's `RunLevel=Highest` requires elevation to modify the task definition). `Stop-Process` by exact PID and `Start-ScheduledTask` do **not** require admin and are the mechanisms actually used to stop/restart the service during a deploy. Never stop processes by generic name (`powershell`, `dotnet`) — always match the exact PID via `CommandLine` (e.g. `-File *watchdog_gsd_backend.ps1*`), since the same Cloud PC also hosts ShiftKiosk and LaptopTracker.

---

## Directory Layout

```
C:\GSDDashboard\
├── Backend\
│   ├── GSDDashboard.API.csproj
│   ├── Program.cs                        ← startup, all MapXxxEndpoints calls, inline DDL
│   ├── appsettings.json                  ← connection string
│   ├── wwwroot\                          ← built frontend (output of npm run build)
│   ├── GSDContext.cs                     ← EF Core DbContext
│   ├── Employee.cs
│   ├── ShiftEntry.cs
│   ├── OtherModels.cs                    ← WicShiftEntry, WicLocation, SickLeave, Vacation, ALBalance, ...
│   ├── WicModels.cs                      ← WicAgentAssignment, WicOpeningHour, WicPipeline, VwicRotationSlot, LeaveQuota
│   ├── BoEntry.cs
│   ├── RtmEntry.cs
│   ├── BreakModels.cs
│   ├── TrainingModels.cs
│   ├── SickLeaveService.cs               ← also contains endpoint mapper
│   ├── VacationService.cs                ← also contains endpoint mapper
│   ├── ALBalanceService.cs               ← also contains endpoint mapper
│   ├── DashboardService.cs               ← also contains endpoint mapper
│   ├── ShiftSyncService.cs
│   ├── WicCardsService.cs                ← also contains endpoint mapper
│   ├── WicShiftService.cs                ← also contains endpoint mapper (largest WIC file)
│   ├── Services\
│   │   ├── AvailabilityResolver.cs       ← GetWicContribution(), GetAbsentIdsAsync()
│   │   ├── CoverageEvaluator.cs          ← Classify(), ClassifyByMinutes()
│   │   ├── ForecastService.cs            ← also contains endpoint mapper
│   │   ├── SubstitutionService.cs        ← also contains endpoint mapper
│   │   ├── WicHoursResolver.cs
│   │   ├── WicLocationMatcher.cs         ← single alias dictionary, RepairDoubleEncoding
│   │   └── ...
│   ├── Modules\
│   │   ├── ALBalance\
│   │   ├── ALCalendar\
│   │   ├── Attendance\
│   │   ├── Backup\
│   │   ├── Breaks\
│   │   ├── BoList\
│   │   ├── BulkRtm\
│   │   ├── Dashboard\
│   │   ├── Employees\
│   │   ├── Overview\
│   │   ├── Pipeline\
│   │   ├── PublicHolidays\
│   │   ├── SickLeave\
│   │   ├── Shifts\
│   │   ├── SubstituteAccept\
│   │   ├── Training\
│   │   ├── Vacations\
│   │   ├── Vwic\
│   │   ├── WicAssistant\
│   │   ├── WicCoverage\
│   │   └── WicSchedule\
│   └── Assistant\
│       ├── AssistantService.cs           ← router + language filter
│       ├── AssistantEndpoints.cs
│       ├── AssistantDtos.cs
│       ├── IDomainHandler.cs
│       ├── SharedParser.cs               ← date/person/location extraction
│       └── *Handler.cs                   ← 12 domain handler implementations
├── Frontend\
│   ├── .env                              ← VITE_KIOSK_API_URL
│   ├── package.json
│   ├── vite.config.ts
│   ├── tailwind.config.js
│   ├── postcss.config.js
│   └── src\
│       ├── App.tsx                       ← route definitions
│       ├── main.tsx
│       ├── index.css                     ← imports tokens.css, global base styles
│       ├── styles\
│       │   └── tokens.css               ← all CSS custom properties (single source of truth)
│       ├── api\
│       │   └── client.ts                ← typed API client (api.wic.*, api.sickLeave.*, etc.)
│       ├── components\                  ← shared UI components
│       ├── layout\                      ← AppShell, Sidebar, Topbar
│       ├── pages\                       ← one file per route
│       └── utils\                       ← resolveEmployee, etc.
├── documentation\                       ← this blueprint
└── task-backups\                        ← Windows Task Scheduler XML exports
    ├── GSDDashboard-Backend.xml
    ├── GSDDashboard-Tunnel.xml
    └── ...
```

---

## Frontend Environment Variables

File: `Frontend/.env`

```
# VITE_API_BASE_URL is intentionally left empty.
# The app calls the API on the same origin it is served from.
# Setting an absolute URL here breaks all other origins
# (devtunnel, localhost:5173 via proxy, etc.).
VITE_KIOSK_API_URL=https://ssr7tm2l-8000.euw.devtunnels.ms
```

`VITE_KIOSK_API_URL` is the devtunnel URL for the ShiftKiosk FastAPI server. If the tunnel is recreated, update this value and rebuild the frontend.

---

# Section 8 — Integrations

## 8.1 ShiftKiosk (FastAPI, port 8000)

A separate Python/FastAPI application running on the same Windows machine at `http://localhost:8000` (exposed externally via devtunnel `ssr7tm2l-8000`).

**Purpose:** Kiosk terminals at WIC locations (Zone B) check in agents via QR code or card scan. The kiosk server records attendance and exposes it via REST.

**Frontend integration:** `WicAttendance.tsx` calls `GET {VITE_KIOSK_API_URL}/api/attendance` directly from the browser (cross-origin call to the devtunnel URL). The request includes no auth — the devtunnel is open.

**Employee matching:** Records are matched by `employee_id` field. **Never match by location name** — names in the kiosk system and in GSDDashboard may differ.

**Current status:** Zone B kiosk hardware is not yet deployed. The attendance page UI is complete but actual live data is absent until the terminals are installed.

---

## 8.2 Pulse Assistant (internal, no external LLM)

The assistant is a keyword-score router entirely inside the GSDDashboard backend. It does not call any external AI service.

**Endpoint:** `POST /api/assistant/ask`

**Request:**
```json
{ "question": "Who is on WIC leave next week?" }
```

**Response (`AssistantResponse`):**
```json
{
  "answerText": "The following agents are on WIC leave 2026-09-07 to 2026-09-13: ...",
  "dateRangeChecked": "2026-09-07 to 2026-09-13",
  "table": [
    { "employee": "Tim Nguyen", "employeeId": "12345", "start": "2026-09-07",
      "end": "2026-09-11", "workDays": 5, "wicLocation": "Essen BP1", "role": "Main" }
  ],
  "error": null,
  "hint": "Ask 'Who can substitute at Essen BP1?' to see available replacements."
}
```

`table` and `hint` are `null` when not applicable. `error` is non-null only on backend exceptions.

**Router logic summary:**
1. Language gate — rejects non-English/German input
2. Normalise umlauts (ä→ae, ö→oe, ü→ue, ß→ss) and lowercase
3. Score all 12 domain handlers by keyword matching
4. If top score is shared by two handlers → return disambiguation message
5. Otherwise call the winning handler with `AssistantParsedQuery`

**Debug endpoint:** `POST /api/assistant/score` with a question returns all 12 handlers with their scores. Useful for diagnosing routing failures.

---

## 8.3 Excel Import Pipeline

There is no live connection to SharePoint, WFM, or any HR system. Files are manually downloaded and processed.

**Shift plan import:**
- Tool: `import_shifts.py` (Python, uses `openpyxl` and `pyodbc`)
- Location: not documented (not in C:\GSDDashboard Backend or Frontend)
- Reads the quarterly Excel shift plan and inserts/updates `ShiftEntries` and `WicShiftEntries`
- Sets `SourceSheet = "EXCEL"` on ShiftEntries

**Annual leave import:**
- Tool: PowerShell scripts using `System.Data.SqlClient`
- Reads AL Excel sheets and inserts into `Vacations` table
- Sets `SourceSheet = "AL_IMPORT"`

**Sick leave entry:**
- Primary method: via the Sick Leave REST API (`POST /api/sickleave`) — creates a record and auto-propagates to ShiftEntries
- Alternative: PS1 script for bulk historical import

**No live data feeds.** All external data (SAP shifts, WFM forecasts, HR absences) must be manually exported to Excel and imported.

---

# Section 9 — Known Problems and Rules

## What Must Never Be Done

**1. Never hardcode hex colours in component code.**
Use design tokens via Tailwind classes (`text-good-fg`, `bg-warn-bg`) or `rgb(var(--token-name))` inline styles. The design token file (`tokens.css`) is the single source of truth for all colours. See [BLUEPRINT_DESIGN.md](./BLUEPRINT_DESIGN.md).

**2. Never create a second alias dictionary for WIC location matching.**
All location string matching must go through `WicLocationMatcher` (`Backend/Services/WicLocationMatcher.cs`). Adding a new alias form means adding it to the `_aliases` dictionary in that file — not creating a local copy in a service.

**3. Never write bilingual strings inline in component code.**
All user-facing strings are externalised via `i18next` / `react-i18next`. Use `const { t } = useTranslation()` and `t("key")`. Adding a new string requires an entry in the locale JSON files.

**4. Never use `alert()` or `confirm()` in the frontend.**
These are browser-native dialogs that break the design system. Use the custom `OverrideConfirmModal` pattern or a proper modal component.

**5. Never merge AL and SL into a single record or allow them to overlap.**
`VacationService.CreateAsync()` explicitly rejects any vacation that overlaps an existing sick leave for the same employee. The API returns `null` (HTTP 400 at the endpoint) in this case.

**6. Never use `ShiftType.Contains("wic")` for substring matching.**
`"VWIC"` also contains the substring "wic". Always compare to `ShiftTypes.WicDuty` ("WIC_DUTY") by exact equality.

**7. Never rely on `IsWicDuty` flag.**
This flag is a fossil. It was abandoned when `ShiftType = "WIC_DUTY"` was introduced. A startup migration clears any corrupt `IsWicDuty=1` rows where `ShiftType != 'WIC_DUTY'`. Use `ShiftType` only.

**8. Never insert Employees with known numeric EmployeeIds directly from external lists without checking the DB.**
WIC agents already exist in the DB via the `WicCoverageImport` seed that runs on every startup. Re-inserting them would violate the unique constraint on `EmployeeId`. The startup import is idempotent (`IF NOT EXISTS`).

---

## Data Quirks

**`wic` vs `vwic` substring collision in ShiftType checks**
Checking `shiftType.Contains("wic", OrdinalIgnoreCase)` matches both `WIC_DUTY` and `VWIC` assignments. Always use `string.Equals(sh.ShiftType, ShiftTypes.WicDuty, ...)` for exact comparison.

**Windows-1252 → UTF-8 double-encoding in `WicShiftEntry.SupportLocation`**
Old Excel imports decoded the file as Windows-1252 while the content was UTF-8. This produced corrupt two-byte sequences like "SaarbrÃ¼cken" instead of "Saarbrücken". `WicLocationMatcher.RepairDoubleEncoding()` fixes this transparently. The guard (`!s.Contains('Ã')`) ensures zero overhead on clean values. **Do not pre-fix the DB** — the repair runs at query time and is idempotent.

**Ghost WicShiftEntry rows (NULL SupportLocation)**
A partial-write bug (called the "B1 transaction bug" internally) could create a row with `SupportLocation IS NULL` alongside a valid sibling. These ghost rows remain harmless because all coverage queries filter `IsOnSite = 1` AND use `WicLocationMatcher.MatchesSupportLocation()` which returns `false` for null. Clean them up with `POST /api/admin/cleanup-wic-orphans` — this endpoint is idempotent.

**Duplicate ShiftEntries per agent+date**
A single agent+date can have two rows: one from the Excel shift plan (`SourceSheet = "EXCEL"`) and one from the AL import (`SourceSheet = "AL_IMPORT"`). Services use `ShiftDuplicateResolver.BestShiftEntry()` (highest Id wins) to pick one. Do not assume uniqueness per (EmployeeId, ShiftDate) in raw queries.

**`SickLeaves.LastDay = 2099-12-31` sentinel**
An open-ended sick leave record (agent has not returned) uses this sentinel value. All date-range queries correctly include it because `2099-12-31 >= any_real_date`. Display code must treat this as "still open" rather than a real end date.

**Vacations.WorkDaysNet column type migration**
This column was `INT` at initial deployment and was altered to `DECIMAL(10,1)` in a startup migration. On a fresh database the column is already DECIMAL. On a legacy database the migration runs once and widens the column. The same migration exists for `ALBalance.PlannedTakenAL` and `ALBalance.RemainingAL`.

**`WicAgentAssignments` uses `EmployeeName` string, not `EmployeeId`**
There is no FK. SubstitutionService joins `WicAgentAssignments.EmployeeName` to `Employees.FullName` by case-insensitive string equality. If an employee's name changes in `Employees`, the assignment record must also be updated manually.

---

## Operational Flows

**Q1 — New Quarter: Import Shift Plan**
1. Download quarterly Excel shift plan from SharePoint
2. Run `import_shifts.py` pointing at the Excel file
3. Script truncates and repopulates `ShiftEntries` and `WicShiftEntries` for the new quarter
4. Review the Shifts page to verify

**Q2 — Weekly Sick Leave Entry**
1. Team lead notifies of sick agents via email
2. Enter via Sick Leave page (`POST /api/sickleave`)
3. For open-ended leave: set end date to `2099-12-31` (sentinel)
4. When agent returns: PATCH the record with the real return date
5. ShiftSyncService automatically writes/reverts SL ShiftEntries

**Q3 — WIC Coverage Review**
1. Open the Overview page → map shows at-risk locations (PARTIAL/UNCOVERED)
2. Click an at-risk location → opens the substitution panel
3. Use What-If to simulate additional absences (e.g. planned AL)
4. Call `GET /api/wic/substitutes?locationCode=...&absentIds=...` for candidate list
5. Select a substitute → `POST /api/substitute-accept` records the decision
6. If reassigning a WIC_DONOR: confirm donor WIC stays covered after removal

---

# Section 10 — What Is Not Yet Finished

## Zone B Kiosk Terminals

The `WicAttendance` page has the full UI: live presence dots (pulse-live animation), manual check-in modal, `AssignAgentModal`, and coverage badge display. However, the physical kiosk terminals for Zone B WIC locations have not been deployed. Until they are installed and the ShiftKiosk FastAPI server receives real scan data, `GET /api/attendance` from the kiosk will return empty or test data only.

## Assistant Domain Handlers — All Implemented

All 12 domain handlers are fully implemented. None are stubs. Each handler reads from real services and returns structured data. Confirmed by reading handler source:

- `WicLeaveHandler` — delegates to `WicAssistantService.AskAsync()`, maps result to table rows
- `SickLeaveHandler` — queries `SickLeaveService`, returns sick leave records with date ranges
- `VacationsHandler` — queries `VacationService`, returns vacation records
- `ALBalanceHandler` — calls `ALBalanceService.GetAllAsync()`, filters by person or lowest balance
- `DashboardHandler` — calls `DashboardService.GetSummaryAsync()`, returns today's summary string
- `WicForecastHandler` — calls `ForecastService.GetForecastAsync()`, returns coverage forecast rows
- `WicCoverageDetailHandler` — calls `WicCoverageService`, does location matching via `WicLocationMatcher`
- `PipelineHandler` — queries pipeline events
- `TrainingHandler` — queries training sessions and topics
- `EmployeesHandler` — queries employee list with role/status filters
- `WicOpeningHoursHandler` — queries `WicOpeningHours` table, builds daily/weekly schedule text
- `AgentAvailabilityHandler` — calls `AvailabilityResolver.GetStatusAsync()` per agent

**The `\bwic\b` scoring trap (Rule 6 in Section 9):** Any `Score()` method that checks `q.Contains("wic")` will match both WIC-related and VWIC-related queries. `ShiftType.Contains("wic")` also matches `"VWIC"`. Always use exact string equality against `"WIC_DUTY"`, not substring search. This specifically affects `WicForecastHandler` and `WicCoverageDetailHandler` — both guard against VWIC collisions by checking `!q.Contains("vwic")` first.

## i18n Translation Coverage

The application uses `i18next` with `i18next-browser-languagedetector`. Many components use `const { t } = useTranslation()` with `t("nav.alBalance")` etc. However, some components (particularly older ones like `Training.tsx`, `Pipeline.tsx`, `BoList.tsx`) use hardcoded English strings instead of `t()` calls. A full i18n audit has not been performed.

## Recharts Integration

`recharts` is listed as a dependency but no charts were found in the pages read during this blueprint exercise. Either the charts are in files not read, or the library was added in anticipation of future use.

## `WICShifts` Page Details

**Route:** `/wic-shifts`  
**File:** `Frontend/src/pages/WICShifts/index.jsx` (JSX, not TSX — note the `.jsx` extension; all other pages are `.tsx`)

Confirmed by reading the file directly. Key facts:

- Polls every 30 seconds using `setInterval`
- Calls two endpoints:
  - `GET /api/wic/cards?date={today}` → card data with `coverageStatus` per location
  - `GET /api/wic?from={today}&to={today}` → shift entries for today
- Merges card data with shift data by `supportLocation` string field
- Filters out closed locations (locations where the API returns `isClosed = true`)
- Maps `coverageStatus` to display variants: `"covered"`, `"partial"`, `"uncovered"`
- Component structure is self-contained in the `WICShifts/` subdirectory, unlike other pages which are single-file

## `ALPlanningModal` and `AssignAgentModal` Routing

Both modal components are imported in `WicCoverage.tsx` and `WicAttendance.tsx`. They are not standalone routes but are used as overlays. Their internal API calls are not documented separately — they share endpoints with the parent pages.

## `LeaveQuotas` Table — Dead/Reserved

The `LeaveQuotas` table is defined in `WicModels.cs` and registered in `GSDContext` as `DbSet<LeaveQuota>`, but **no application code reads or writes it**. Confirmed by searching the entire Backend — there is no `db.LeaveQuotas` usage in any service or endpoint.

`VacationService` uses a `maxLeave` concept but reads it as a plain `int` query parameter (default `8`), not from this table. The table exists in the schema and EF model as a reserved structure for future quota management. To activate it, a new service would need to read from `LeaveQuotas` and the `/api/vacations/availability` endpoint would need to use it instead of the query param.

## `DailyAttendance` Import — External ETL Only

The `DailyAttendance` table is **read-only from the application's perspective**. Confirmed by searching the entire Backend — `AttendanceService.cs` contains two read queries (`_db.DailyAttendances.Where(...)`) and zero write operations. There is no `DailyAttendances.Add(...)` call anywhere in the codebase.

The table is populated externally, outside the application — likely via a SQL script or a PowerShell/Python ETL job that runs periodically and inserts rows directly into the database. The specific import tool is not in `C:\GSDDashboard`. To repopulate or backfill this table, a direct SQL INSERT or external script must be used; the application has no write endpoint for it.
