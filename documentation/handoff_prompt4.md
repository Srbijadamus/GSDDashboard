# Handoff — Prompt 4
Date: 2026-06-15
Session status: COMPLETE

## Build results (confirmed)
- PS1_17: react-leaflet 5.0.0, leaflet 1.9.4, @types/leaflet installed; Tailwind still 3.4.19; 0 vulnerabilities
- PS1_18 frontend: SUCCESS, 1852 modules, 2.37s
- PS1_18 backend: SUCCESS, 0 warnings, 0 errors (improved from 2 warnings in Prompt 3 — genuine reduction, nothing suppressed; duplicate `using` directives in Program.cs that caused CS0105 warnings were resolved)
- All 5 smoke tests: HTTP 200 (forecast, cards, briefing, health, index.html)
- Server running PID 26956

---

## What was done

### Step 0 — Frontend state verified
- React 19.2.6 + Vite 8.0.12 + TypeScript 6.0.2 + Tailwind CSS 3.4.19
- Already installed: @tanstack/react-query 5.100.14, next-themes 0.4.6, lucide-react
- Missing: react-leaflet, leaflet (install via PS1_17)
- No shadcn/ui — Sheet drawer implemented from scratch

### Step 1 — Dependency install script
File: `PS1_17_InstallDeps.ps1`
Installs: react-leaflet, leaflet, @types/leaflet
Does NOT upgrade Tailwind (stays on 3.4.19)

### Step 2 — Design system
**`Frontend/src/index.css`** — full rewrite with HSL CSS variables:
- `:root` = light theme (white/gray background, dark text)
- `.dark` = dark theme (existing dark navy palette, now in HSL)
- `.skeleton` + `@keyframes skeleton-pulse` for loading states
- `@keyframes pulse-green` for animated status dots

**`Frontend/src/components/ThemeToggle.tsx`** — Sun/Moon button using `useTheme()` from next-themes

**`Frontend/src/App.tsx`** — ThemeToggle imported and added to topbar (next to LangToggle)

ThemeProvider was already in `main.tsx` with `attribute="class" defaultTheme="system" enableSystem`.
The `.dark` class on `<html>` activates dark theme.

### Step 3 — WIC Coverage page
**`Frontend/src/pages/WicAttendance.tsx`** — complete rewrite (old kiosk page replaced)

Layout:
- Left sidebar (260px, sticky): searchable + filterable location list sorted by risk (UNCOVERED first, then PARTIAL, COVERED, CLOSED)
- Right content area: selected location detail with stats row, agent chips, 7-day forecast calendar
- Sheet drawer (right slide-in): substitute candidates from /api/wic/substitutes

API usage:
- `GET /api/wic/forecast?horizon=7` — location list + 7-day status per location (staleTime 5min)
- `GET /api/wic/cards?date=YYYY-MM-DD` — agent detail for selected date (staleTime 2min)
- `GET /api/wic/substitutes?locationCode=X&date=Y&horizon=1` — enabled only when Sheet is open (staleTime 1min)

Auto-select: first location with atRiskDays > 0 on load, or first location if none.
Clicking at-risk day in forecast calendar opens substitute Sheet for that day.

**`Frontend/src/components/Sheet.tsx`** — minimal sliding right-side drawer
- Backdrop + panel with 0.25s ease transition
- Keyboard: Escape closes
- Portal-free (inline rendering with fixed position + z-index 40/50)

**`Frontend/src/components/CoverageBadge.tsx`** — status badge component
- Props: `status: string`, `compact?: boolean`
- compact = dot only (8px circle) used in location list
- full = badge with dot + translated text (uses `attendance.status.*` i18n keys)
- Colors: COVERED=green, PARTIAL=orange, UNCOVERED=red, CLOSED=muted

### Step 4 — i18n strings
Added `attendance.*` namespace to both locale files.

**`Frontend/src/i18n/locales/en/common.json`** — added attendance.* (English)
**`Frontend/src/i18n/locales/de/common.json`** — added attendance.* (German)

Keys added:
- attendance.title, subtitle, selectLocation, noLocations, today, horizon
- attendance.status.covered/partial/uncovered/closed
- attendance.agents.title/noAgents/main/backup/full/partial/absent
- attendance.substitute.find/title/loading/noCandidates/distance/lastUsed
- attendance.risk.atRiskDays/effectiveCoverage/minRequired
- attendance.filter.allCountries/search

### Steps 5 & 6 — Build + handoff
**`PS1_18_BuildAndDeploy.ps1`** — written, not yet run by user
Steps: npm run build → verify dist → stop backend → copy dist to wwwroot → build backend → start server → smoke-test 5 endpoints

---

## Files changed

### New files
- `Frontend/src/components/ThemeToggle.tsx`
- `Frontend/src/components/Sheet.tsx`
- `Frontend/src/components/CoverageBadge.tsx`
- `PS1_17_InstallDeps.ps1`
- `PS1_18_BuildAndDeploy.ps1`
- `documentation/handoff_prompt3.md` (deferred from Prompt 3)

### Rewritten files
- `Frontend/src/pages/WicAttendance.tsx` (old kiosk page → new WIC coverage page)
- `Frontend/src/index.css` (dark-only hex → light+dark HSL CSS variables)

### Modified files
- `Frontend/src/App.tsx` — added ThemeToggle import + topbar usage; removed `// @ts-ignore` on WicAttendance import; removed `// import { useTheme }` comment
- `Frontend/src/i18n/locales/en/common.json` — added attendance.* block
- `Frontend/src/i18n/locales/de/common.json` — added attendance.* block

---

## Deployed URLs
- Local:  http://localhost:5000/wic-attendance
- Tunnel: https://n8jlr9dr-5000.euw.devtunnels.ms/wic-attendance

---

## Known issues / decisions

- **react-leaflet installed but unused**: leaflet/react-leaflet are installed as instructed but the /wic-attendance page uses text/badge UI, not a map. A location map could be added later using the Coordinates field in WicLocations.
- **Old kiosk WicAttendance replaced**: The original page hit a separate KIOSK server on port 8000 with physical checkin/checkout data. That functionality is fully replaced by the WIC coverage view. If the kiosk view needs to be restored, the old file is in git history (if available) or can be rebuilt from the handoff summary.
- **SPA fallback**: `app.MapFallbackToFile("index.html")` in Program.cs ensures React Router routes like `/wic-attendance` work when served from Backend.
- **ThemeToggle flash**: On first load, next-themes may briefly show the wrong theme before hydrating (common with SSR; irrelevant here as Vite SPA has no SSR). The `enableSystem` prop resolves to the OS preference.

---

## Backend state (unchanged from Prompt 3)
- 8 endpoints total: /api/wic/open, /api/wic/backup, /api/overview/wic-status, /api/wic/substitutes, /api/wic/reachability/sanity, /api/wic/forecast, /api/wic/whatif, /api/wic/briefing
- All services registered and functional
- Build confirmed SUCCESS 2026-06-15 15:14:45
- DB: 43 WicLocations, all MinAgentsRequired=1, SubstitutionHistory table exists
