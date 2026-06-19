# GSD Dashboard — Handoff (Current State)

**Date:** 2026-06-17  
**Build status:** VERIFIED PASSING — PS1_19_FinalBuildVerify.ps1 all checks green 2026-06-15  
**Stack:** ASP.NET Core 8 · React 19 + Vite 8 · TypeScript 6 · Tailwind 3.4.19 · SQL Server Express

---

## 1. Rules for the Next Agent

Read these before doing anything else.

| Rule | Detail |
|------|--------|
| No git | Never run git add/commit/push/status unless the user explicitly asks |
| No bash | MSYS2 is broken on this machine (exit code 0xC0000142). All shell ops go via PS1 scripts that the **user runs manually** |
| No Unicode in PS1 | No here-strings, no Unicode characters in PowerShell scripts |
| No Invoke-Sqlcmd | Use `System.Data.SqlClient` pattern in PS1 scripts |
| Language | English and German only. No Serbian, no other languages |
| SickLeaves columns | `FirstDay` / `LastDay` (NOT StartDate/EndDate) |
| WicOpeningHours codes | Uses new-style LocationCodes (`DE~...`) |
| WicAgentAssignments codes | Old-style (`DE_Dortmund`). Resolved via `WicLocations.LocationCodeLegacy`. Never rewrite existing assignments |
| SSP/Voice base location | Dusseldorf HQ: `lat=51.2154, lon=6.7837` |
| CoverageEvaluator | All COVERED/PARTIAL/UNCOVERED/CLOSED classification must go through this service. No inline chains anywhere else |

---

## 2. Infrastructure

### Tunnel URLs (expire every 4 days, auto-restart via Task Scheduler)

| Service | URL |
|---------|-----|
| GSD Dashboard | https://8nh5k5g1-5000.euw.devtunnels.ms |
| Kiosk | https://ssr7tm2l-8000.euw.devtunnels.ms |
| Kiosk Dashboard | https://ssr7tm2l-8000.euw.devtunnels.ms/dashboard |

### Task Scheduler (auto-starts on reboot)

- `GSDDashboard-Backend` — ASP.NET Core backend, port 5000
- `GSDDashboard-Tunnel` — devtunnel for dashboard
- `ShiftKioskServer` — Python FastAPI kiosk, port 8000
- `ShiftKioskTunnel` — devtunnel for kiosk
- `DevTunnel-AutoStart` — devtunnel CLI auth + start

### Deploy Script

```powershell
powershell -ExecutionPolicy Bypass -File C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1
```

Builds frontend, copies to `Backend/wwwroot/`, builds backend, starts server, runs smoke tests.

---

## 3. Database State

**Server:** `localhost\SQLEXPRESS`  
**Database:** `GSDDashboard`  
**Auth:** Windows Authentication

| Table / Column | Value |
|----------------|-------|
| `WicLocations` | 43 rows (41 DE + 2 NL), all `IsActive=1` |
| `WicLocations.Coordinates` | 43/43 geocoded (5 city-only precision, acceptable) |
| `WicLocations.MinAgentsRequired` | All = 1 (default) |
| `WicLocations.Bundesland` | 41 DE populated, 2 NL = NULL (correct) |
| `WicLocations.LocationCodeLegacy` | 42/43 populated (RENDSBURG = NULL, direct match, no legacy needed) |
| `WicLocations.PostalCode` | Extracted from LocationCode for all 41 DE rows; NL rows NULL |
| `Employees` | 130 rows |
| `WicAgentAssignments.AssignmentType` | MAIN or BACKUP |
| `SickLeaves.LeaveType` | Only value: "Self" — not used for absence logic |
| `SubstitutionHistory` | Table exists; grows at runtime when substitutes are accepted |
| `DayOfWeek` convention | 0=Sun … 6=Sat (.NET convention throughout) |

---

## 4. Backend API Endpoints

### WIC Core

| Method | URL | Service |
|--------|-----|---------|
| GET | `/health` | Liveness probe |
| GET | `/api/wic/locations` | All WIC locations |
| GET | `/api/wic/cards?date=` | Coverage status cards |
| GET | `/api/wic/shifts?from=&to=&locationCode=` | WIC shifts |
| GET | `/api/wic/open?date=&horizon=` | Open/coverage status N days |
| GET | `/api/wic/forecast?horizon=14&locationCode=` | 14-day forecast (clamped 1-30) |
| GET | `/api/wic/substitutes?locationCode=&date=&horizon=&absentIds=` | Ranked substitutes |
| **POST** | `/api/wic/substitutes/accept` | Accept substitute (writes SubstitutionHistory) |
| GET | `/api/wic/whatif?absentEmployeeId=&date=&horizon=` | What-if simulation |
| GET | `/api/wic/briefing` | Today's absences + gaps + next at-risk |
| GET | `/api/wic/briefing/export` | Excel export (Absences / Gaps / AT_RISK sheets) |
| GET | `/api/wic/reachability?from=&to=` | Haversine matrix |
| GET | `/api/wic/reachability/sanity` | Berlin→Munich ~504 km check |
| GET | `/api/wic/schedule` | WIC opening hours |

### Employees + Leave

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/employees` | Employee list |
| POST | `/api/employees` | Create employee |
| DELETE | `/api/employees/{id}` | Delete employee |
| PATCH | `/api/employees/{id}/albalance` | Update AL balance |
| GET | `/api/sickleave` | Sick leave records |
| POST | `/api/sickleave` | Add sick leave |
| GET | `/api/sickleave/stats` | Aggregate stats |
| GET | `/api/vacations` | Vacation records |
| DELETE | `/api/vacations/{id}` | Delete vacation |
| GET | `/api/albalance` | AL balance |
| PATCH | `/api/albalance/{id}` | Update AL balance |

### Shifts + Pipeline + Other

| Method | URL | Description |
|--------|-----|-------------|
| GET | `/api/shifts` | Shift plan |
| GET | `/api/pipeline` | Pipeline events |
| POST | `/api/pipeline` | Create event |
| PATCH | `/api/pipeline/{id}` | Update event |
| DELETE | `/api/pipeline/{id}` | Delete event |
| GET | `/api/attendance` | Daily attendance |
| GET | `/api/training` | Training records |
| GET | `/api/public-holidays` | Public holiday calendar |

---

## 5. Frontend Pages

| Route | Component | Write ops |
|-------|-----------|-----------|
| `/` | Overview.tsx | None |
| `/shifts` | Shifts.tsx | None |
| `/wic-shifts` | WicShifts.tsx | Reassign agents, new shift modal |
| `/vwic` | Vwic.tsx | Add/remove agents, 07-18 timeline |
| `/wic-attendance` | WicAttendance.tsx | Accept substitute, assign agent, manual check-in, AL planning |
| `/wic-schedule` | WicSchedule.tsx | None |
| `/pipeline` | Pipeline.tsx | Create/edit/delete events |
| `/training` | Training.tsx | None |
| `/wic-locations` | WicLocations.tsx | None |
| `/attendance` | Attendance.tsx | None |
| `/sickleave` | SickLeave.tsx | Add sick leave |
| `/vacations` | Vacations.tsx | None |
| `/albalance` | ALBalance.tsx | None |
| `/al-calendar` | ALCalendar.tsx | None |
| `/employees` | Employees.tsx | Create/delete employees, edit AL balance |

---

## 6. Substitution Engine

### Tiers (higher score = preferred)

| Tier | Score | Condition |
|------|-------|-----------|
| BACKUP | 10,000 | Designated backup in `WicAgentAssignments` |
| SSP | 5,000 | `PrimaryRole = "SSP"` |
| WIC_DONOR | 0 + reachability (0–500) | Nearby WIC with surplus (`surplus = effectiveCoverage - minRequired > 0`) |
| CALL_IN | –100 + reachability | All other reachable agents |

Fairness penalty: `-LoadScore * 10` (30-day rolling SubstitutionHistory).  
HALF_AL agents: 0.5 coverage credit, NOT excluded.

### Known nulls (always null in SubstituteCandidate)

- `contactEmail` — no email field on Employees table
- `travelMinutes` — haversine only, no routing API
- `hasDirectTrain` — no transit data
- `fairnessScore` raw value — computed internally as LoadScore

---

## 7. Key Design Decisions

### LocationCodeLegacy

`WicAgentAssignments.LocationCode` stores old codes (`DE_Dortmund`).  
`WicLocations.LocationCode` stores new tilde codes (`DE~44139~Dortmund~Str.`).  
`WicLocations.LocationCodeLegacy` bridges them.  
**Never write new assignments with old-style codes.**  
All 5 services that filter by location are updated to check both.

### WicLocationMatcher (static)

30-entry alias dictionary. Two static methods:
- `MatchesSupportLocation(sl, loc)` — checks DisplayName, City, aliases vs. both LocationCode and LocationCodeLegacy
- `MatchesAssignmentCode(code, loc)` — checks LocationCode OR LocationCodeLegacy

**Never duplicate the alias map** — all callers use this static class.

### ReachabilityService (singleton)

Haversine matrix. 4h TTL cache. Double-checked locking with `SemaphoreSlim`. Uses `IServiceScopeFactory` to resolve scoped `GSDContext` inside a Singleton.

### HALF_AL

`presentDouble += 0.5`, then `effectiveCoverage = (int)Math.Floor(presentDouble)`. Consistent across all 6 coverage-computing services.

---

## 8. Overview Command Center

```
Overview.tsx
├── KpiCard x5  (Open Today, At-Risk, Closure Risk, Absent, Coverage %)
│     data: /api/wic/forecast + /api/wic/briefing
├── Coverage Heatmap
│     rows=WIC locations, cols=next 14 days
│     click → SubstituteDrawer (Sheet)
├── WicMapView  (react-leaflet + OpenStreetMap)
│     CircleMarkers coloured by today's status
│     STATUS_HEX hardcoded (Leaflet can't use CSS vars) — intentional
│     ThemedTileLayer: light=OSM / dark=CartoDB dark_all
├── Recommendations Panel
│     today's gaps from briefing + best substitute name
│     click → SubstituteDrawer
└── CommandPalette (Ctrl+K)
      searches /api/wic/locations, navigates to /wic-attendance?location=
```

---

## 9. Build Status (last confirmed)

**2026-06-15 — PS1_19_FinalBuildVerify.ps1 — all green**

- Frontend: 0 TypeScript errors, 1852 modules, 2.37s
- Backend: 0 errors, 0 warnings
- `/health` → HTTP 200
- `/api/wic/locations` → HTTP 200, 43 locations
- `/api/wic/forecast?horizon=7` → HTTP 200
- `/api/wic/briefing` → HTTP 200
- `/api/wic/substitutes?locationCode=DE~86150~Augsburg~Schaezlerstr.%203&date=2026-06-15` → HTTP 200

---

## 10. What Was NOT Implemented

### Map connector lines

SVG lines from AT_RISK pins to nearest backup agents were spec'd but not built. `SubstituteDto` does not include `homeCoordinates`. AT_RISK pins use larger radius + thicker border instead.  
**Fix:** Add `homeLocationCode` + `homeCoordinates` to `SubstituteDto`, draw `<Polyline>` in `Overview.tsx`.

### Team lead grouping in absence feed

`GET /api/wic/briefing` absence objects have no `teamLeadId`. A flat list is shown with a WarningBanner.  
**Fix:** Add `teamLeadName` to `BriefingAbsenceDto`.

### WIC map when all coordinates are null

Map hidden, WarningBanner shown. Intentional — empty Leaflet map is useless.  
**Fix:** Geocode any locations with null coordinates.
