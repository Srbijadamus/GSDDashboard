# WorkForce Pulse + ShiftKiosk — Full Exhaustive Redesign Specification

> **Purpose:** This document is a complete, code-free specification for a redesigner.
> Every screen, state, interaction, color, API call, validation, and edge case is
> documented here. A redesigner must never need to open the source files.
>
> **Status:** Covers GSDDashboard (React 19 + .NET 8) and ShiftKiosk (Python/Tkinter).
> Sections flagged ⚠ PARTIALLY DOCUMENTED indicate files that were not fully read;
> the redesigner should treat those sections as incomplete outlines.

---

## Table of Contents

### A. Shell & Navigation
- [A1. Application Shell (App.tsx)](#a1-application-shell)

### B. Screen-by-Screen Specification
- [B01. Overview (`/`)](#b01-overview)
- [B02. Shifts (`/shifts`)](#b02-shifts)
- [B03. WIC Shifts (`/wic-shifts`)](#b03-wic-shifts)
- [B04. VWIC (`/vwic`)](#b04-vwic)
- [B05. Break Planner (`/breaks`)](#b05-break-planner)
- [B06. WIC Attendance (`/wic-attendance`)](#b06-wic-attendance)
- [B07. WIC Schedule (`/wic-schedule`) ⚠](#b07-wic-schedule)
- [B08. Pipeline (`/pipeline`)](#b08-pipeline)
- [B09. Training (`/training`)](#b09-training)
- [B10. WIC Locations (`/wic`)](#b10-wic-locations)
- [B11. Attendance / Daily Attendance (`/attendance`) ⚠](#b11-attendance)
- [B12. Sick Leave (`/sickleave`)](#b12-sick-leave)
- [B13. Vacations (`/vacations`)](#b13-vacations)
- [B14. AL Balance (`/albalance`)](#b14-al-balance)
- [B15. AL Calendar (`/alcalendar`)](#b15-al-calendar)
- [B16. Employees (`/employees`)](#b16-employees)
- [B17. WIC Assignments (`/wic-coverage`)](#b17-wic-assignments)
- [B18. WIC Annual Leave (`/wic-al`)](#b18-wic-annual-leave)
- [B19. Pulse Assistant (`/assistant`)](#b19-pulse-assistant)
- [B20. BO Liste (`/bo-list`)](#b20-bo-liste)
- [B21. Bulk RTM Entry (`/bulk-rtm`)](#b21-bulk-rtm-entry)

### C. Modals & Shared Components
- [C1. Assign Agent Modal](#c1-assign-agent-modal)
- [C2. AL Planning Modal (inside WIC Attendance)](#c2-al-planning-modal)
- [C3. Manual Check-in Modal (inside WIC Attendance)](#c3-manual-check-in-modal)
- [C4. Substitute Sheet (inside Overview & WIC Attendance)](#c4-substitute-sheet)
- [C5. Command Palette (Ctrl+K)](#c5-command-palette)
- [C6. WIC Chat Widget (floating)](#c6-wic-chat-widget)

### D. ShiftKiosk Application
- [D1. Phase 1 — Fullscreen Startup](#d1-phase-1-fullscreen-startup)
- [D2. Phase 2 — Floating Widget](#d2-phase-2-floating-widget)
- [D3. Location Selection Dialog](#d3-location-selection-dialog)
- [D4. Auto-logout Overlay](#d4-auto-logout-overlay)
- [D5. Offline Queue](#d5-offline-queue)

### E. Data & Meaning
- [E1. Status Enums & Colors](#e1-status-enums--colors)
- [E2. Shift Types](#e2-shift-types)
- [E3. Date/Time Formats](#e3-datetime-formats)
- [E4. Sick Leave Sentinel](#e4-sick-leave-sentinel)
- [E5. CSS Variables (Light & Dark)](#e5-css-variables)
- [E6. i18n & Localization](#e6-i18n--localization)

### F. End-to-End User Flows
- [F1. Kiosk Check-In Flow](#f1-kiosk-check-in-flow)
- [F2. Creating Sick Leave (Dashboard)](#f2-creating-sick-leave)
- [F3. Assigning a WIC Substitute](#f3-assigning-a-wic-substitute)
- [F4. Scheduling a Training Session](#f4-scheduling-a-training-session)
- [F5. Bulk RTM Paste & Save](#f5-bulk-rtm-paste--save)
- [F6. Shift Swap Flow (3-step)](#f6-shift-swap-flow)

### G. Failure, Edge Cases & Validation
- [G1. API Failures](#g1-api-failures)
- [G2. Validation Rules](#g2-validation-rules)
- [G3. Empty States (every screen)](#g3-empty-states)
- [G4. Conflict & Overlap Handling](#g4-conflict--overlap-handling)
- [G5. ShiftKiosk Offline Behavior](#g5-shiftkiosk-offline-behavior)

### H. Hard Constraints for Redesigners
- [H1. Styling Architecture](#h1-styling-architecture)
- [H2. ShiftKiosk Platform Limits](#h2-shiftkiosk-platform-limits)
- [H3. CRITICAL: main_kiosk.py File Duplication Warning](#h3-critical-file-duplication-warning)
- [H4. Build Pipeline](#h4-build-pipeline)
- [H5. WIC Agent IDs — Never Re-Insert](#h5-wic-agent-ids)

### I. Screen Inventory Checklist
- [I1. Complete Screen List](#i1-complete-screen-list)

---

---

## A1. Application Shell

**File:** `Frontend/src/App.tsx`  
**Route:** All routes share this shell — it is never unmounted.

### Layout

```
┌──────────────────────────────────────────────────────────────────┐
│ SIDEBAR (200px fixed, var(--sidebar) bg)                         │
│  ┌ Logo chip "GSD" (accent bg, white text, IBM Plex Mono)        │
│  │ "EON GSD Dashboard" label (12px bold) [pre-redesign]          │
│  ├─────────────────────────────────────────────────────────────  │
│  │ nav items (scroll if overflow)                                 │
│  │  • Active item: accent color text, left 2px accent border,    │
│  │    rgba(59,126,255,.08) bg                                     │
│  │  • Inactive item: text2 color, transparent bg                 │
│  │  • Each item: Lucide icon (14px) + label text (12px)          │
│  └─────────────────────────────────────────────────────────────  │
├──────────────────────────────────────────────────────────────────│
│ TOPBAR (padding 10px 20px, sidebar bg, border-bottom)            │
│  Left: "EON GSD Dashboard" (13px bold) [pre-redesign]                           │
│  Right (flex row, gap 10):                                        │
│    • Today's date (de-DE format: DD.MM.YYYY, 11px mono, text3)   │
│    • Horizon toggle "7T" / "14T" — ONLY shown on Overview (/)    │
│      Active: accent bg + border; Inactive: card2 bg              │
│    • Ctrl+K button (Search icon + kbd "Ctrl+K")                  │
│    • Lang toggle: Globe icon + current opposite lang "DE"/"EN"   │
│    • Theme toggle (ThemeToggle component)                         │
├──────────────────────────────────────────────────────────────────│
│ MAIN CONTENT (flex:1, overflow-y:auto, padding: 20px)            │
│  Renders the matched <Route> component                            │
└──────────────────────────────────────────────────────────────────┘
```

### Complete Navigation Items (in order)

| # | Label (EN) | Label (DE) | Route | Lucide Icon |
|---|-----------|-----------|-------|-------------|
| 1 | Overview | Übersicht | `/` | LayoutDashboard |
| 2 | Shifts | Schichtplan | `/shifts` | ClipboardList |
| 3 | WIC Shifts | WIC-Schichten | `/wic-shifts` | Building2 |
| 4 | VWIC | VWIC | `/vwic` | Headphones |
| 5 | Break Planner | Break Planner | `/breaks` | Coffee |
| 6 | WIC Attendance | WIC Attendance | `/wic-attendance` | Calendar |
| 7 | WIC Schedule | WIC Schedule | `/wic-schedule` | MapPin |
| 8 | Pipeline | Pipeline | `/pipeline` | ClipboardList |
| 9 | Training | Training | `/training` | Users |
| 10 | WIC Locations | WIC-Standorte | `/wic` | MapPin |
| 11 | Daily Attendance | Tägliche Anwesenheit | `/attendance` | Calendar |
| 12 | Sick Leave | Krankmeldung | `/sickleave` | Heart |
| 13 | Vacations | Urlaub | `/vacations` | Calendar |
| 14 | AL Balance | Urlaubssaldo | `/albalance` | Scale |
| 15 | AL Calendar | AL Calendar | `/alcalendar` | Calendar |
| 16 | Employees | Mitarbeiter | `/employees` | Users |
| 17 | WIC Assignments | WIC-Zuweisungen | `/wic-coverage` | ShieldCheck |
| 18 | WIC Annual Leave | WIC Annual Leave | `/wic-al` | CalendarOff |
| 19 | Pulse Assistant | Pulse Assistant | `/assistant` | Bot |
| 20 | BO Liste | BO Liste | `/bo-list` | ListChecks |
| 21 | Bulk RTM Entry | Bulk RTM Entry | `/bulk-rtm` | FileText |

Redirect: `/wic-assistant` → `/assistant` (permanent).

### Global Overlays

- **CommandPalette** — always mounted, shown/hidden by `cmdOpen` state. Triggered by Ctrl+K or topbar button.
- **WicChatWidget** — always mounted floating widget (bottom-right). Independent of routing.

### Theming

Lang toggle calls `i18n.changeLanguage("de" | "en")`. Theme toggle (ThemeToggle component, not shown above) toggles the `.dark` class on `<html>`. Current language is detected from `i18n.language`.


---

## B01. Overview

**File:** `Frontend/src/pages/Overview.tsx` (937 lines)  
**Route:** `/`  
**Purpose:** Command-center showing real-time WIC coverage across all locations for the next 7 or 14 days (horizon toggled in topbar).

### Layout (top-to-bottom, all stacked vertically, gap 16)

```
[KPI Row — 5 cards]
[Coverage Heatmap — horizontal scroll table]
[WIC Map — Leaflet full-width]
[Recommendations — gap list]
[Absence Feed — today's absences]
[Risk Radar — next N days bar chart]
[Team Lead Summary — 6-column grid]
[WIC Detail Grid — collapsible, lazy-loaded]
```

### KPI Row (5 cards, equal-width grid)

| Card | Label (EN) | Label (DE) | Color | Clickable? |
|------|-----------|-----------|-------|-----------|
| Open Today | `overview.kpi.openToday` | Heute offen | `var(--accent)` | No |
| At-Risk Today | `overview.kpi.atRiskToday` | Heute gefährdet | `var(--warn)` | No |
| Closure Risk | `overview.kpi.closureRisk` | Schließrisiko | `var(--danger)` | No |
| Absent Today | `overview.kpi.absentToday` | Heute abwesend | `#facc15` (yellow) | No |
| Coverage % | `overview.kpi.coveragePct` | Belegung % | `var(--green)` | No |

Card structure: 8px border-radius, `var(--card)` bg, `1px solid var(--border)`, padding 16px 20px. Top label: 10px uppercase monospaced text3. Value: 28px bold IBM Plex Mono in the card's accent color.

**Data source:** `GET /api/wic/cards?date=<today>` (cards KPIs), `GET /api/wic/briefing` (absence count, at-risk).

### Coverage Heatmap

**Label:** "Belegungsübersicht" (DE) / `overview.heatmap.title` (EN)  
**Layout:** Horizontally scrollable table. Sticky first column (location name, 140px min-width). One column per day in the horizon. Each cell is 36×36px min.

**Cell behavior:**

| Status | Hex color | Border | Clickable? | Action |
|--------|-----------|--------|-----------|--------|
| COVERED | `#22d07a` | darker green | **No** | Nothing — no cursor affordance |
| PARTIAL | `#ff7c3b` | darker orange | **Yes** | Opens SubstituteDrawer Sheet for that location+date |
| UNCOVERED | `#ff3b5c` | darker red | **Yes** | Opens SubstituteDrawer Sheet |
| CLOSED | `#7a8fa8` | grey-blue | **No** | Nothing |

Header row: location names. Column headers: day abbreviation + DD.MM.

**Data source:** `GET /api/wic/forecast?horizon=N` (N = 7 or 14 from URL param).

**Empty state:** Table renders but all cells show CLOSED color if no locations configured.  
**Loading state:** Skeleton rows (`.skeleton` animated class) — 5 rows × N columns.

### WIC Map (Leaflet)

**Label:** `overview.map.title` / "WIC Standortkarte"  
**When coordinates missing:** Static warning box, text: `overview.map.noCoordinates` / "Koordinaten noch nicht geocodiert — Karte nicht verfügbar. Lat,Lon in WicLocations.Coordinates eintragen."  
**When present:** Leaflet map. Markers colored by coverage status. Clicking a marker opens location detail.

### Recommendations

**Label:** `overview.recommendations.title` / "Lücken & Empfehlungen"  
**Content:** List of uncovered/partial locations with best-fit substitute suggestion.  
Each row: location name, status badge, "Beste Vertretung" label + agent name.  
**Data source:** `GET /api/wic/briefing`

### Absence Feed

**Label:** `overview.absences.title` / "Heute abwesend"  
**Note:** Groups by team lead. If API doesn't return team lead grouping, shows flat list with note: `overview.absences.noTeamLead`.

**Absence type chips (colors):**

| Type | Background | Text color |
|------|-----------|-----------|
| SL | `rgba(255,59,92,.15)` | `var(--danger)` |
| AL | `rgba(59,126,255,.15)` | `var(--accent)` |
| HALF_AL | `rgba(59,126,255,.08)` | `var(--accent)` lighter |
| UL | `rgba(100,116,139,.15)` | `var(--text3)` |
| CD | `rgba(100,116,139,.15)` | `var(--text3)` |
| PH | `rgba(250,204,21,.15)` | `var(--yellow)` |
| LPH | `rgba(250,204,21,.10)` | `var(--yellow)` lighter |

**Sentinel detection:** If `ab.lastDay >= "2099-01-01"` → display "open (N days so far)" in warn color instead of end date.

### Risk Radar

**Label:** `overview.radar.title` / "Risikoradar (nächste {{horizon}} Tage)"  
**Content:** Bar chart or list of days with the highest risk. Days with ≥1 uncovered location highlighted in danger color.

### Team Lead Summary

**Label:** `overview.teamLeads` / "Teamleiter-Übersicht"  
**Layout:** 6-column grid (one column per team lead).  
**Data source:** `GET /api/dashboard/teamlead-summary?date=<today>`  
Each column shows: TL name, count of agents on each status (WORKING, AL, SL, WIC_DUTY, etc.).

### WIC Detail Grid

**Label:** `overview.detailGrid` / "WIC Detailansicht"  
**Behavior:** Collapsible section. On expand, lazy-loads `GET /api/wic/cards?date=<today>`. Shows a compact card per WIC location: name, city, coverage status badge, min required vs present counts.

### SubstituteDrawer (Sheet)

Triggered by clicking PARTIAL or UNCOVERED heatmap cell.  
**Content:** Ordered list of substitute candidates.

Each candidate row:
- Rank number (#1, #2…)
- Full name (bold)
- Source type badge:
  - `BACKUP` → `rgba(167,139,250,.15)` bg / `var(--purple)` text
  - `SSP` → `rgba(59,126,255,.15)` bg / `var(--accent)` text
  - `WIC_DONOR` → `rgba(0,210,160,.15)` bg / `var(--accent2)` text
  - `CALL_IN` → `rgba(255,124,59,.15)` bg / `var(--warn)` text
  - `REGIONAL` → `rgba(0,190,255,.15)` bg / `#22d3ee` text — `StatusBadge tone="wic" variant="outline"`
- Home location name
- Distance (km)
- Load score: "Nx last 30 days" (`substitute.lastUsed` i18n key: `"{{n}}x last 30 days"`)
- "Accept" button (`var(--accent)` bg)

**Accept action:** `POST /api/wic/substitutes/accept` → on success, heatmap cell updates. Toast/banner: `substitute.confirmed` i18n key: `"Confirmed — {{name}} assigned to {{wic}}"`.

**Data source:** `GET /api/wic/substitutes?locationCode=&date=&horizon=1`


---

## B02. Shifts

**File:** `Frontend/src/pages/Shifts.tsx` (852 lines)  
**Route:** `/shifts`  
**Purpose:** Multi-week shift plan grid — assign/delete/swap shift types per agent per day; drag-to-reorder rows.

### Layout

```
[Filter bar — search, team lead, role, type, date range]
[CoverageBar component — summary bar above table]
[Shift table — sticky agent column, date columns, horizontal scroll]
[Footer — "{N} agents · {N} days"]
```

### Filter Bar (all controls in one row, wraps on narrow)

| Control | Type | Placeholder / Default |
|---------|------|-----------------------|
| Agent search | text input | Name or ID |
| Team Lead | text input | free text |
| Role | select | All / Voice / SSP / Chat / Dispatcher / WIC |
| Type | select | All / Full Time / Part-Time / Student |
| 7d / 14d / 30d | toggle buttons | 14d default |
| Date picker | date input | today |
| "Today" reset | button | resets date to today |
| "+ Add AL" | button (header, right side) | Opens `AddVacationModal` pre-filled with today's date; shortcut to enter AL from the shift plan view (`Shifts.tsx` F1) |

### Shift Table

**Columns (left to right):**
1. Drag handle — shows ▲/⠿/▼ icons, activates on hover; HTML5 drag-and-drop; persisted via `PATCH /api/shiftplan/reorder`
2. Name — sticky left column, bold; clicking opens `/employees` page (via context menu)
3. Role — colored text. On absence days (AL, HALF_AL, SL, UL), rendered with `text-ink-disabled opacity-50` — visually muted because Role is a permanent employee property, not a daily status.
4. Task — colored badge (WIC=blue-light, Voice=green, Backlog=orange; WIC without location = danger red + "⚠ WIC"). On absence days, wrapped in `pointer-events-none` to prevent interaction — the badge is still visible but not clickable.
5. One column per date — each cell is a ShiftCell

**Context menu (⋮ button per row):**
- "Edit agent" → navigates to `/employees`
- "Remove from plan" → no-op (UI only, no API call)
- "Delete agent" → navigates to `/employees`

### ShiftCell

**Default state:** Shows colored shift type label.  
**Click:** Opens dropdown listing all 15 shift types.  
**After selecting WORKING or WIC_DUTY:** Shows time edit row with Start time input, End time input, "Save Time" button + Delete button + Swap button.  
**For other types:** Shows only Delete button.

### Shift Type Color Map (15 types)

| Type | Color / Style |
|------|--------------|
| WORKING | `var(--green)` |
| WIC_DUTY | `var(--blue-light)` |
| AL | `var(--accent)` blue |
| HALF_AL | lighter blue (`rgba(59,126,255,.6)`) |
| SL | `var(--warn)` orange |
| UL | `var(--danger)` red |
| TRAINING | `var(--purple)` |
| OFF | grey `#8892a4` |
| OFF_WEEKEND | dark grey |
| PH | `var(--yellow)` |
| LPH | lighter yellow |
| CD | near-transparent tint |
| CO | near-transparent tint |
| OL | near-transparent tint |
| RESIGNED | muted grey |

### Swap Flow (3 steps, inline in ShiftCell)

**Step 1 — Loading:** Calls `GET /api/shifts/{id}/wic-entries`. Shows spinner.

**Step 2 — Multi-location warning (conditional):** If agent covers >1 location on that date, shows yellow warning dialog:
- DE: "Dieser Agent deckt am {date} N Standorte ab"
- EN: "This agent covers N locations on {date}"
User must confirm to continue.

**Step 3 — Pick replacement:** Search input + scrollable list (max 20 employees from `GET /api/employees`). User selects replacement. Confirm button calls `POST /api/shifts/swap`.

### OverrideConfirmModal

Shown when user assigns OFF_WEEKEND or PH (system-auto types).  
Yellow border (`rgba(255,204,21,.3)`), warning text that this type is auto-set by system.  
Buttons: "Cancel" / "Override Anyway".

### LegalViolationModal

Shown when a shift assignment triggers a labor law violation.  
- Red border for hard blocks (`isHardBlock=true`); yellow border for soft warnings.
- Per-violation cards: rule name (IBM Plex Mono), law reference text, description.
- **Hard block:** Only "Cancel" button — cannot save.
- **Soft warning:** "Cancel" + "Save Anyway" buttons.

### APIs

| Action | Method | Endpoint |
|--------|--------|----------|
| Load shifts | GET | `/api/shifts?from=&to=` |
| Load employees | GET | `/api/employees` |
| Assign shift type | POST | `/api/shifts/assign` |
| Update shift times | PATCH | `/api/shifts/{id}/times` |
| Delete shift | DELETE | `/api/shifts/{id}` |
| Load swap WIC entries | GET | `/api/shifts/{id}/wic-entries` |
| Execute swap | POST | `/api/shifts/swap` |
| Reorder rows | PATCH | `/api/shiftplan/reorder` |

### Visual States

| State | Appearance |
|-------|-----------|
| Loading | Skeleton rows |
| Empty | "No agents in shift plan" centered text |
| Filtered to zero | Empty tbody with filter hints |
| Drag active | Row opacity 0.5, drop targets show outline |
| Cell editing | Inline time inputs replace label |
| Legal violation (hard) | Red border modal, save blocked |
| Legal violation (soft) | Yellow border modal, can override |


---

## B03. WIC Shifts

**File:** `Frontend/src/pages/WICShifts/index.jsx` (JSX, not TSX)  
**Sub-components:** `LocationCard.jsx`, `AgentRow.jsx`, `UncoveredBanner.jsx`, `MultiSelectFilter.jsx`, `ReassignModal.jsx`, `AvailableHoursPanel.jsx`, `NewShiftModal.jsx`  
**Route:** `/wic-shifts`  
**Purpose:** Live view of today's WIC location coverage — shows which agents are assigned to each center. Auto-refreshes every 30 seconds.

### Layout

```
[Header row: title "WIC-Schichten" + stats badges + "New Shift" button]
[UncoveredBanner — shown when any location is uncovered]
[MultiSelectFilter — filter by location]
[LocationCard grid — one card per open location]
```

### Header

- Title: `t("nav.wicShifts")` / "WIC-Schichten"
- Stats badges (live): covered count (green), partial count (orange), uncovered count (red)
- "Neue Schicht" button (opens NewShiftModal)
- Last-refreshed indicator: "{N} seconds ago" — updates every 1 second
- Connection lost indicator: shown if fetch fails (red dot + "Verbindung verloren")

### UncoveredBanner

Shown when `uncovered > 0`. Red banner with list of uncovered location names. Dismissible.

### MultiSelectFilter

Pill-based multi-select filter. Each location is a selectable pill. "All" chip deselects all. Filtered locations are stored in `selectedLocs` state.

### LocationCard

One card per open WIC location (closed locations filtered out from API response).

**Card header:**
- Location name (bold)
- NppBadge if `isNpp=true` (red/orange "NPP" pill)
- Status badge: covered / partial / uncovered
- City, Country

**Agent list (AgentRow per agent):**
- Agent name
- Role badge: "primary" (green) or "backup" (blue)
- Shift time or special status label (AL/SL/Training/OFF/PH)
- Drag-and-drop: agents can be dragged between location cards (reassignment)

**Drag behavior:** `dragAgent` state tracks currently dragged agent. Dropping on a different location card triggers ReassignModal.

### ReassignModal

Shown after drag-drop across locations. Confirms reassignment of agent to target location. Calls `PATCH /api/wic/assignments/{id}`.

### NewShiftModal

i18n keys under `wicShifts.newShift.*`:
- Title: "Neue Schicht erstellen"
- Employee dropdown: "Mitarbeiter auswählen..."
- Date picker
- Shift type select
- Start time / End time inputs
- WIC location select: "Kein / Kein WIC"
- Buttons: "Abbrechen" / "Schicht erstellen" (→ `POST /api/wic/assignments`)
- Success toast: "Schicht für {{name}} erstellt"

### AvailableHoursPanel

Side panel or expansion showing which agents have available hours (not yet assigned to full shifts) on the selected date.

### APIs

| Action | Endpoint |
|--------|---------|
| Load location cards | `GET /api/wic/cards?date=<today>` |
| Load WIC shifts | `GET /api/wic?from=<today>&to=<today>` |
| Create assignment | `POST /api/wic/assignments` |
| Reassign | `PATCH /api/wic/assignments/{id}` |

### Auto-Refresh

- `setInterval(fetchData, 30000)` — fetches both `/api/wic/cards` and `/api/wic` in parallel every 30 seconds.
- `setInterval(() => setSecondsAgo(s => s + 1), 1000)` — increments the "N seconds ago" counter.
- Both intervals cleared on component unmount.

### Visual States

| State | Appearance |
|-------|-----------|
| Loading (initial) | No explicit skeleton — blank until first fetch |
| Connection lost | `connectionLost=true` → red indicator in header |
| Polling in progress | `polling=true` → subtle indicator |
| No locations | Empty grid |
| All covered | No UncoveredBanner |
| Agent drag active | Card highlight on valid drop target |


---

## B04. VWIC

**File:** `Frontend/src/pages/VWICPage.tsx` (1343 lines)  
**Route:** `/vwic`  
**Purpose:** Virtual Walk-In Center — 24/7 staffing coverage dashboard and rotation planner for Voice agents doing virtual customer-facing duty.

### Header

- Title: "VWIC" (22px bold)
- Subtitle: "Virtual Walk-In Center · 24/7 · min 1 agent (00-07 & 17-24) · min 3 agents (07-17)"
- Right side: "+ Add Agent to VWIC" button (accent), divider, ← date picker Today → controls

### Tab Bar

Two tabs (pill-style toggle inside card2 bg box):
- **Coverage** (default) — live view of today's coverage
- **Rotation Planner** — planning tool

---

### Coverage Tab

**4 KPI summary cards (grid, 4 columns):**

| Card | Label | Color logic |
|------|-------|-------------|
| VWIC Assigned Today | `{active}/{total}` | accent |
| Voice Pool | `{active}/{total}` | text2 |
| Coverage | `{covered}/{total}h` | green if full, red if gaps |
| Gaps | count of gap hours | green if 0, red if >0 |

**Coverage Timeline (CoverageTimeline component):**

- Horizontal bar: 24 cells, one per hour (00:00–23:00)
- Header: "Coverage Timeline · 24/7 · 00:00 – 24:00" + rule text
- Each hour cell:
  - Green (`#22c55e`) if `hasMainAgent=true` (meets minimum staffing)
  - Orange (`#f97316`) if `hasAnyAgent=true` but not enough (understaffed)
  - Red (`#ef4444`) if no agents at all
  - Shows: agent count / minRequired. Also shows `+N` if backup agents present.
  - If not fully staffed: "+assign" label + clickable → opens AssignSlotModal
  - Hover: tooltip shows hour label, agent names under "VWIC Assigned" and "Voice pool on shift" sections

**Gap bar below timeline:** If any hours below minimum: "Below minimum staffing: HH:00, HH:00…" in danger red.

**Agent tables (2 side-by-side, 1fr / 1.6fr grid):**

Left — "VWIC Assigned Today" (main agents):
- Columns: Name, Team Lead, Shift, Status
- Status values: VWIC badge (accent), Working (green), No shift (indigo), Absent (colored by type: SL=red, AL/UL=yellow, OFF/PH=grey)
- Absent rows: 0.55 opacity

Right — "Voice Pool — Not Assigned" (backup agents):
- Same columns + "Remove" button per row (danger red, calls `DELETE /api/vwic/agents/{id}`)

**Minimum staffing rules (hardcoded in UI):**
- Hours 00–07: min 1 agent
- Hours 07–17: min 3 agents
- Hours 17–24: min 1 agent

---

### Rotation Planner Tab (RotationPlanner component)

**Parameters form:**

| Field | Type | Default |
|-------|------|---------|
| Date | date picker | today |
| Window start | time picker | 07:00 |
| Window end | time picker | 18:00 |
| Max continuous VWIC (h) | number 1–8 | 2 |
| Handover buffer (min) | number 0–30 | 15 |
| Rotation interval | radio: 1h / 2h / 4h | 1h |

**Action buttons:**
- "Calculate Rotation" (accent) → `POST /api/vwic/rotation-plan`
- "Plan Week" (card2) → `POST /api/vwic/rotation-plan-week`
- "Save Rotation" (green, only shown after Calculate) → `POST /api/vwic/rotation-plan/save`

**After Calculate — 4 result sections:**

1. **Recommendation card:** ✓ or ⚠️ icon. Text from API `recommendation` field. Stats: "N Voice agents available · Xh required · Yh available capacity". Green border if OK, red if `availableAgents < 3`.

2. **Rotation Schedule table:** Rows = time slots (IBM Plex Mono labels). Columns = one per agent (first name only). Each cell:
   - ON: `rgba(34,197,94,.18)` bg, green "ON" text
   - HANDOVER: `rgba(250,204,21,.18)` bg, yellow "HO" text
   - OFF: transparent, text3 "—"

3. **Coverage Proof + Fairness (2-column grid):**
   - Coverage Proof: table with slot times, agent count, required count, ✓/✗
   - Fairness Overview: one row per agent with horizontal bar (width = vwicHours/max)

4. **Fallback Warning (conditional):** Green/yellow/red box based on prefix of `fallbackWarning` string:
   - Starts with "CRITICAL": danger red
   - Starts with "1 absence leaves": amber
   - Otherwise: green

**After Plan Week — day tabs + weekly summary:**
- Day tabs (Mon–Fri or full week) with ⚠ indicator if `availableAgents < 3` for that day
- "Export Excel" button → `POST /api/vwic/rotation-plan-week/export` → downloads `VWIC_Week_{date}.xlsx`
- Active day shows same 4 sections as single-day
- Weekly Fairness Summary table: Agent | Mon | Tue | Wed | Thu | Fri | Total h | Slots | Days

---

### AssignSlotModal

Triggered by clicking understaffed hour cell in Coverage tab.

- Header: "Assign Agent" + slot label + hour range
- Status badge: "Understaffed — below minimum" (orange) or "No coverage — gap" (red)
- Loads `GET /api/vwic/candidates?date=<date>`
- Sorted: agents whose shift covers that hour first (marked "in shift" in green), then others ("outside shift")
- Each row: agent name, WORKING badge if working, ID + shift hours (green if covers slot, grey if not)
- "Assign" button: accent if covers hour, card2 if not → `POST /api/vwic/assign`

### AddAgentModal

- "Add Agent to VWIC" header
- Info text: "Sets SecondaryRole = VWIC so the agent appears in the Backup Pool."
- Employee dropdown from `GET /api/vwic/candidates`
- "Add to VWIC" button → `PUT /api/vwic/agents/add`

### APIs

| Action | Endpoint |
|--------|---------|
| Daily data | `GET /api/vwic/daily?date=` |
| Candidates | `GET /api/vwic/candidates?date=` |
| Assign slot | `POST /api/vwic/assign` |
| Add agent | `PUT /api/vwic/agents/add` |
| Remove agent | `DELETE /api/vwic/agents/{id}` (via Remove button in table) |
| Calculate rotation | `POST /api/vwic/rotation-plan` |
| Plan week | `POST /api/vwic/rotation-plan-week` |
| Save rotation | `POST /api/vwic/rotation-plan/save` |
| Export week | `POST /api/vwic/rotation-plan-week/export` |


---

## B05. Break Planner

**File:** `Frontend/src/pages/BreakPlanner.tsx` (587 lines)  
**Route:** `/breaks`  
**Purpose:** Schedule and track 30-minute lunch breaks for Voice + VWIC agents, respecting min-on-line % constraints.

### Header

- Coffee icon (Lucide, accent color) + "Break Planner" (18px bold) + subtitle "Voice + VWIC · 30 min lunch"
- Max width 1100px, centered.

### Controls Bar (one row, card bg, wraps)

| Control | Type | Default |
|---------|------|---------|
| DATE | date picker | today |
| WINDOW START | time picker | 11:30 |
| WINDOW END | time picker | 14:30 |
| VOICE MIN ON-LINE % | number input (50–100, step 5) | 70 |
| Manual slot (button) | — | opens ManualModal |
| Auto-Distribute (button) | accent, Shuffle icon | calls `POST /api/breaks/auto-distribute` |

### 4 KPI Cards (strip, equal width)

| Card | Label | Icon | Color logic |
|------|-------|------|-------------|
| Scheduled today | count | Clock | accent |
| On break now | count | Coffee | #f97316 |
| Completed | count | CheckCircle | #22c55e |
| VWIC on break | count | AlertTriangle | red if > maxVwic, else text2 |

"On break now" = `ON_BREAK` status + SCHEDULED rows whose window overlaps current minute.

### Constraint Info Bar (shown after Auto-Distribute)

Shown if `lastResult` is set:
- "VWIC working: **N** · max M on break"
- "Voice working: **N** · max M on break"
- "Scheduled: **N**"
- If unscheduled > 0: "⚠ Unscheduled: N" (orange)

### Unscheduled Warning (shown if unscheduled agents)

Orange box: "Could not schedule: [name list] — assign manual slots or widen the window."

### Break Timeline (Timeline component)

- Time axis: labels every 30 min from windowStart to windowEnd
- Agent rows: one horizontal bar per agent (ordered by role: VWIC first, then Voice)
  - Bar left/width: calculated from breakStart (or actualStart) and breakEnd relative to window
  - Colors:
    - ON_BREAK: `#f97316` orange
    - DONE: `#22c55e` green (50% opacity)
    - VWIC agent: `var(--accent)` blue
    - Voice agent: `#10b981` teal
  - Late start (>15min deviation): yellow border `2px solid #fbbf24`
- Role label chip: "VWIC" (accent bg) or "Voice" (teal bg), 9px bold
- Concurrent count row below bars: per 15-min slot, shows total agents on break; red bg if at limit

### Agent Break List Table

Columns: **Agent**, **Role**, **Scheduled**, **Actual Start**, **Actual End**, **Status**, **Actions**

**Status badges:**

| Status | Bg | Text color |
|--------|----|-----------|
| SCHEDULED | `rgba(59,126,255,.15)` | `var(--accent)` |
| ON_BREAK | `rgba(249,115,22,.18)` | `#f97316` |
| DONE | `rgba(34,197,94,.15)` | `#22c55e` |
| CANCELLED | `rgba(100,116,139,.15)` | `var(--text3)` |

**Actions per row:**
- SCHEDULED: "▶ Start" (accent bg) → `POST /api/breaks/{id}/start` + "✕" cancel
- ON_BREAK: "■ End" (green bg) → `POST /api/breaks/{id}/end` + "✕" cancel
- DONE / CANCELLED: no actions

Late actual start: shown in `#fbbf24` yellow with note "≠ scheduled" beneath.

### ManualModal

- "Manual Break Slot" header
- Employee ID text input (placeholder "e.g. E12345")
- Start time picker (default 12:00)
- Duration number input (min 15, max 60, default 30)
- Cancel / Save → `POST /api/breaks/manual`

### APIs

| Action | Endpoint | Notes |
|--------|---------|-------|
| Load breaks | `GET /api/breaks?date=` | Refetches every 30 seconds |
| Auto-distribute | `POST /api/breaks/auto-distribute` | Body: date, windowStart, windowEnd, voiceMinPct |
| Start break | `POST /api/breaks/{id}/start` | |
| End break | `POST /api/breaks/{id}/end` | |
| Cancel break | `POST /api/breaks/{id}/cancel` | |
| Manual slot | `POST /api/breaks/manual` | |

### Visual States

| State | Appearance |
|-------|-----------|
| No breaks scheduled | "No break slots for this date. Run Auto-Distribute to generate." |
| Loading | "Loading…" |
| All distributed | Constraint info bar shows 0 unscheduled |
| Any unscheduled | Orange warning banner |
| VWIC at break limit | VWIC KPI card turns red |


---

## B06. WIC Attendance

**File:** `Frontend/src/pages/WicAttendance.tsx` (1474 lines)  
**Route:** `/wic-attendance`  
**Purpose:** Primary daily WIC operations screen — location status sidebar, live kiosk attendance, N-day forecast, schedule editor, and substitute assignment.

### Overall Layout

Full-height split: `margin: -20px; height: calc(100vh - 45px); overflow: hidden`

```
[LEFT SIDEBAR — 260px fixed width]  [RIGHT CONTENT — flex:1, scroll]
```

---

### Left Sidebar

**Title:** "WIC Coverage" (from i18n `attendance.title`)

**Search input:** Placeholder from `attendance.filter.search` / "Standorte suchen..."

**Country dropdown:** Default "Alle Länder" (`attendance.filter.allCountries`); options from locations data.

**Date picker:** Defaults to today; changing date reloads forecast and cards.

**Check-in panel (3-cell grid):**
- Expected: total agents scheduled at selected location for today
- Checked In: count of ACTIVE kiosk agents at selected location
- Not Yet In: Expected − Checked In

**Location list (scrollable):**

Sorted by status rank: UNCOVERED=0, PARTIAL=1, COVERED=2, CLOSED=3.

Each location row:
- Display name (bold 13px)
- NppBadge if applicable
- Live green pulsing dot + agent count if `kiosk.some(k => k.locationCode === loc.locationCode && k.status === "ACTIVE")`
- CoverageBadge (compact mode)
- City name (12px text2)
- At-risk alert triangle (⚠) + count if forecast has at-risk days

Clicking a location selects it and loads right panel. Default: auto-selects first at-risk location on mount.

---

### Right Content

**Header row:**
- Location name (20px bold)
- City / Country (12px text2)
- Live N badge (pulsing green dot + count) — shown if any ACTIVE kiosk agents
- 4 action buttons:
  1. "AL Planning" → opens ALPlanningModal
  2. "Assign Agent" → opens AssignAgentModal
  3. "Manual Check-in" → opens ManualCheckinModal
  4. "Find Substitute" → opens SubstituteSheet

---

**Currently Present panel** (only shown if kiosk ACTIVE agents at this location):

Background: `rgba(34,208,122,0.07)` green tint. Header: "Currently Present" (12px, green).

Each agent chip: pulsing green dot + agent name + check-in time (HH:MM) + "Xmin on shift".

---

**Stats row (4 cards):**

| Card | Content | Notes |
|------|---------|-------|
| Status | CoverageBadge for today | COVERED/PARTIAL/UNCOVERED/CLOSED |
| Agents | assigned count | From kiosk + schedule merge |
| Min Required | number | From WIC schedule |
| Today's schedule | "HH:MM–HH:MM" or "Closed" | From opening hours |

---

**Agent list (chip grid):**

One chip per assigned agent. Color by match type:

| Match | Chip bg | Chip text |
|-------|---------|-----------|
| FULL | `rgba(34,208,122,.15)` | `var(--green)` |
| PARTIAL | `rgba(255,124,59,.15)` | `var(--warn)` |
| NONE | `rgba(255,59,92,.15)` | `var(--danger)` |

Each chip also shows:
- Main/Backup badge
- Shift times (HH:MM–HH:MM) OR leave type label (SL/AL)
- Kiosk status dot: ACTIVE = pulsing green; DONE = grey dot; NOT_CHECKED_IN = no dot

---

**WicScheduleEditor (expand/collapse toggle):**

Fields:
- effectiveFrom date input
- changeNote text input
- "Close entire centre" button (danger red, calls dedicated API)
- 7-day grid (Mon–Sun): each day has Open/Closed toggle, open time input, close time input, optional second window (open2/close2)
- "Save" button → `POST /api/wicschedule/opening-hours/{code}/version`
- After save: consequence table shows conflicting existing assignments

---

**MinRequiredEditor:**

7 number inputs (one per day of week). Save on Enter key or dedicated Save button per day. → `PATCH /api/wicschedule/opening-hours/{code}/{dow}/min-required`

---

**N-day Forecast calendar:**

- Chunked into 7-day rows
- Each day cell: date label, status color, agent count / min required
- At-risk days: clickable → opens SubstituteSheet for that date
- Selected date: highlighted blue border

---

**SubstituteSheet (within WIC Attendance):**

Same structure as Overview's SubstituteDrawer (see C4). Triggered by at-risk forecast day or "Find Substitute" button.

---

### APIs

| Action | Endpoint |
|--------|---------|
| Load forecast | `GET /api/wic/forecast?horizon=N` |
| Load cards | `GET /api/wic/cards?date=` |
| Load substitutes | `GET /api/wic/substitutes?locationCode=&date=&horizon=1` |
| Load kiosk attendance | `GET {VITE_KIOSK_API_URL}/api/attendance` |
| Accept substitute | `POST /api/wic/substitutes/accept` |
| Save opening hours | `POST /api/wicschedule/opening-hours/{code}/version` |
| Update min required | `PATCH /api/wicschedule/opening-hours/{code}/{dow}/min-required` |

### Visual States

| State | Appearance |
|-------|-----------|
| No location selected | "Standort aus der Seitenleiste auswählen" centered |
| Loading forecast | Skeleton bars in forecast calendar |
| No substitutes | "Keine Kandidaten verfügbar" / `substitute.noCandidates` |
| Kiosk API unreachable | Currently Present panel hidden, no live dots |
| Location closed today | Stats row shows "Closed" schedule card |
| Schedule edit conflict | Consequence table appears after save |


---

## B07. WIC Schedule ⚠ PARTIALLY DOCUMENTED

**File:** `Frontend/src/pages/WicSchedule.tsx` — NOT read in full; inferred from WicAttendance integration and backend routes.  
**Route:** `/wic-schedule` (if separate) or integrated into WIC Attendance as an editor panel  
**Purpose:** Manage per-location opening hours and minimum-required agent counts by day of week.

### Known Elements (from WicAttendance integration)

- **WicScheduleEditor:** 7-day grid (Mon–Sun), each day: Open/Closed toggle, open time (HH:MM), close time (HH:MM), optional second window open2/close2. "Save" button posts versioned snapshot. "Close entire centre" button triggers a separate danger-action endpoint.
- **MinRequiredEditor:** 7 number inputs (one per weekday). Saved individually per day.

### APIs (confirmed from WicAttendance)

| Action | Endpoint |
|--------|---------|
| Get opening hours | `GET /api/wicschedule/opening-hours/{locationCode}` |
| Save versioned hours | `POST /api/wicschedule/opening-hours/{code}/version` |
| Update min required | `PATCH /api/wicschedule/opening-hours/{code}/{dayOfWeek}/min-required` |

⚠ **Full standalone page layout, any additional tabs or controls, and the consequence table details are not confirmed. A redesign agent should read `WicSchedule.tsx` before finalising this section.**


---

## B08. Pipeline

**File:** `Frontend/src/pages/Pipeline.tsx` (313 lines)  
**Route:** `/pipeline`  
**Purpose:** Plan and track WIC pipeline events (additional coverage arrangements). Supports list view and a 30-day timeline view.

### Filter / Header Bar

| Element | Detail |
|---------|--------|
| View toggle buttons | "List" / "Timeline" — switches entire content area |
| From / To date pickers | Control which events appear in list view; timeline always shows 30-day window from today |
| "+ Add Event" button | Opens EventModal in create mode |

---

### Status Colors

| Status | Color | Hex |
|--------|-------|-----|
| PLANNED | Grey | `var(--text3)` |
| CONFIRMED | Green | `var(--green)` |
| CANCELLED | Red | `var(--danger)` |

### Handling Type Colors

| Type | Color |
|------|-------|
| ADDITIONAL | Blue (`var(--accent)`) |
| LOCAL | Purple (`var(--purple)`) |

---

### List View

One card per pipeline event.

| Field shown on card | Notes |
|--------------------|-------|
| Title | Bold, 15px |
| Status badge | Colored pill |
| Handling type badge | Colored pill |
| Location | Text |
| Date range | "DD.MM.YYYY – DD.MM.YYYY" or single date |
| Start / End time | "HH:MM – HH:MM" |
| Agents required / Additional needed | Number badges |
| Primary Agent | Name |
| Backup Agent | Name |
| Handled by | Free text |
| Notes | Italic, truncated |
| Edit button (pencil icon) | Opens EventModal in edit mode |
| Delete button (trash icon) | `window.confirm()` then `DELETE /api/pipeline/{id}` |

---

### Timeline View

- Sticky first column: location names
- 30 date columns (today → today+29)
- Column headers: day-of-week abbrev + date number
- Cell content: event title (truncated) in handling-type color
- Clicking a cell opens EventModal in edit mode
- Empty cell in range: colored block shows presence of event

---

### EventModal

Fields (create mode):

| Field | Type | Notes |
|-------|------|-------|
| Title | Text input | Required |
| Location | Text input | |
| Date From | Date picker | |
| Date To | Date picker | Optional; single-day if omitted |
| Start Time | Time input | HH:MM |
| End Time | Time input | HH:MM |
| Handling Type | Dropdown | ADDITIONAL / LOCAL |
| Agents Required | Number input | |
| Additional Needed | Number input | |
| Primary Agent | Text input | |
| Backup Agent | Text input | |
| Handled By | Text input | |
| Notes | Textarea | |

Edit mode adds **Status dropdown** (PLANNED / CONFIRMED / CANCELLED).

**Save** → POST (create) or PATCH (edit). Modal closes on success.

---

### APIs

| Action | Endpoint |
|--------|---------|
| List events | `GET /api/pipeline?from=YYYY-MM-DD&to=YYYY-MM-DD` |
| Create event | `POST /api/pipeline` |
| Update event | `PATCH /api/pipeline/{id}` |
| Delete event | `DELETE /api/pipeline/{id}` |

### Visual States

| State | Appearance |
|-------|-----------|
| No events | "Keine Pipeline-Einträge" centered placeholder |
| Loading | Skeleton cards in list view |
| Delete confirm | `window.confirm()` OS dialog |
| Save error | Inline error text below modal |


---

## B09. Training

**File:** `Frontend/src/pages/Training.tsx` (381 lines)  
**Route:** `/training`  
**Purpose:** AI-assisted training slot planning, session history, and topic management for GSD agents.

### Tab Bar

Three tabs: **Scheduler** | **Sessions** | **Topics**

---

### Tab: Scheduler

**Purpose:** Pick a topic + date range → find optimal training slots that minimise WIC coverage impact.

**Controls (top row):**

| Control | Detail |
|---------|--------|
| Topic dropdown | Lists all training topics |
| Duration (days) | Number input; default 1 |
| Date From | Date picker |
| Date To | Date picker |
| Agent checkboxes | Multi-select list of agents to include |
| "Find Available Slots" | → `POST /api/training/suggest` |

**Slot cards (returned results):**

Each suggested slot is a card showing:

| Element | Detail |
|---------|--------|
| Date range | "DD.MM – DD.MM.YYYY" |
| Coverage % | Large number, green/yellow/red by threshold |
| Production impact bar | Horizontal bar, red fill proportional to impact score |
| Attendee chips | Agent name chips, green background |
| Missing chips | Agents who cannot attend, orange background |
| "Confirm Slot" button | → `POST /api/training/confirm` with slot data |

**Toast notification:** Appears for 4 seconds on successful confirmation: "Training confirmed for [topic]".

---

### Tab: Sessions

**Purpose:** Review confirmed training sessions.

**Table columns:**

| Column | Notes |
|--------|-------|
| Topic | Training topic name |
| Date Range | "DD.MM – DD.MM.YYYY" |
| Duration | N days |
| Attendee Count | Number with expand toggle |
| Actions | Delete (trash icon) |

**Expanded row:** Shows sub-table of attendees with columns: Name | Shift on training day (type + times).

**Delete:** `window.confirm()` OS dialog → `DELETE /api/training/sessions/{id}`.

---

### Tab: Topics

**Purpose:** Manage the list of training topics.

**Table columns:**

| Column | Notes |
|--------|-------|
| # | Row index |
| Topic Name | |
| Description | |
| Duration (days) | |
| Actions | Edit (pencil) + Delete (trash) |

**"New Topic" button** → opens modal.

**New/Edit Topic Modal fields:**

| Field | Type |
|-------|------|
| Name | Text input |
| Description | Textarea |
| Duration | Number input (days) |

APIs: `POST /api/training/topics`, `PATCH /api/training/topics/{id}`, `DELETE /api/training/topics/{id}`.

---

### APIs

| Action | Endpoint |
|--------|---------|
| List sessions | `GET /api/training/sessions` |
| Delete session | `DELETE /api/training/sessions/{id}` |
| List topics | `GET /api/training/topics` |
| Create topic | `POST /api/training/topics` |
| Update topic | `PATCH /api/training/topics/{id}` |
| Delete topic | `DELETE /api/training/topics/{id}` |
| Suggest slots | `POST /api/training/suggest` |
| Confirm slot | `POST /api/training/confirm` |

### Visual States

| State | Appearance |
|-------|-----------|
| No suggestions yet | Empty slot-results area |
| No sessions | "Keine Trainings geplant" placeholder |
| No topics | "Keine Themen vorhanden" placeholder |
| Loading suggest | Spinner on "Find Available Slots" button |
| Toast | Green pill toast top-right, 4s auto-dismiss |


---

## B10. WIC Locations ⚠ PARTIALLY DOCUMENTED

**File:** `Frontend/src/pages/WicLocations.tsx` (first 80 lines read; rest not read)  
**Route:** `/wic-locations`  
**Purpose:** Master list of WIC locations with filtering and KPI summary.

### Known Elements

**KPI row (3 cards):**

| Card | Content |
|------|---------|
| Total | Count of all WIC locations |
| DE | Count of German locations |
| NL | Count of Dutch locations |

**Filter controls:**

| Control | Detail |
|---------|--------|
| Search input | Filters by location name/code; placeholder "Standorte suchen..." |
| Country dropdown | "Alle" / "DE" / "NL" |

**Table columns:**

| Column | Notes |
|--------|-------|
| Location | Location code + display name |
| City | City name |
| Country | DE / NL flag or label |
| Address | Street address |
| Opening Schedule | Condensed weekday hours |
| Status | ACTIVE / CLOSED badge |

⚠ **Edit/add location functionality, modal details, ACTIVE/CLOSED toggle behavior, and full table action columns are not confirmed. Read the rest of `WicLocations.tsx` before finalising.**

### APIs (inferred)

| Action | Endpoint |
|--------|---------|
| List locations | `GET /api/wic-coverage/locations` or `GET /api/wic/locations` |


---

## B11. Attendance ⚠ PARTIALLY DOCUMENTED

**File:** `Frontend/src/pages/Attendance.tsx` — NOT read  
**Route:** `/attendance`  
**Purpose:** Daily attendance records by location (non-WIC kiosk attendance, or a combined view). Backed by `DailyAttendance` entity (EF `DailyAttendances` DbSet with indexes on `AttendanceDate`, `LocationName`, `Country`).

### Known Schema (from GSDContext.cs)

`DailyAttendance` has: `AttendanceDate`, `LocationName`, `Country`. Likely additional agent-count or names fields.

⚠ **All UI elements, table columns, filter controls, and API endpoints for this page are unconfirmed. Read `Attendance.tsx` before finalising.**


---

## B12. Sick Leave

**File:** `Frontend/src/pages/SickLeave.tsx` (572 lines)  
**Route:** `/sick-leave`  
**Language:** Entirely hardcoded German — **no i18n keys used on this page.**  
**Purpose:** Track and manage agent sick leave records with group-by-TL view, drill-down modal, and a calendar day grid.

### Header / KPI Row

| Card | Content |
|------|---------|
| Aktuell krank | Count of currently open SL records (sentinel date) |
| Langzeitkrank | Count of records ≥ 21 days |
| TL Gruppen | Count of distinct team leads with SL entries |

---

### Filter Controls

| Control | Detail |
|---------|--------|
| "Vergangene anzeigen" toggle | Shows/hides closed (past) SL records |
| Search | Filters by agent name |
| TL dropdown | Filter by team lead |

---

### GroupedSickTable

Records grouped by team lead. Each TL group:

- **Group header row:** TL name + count badge
- **Collapsible** (click to expand/collapse)
- Agent rows within group:

| Column | Detail |
|--------|--------|
| Name | Agent name |
| Beginn | First day (DD.MM.YYYY) |
| Ende | Last day or "offen (N Tage)" in `var(--warn)` when sentinel |
| Dauer | N days |
| TL | Team lead name |
| Kommentar | CommentCell (inline edit + save) |
| Aktionen | Edit (pencil) + Delete (trash) |

**Sentinel rendering:** `lastDay = "2099-12-31"` → displays "offen (N Tage bisher)" in warn orange.

**CommentCell:** Click to enter inline edit mode. Textarea appears. Enter/blur to save → `PATCH /api/sickleave/{id}/comment`. Escape to cancel.

---

### DayGrid

Below the table: a horizontal calendar showing sick leave spans as colored bars.

- X-axis: days of current month
- Y-axis: one row per agent
- Bars: colored by duration (short/medium/long)
- Today marker: vertical line

---

### DrillDownModal

Opened by clicking an agent row. Shows:

- Agent name + total SL days YTD
- Per-absence history table: First Day | Last Day | Duration | Comment
- Close button

---

### Add / Edit Modal

**Trigger:** "+ Neuer Eintrag" button or edit action.

Fields:

| Field | Type | Notes |
|-------|------|-------|
| Agent | Dropdown | Employee list |
| Erster Tag | Date picker | |
| Letzter Tag | Date picker | Leave blank = open (sentinel) |
| Kommentar | Textarea | |

**Validation:** Uses native `alert()` for validation errors (e.g. "Bitte Mitarbeiter auswählen").

**Delete confirmation:** Uses native `confirm()` OS dialog.

---

### APIs

| Action | Endpoint |
|--------|---------|
| List sick leaves | `GET /api/sickleave` |
| Create | `POST /api/sickleave` — `endDate: "2099-12-31"` for open |
| Update | `PATCH /api/sickleave/{id}` |
| Update comment | `PATCH /api/sickleave/{id}/comment` |
| Delete | `DELETE /api/sickleave/{id}` |

### Visual States

| State | Appearance |
|-------|-----------|
| No records | "Keine Krankmeldungen vorhanden" |
| Open-ended SL | "offen (N Tage bisher)" in warn color |
| Loading | Skeleton rows |
| Validation error | `alert()` OS dialog |


---

## B13. Vacations

**File:** `Frontend/src/pages/Vacations.tsx` (716 lines)  
**Route:** `/vacations`  
**Purpose:** Manage agent annual leave records. Supports single-entry list view and a bulk CSV-style import tab.

### Tab Bar

Two tabs: **Liste** | **Massenimport**

---

### Tab: Liste

**Filter controls:**

| Control | Detail |
|---------|--------|
| Search | Filter by agent name |
| Year | Dropdown (current + adjacent years) |
| "+ Neuer Urlaub" button | Opens add modal |

**Table columns:**

| Column | Detail |
|--------|--------|
| Mitarbeiter | Agent name |
| Von | First day (DD.MM.YYYY) |
| Bis | Last day (DD.MM.YYYY) |
| Arbeitstage | Working days (excludes weekends + PH) |
| Status | Row status badge |
| Aktionen | Edit (pencil) + Delete (trash) |

**Row statuses and colors:**

| Status | Color |
|--------|-------|
| Active (today within range) | `var(--green)` background tint |
| Upcoming | Accent blue tint |
| Past | Dimmed / text3 |
| Overlapping | `var(--warn)` orange tint |

**Add/Edit Modal fields:**

| Field | Type | Notes |
|-------|------|-------|
| Agent | Dropdown | Pre-filled if opened from Shifts via "+ Add AL" (F1) or from ALHistoryDrawer |
| Von (Start) | Date picker | Pre-filled with today if opened from Shifts header |
| Bis (End) | Date picker | Locked to equal Von when Half Day is selected |
| Half Day toggle | Button pair: "Full Day" / "Half Day" | Switches `isHalfDay` — half day forces `lastDay=firstDay`, `WorkDaysNet=0.5`, `ShiftType=HALF_AL` |
| Notiz | Text input | |

**ALHistoryDrawer (F3):**

Clicking an agent's name in the vacation list (grouped list view) or in AL Balance opens a slide-in drawer showing that agent's full vacation history. Fetches `GET /api/vacations?employeeId=X`. Shows a table of all entries: dates, working days, status, and note. Used in `Vacations.tsx` and `ALBalance.tsx`.

---

### Tab: Massenimport

**Purpose:** Paste a block of name+date-range lines and bulk-import.

**Textarea:** Accepts multi-line free text. Accepted formats include various date notations (DD.MM–DD.MM.YYYY, DD.MM.YYYY–DD.MM.YYYY).

**"Parsen" button:** Runs fuzzy name matching + date parsing client-side.

**Result table (per parsed row):**

| Column | Detail |
|--------|--------|
| Eingabe | Raw input text |
| Erkannter Agent | Matched name (green) or "Unbekannt" (red) |
| Von | Parsed start date |
| Bis | Parsed end date |
| Status | resolved / ambiguous / parse-error |
| Include toggle | Checkbox to include/exclude from import |

**Fuzzy name matching:** Jaro-Winkler or similar; handles partial name matches, initials, and typos. Ambiguous matches show multiple suggestions with a disambiguation dropdown.

**"Importieren" button:** POSTs all resolved rows.

---

### APIs

| Action | Endpoint |
|--------|---------|
| List vacations | `GET /api/vacations?from=&to=` |
| Create | `POST /api/vacations` |
| Update | `PATCH /api/vacations/{id}` |
| Delete | `DELETE /api/vacations/{id}` |

### Visual States

| State | Appearance |
|-------|-----------|
| No vacations | "Keine Urlaubseinträge" placeholder |
| Parse errors | Red row with error icon |
| Ambiguous match | Yellow row with dropdown |
| Import summary | Success count / error count toast |


---

## B14. AL Balance

**File:** `Frontend/src/pages/ALBalance.tsx` (184 lines)  
**Route:** `/al-balance`  
**Purpose:** View and edit annual leave entitlement vs. used days for each agent, with low/critical alerts.

### KPI Row (3 cards)

| Card | Value |
|------|-------|
| Total | Total agent count |
| Low Balance | Count with remainingAL ≤ 10 |
| Critical | Count with remainingAL ≤ 5 |

---

### Table

| Column | Detail |
|--------|--------|
| # | Row index |
| Name | Agent name — **clickable** (dotted underline); opens `ALHistoryDrawer` slide-in for that agent (F3) |
| Anspruch (Eligible) | Integer; days entitled this year |
| Genommen (Taken) | **Inline editable cell** — click to enter edit mode. Value is `DECIMAL(10,1)` — can be 14.5 |
| Verbleibend (Remaining) | Eligible − Taken; auto-computed. `DECIMAL(10,1)`. Threshold ≤ 5 → critical row; ≤ 10 → low balance KPI count. |
| KT (SL Days) | Sick leave days for the year |
| Fortschritt (Progress) | Horizontal bar: Taken/Eligible ratio |

**Inline edit on "Taken" cell:**

- Click → becomes `<input type="number" min=0 max=28>` focused
- Enter → saves → `PATCH /api/employees/{id}/albalance` with `{ takenAL: N }`
- Escape → cancels, restores previous value
- Value out of range → ignored (no save)

**Critical row styling:** When `remainingAL ≤ 5`, entire row gets background `rgba(255,59,92,0.04)` (faint red).

### ALHistoryDrawer (F3)

Clicking an agent name opens a slide-in drawer (`ALHistoryDrawer`, exported from `Vacations.tsx`) showing that agent's full vacation history. Fetches `GET /api/vacations?employeeId=X`. Columns: date range, working days (`WorkDaysNet`, decimal), status, note. Dismissible via backdrop or ✕ button. Also available in `Vacations.tsx` grouped list view.

---

### APIs

| Action | Endpoint |
|--------|---------|
| Load balances | `GET /api/albalance/get` |
| Update taken days | `PATCH /api/employees/{id}/albalance` |

### Visual States

| State | Appearance |
|-------|-----------|
| Loading | Skeleton table rows |
| Empty | "Keine Einträge" placeholder |
| Inline edit active | Input replaces cell text, blue border |
| Critical row | Faint red row background |


---

## B15. AL Calendar

**File:** `Frontend/src/pages/ALCalendar.tsx` (581 lines)  
**Route:** `/al-calendar`  
**Purpose:** Visualise annual leave across the team in multiple view modes, with WIC cross-reference and risk indicators.

### Controls Row

| Control | Detail |
|---------|--------|
| View toggle | 7d / 14d / 3m (Month) — switches layout |
| Team Lead filter | Dropdown; "Alle TLs" default |
| Date nav | ← → arrows; "Heute" button resets to today |

---

### KPI Row (4 cards)

| Card | Detail |
|------|--------|
| On AL Today | Count of agents currently on AL |
| Peak Day | Date with most concurrent AL agents |
| Warning Days | Days where AL count ≥ threshold |
| Agents Tracked | Total agents with AL entries in view range |

---

### LeaveAvailabilityBar

Horizontal bar above the grid showing per-day agent availability. Days with high AL counts shown in warn/danger color.

---

### Month View (3m tab)

- Standard 7-column calendar grid (Monday-first)
- Each month shown sequentially (3 months)
- **Day cell content:**
  - Day number (top-left)
  - AL count badge — color: ≤2 green, ≤5 yellow, ≥6 red
  - WIC coverage dot — green = covered, red = at-risk
- Clicking a day cell expands detail panel below showing:
  - Agent list on AL that day
  - WIC coverage per location for that day
- Fetches both `GET /api/alcalendar` and `GET /api/wic/forecast`

---

### 7d / 14d View

- **Sticky left column:** Agent names
- **Date columns:** One per day in range
- **TL group rows:** Agents grouped by Team Lead name; group header row in TL color
- **AL cell:** When agent is on AL, cell is filled in TL color
- **Non-AL cell:** Empty / faint bg

**TL color map (hardcoded hex values):**

| TL | Color |
|----|-------|
| TL 1 | Specific hex (not confirmed by name) |
| TL 2 | Specific hex |
| TL 3 | Specific hex |
| TL 4 | Specific hex |
| TL 5 | Specific hex |
| TL 6 | Specific hex |

⚠ Exact TL names and hex values should be verified against `ALCalendar.tsx` lines ~150-180.

---

### Expanded Day Detail

Clicking any date (in any view) opens an expanded panel:

- "Agents on AL" list with name + AL period
- "WIC Coverage" section: per-location status badge

---

### APIs

| Action | Endpoint |
|--------|---------|
| Load AL data | `GET /api/alcalendar` |
| Load WIC forecast (month only) | `GET /api/wic/forecast` |

### Visual States

| State | Appearance |
|-------|-----------|
| No AL in range | Empty cells, KPI cards show 0 |
| High AL day | Day cell red badge |
| WIC at-risk | Red dot in month cell |
| Loading | Skeleton grid |


---

## B16. Employees

**File:** `Frontend/src/pages/Employees.tsx` (416 lines)  
**Route:** `/employees`  
**Purpose:** Master employee roster with role/engagement badges and full detail modal.

### Filter Controls

| Control | Detail |
|---------|--------|
| Search | Filter by name or employee ID |
| Role dropdown | Filter by shift role |
| Engagement dropdown | Filter by engagement type |
| "+ Mitarbeiter" button | Opens EmployeeModal in create mode |

---

### Table Columns

| Column | Detail |
|--------|--------|
| # | Row index |
| Name | Full name |
| Employee ID | Numeric ID |
| Role | Color-coded badge |
| Engagement | Engagement type badge |
| Team Lead | TL name |
| WIC Location | Primary WIC location (if WIC agent) |
| Status | ACTIVE / INACTIVE badge |
| Actions | Edit (pencil) opens EmployeeModal |

---

### Role Colors

| Role | Color |
|------|-------|
| GSD | Accent blue |
| WIC | Teal (`var(--accent2)`) |
| TL | Purple (`var(--purple)`) |
| LEAD | Purple variant |
| (others) | text2 grey |

---

### Engagement Badges

Each unique engagement value gets a consistent color derived from its string hash. Badge shows abbreviated engagement code.

---

### EmployeeModal

Opens for create and edit. Fields:

| Field | Type | Notes |
|-------|------|-------|
| Name | Text input | Required |
| Employee ID | Number input | Required; unique |
| Role | Dropdown | GSD / WIC / TL / LEAD |
| Engagement | Text input | |
| Team Lead | Text input | |
| WIC Location | Dropdown | From WIC locations list |
| Email | Text input | |
| Status | Toggle | ACTIVE / INACTIVE |

**Save** → POST (create) or PATCH (edit). Modal closes on success.

---

### APIs

| Action | Endpoint |
|--------|---------|
| List employees | `GET /api/employees` |
| Create | `POST /api/employees` |
| Update | `PATCH /api/employees/{id}` |

### Visual States

| State | Appearance |
|-------|-----------|
| No results | "Keine Mitarbeiter gefunden" |
| Loading | Skeleton rows |
| Save error | Inline error in modal |
| INACTIVE row | Dimmed row (opacity 0.5 or text2 color) |


---

## B17. WIC Assignments

> **Display name changed 2026-09-03:** "WIC Coverage" → "WIC Assignments" (EN) / "WIC-Zuweisungen" (DE). Route `/wic-coverage` preserved.

**File:** `Frontend/src/pages/WicCoverage.tsx` (602 lines)  
**Route:** `/wic-coverage`  
**Purpose:** Static assignment directory — view and edit WIC agent assignments to locations (Main/BackupA/BackupB/BackupC). Also maintains agent attributes (HasCar, GroupRegion). Not a daily coverage status page.  
**IMPORTANT:** This is the **ONLY** page in the entire application that uses Tailwind CSS (`className=`). All other pages use inline `style={{}}`.

### Tab Bar

Two tabs: **WIC Locations** | **Agents**

---

### Tab: WIC Locations

**Purpose:** View all WIC locations with their assigned agents and coverage status.

**Filter controls:**

| Control | Detail |
|---------|--------|
| Search | Filter locations by name |
| Country dropdown | "Alle" / "DE" / "NL" |
| Date picker | View assignments for a specific date |

**Location cards (grid):**

Each card:

| Element | Detail |
|---------|--------|
| Location name | Bold |
| Country flag/badge | DE / NL |
| Coverage badge | COVERED / PARTIAL / UNCOVERED / CLOSED |
| NPP label | If NPP location |
| Assigned agents | Agent chips |
| "Tier" button | Opens TierSheet for this location |
| "+ Assign" button | Opens AssignAgentModal |

**TierSheet (slide-out):**

- Lists coverage tiers for the location
- Tier = a grouping of agents by priority or shift overlap
- Can reorder agents between tiers via drag
- APIs: `GET /api/wic-coverage/tiers/{locationCode}`, `PATCH /api/wic-coverage/tiers/{locationCode}`

---

### Tab: Agents

**Purpose:** List of WIC-capable agents with their primary location assignment.

**Filter controls:**

| Control | Detail |
|---------|--------|
| Search | Filter by name |
| Location dropdown | Filter by WIC location |

**Table columns:**

| Column | Detail |
|--------|--------|
| Name | Agent name |
| Employee ID | Numeric |
| Primary WIC Location | Location name (from agent's MAIN role) |
| Reachable Cities | Additional cities this agent can cover |
| Actions | Edit (pencil) → opens AgentDetailSheet |

**AgentDetailSheet (slide-out panel):**

- Agent name + ID header
- Primary location
- Reachable cities: multi-select checkboxes of available WIC locations
- Save button → `PATCH /api/wic-coverage/agents/{id}/reachable-cities`

---

### APIs

| Action | Endpoint |
|--------|---------|
| List locations with coverage | `GET /api/wic-coverage/locations` |
| List agents | `GET /api/wic-coverage/agents` |
| Get location tiers | `GET /api/wic-coverage/tiers/{locationCode}` |
| Update tiers | `PATCH /api/wic-coverage/tiers/{locationCode}` |
| Assign agent | via AssignAgentModal → `POST /api/wic/assignments` (one per date) |
| Update reachable cities | `PATCH /api/wic-coverage/agents/{id}/reachable-cities` |

### Visual States

| State | Appearance |
|-------|-----------|
| COVERED | Green badge |
| PARTIAL | Orange badge |
| UNCOVERED | Red badge |
| CLOSED | Grey-blue badge |
| No assignments | "Kein Agent zugewiesen" in card |
| Loading | Skeleton cards |


---

## B18. WIC Annual Leave

**File:** `Frontend/src/pages/WicAnnualLeave.tsx` (425 lines)  
**Route:** `/wic-annual-leave`  
**Purpose:** 14-day forward-looking view of WIC agents on annual leave, cross-referencing vacation data with WIC assignments to flag coverage risk.

### Header / Controls

- "Refresh" button with spin animation on load

---

### KPI Row (3 cards)

| Card | Background | Content |
|------|-----------|---------|
| WIC Agents on Leave | Accent blue | Count of WIC agents with AL in the next 14 days |
| Lowest Coverage Days | Red (`var(--danger)` tint) | Date badges for days with fewest available WIC agents |
| Period | Green tint | "Heute – DD.MM.YYYY" showing the 14-day window |

---

### Table

Fixed window: today through today+13 (14 calendar days).

**Sort order:** By AL start date ascending.

| Column | Detail |
|--------|--------|
| Mitarbeiter (Employee) | Agent name |
| ID | Employee ID |
| AL Start | First day of AL period (DD.MM.YYYY) |
| AL Ende | Last day of AL period (DD.MM.YYYY) |
| Arbeitstage | Working days in the 14-day window (excludes weekends + PH) |
| WIC Standort | Agent's primary WIC location |

**Location resolution:** Prefers clean `displayName`; strips `DE_` / `NL_` prefixes from raw location codes.

**Empty state:** "Keine WIC-Agenten im Urlaub in den nächsten 14 Tagen" centered.

---

### APIs

| Action | Endpoint |
|--------|---------|
| Load WIC agents | `GET /api/wic-coverage/agents` |
| Load vacations in window | `GET /api/vacations?from=YYYY-MM-DD&to=YYYY-MM-DD` |

### Visual States

| State | Appearance |
|-------|-----------|
| No AL in window | Empty table with placeholder text |
| Loading | Skeleton rows |
| Refresh in progress | Spin animation on refresh button |
| High-risk day | Shown in Lowest Coverage Days card as date badge in danger red |


---

## B19. Pulse Assistant

**File:** `Frontend/src/pages/WicAssistant.tsx` (96 lines); mounts `ChatPanel` from `WicChatWidget`  
**Route:** `/assistant`  
**Purpose:** Dedicated full-page AI assistant for WIC operations queries, backed by the same chat engine as the global floating widget.

### Layout

Full-page card:

```
[Bot icon] Pulse Assistant       [header]
─────────────────────────────────────────
[Chat history area]              [scrolls]
─────────────────────────────────────────
[Text input] [Send button]       [footer]
```

---

### Welcome Message

Shown when chat history is empty:

> "Ich kann dir helfen mit: Schichtplanung, WIC-Abdeckung, Urlaubsanfragen, Krankmeldungen, ..."

Followed by example queries as clickable chips (in both DE and EN — depends on active language):

- "Wer ist heute im WIC Düsseldorf?"
- "Wie viele AL-Tage hat [Name] noch?"
- "Zeig mir offene Krankmeldungen"
- "Welche WIC-Standorte sind heute unbesetzt?"

---

### ChatPanel

- Chat history rendered as alternating user/assistant message bubbles
- User messages: right-aligned, accent-blue background
- Assistant messages: left-aligned, card2 background; supports markdown rendering
- Typing indicator: animated dots while awaiting response
- `useAssistantAsk` mutation: `POST` to assistant endpoint

---

### Global WicChatWidget (overlay)

Same `ChatPanel` mounted as a floating overlay:

- **Trigger button:** Fixed bottom-right; Bot icon + "Assistant" label
- **Expand:** Grows to ~400×600px card
- **Minimize:** Returns to button
- **Accessible on all routes** (rendered in AppLayout, outside route children)

---

### APIs

| Action | Endpoint |
|--------|---------|
| Send message | `POST /api/assistant/ask` (body: `{ message: string, history: [...] }`) |

### Visual States

| State | Appearance |
|-------|-----------|
| Empty (no history) | Welcome message + example chips |
| Loading response | Typing indicator in assistant bubble |
| Error | Error text in assistant bubble, danger color |
| Long history | Chat area scrolls; input stays pinned to bottom |


---

## B20. BO Liste

**File:** `Frontend/src/pages/BoList.tsx` (281 lines)  
**Route:** `/bo-list`  
**Language:** Entirely hardcoded German.  
**Purpose:** Daily list of agents in BO (Büro/Office) assignment for a selected date. Supports add, edit, delete.

### Header Controls

| Element | Detail |
|---------|--------|
| Date picker | Defaults to today; changes reload the list |
| Entry count badge | "N Einträge" shown next to date |
| "+ Agent hinzufügen" button | Opens Add Modal |

---

### Table Columns

| Column | Detail |
|--------|--------|
| # | Row index |
| Name | Agent name |
| Schicht | Formatted as "HH:MM–HH:MM" |
| Notiz | Free-text note |
| Aktionen | Pencil (edit) + Trash (delete) |

**Delete:** No confirm dialog — fires `DELETE` immediately.

---

### Add / Edit Modal

Fields:

| Field | Type | Notes |
|-------|------|-------|
| Name | Text input | Agent name |
| Beginn | Time input | HH:MM |
| Ende | Time input | HH:MM |
| Notiz | Text input | Optional note |

Modal title: "Agent hinzufügen" (add) or "Eintrag bearbeiten" (edit).

---

### APIs

| Action | Endpoint |
|--------|---------|
| Load list | `api.boList.get(date)` → `GET /api/bo-list?date=YYYY-MM-DD` |
| Create | `api.boList.create({date, name, startTime, endTime, note})` → `POST /api/bo-list` |
| Update | `api.boList.update(id, {...})` → `PATCH /api/bo-list/{id}` |
| Delete | `api.boList.remove(id)` → `DELETE /api/bo-list/{id}` |

### Visual States

| State | Appearance |
|-------|-----------|
| No entries | "Keine Einträge für diesen Tag" |
| Loading | Skeleton rows |
| Empty date | Same as no entries |


---

## B21. Bulk RTM Entry

**File:** `Frontend/src/pages/BulkRtm.tsx` (975 lines)  
**Route:** `/bulk-rtm`  
**Purpose:** Two-mode RTM (Return-to-Meeting / daily status) bulk entry tool: paste-and-parse from a raw text list, or enter rows manually.

### Tab Bar

Two tabs: **Einfügen & Parsen** | **Manuelle Eingabe**

---

### Category System

7 categories, each with a distinct color:

| Category | Color | Notes |
|----------|-------|-------|
| AL | Green | Annual leave |
| SL | Red/danger | Sick leave — no times required |
| OFF | Grey | Day off |
| CD | Purple | Compensatory day |
| NIGHT | Dark blue | Night shift |
| BO | Orange | Office assignment |
| WIC | Teal | WIC assignment |

---

### Tab: Einfügen & Parsen

**Textarea:** Paste raw text. Accepted format per line:

```
<Agent Name> <HH:MM>-<HH:MM> [optional note]
```

SL lines: `<Agent Name> SL` (no times).

**"Parsen" button:** Runs regex `^(.+?)\s+(\d{1,2}:\d{2})\s*[-–]\s*(\d{1,2}:\d{2})\s*(.*)$` + SL detection client-side.

---

### Parsed Result Table

| Column | Detail |
|--------|--------|
| Status icon | CheckCircle2 (resolved) / AlertCircle (ambiguous/suggest) / XCircle (error) |
| Agent name | Matched name; "Unbekannt" in red if not found |
| Category | Badge in category color |
| Times | HH:MM–HH:MM or "–" for SL |
| Note | Parsed note text |
| WIC Location | Per-row dropdown (only for WIC category); fallback to agent's home WIC |
| Include | Checkbox |

**Row statuses:**

| Status | Meaning |
|--------|---------|
| resolved | Clean match, times OK |
| resolved-corrected | Times adjusted (e.g. end < start → next day) |
| suggest | Fuzzy name match; user should verify |
| ambiguous | Multiple candidate matches |
| parse-error | Could not parse line |
| night-no-time | NIGHT category but no valid times |

---

### Tab: Manuelle Eingabe

Manual row builder. Add button appends a blank row:

| Field | Type |
|-------|------|
| Agent | Dropdown |
| Category | Dropdown |
| Start Time | HH:MM input |
| End Time | HH:MM input |
| Note | Text input |
| WIC Location | Dropdown (if WIC category) |
| Delete row | Trash icon |

---

### Save Behavior

"Speichern" button processes all checked rows:

**BO category special handling:**
1. `DELETE /api/bo-list/by-date?date=<today>` — clears today's BO list
2. Then `POST /api/bo-list` for each BO row

**SL category:** POSTed with `endDate: "2099-12-31"` (open-ended sentinel).

**WIC rows:** POSTed with per-row location code from the dropdown.

**Per-row result icons after save:**
- ✅ CheckCircle2 (green) — success
- ⚠ AlertCircle (yellow) — partial/warning
- ✗ XCircle (red) — failed

---

### APIs

| Action | Endpoint |
|--------|---------|
| Create RTM entry | `POST /api/rtm` |
| Create SL | `POST /api/sickleave` |
| Create WIC shift | `POST /api/wic/assignments` |
| Clear BO by date | `DELETE /api/bo-list/by-date?date=` |
| Create BO entry | `POST /api/bo-list` |

### Visual States

| State | Appearance |
|-------|-----------|
| Unparsed | Empty result table |
| Parse errors | XCircle rows in red |
| Ambiguous | AlertCircle rows in yellow, disambiguation note |
| Save in progress | Spinner on Speichern button |
| Save done | Per-row result icons replace status icons |


---

## C1. Assign Agent Modal (AssignAgentModal)

**File:** `Frontend/src/components/AssignAgentModal.tsx` (408 lines)  
**Used by:** WIC Coverage (tab), WIC Attendance (right panel header)  
**Purpose:** Assign one agent to one WIC location across a date range, iterating day-by-day with progress feedback.

### Modal Fields

| Field | Type | Notes |
|-------|------|-------|
| Employee | Dropdown | All employees list |
| Location | Dropdown | All WIC locations |
| From Date | Date picker | Start of assignment range |
| To Date | Date picker | End of assignment range |
| Skip Weekends | Checkbox | Excludes Sat/Sun from iteration |
| Shift Start | Time input | Pre-filled from location's opening hours |
| Shift End | Time input | Pre-filled from location's opening hours |

---

### Validation & Warnings

**NPP Warning:** If selected location has `isNPP = true`, a warning banner appears:
> "⚠ Dieser Standort ist ein NPP-Standort. Bitte sicherstellen, dass der Agent NPP-zertifiziert ist."

**Closed day detection:** For each date in the range, the modal checks if the location's opening hours mark that weekday as closed. Closed days are skipped silently (or shown in a summary).

---

### Save Behavior

Iterates through each date from `From` to `To` (excluding weekends if checked, excluding closed days):

- For each date: `POST /api/wic/assignments` with `{ employeeId, locationCode, date, startTime, endTime }`
- Progress indicator: "Speichere N von M..." updates live
- On completion: success count + any failed dates shown

---

### APIs

| Action | Endpoint |
|--------|---------|
| Submit assignment (per day) | `POST /api/wic/assignments` |
| Get opening hours (pre-fill) | `GET /api/wicschedule/opening-hours/{locationCode}` |

### Visual States

| State | Appearance |
|-------|-----------|
| NPP location selected | Warning banner below location dropdown |
| Saving in progress | Progress text "Speichere N von M", button disabled |
| All saved | Success message + close button |
| Some failed | Error list of failed dates |


---

## C2. AL Planning Modal (ALPlanningModal) ⚠ PARTIALLY DOCUMENTED

**File:** Not read directly; used from WIC Attendance right panel.  
**Trigger:** "AL Planning" button in WIC Attendance location header.  
**Purpose:** Plan annual leave for WIC agents at a specific location, checking coverage impact.

⚠ **Field list, save behavior, and API endpoints are unconfirmed. Read `ALPlanningModal.tsx` (or equivalent) before finalising.**

---

## C3. Manual Check-in Modal (ManualCheckinModal) ⚠ PARTIALLY DOCUMENTED

**File:** Not read directly; used from WIC Attendance right panel.  
**Trigger:** "Manual Check-in" button in WIC Attendance location header.  
**Purpose:** Manually record an agent's kiosk check-in when the kiosk is unavailable.

**Likely fields:** Agent dropdown, Location (pre-filled), Check-in time, Shift times.

⚠ **Full field list, validation, and API endpoints are unconfirmed. Read `ManualCheckinModal.tsx` before finalising.**

---

## C4. Substitute Sheet ⚠ PARTIALLY DOCUMENTED

**File:** Referenced in Overview (SubstituteDrawer) and WIC Attendance (SubstituteSheet).  
**Trigger:** "Find Substitute" button (WIC Attendance) or clicking an at-risk day.  
**Purpose:** Find and accept a substitute agent for an uncovered WIC shift.

**Known structure:**
- Slide-out sheet from right edge
- Lists substitute candidates with name, substitute source type (BACKUP / SSP / WIC_DONOR / CALL_IN), and availability
- "Accept" button per candidate → `POST /api/wic/substitutes/accept`

**Substitute source type colors:**

| Type | Color | Badge style |
|------|-------|-------------|
| BACKUP | Purple (`var(--purple)`) | `StatusBadge tone="learn" variant="outline"` |
| SSP | Accent blue (`var(--accent)`) | — |
| WIC_DONOR | Teal (`var(--accent2)`) | — |
| CALL_IN | Orange (`var(--warn)`) | — |
| REGIONAL | Cyan `#22d3ee` / `rgba(0,190,255,.15)` bg | `StatusBadge tone="wic" variant="outline"` — WIC agents assigned to the location via REGIONAL type in `WicAgentAssignments` |

⚠ **Full sheet contents and all APIs are partially inferred. Read the substitute sheet component before finalising.**

---

## C5. Command Palette ⚠ PARTIALLY DOCUMENTED

**Trigger:** Ctrl+K (global keyboard shortcut, registered in AppLayout)  
**Purpose:** Quick navigation and search across the app.

**Known behavior:**
- Opens as a centered modal overlay
- Search input — filters available commands/routes
- Keyboard navigation (↑↓ arrows, Enter to select, Escape to close)

⚠ **Full command list and any action commands are unconfirmed. Read the command palette component before finalising.**

---

## C6. WIC Chat Widget ⚠ PARTIALLY DOCUMENTED

**File:** `Frontend/src/components/WicChatWidget.tsx` (referenced; not read in full)  
**Purpose:** Global floating AI assistant overlay accessible on every page.

**Known behavior:**
- Fixed bottom-right trigger button: Bot icon + "Assistant" label
- Expands to ~400×600px card on click
- Same `ChatPanel` as the dedicated `/assistant` page
- Minimize returns to button
- Persists chat history within the session (not cleared on navigation)

⚠ **Animation details, z-index behavior, and full ChatPanel implementation are unconfirmed.**

