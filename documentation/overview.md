# GSD Dashboard — Feature Overview

## What Is It?

The **GSD Dashboard** is an internal workforce management and visibility platform for the **GSD DE (Global Service Desk Germany)** and **WIC (Workforce In Contact)** teams. It gives team leads and management a real-time window into scheduling, shift coverage, attendance, sick leave, vacations, and annual leave balances.

The database is read by a separate upstream system. The dashboard surfaces data and adds its own write operations for WIC shift management, substitutions, and employee administration.

---

## Who Uses It?

| User | Purpose |
|------|---------|
| Team Leads | Monitor team shifts, leave, and WIC assignments |
| Management | High-level coverage overview across all locations |
| HR / Scheduling | Sick leave, vacations, AL balances, compliance |
| WIC Coordinators | Daily attendance, coverage gaps, substitution |

Approximately **130 employees**, **43 WIC locations** (41 DE, 2 NL).

---

## Pages and Feature Status

### Overview `/`
**Read only.**
- KPI cards: open locations today, at-risk count, closure risk, absences, coverage %
- 14-day coverage heatmap (all 43 WIC locations x next N days)
- WIC map (react-leaflet + OpenStreetMap, CircleMarkers coloured by coverage status)
- Recommendations panel with best-substitute name per gap
- Command palette (Ctrl+K) — search locations, navigate to WIC Attendance

### Shift Plan `/shifts`
**Read only.** Full shift plan calendar, filterable by team lead, role, date range. Colour-coded by shift type.

### WIC Shifts `/wic-shifts`
**Write.** Daily WIC-specific view. Reassign agents between locations. New shift modal to add WIC duty entries.

### VWIC `/vwic`
**Write.** Virtual WIC coverage grid for 07:00-18:00. Timeline view. Add/remove agents from virtual coverage slots. Generate and save rotation plans (daily and weekly with fairness tracking).

### Break Planner `/breaks`
**Write.** Voice-agent break scheduling for a selected date. Auto-distribute 30-min breaks across a configurable window (respects VWIC rotation slots, concurrent staffing limits, daily seeded fairness shuffle). Start / end / cancel individual breaks. Manual override per agent.

### WIC Attendance `/wic-attendance`
**Write.** Per-location coverage with:
- Substitute finder (4-tier ranked: BACKUP / SSP / WIC_DONOR / CALL_IN)
- Accept substitute action (writes to SubstitutionHistory, records confirmed assignment)
- AL planning integration
- Assign agent to location
- Manual check-in

### WIC Schedule `/wic-schedule`
**Read only.** Opening hours per WIC location.

### Pipeline `/pipeline`
**Write.** Create, edit, and delete pipeline events (planned projects / workload at WIC locations).

### Training `/training`
**Read only.** Training topics, sessions, and agent assignments.

### WIC Locations `/wic`
**Read only.** Master list of 43 WIC locations with coordinates, Bundesland, and legacy code mapping.

### Daily Attendance `/attendance`
**Read only.** Daily presence log per WIC location.

### Sick Leave `/sickleave`
**Write.** Sick leave records grouped by agent with day-grid view. Add, edit (patch dates/type), and delete sick leave entries. Editing a record automatically reverts and re-syncs the corresponding ShiftEntries via ShiftSyncService.

### Vacations `/vacations`
**Read only.** Approved, pending, and upcoming vacation requests per employee.

### AL Balance `/albalance`
**Read only.** Eligible / taken / remaining annual leave days per employee.

### AL Calendar `/alcalendar`
**Read only.** Calendar visualisation of annual leave across the team.

### Employees `/employees`
**Write.** Create employees, delete employees, edit AL balance.

### WIC Coverage `/wic-coverage`
**Write.** Agent-centric WIC coverage management. Shows each agent's KID pair, emails, HasCar flag, GroupRegion, reachable cities, and WIC roles. Per-WIC view shows Main, BackupA (assigned backups), BackupB (reachable-city pool), and BackupC (regional assignments). Pin a BackupB agent to promote them to a formal BACKUP assignment.

---

## Backend Services

| Service | Status | Purpose |
|---------|--------|---------|
| `DashboardService` | Implemented | Top-level KPI metrics; per-TeamLead and per-WIC breakdowns |
| `ShiftService` | Implemented | Shift plan queries, Excel export, shift updates |
| `ShiftSyncService` | Implemented | Bidirectional SickLeave/Vacation to ShiftEntries propagation with revert support |
| `ShiftValidationService` | Implemented | Validates shift changes against German labour law rules |
| `AvailabilityResolver` | Implemented | Canonical single-employee and bulk absence resolver; defines `FullAbsenceTypes` |
| `WicShiftService` | Implemented | WIC-specific shifts, on-site vs. office, assignment management |
| `WicCardsService` | Implemented | Per-location coverage status cards using CoverageCalculator |
| `CoverageEvaluator` | Implemented | Canonical COVERED/PARTIAL/UNCOVERED/CLOSED classifier (static + instance variants) |
| `CoverageCalculator` | Implemented | Minute-based coverage overlap calculation for split-hours schedules |
| `WicLocationMatcher` | Implemented | Static helper — 30-entry alias map, matches old-style codes via `LocationCodeLegacy` |
| `ReachabilityService` | Implemented | Haversine distance matrix, 4h TTL cache, Singleton |
| `SubstitutionService` | Implemented | 4-tier ranked substitute engine (BACKUP / SSP / WIC_DONOR / CALL_IN), donor guard, fairness penalty |
| `BackupService` | Implemented | Older substitute candidate engine at `/api/wic/backup` (pre-SubstitutionService) |
| `ForecastService` | Implemented | 14-day coverage forecast per WIC location |
| `WhatIfService` | Implemented | What-if simulation — impact if one agent is absent |
| `BriefingService` | Implemented | Daily briefing JSON + Excel export (absences, gaps, next at-risk) |
| `ALPlanningService` | Implemented | Annual leave planning per WIC location |
| `VwicService` | Implemented | Virtual WIC coverage management (07-18 timeline), rotation plan generator |
| `BreakService` | Implemented | Voice-agent break scheduling, auto-distribution, VWIC-aware, daily fairness shuffle |
| `WicCoverageService` | Implemented | Agent and WIC coverage plan management (KIDs, emails, HasCar, GroupRegion, BackupB pinning) |
| `WicCoverageImport` | Implemented | One-time startup seeder for AgentReachableCities and WicAgentAssignments from hardcoded seed data |
| `SickLeaveService` | Implemented | Sick leave records, stats, create/patch/delete, ShiftSync integration, Excel export |
| `VacationService` | Implemented | Vacation records, Excel export |
| `ALBalanceService` | Implemented | AL balance calculations |
| `EmployeeService` | Implemented | Employee master data |
| `AttendanceService` | Implemented | Daily WIC attendance |
| `PublicHolidayService` | Implemented | National + regional holiday calendar |
| `TrainingService` | Implemented | Training topics and sessions |
| `PipelineService` | Implemented | Pipeline event CRUD |
| `WicScheduleService` | Implemented | WIC opening hours |
| `OverviewService` | Implemented | Cross-module overview aggregation; wic-status and detail endpoints |
| `PlzBundesland` | Implemented | Static PLZ to Bundesland fallback (38-entry map) |

---

## Substitution Tiers

| Tier | Base Score | Condition |
|------|-----------|-----------|
| BACKUP | 10,000 | Designated backup in `WicAgentAssignments` |
| SSP | 5,000 | `PrimaryRole = "SSP"` |
| WIC_DONOR | 0 + reachability (0-500) | Nearby WIC with surplus agents |
| CALL_IN | -100 + reachability | All remaining reachable agents |

Donor guard: if `surplus = effectiveCoverage - minRequired <= 0`, the donor location is skipped.
Fairness penalty: `-LoadScore * 10` based on 30-day substitution history.

---

## Excel Exports

Every major data view has an `.xlsx` download button. BriefingService exports a 3-sheet workbook (Absences, Coverage Gaps, Next AT_RISK Days). VwicService exports week rotation plans (per-day sheets + Summary sheet).

---

## Internationalisation

Full **German (DE)** and **English (EN)** UI, switchable at runtime. No other languages. Translation files in `Frontend/src/i18n/locales/{de,en}/common.json`.

---

## Theme

Dark and light mode, switchable via the topbar ThemeToggle. IBM Plex Sans (body) and IBM Plex Mono (data cells). Powered by `next-themes` with `darkMode: "class"` in Tailwind.
