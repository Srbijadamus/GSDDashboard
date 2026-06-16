# GSD Dashboard — Final Handoff

**Project:** WIC Asset Tracker / GSD Operations Dashboard  
**Stack:** ASP.NET Core 10 (Backend) + React 19 + Vite 8 + TypeScript 6 + Tailwind 3.4 (Frontend)  
**Date:** 2026-06-15  
**Build status:** VERIFIED PASSING — PS1_19_FinalBuildVerify.ps1 all checks green 2026-06-15

---

## 1. Backend API Endpoints

All endpoints are read-only GET unless noted.

| Endpoint | Method | Description |
|---|---|---|
| `GET /health` | GET | Liveness probe |
| `GET /api/wic/locations` | GET | All WIC locations (id, code, city, country, coordinates) |
| `GET /api/wic/forecast?horizon=N` | GET | N-day coverage forecast per location (default 14) |
| `GET /api/wic/briefing` | GET | Today's absences + gaps + next-at-risk |
| `GET /api/wic/substitutes?locationCode=X&date=Y&horizon=N` | GET | Substitute candidates ranked by proximity |
| `GET /api/attendance?from=&to=&country=` | GET | Daily attendance records |
| `GET /api/vacations?year=&sheet=` | GET | Vacation records |
| `DELETE /api/vacations/{id}` | DELETE | Delete vacation entry |
| `GET /api/employees` | GET | Employee list |
| `PATCH /api/employees/{id}/albalance` | PATCH | Update AL used |
| `GET /api/albalance` | GET | AL balance per employee |
| `GET /api/sickleave?from=&to=&activeOnly=` | GET | Sick leave records |
| `PATCH /api/sickleave/{id}` | PATCH | Update sick leave notes |
| `GET /api/sickleave/stats` | GET | Aggregate sick leave stats |
| `GET /api/shifts?from=&to=&locationCode=` | GET | Shift plan |
| `GET /api/pipeline?from=&to=` | GET | Pipeline events |
| `POST /api/pipeline` | POST | Create pipeline event |
| `PATCH /api/pipeline/{id}` | PATCH | Update pipeline event |
| `DELETE /api/pipeline/{id}` | DELETE | Delete pipeline event |
| `GET /api/training` | GET | Training records |
| `GET /api/teamleads/summary` | GET | Team lead summary with WIC status |

---

## 2. Frontend Pages

| Route | Component | Description |
|---|---|---|
| `/` | `Overview.tsx` | Command center: KPI row, coverage heatmap, WIC map, recommendations, absence feed, risk radar |
| `/wic-attendance` | `WicAttendance.tsx` | Per-location coverage with substitute finder |
| `/attendance` | `Attendance.tsx` | Daily attendance log |
| `/vacations` | `Vacations.tsx` | Annual leave management with delete |
| `/albalance` | `ALBalance.tsx` | AL balance per employee with inline edit |
| `/sickleave` | `SickLeave.tsx` | Sick leave grouped by agent with day-grid views |
| `/shifts` | `Shifts.tsx` | Shift plan calendar |
| `/employees` | `Employees.tsx` | Employee directory |
| `/wic-locations` | `WicLocations.tsx` | WIC location list |
| `/pipeline` | `Pipeline.tsx` | Pipeline event list + timeline view |
| `/training` | `Training.tsx` | Training schedule |

---

## 3. Overview Command Center — Component Map

```
Overview.tsx
├── KpiCard ×5  (Open Today, At-Risk, Closure Risk, Absent, Coverage %)
│     data: /api/wic/forecast  +  /api/wic/briefing
├── Coverage Heatmap
│     rows = WIC locations, cols = next 14 days (URL ?horizon=N)
│     cell color = CoverageStatus CSS var
│     click → SubstituteDrawer (Sheet component)
├── WicMapView  (react-leaflet + OpenStreetMap)
│     rendered only when ≥1 location has non-null coordinates
│     WarningBanner shown if no coordinates
│     CircleMarker colored by today's status
│     ThemedTileLayer: light=OSM / dark=CartoDB dark_all
├── Recommendations Panel
│     today's gaps from briefing
│     inline best-substitute name
│     click → SubstituteDrawer
├── Absence Feed
│     today's absences from briefing (flat list)
│     WarningBanner: team lead grouping unavailable
└── Risk Radar
      next 7 days from forecast filtered to AT_RISK / UNCOVERED
      sorted by date
```

---

## 4. Global Topbar (App.tsx / AppLayout)

- **Left:** app name
- **Center:** today's date + horizon selector (7d / 14d) — visible only on `/` route, writes `?horizon=N` to URL
- **Right:** Ctrl+K CommandPalette trigger + ThemeToggle + DE/EN language toggle

**CommandPalette (`CommandPalette.tsx`):**
- Triggered by Ctrl+K (or Cmd+K)
- Fetches `/api/wic/locations`, filters by name/code
- Keyboard-navigable (↑↓ Enter Esc)
- Navigates to `/wic-attendance?location=[code]`
- Built from scratch — no shadcn dependency

---

## 5. Theme System

| Variable | Light | Dark |
|---|---|---|
| `--bg` | white | dark navy |
| `--card` | light card | dark card |
| `--card2` | slightly darker | slightly lighter |
| `--border` | soft grey | soft dark grey |
| `--text`, `--text2`, `--text3` | dark → grey gradient | light → grey gradient |
| `--accent` | blue | blue |
| `--green` | green | green (lighter) |
| `--warn` | orange | orange (lighter) |
| `--danger` | red | red (lighter) |
| `--purple` | purple | purple (lighter) |
| `--yellow` | amber | amber (lighter) |
| `--blue-light` | sky blue | sky blue (lighter) |
| `--status-covered` | green | green |
| `--status-partial` | orange | orange |
| `--status-uncovered` | red | red |
| `--status-closed` | slate | slate |

Theme managed by `next-themes` (`useTheme()`). `darkMode: "class"` in `tailwind.config.js`. Toggle via topbar ThemeToggle component.

**Note on Leaflet:** CSS variables cannot be used inside Leaflet canvas/SVG rendering. The `STATUS_HEX` dictionary in `Overview.tsx` provides hardcoded hex values specifically for map pin colors — this is intentional and necessary.

---

## 6. i18n

- Languages: **German (DE)** and **English (EN)** only.
- Files: `src/i18n/locales/de/common.json`, `src/i18n/locales/en/common.json`
- Library: `react-i18next`
- Language toggle in topbar writes to localStorage via i18next.
- All user-visible strings in Overview, CommandPalette, and polished pages are in both locale files.

---

## 7. TanStack Query Setup

- `QueryClientProvider` wraps the app in `main.tsx`.
- `staleTime` defaults per query:
  - locations: 10 min
  - forecast/briefing: 5 min
  - employee/static data: 10+ min
- Loading states: all table pages use animated `.skeleton` rows (CSS class defined in `index.css`).
- Empty states: all tables have a "no data" row when the array is empty.

---

## 8. Known Nulls and Warning Banners

| Location | Null case | Handling |
|---|---|---|
| Theme `class=` in static HTML | `next-themes` sets `class="dark"` or `class="light"` on `<html>` at JS hydration time, not in the server-rendered shell | Expected — the static HTML will never contain `class=dark/light`; this is not a bug |
| Overview map | `WicLocation.coordinates` null | `WarningBanner` shown; map not rendered |
| Absence feed | TeamLead field absent on briefing DTO | `WarningBanner` "flat list only" |
| WIC map tiles | Theme change | `ThemedTileLayer` remounts via `key={isDark?"dark":"light"}` |
| Substitute score | May be null | Rendered as `—` |

---

## 9. What Was NOT Implemented (and Why)

### 9.1 Map connector lines (AT_RISK pins → nearest backup sources)

The original spec called for SVG connector lines from AT_RISK location pins to the nearest 2 available backup agents.

**Why not implemented:** The `/api/wic/substitutes` response returns `{employeeId, name, distance, score, ...}` but does NOT include `homeLocationCode` or `homeCoordinates` for the backup person. Without coordinates, connectors cannot be drawn. AT_RISK pins are highlighted with a larger radius and thicker border instead.

**How to fix:** Extend the `SubstituteDto` in the backend to include the employee's home WIC location code and its coordinates. Then in `Overview.tsx`, draw `<Polyline>` from the at-risk pin to each backup home pin.

### 9.2 Team Lead grouping in absence feed

The `GET /api/wic/briefing` absence objects contain `{employeeId, fullName, leaveType, locationCode}` — no `teamLeadId` or `teamLeadName`. The employees table has TeamLead data but would require a separate query and a join that isn't part of the briefing contract.

**Why not implemented:** Would require either (a) changing the briefing endpoint to include teamLead, or (b) making a second `GET /api/employees` call and joining client-side. The spec kept briefing as a single fast call.

**How to fix:** Add `teamLeadName` to the `BriefingAbsenceDto` in the backend.

### 9.3 WIC map when all coordinates are null

The map section is entirely hidden when no WIC location has coordinates. A warning banner is shown instead. This is intentional — rendering an empty Leaflet map with no pins is useless.

**How to fix:** Geocode WIC locations by adding `lat,lon` values to the `WicLocations.Coordinates` column.

---

## 10. Build and Deployment

### Script

```powershell
# C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1
```

Run in PowerShell:
```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\PS1_19_FinalBuildVerify.ps1
```

### Manual steps (same as script)

```powershell
# 1. Build frontend
cd C:\GSDDashboard\Frontend
npm run build

# 2. Deploy to wwwroot
Remove-Item -Recurse -Force C:\GSDDashboard\Backend\wwwroot
Copy-Item -Recurse C:\GSDDashboard\Frontend\dist C:\GSDDashboard\Backend\wwwroot

# 3. Build + run backend
cd C:\GSDDashboard\Backend
dotnet build --configuration Release
dotnet run --configuration Release --no-build
```

### Tunnel URL

```
https://n8jlr9dr-5000.euw.devtunnels.ms/
```

### Verification checklist — PS1_19 confirmed 2026-06-15

- [x] `GET /health` → HTTP 200
- [x] `GET /api/wic/locations` → HTTP 200
- [x] `GET /api/wic/forecast?horizon=7` → HTTP 200
- [x] `GET /api/wic/briefing` → HTTP 200
- [x] `GET /api/wic/substitutes?locationCode=DE~86150~Augsburg~Schaezlerstr.%203&date=2026-06-15` → HTTP 200
- [x] `GET /` → HTTP 200, content-type text/html
- [x] `GET /wic-attendance` → HTTP 200, content-type text/html
- [x] Frontend build: 0 TypeScript errors
- [x] Backend build: 0 errors, 0 warnings
- [x] Theme check: SPA shell valid (next-themes applies class at JS hydration — expected)
- [x] i18n check: no bad patterns in locale files

---

## 11. Files Changed — Prompt 5 Summary

| File | Change type |
|---|---|
| `src/pages/Overview.tsx` | Full rewrite — Overview command center |
| `src/App.tsx` | Full rewrite — AppLayout, topbar, CommandPalette integration |
| `src/components/CommandPalette.tsx` | New file |
| `src/index.css` | New CSS variables (--purple, --yellow, --blue-light, --status-*) |
| `tailwind.config.js` | Full rewrite — all colors via CSS vars |
| `src/i18n/locales/en/common.json` | New keys: overview.*, nav.cmd* |
| `src/i18n/locales/de/common.json` | Same keys in German |
| `src/pages/Vacations.tsx` | Color tokens, skeleton loader |
| `src/pages/Attendance.tsx` | Color tokens, skeleton loader |
| `src/pages/Shifts.tsx` | Color tokens (all hardcoded hex → vars) |
| `src/pages/Employees.tsx` | Color tokens |
| `src/pages/SickLeave.tsx` | Color tokens |
| `src/pages/Training.tsx` | Critical: `#181e2e` card bg → `var(--card)` |
| `src/pages/WicLocations.tsx` | Color tokens, skeleton loader, empty state |
| `src/pages/ALBalance.tsx` | Color tokens, skeleton loader |
| `src/pages/Pipeline.tsx` | Color tokens (agent colors, border, weekend bg) |
| `PS1_19_FinalBuildVerify.ps1` | New — build + verify script |

---

*End of handoff.*
