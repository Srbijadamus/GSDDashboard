# GSDDashboard — Screens and Pages Reference (Section 6)

**Part of the Project Blueprint.** See [PROJECT_BLUEPRINT.md](./PROJECT_BLUEPRINT.md) for the index.

All routes are defined in `Frontend/src/App.tsx` and rendered inside the `AppShell` layout (sidebar + topbar). The SPA is served by the ASP.NET Core backend as static files from `wwwroot/`; all unknown paths fall back to `index.html`.

---

## Layout Shell

**`AppShell`** (`src/layout/AppShell.tsx`) wraps every page. It renders:
- `Sidebar` — left navigation rail with `data-nav-theme="dark"` always active
- `Topbar` — contains the `ThemeToggle`, `CommandPalette` trigger, and date display
- `<Outlet>` — the active page

**`Sidebar`** reads nav token colours from CSS. Nav items are grouped into sections (Shifts, WIC, Leave/HR, Admin). The sidebar is always dark, independent of the app light/dark theme.

---

## 1. Overview

**Route:** `/` (index)
**File:** `src/pages/Overview.tsx`

The ops command centre. Loads a live forecast map and a daily briefing.

**Main API calls:**
- `GET /api/wic/forecast?horizon=28` → `ForecastResponse` — 28-day coverage forecast for all locations, powers the map markers and the DayStrip
- `GET /api/wic/briefing` → `Briefing` — today's absences, coverage gaps, and next at-risk days
- `GET /api/wic/cards?date=today` → `WicCardDto2[]` — per-location coverage cards

**Key components:**
- `MapContainer` (react-leaflet) with `CircleMarker` per WIC location — colour from `statusColor()` based on `todayStatus`
- `DayStrip` (subcomponent) — horizontal 28-day forecast strip showing at-risk days
- `ExceptionList` (subcomponent) — list of today's absences and gaps from the briefing
- `Sheet` (drawer) — opens when clicking a map marker to show location detail
- `NppBadge` — displayed on NPP location markers

**Notable behaviour:**
- Uses Leaflet `CircleMarker` with popup on click, not a custom icon, to avoid Leaflet icon path issues in Vite
- The briefing data drives the "At Risk" exception list in the top section
- `useSearchParams()` is used to support deep-linking to a specific location via `?loc=DE_Essen_BP1`

---

## 2. Shifts

**Route:** `/shifts`
**File:** `src/pages/Shifts.tsx`

Weekly/monthly shift plan grid. Shows all agents × all dates.

**Main API calls:**
- `GET /api/shifts` (with date range) → `ShiftEntry[]`
- `GET /api/employees` → employee list for agent rows
- `GET /api/vacations` — overlaid on the grid
- `GET /api/sickleave` — overlaid on the grid

**Key components:**
- Custom grid table with `shiftColor()` mapping ShiftType to token colours
- `taskStyle()` badges for AgentTask field (WIC, Voice, Backlog)
- `OverrideConfirmModal` — shown before overriding OFF_WEEKEND or PH cells (system-generated, require confirmation)
- `AddVacationModal` (imported from Vacations.tsx) — inline vacation creation from the shifts grid

**Notable behaviour:**
- `OVERRIDE_CONFIRM_TYPES = ["OFF_WEEKEND", "PH"]` — editing these triggers the modal
- Duplicate ShiftEntries per agent+date are resolved by taking the highest Id; a `ShiftDuplicateResolver` class in the backend handles this
- `maxFutureDateStr` constant prevents booking too far into the future

---

## 3. WIC Shifts (`WICShifts`)

**Route:** `/wic-shifts`
**File:** `src/pages/WICShifts/index` (subdir, not read — may be a compiled page)

WIC schedule table showing which agents are assigned to which locations per day.

**Main API calls:**
- `GET /api/wic/` (with from/to/location/employeeId/teamLead filters) → `WicShiftDto[]`
- `GET /api/wic/locations` → location list
- `PATCH /api/wic/{id}` — edit task, location, or IsOnSite

**Notable behaviour:**
- Download Excel button at `GET /api/wic/download`

---

## 4. VWIC (Virtual WIC)

**Route:** `/vwic`
**File:** `src/pages/VWICPage.tsx`

Virtual WIC daily coverage view. VWIC agents work from the GSD office but serve a WIC remotely.

**Main API calls:**
- `GET /api/vwic/daily?date=...` → `VwicDailyResponse` with agent list, timeline, gaps, covered/total hours
- `GET /api/vwic/candidates?date=...` → `VwicCandidate[]` for gap filling
- `GET /api/vwic/rotation?date=...` → `VwicRotationScheduleRow[]`

**Key components:**
- Timeline visualization showing hourly VWIC coverage
- `VwicRotationSlots` table for rotation planning
- Candidate list for filling gaps

**Notable behaviour:**
- VWIC rotation slots are stored in `VwicRotationSlots` table
- CoverageProof view shows granular per-slot coverage

---

## 5. Break Planner

**Route:** `/breaks`
**File:** `src/pages/BreakPlanner.tsx`

Schedules and tracks agent breaks (VWIC and Voice roles) to ensure coverage during break periods.

**Main API calls:**
- `GET /api/breaks?date=...` → `BreakSlotDto[]`
- `POST /api/breaks/distribute?date=...` → `BreakDistributeResult` — auto-distributes breaks
- `PATCH /api/breaks/{id}` — update break status (ON_BREAK, DONE, CANCELLED)

**Key components:**
- Break slot cards grouped by role (VWIC, Voice)
- Shuffle button triggers `/distribute` endpoint
- `overlaps()` utility to check time conflicts client-side

**Notable behaviour:**
- Status transitions: SCHEDULED → ON_BREAK → DONE (or CANCELLED at any point)
- `maxVwicConcurrent` and `maxVoiceConcurrent` from distribute result show how many agents are on break simultaneously at peak

---

## 6. WIC Attendance

**Route:** `/wic-attendance`
**File:** `src/pages/WicAttendance.tsx`

Live attendance tracking from the ShiftKiosk terminals. Shows which agents have physically checked in at WIC locations.

**Main API calls:**
- `GET {VITE_KIOSK_API_URL}/api/attendance` — calls the **ShiftKiosk** server (FastAPI, port 8000), not the GSDDashboard backend. URL configured via `VITE_KIOSK_API_URL` environment variable.
- `GET /api/wic/cards?date=today` — for the scheduled/expected agents list

**Key components:**
- `CoverageBadge` — shows COVERED/PARTIAL/UNCOVERED
- `NppBadge` — for NPP locations
- Live pulse animation (`.pulse-live` / `--signal-live`) on agents currently checked in
- `ManualCheckinModal` — allows manual check-in when kiosk is unavailable
- `AssignAgentModal` — quick assignment from this screen
- `Sheet` drawer for location detail

**Notable behaviour:**
- Match between kiosk records and GSDDashboard employees is by `employee_id` field, **never** by location name.
- Zone B kiosk terminals are not yet deployed. The UI is ready but the hardware is pending. Until kiosks are active, data will only show manual check-ins.
- `VITE_KIOSK_API_URL` is set in `Frontend/.env`. If the devtunnel is recreated, this value must be updated.

---

## 7. WIC Schedule

**Route:** `/wic-schedule`
**File:** `src/pages/WicSchedule.tsx`

Multi-view WIC schedule table: 14-day, weekly, and available-hours views.

**Main API calls:**
- `GET /api/wic/` — WIC shift entries for the date range
- `GET /api/wic/availableHours?from=...&to=...` — agents with time remaining after WIC close
- `GET /api/wic/open?date=...&horizon=3` — open/closed status per location

**Key components:**
- `DayCell` sub-component colours cells by shift status (PH, AL, SL, OFF, on-site, working)
- Team lead colour coding (six hardcoded colours mapped by TL name)
- Available hours CSV download at `GET /api/wic/availableHours/download`

**Notable behaviour:**
- `TL_COLORS` is hardcoded in the component for the six active team leads. Adding a new TL requires a code change.

---

## 8. Pipeline

**Route:** `/pipeline`
**File:** `src/pages/Pipeline.tsx`

WIC pipeline event management — special activities (meter readings, campaigns, etc.) requiring extra agents.

**Main API calls:**
- `GET /api/pipeline` → `WicPipelineItem[]`
- `POST /api/pipeline` — create event
- `PUT /api/pipeline/{id}` — update event
- `DELETE /api/pipeline/{id}` — delete event

**Key components:**
- `EventModal` — create/edit form
- `Badge` component using `STATUS_COLORS` and `HANDLING_COLORS` from token values
- Location dropdown populated from `GET /api/wic/locations`

---

## 9. Training

**Route:** `/training`
**File:** `src/pages/Training.tsx`

Training session scheduler. Three tabs: Scheduler, Sessions, Topics.

**Main API calls:**
- `GET /api/training/topics` → `TrainingTopic[]`
- `GET /api/training/sessions` → `TrainingSession[]`
- `POST /api/training/topics` — create topic
- `POST /api/training/sessions` — schedule session
- `GET /api/employees` — for agent selection

**Key components:**
- `TopicModal` — new topic form (mandatory/not mandatory toggle uses status token colours)
- Session list with status badges

**Notable behaviour:**
- `AgentIds` on TrainingSession is a comma-separated string, not a join table. Client must split on comma to display names.

---

## 10. WIC Locations

**Route:** `/wic`
**File:** `src/pages/WicLocations.tsx`

Read-only reference table of all 43 WIC locations.

**Main API calls:**
- `GET /api/wic/locations` → location list with opening schedule strings

**Key components:**
- Country filter (DE/NL)
- Search by display name or city
- `NppBadge` for NPP locations
- Stat tiles for Total/DE/NL counts

---

## 11. Attendance

**Route:** `/attendance`
**File:** `src/pages/Attendance.tsx`

Historical DailyAttendance records.

**Main API calls:**
- `GET /api/attendance?from=...&to=...&country=...` → `DailyAttendance[]`
- `GET /api/attendance/download/today` — Excel download
- `GET /api/attendance/download/7d` — 7-day Excel
- `GET /api/attendance/download/30d` — 30-day Excel

**Key components:**
- `DownloadButtons` — three download buttons
- Country filter (DE/NL/CZ)
- Status badge colours: ASSIGNED=good, WO=warn, CLOSED=neutralst, PH=holiday

---

## 12. Sick Leave

**Route:** `/sickleave`
**File:** `src/pages/SickLeave.tsx`

Sick leave registry. View, create, patch, and delete sick leave records.

**Main API calls:**
- `GET /api/sickleave?from=...&to=...&teamLead=...&type=...&activeOnly=...` → `SickLeaveDto[]`
- `GET /api/sickleave/stats` → `SickLeaveStatsDto`
- `POST /api/sickleave` — create
- `PATCH /api/sickleave/{id}` — update end date, type, notes
- `DELETE /api/sickleave/{id}` — delete
- `GET /api/sickleave/download` — Excel export

**Key components:**
- `CommentCell` — inline editable note field
- `DownloadButtons` (if present)
- Stats panel: total active, average duration, by team lead breakdown

**Notable behaviour:**
- Sentinel `LastDay = 2099-12-31` displays as "Open" in the UI
- Langzeitkrank badge shown for records ≥ 21 calendar days
- On create, backend writes SL ShiftEntries via `ShiftSyncService` (weekdays only, capped at 90 days)

---

## 13. Vacations

**Route:** `/vacations`
**File:** `src/pages/Vacations.tsx`

Annual leave bookings. Includes a bulk-import panel for pasting text-format vacation lists.

**Main API calls:**
- `GET /api/vacations?from=...&to=...&year=...` → `VacationDto[]`
- `GET /api/vacations/availability?from=...&to=...&maxLeave=8` → `DailyLeaveCountDto[]`
- `POST /api/vacations` — create
- `DELETE /api/vacations/{id}` — delete
- `GET /api/vacations/download` — Excel export

**Key components:**
- `BulkImportPanel` — pastes lines like "Nguyen Tim 28.07.26 - 31.07.26" and fuzzy-resolves to employees
- `ALHistoryDrawer` — shows an agent's vacation history
- `AddVacationModal` — used from this page and from the Shifts page

**Notable behaviour:**
- Bulk import parser `parseVacLine()` handles European date format (dd.mm.yy or dd.mm.yyyy) and em/en-dash separators
- Employee resolution uses fuzzy name matching (`resolveEmployee` utility)
- Backend rejects any vacation that overlaps an existing sick leave for the same employee

---

## 14. AL Balance

**Route:** `/albalance`
**File:** `src/pages/ALBalance.tsx`

Annual leave balance overview for all agents.

**Main API calls:**
- `GET /api/albalance` → `ALBalanceDto[]`
- `PATCH /api/employees/{employeeId}/albalance` — manual adjustment of `alUsed`

**Key components:**
- Stat tiles: Total Employees, Negative (remainingAL < 0), Critical (0–5 days), Warning (6–10 days)
- Inline edit for eligible days
- `ALHistoryDrawer` (from Vacations.tsx)

---

## 15. AL Calendar

**Route:** `/alcalendar`
**File:** `src/pages/ALCalendar.tsx`

Calendar view of annual leave across all agents. Three views: 14-day, 7-day, 3-month calendar.

**Main API calls:**
- `GET /api/vacations?from=...&to=...` → vacation rows
- `GET /api/vacations/availability?from=...&to=...` → `DailyLeaveCountDto[]`

**Key components:**
- `LeaveAvailabilityBar` — visual bar showing daily leave utilisation
- Month calendar grid (3-month view)
- Team lead colour coding using the same `TL_COLORS` dictionary as WicSchedule

---

## 16. Employees

**Route:** `/employees`
**File:** `src/pages/Employees.tsx`

Employee directory with create/edit/delete.

**Main API calls:**
- `GET /api/employees` → `Employee[]`
- `POST /api/employees` — create
- `PUT /api/employees/{id}` — update
- `DELETE /api/employees/{id}` — delete

**Key components:**
- `EmployeeModal` — full create/edit form with role, team lead, Bundesland, shift pattern, NPP qualified, etc.
- Engagement badge (Full Time/Part-Time/Student)
- Role badge and shift pattern badge

**Notable behaviour:**
- `TEAM_LEADS`, `ROLES`, `ENGAGEMENTS`, `BUNDESLAENDER` are hardcoded arrays in the component. Changing them requires a code change.

---

## 17. WIC Assignments

**Route:** `/wic-coverage`
**File:** `src/pages/WicCoverage.tsx`

Static assignment directory for WIC agents. Shows each WIC location's Main, BackupA, BackupB, and BackupC agent assignments. Supports patching agent attributes (HasCar, GroupRegion) and pinning a BackupB agent to a formal BACKUP assignment. This is not a daily coverage status page — it shows the standing assignment structure only.

> **Display name changed 2026-09-03:** "WIC Coverage" → "WIC Assignments" (EN) / "WIC-Zuweisungen" (DE). Route `/wic-coverage` is intentionally preserved.

**Main API calls:**
- `GET /api/wic/forecast?horizon=28` → `ForecastResponse`
- `GET /api/wic/cards?date=today` → `WicCardDto2[]`
- `GET /api/wic-coverage` → per-location agent tier DTOs
- `GET /api/wic/substitutes?locationCode=...&date=...&horizon=1&absentIds=...` → `SubstitutionResponse`

**Key components:**
- `Sheet` drawer for location detail
- `NppBadge`
- `CoverageBadge` — COVERED/PARTIAL/UNCOVERED/CLOSED
- `ALPlanningModal` — plan agent's AL within the coverage context
- `AssignAgentModal` — assign agent to a WIC from the coverage view
- `DataTable` — reusable sortable table

**Notable behaviour:**
- What-If simulation: pass `absentIds` query param to the substitutes endpoint to simulate absences
- Clicking a PARTIAL or UNCOVERED day opens the substitution panel

---

## 18. WIC Annual Leave

**Route:** `/wic-al`
**File:** `src/pages/WicAnnualLeave.tsx`

Shows AL records for WIC agents only (agents who have a MAIN assignment in `WicAgentAssignments`).

**Main API calls:**
- `GET /api/wic-coverage` → WIC agents list
- `GET /api/vacations` → vacation records filtered to WIC agents

**Key components:**
- Joined client-side: fetches all WIC agents then filters vacations to those agents
- `cleanLocation()` — strips `DE_` / `NL_` prefix from location codes for display

---

## 19. Pulse Assistant

**Route:** `/assistant` (also `/wic-assistant` redirects here)
**File:** `src/pages/WicAssistant.tsx`

Chat interface for the internal AI assistant. No external LLM — all responses come from the backend domain handlers.

**Main API calls:**
- `POST /api/assistant/ask` with `{ question: string }` → `AssistantResponse`

**Key components:**
- `ChatPanel` (from `src/components/WicChatWidget.tsx`) — message history, input, send button
- `useAssistantAsk` hook — wraps the mutation
- Welcome message with example questions

**Notable behaviour:**
- Assistant accepts English and German
- A disambiguation message is returned when two handlers score equally
- `data.hint` field (optional) shows a follow-up suggestion
- `data.table` field (optional) renders an `AssistantTableRow[]` inline in the response bubble

---

## 20. BO List (Back-Office List)

**Route:** `/bo-list`
**File:** `src/pages/BoList.tsx`

Daily list of agents working in the GSD back-office (as opposed to WIC travel).

**Main API calls:**
- `GET /api/bo-list?date=...` → `BoEntry[]`
- `POST /api/bo-list` — create entry
- `PUT /api/bo-list/{id}` — update shift times or note
- `DELETE /api/bo-list/{id}` — delete entry

**Key components:**
- Add/edit inline form
- Drag-and-drop sort order (SortOrder field)

---

## 21. Bulk RTM (Return to Main)

**Route:** `/bulk-rtm`
**File:** `src/pages/BulkRtm.tsx`

Paste-in bulk shift management tool. Parses free-text blocks with headers like "AL", "SL", "OFF", "CD", "NIGHT", "BO", "WIC" and applies the corresponding shift changes for the current date.

**Main API calls:**
- `GET /api/employees?active=true` → employee list for name resolution
- `POST /api/bulk-rtm` — submit the parsed batch
- `GET /api/wic/locations` → for WIC location assignment block
- Various per-entry endpoints depending on parsed block type

**Key components:**
- `resolveEmployee` utility — fuzzy name matching with `BaseRowStatus` and `MatchType`
- Per-header colour coding using token values (AL=info-fg, SL=crit-fg, OFF=neutralst-fg, etc.)
- Preview grid before committing

**Notable behaviour:**
- Valid headers: `AL`, `SL`, `OFF`, `CD`, `NIGHT`, `BO`, `WIC`
- Each header block replaces the corresponding shift type for all agents listed under it
- The `BO` header replaces today's BO List entries entirely

---

## Sub-components / Modals

The following are not standalone routes but are used across multiple pages:

| File | Description |
|---|---|
| `ALPlanningModal.tsx` | Modal for planning AL within a coverage context |
| `AssignAgentModal.tsx` | Modal for assigning an agent to a WIC for a specific date |
| `ManualCheckinModal.tsx` | Modal for manually checking in an agent when kiosk is unavailable |
| `overview/DayStrip.tsx` | 28-day horizontal forecast strip used in Overview |
| `overview/ExceptionList.tsx` | Today's absences and gaps from briefing data |

---

## Shared UI Components (`src/components/`)

| Component | Description |
|---|---|
| `Button.tsx` | Primary/secondary/ghost button variants using action tokens |
| `CommandPalette.tsx` | Global search palette (z-index: palette=90) |
| `CoverageBadge.tsx` | COVERED/PARTIAL/UNCOVERED/CLOSED status badge |
| `DataTable.tsx` | Reusable sortable table with `DataColumn[]` definition |
| `DownloadButtons.tsx` | Today/7-day/30-day download button group |
| `EmptyState.tsx` | Empty state placeholder with optional icon |
| `NppBadge.tsx` | Nuclear Power Plant site badge |
| `Panel.tsx` | Card/panel container with optional header |
| `Sheet.tsx` | Slide-in side drawer (z-index: drawer=60) |
| `ShiftBadge.tsx` | ShiftType badge using shift colour tokens |
| `ShiftTypeCell.tsx` | Shift cell for the grid view |
| `StatusBadge.tsx` | Generic status badge |
| `ThemeToggle.tsx` | Light/dark theme toggle (uses `next-themes`) |
| `WicChatWidget.tsx` | Chat panel and `useAssistantAsk` hook |
