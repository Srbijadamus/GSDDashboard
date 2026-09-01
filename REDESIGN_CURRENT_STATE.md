# Current-State Design Report
## EON GSD Dashboard + ShiftKiosk — Visual Redesign Brief for External Agent

> **Purpose of this document:** A second AI agent will use this as its sole reference to redesign both tools. It will not have direct access to the codebase at the start. Every statement here is based on reading the actual source files; nothing is assumed.

---

## CRITICAL CONTEXT — TWO SEPARATE STACKS

These are two entirely independent tools that share a domain (EON GSD shift operations) but have nothing else in common technically. A redesign must produce two separate, independent deliverables:

| | GSDDashboard | ShiftKiosk |
|---|---|---|
| **Type** | Internal web app | Windows desktop kiosk |
| **Stack** | React 19 + TypeScript + Vite | Python 3.12 + Tkinter |
| **Styling system** | CSS custom properties + inline React styles | Hardcoded hex color constants in Python |
| **Styling language** | CSS (but expressed as JS objects in `style={{}}`) | Tkinter widget arguments (`bg=`, `fg=`, `font=`) |
| **Location** | `C:\GSDDashboard\Frontend\src\` | `C:\ShiftKiosk\kiosk\main_kiosk.py` |

A web design system (tokens, component classes, CSS variables) applies **only to GSDDashboard**. Tkinter cannot render CSS, border-radius, box-shadows, gradients, or custom fonts — any redesign of ShiftKiosk must be expressed as changes to Python/Tkinter widget properties only.

---

## REPO STATE — MUST FIX BEFORE REDESIGN

**ShiftKiosk** (`C:\ShiftKiosk`): The local `main` branch has 11 commits that have never been pushed. The remote has 1 commit (dated June 2) that was not merged locally — a `git merge` was attempted and aborted due to conflicts in `kiosk/main_kiosk.py`, `deploy/Files/main_kiosk.py`, and `deploy/Deploy-Application.ps1`. The git state is **diverged and unresolved**. The second agent must NOT edit any files in this repo until the developer has resolved the merge conflict and confirmed the branch state is clean.

**GSDDashboard** (`C:\GSDDashboard`): Local `main` is ahead of `origin/main` by 7 commits as of this writing, all pushed. Git state is clean. Safe to work on.

---

---

# PART 1 — GSDDashboard

## 1. Inventory of Screens / Views

### Navigation structure
The app uses a fixed 200px left sidebar with a flat list of 21 nav links, no grouping or sections. The active link is indicated by `var(--accent)` color and a 2px blue left-border highlight. Nav items each have a Lucide icon (14px) and a 12px text label.

### Pages (all served at `/` within a shared shell)

| Route | File | Purpose |
|---|---|---|
| `/` | `Overview.tsx` | Command-centre: KPI row, coverage heatmap, Germany map, recommendations, absence feed, risk radar, team lead summary, expandable WIC detail grid |
| `/shifts` | `Shifts.tsx` | Shift plan table for GSD agents |
| `/wic-shifts` | `WICShifts/index.jsx` | WIC location cards + per-location agent rows; uncovered banner |
| `/vwic` | `VWICPage.tsx` | VWIC (virtual WIC) rotation slots |
| `/breaks` | `BreakPlanner.tsx` | Break scheduling planner |
| `/wic-attendance` | `WicAttendance.tsx` | **Master WIC page**: 260px left sidebar (location list + check-in panel + date picker) + right content (agent chips, forecast calendar, schedule editor) |
| `/wic-schedule` | `WicSchedule.tsx` | WIC opening hours management (separate view) |
| `/pipeline` | `Pipeline.tsx` | WIC pipeline items (upcoming visits/events) |
| `/training` | `Training.tsx` | Training schedule and topics |
| `/wic` | `WicLocations.tsx` | WIC location master data management |
| `/attendance` | `Attendance.tsx` | Daily attendance tracking (non-WIC) |
| `/sickleave` | `SickLeave.tsx` | Sick leave: KPI cards, grouped agent table, 7/14-day calendar grid |
| `/vacations` | `Vacations.tsx` | Annual leave records |
| `/albalance` | `ALBalance.tsx` | Annual leave balance per agent |
| `/alcalendar` | `ALCalendar.tsx` | Annual leave calendar view |
| `/employees` | `Employees.tsx` | Agent master data, activation/deactivation |
| `/wic-coverage` | `WicCoverage.tsx` | WIC coverage analysis |
| `/wic-al` | `WicAnnualLeave.tsx` | WIC-specific annual leave view |
| `/assistant` | `WicAssistant.tsx` | AI chat assistant (GSD Assistant) |
| `/bo-list` | `BoList.tsx` | BO (Backoffice) daily entry list |
| `/bulk-rtm` | `BulkRtm.tsx` | Bulk RTM (Ready To Move) entry form |

### Modals and overlays

| Component | Trigger | File |
|---|---|---|
| `AddSickLeaveModal` | "+ Krankmeldung" button on SickLeave page | `SickLeave.tsx` |
| `DrillDownModal` | Clicking KPI cards on SickLeave page | `SickLeave.tsx` |
| `AssignAgentModal` | "Assign Agent" button on WicAttendance | `AssignAgentModal.tsx` |
| `ALPlanningModal` | "AL Planning" button on WicAttendance | `ALPlanningModal.tsx` |
| `ManualCheckinModal` | "Manual Check-in" button on WicAttendance | `ManualCheckinModal.tsx` |
| `NewShiftModal` | Add shift button in WICShifts | `WICShifts/NewShiftModal.jsx` |
| `ReassignModal` | Reassign action in WICShifts | `WICShifts/ReassignModal.jsx` |
| `CommandPalette` | Ctrl+K keyboard shortcut | `components/CommandPalette.tsx` |
| `Sheet` (drawer) | "Find Substitute" button, map pin click | `components/Sheet.tsx` |
| `WicChatWidget` | Floating button (always visible) | `components/WicChatWidget.tsx` |

---

## 2. Current Visual State

### Shell layout (`App.tsx`)
- **Sidebar**: 200px fixed, `background: var(--sidebar)`, `border-right: 1px solid var(--border)`. Logo area: blue `GSD` badge (10px font, IBM Plex Mono) + "EON GSD Dashboard" (12px font). The list is scrollable and has no section headings or visual groupings.
- **Topbar**: Full width, `padding: 10px 20px`. Shows "EON GSD Dashboard" text on the left (redundant with sidebar) and date, horizon toggle, Ctrl+K button, language toggle, theme toggle on the right.
- **Main content**: `padding: 20px`, `overflowY: auto`. Note: `WicAttendance` overrides this with `margin: -20px` to create its own full-height panel layout — this means WicAttendance visually breaks out of the normal padding, which is intentional but fragile.

### What looks bad today (honest assessment)

1. **21 flat nav items with no grouping.** The sidebar lists everything from "Overview" to "Bulk RTM Entry" without any section headers. A user has to scan all 21 items to find anything. WIC-related pages (6+) are interspersed with HR pages (sick leave, vacations, AL).

2. **Topbar duplicates the sidebar header.** "EON GSD Dashboard" appears in both the sidebar logo area and the topbar as a large label. The topbar mainly serves as a container for controls that don't belong to any page.

3. **Everything is inline styles.** All 50+ component files use `style={{}}` JSX inline styles. There is no shared component library. The CSS file (`index.css`) defines CSS variables and three animations but almost nothing else. Tailwind is imported (`@tailwind base/components/utilities`) but its utility classes are essentially unused in components — only the `skeleton` and `spin` class names from `index.css` appear.

4. **Font sizes are very small.** Nav labels: 12px. Table headers: 10px uppercase. Table cells: 11–12px. Sidebar check-in panel labels: 8–10px. KPI card subtext: 10px. Several inline stat numbers go as small as 9px (e.g. agent chip status labels in WicAttendance).

5. **Overview page has no visual hierarchy.** The page is a single vertical stack of ~8 major sections (KPIs, heatmap, map, recommendations, absences, radar, team leads, detail grid) with only uppercase 10–11px `SectionHeader` labels between them. Everything has the same card-with-border treatment.

6. **Heatmap cells are tiny and hard to read.** Each cell is 34x22px with just a small colored dot or colored background. No number is shown in the cells (coverage counts are only visible on hover via `title` attribute). The status color is the only signal.

7. **Mixed German/English.** Some pages are German (SickLeave modal titles: "Krankmeldung erfassen", "Agent auswaehlen"; button: "+ Krankmeldung"). Most of the WIC pages use i18n properly. This is inconsistent. The WicAttendance opening hours editor uses bilingual labels inline: "Save / Speichern", "Effective from / Gültig ab".

8. **Native browser `alert()` and `confirm()` are still used.** `SickLeave.tsx` lines 89, 161, 316 use `alert()` and `confirm()`. These pop up native OS dialogs that are styled completely differently from the app.

9. **Buttons have no consistent size or padding.** Primary buttons (`background: var(--accent)`) appear at 5px–16px vertical padding depending on context. Icon-only buttons vs. text buttons vs. icon+text buttons have no standardized sizing.

---

## 3. Component & Pattern Catalog

### Reusable components

| Component | File | Description |
|---|---|---|
| `Sheet` | `components/Sheet.tsx` | Right-sliding drawer, 440px wide. Semi-transparent backdrop, slide-in transition, X close button. Used for substitute finder. |
| `CoverageBadge` | `components/CoverageBadge.tsx` | Colored status pill. `compact` prop for small inline version. |
| `NppBadge` | `components/NppBadge.tsx` | Small "NPP" indicator for nuclear-power-plant WIC sites. |
| `ThemeToggle` | `components/ThemeToggle.tsx` | Light/dark toggle. |
| `CommandPalette` | `components/CommandPalette.tsx` | Ctrl+K spotlight search. |
| `WicChatWidget` | `components/WicChatWidget.tsx` | Floating AI chat bubble. |
| `ShiftBadge` | `components/ShiftBadge.tsx` | Shift type badge. |
| `DownloadButtons` | `components/DownloadButtons.tsx` | Today / 7d / 30d CSV download group. |

### Page-level sub-components (defined inline in their parent files)

- **`KpiCard`** (`Overview.tsx:53`): Card with top colored border, uppercase label (10px), large monospaced number (32px). Skeleton loading state. 5-column grid on Overview.
- **`WicCard`** (`Overview.tsx:350`): Small location card showing name, city, status badge, and per-agent shift times. Used in the expandable detail grid.
- **`TLCard`** (`Overview.tsx:335`): Team lead summary card with grid of 5 stats (Total, Working, On AL, On SL, WIC).
- **`WicMapView`** (`Overview.tsx:121`): Leaflet map of Germany, circle markers color-coded by coverage status, popups with "Find substitute" button.
- **`SubstituteDrawer`** (`Overview.tsx:212`): Content inside the Sheet — ranked list of substitute candidates with source-type badge, distance, score, accept button.
- **`GroupedSickTable`** (`SickLeave.tsx:302`): Expandable grouped table. Primary rows = one per agent; clicking expands to show individual sick-leave periods. `CommentCell` component for inline editable notes.
- **`DayGrid`** (`SickLeave.tsx:225`): Day-by-day sick coverage grid. Agents as rows, dates as columns. Red cell if sick on that day.
- **`WicScheduleEditor`** (`WicAttendance.tsx:221`): 7-day grid for editing opening hours per weekday. Each day has Open/Closed toggle, time inputs, optional second window. Shows consequence list after save.

### Data-heavy views

| View | Density problem |
|---|---|
| Coverage heatmap (Overview) | Very dense. 30+ location rows × 28 date columns. Cells are 34px wide with a colored dot. Hard to read at a glance. |
| WicAttendance sidebar list | 60+ locations in a 260px sidebar, each row ~35px. Text is 12px. Small `CoverageBadge` is the only status signal. |
| GroupedSickTable | Reasonable density but comment cell hover-to-edit pattern is non-obvious. |
| WIC agent chips | Cards in a `flexWrap` grid, minimum 130px wide. 9px subtext. Dense when a location has many agents. |
| WICShifts/AgentRow | Unknown without reading (JSX file). |

### Color conventions that carry semantic meaning — MUST be preserved

These colors have functional meaning throughout the app. A redesign may restyle but must not reassign the mapping:

| Meaning | Current color | Variable |
|---|---|---|
| COVERED status | `#22d07a` (bright green) | `var(--status-covered)` / `var(--green)` |
| PARTIAL status | `#ff7c3b` (orange) | `var(--status-partial)` / `var(--warn)` |
| UNCOVERED status | `#ff3b5c` (red) | `var(--status-uncovered)` / `var(--danger)` |
| CLOSED status | `#7a8fa8` (muted grey-blue) | `var(--status-closed)` / `var(--text3)` |
| Sick Leave (SL) absence chip | `var(--danger)` bg at 15% opacity | Hardcoded `rgba()` in component; token: `--st-crit-solid` |
| Annual Leave (AL) absence chip | `var(--accent)` bg at 15% opacity | Hardcoded `rgba()` in component; token: `--st-info-solid` |
| Half AL absence chip | `var(--accent)` bg at 8% opacity | Hardcoded `rgba()` in component; same token, lower opacity |
| Substitute source: BACKUP | `var(--purple)` / `--st-learn-solid` | |
| Substitute source: SSP | `var(--accent)` / `--st-info-solid` | |
| Substitute source: WIC_DONOR | `var(--accent2)` / `--st-wic-solid` | |
| Substitute source: CALL_IN | `var(--warn)` / `--st-warn-solid` | |
| Substitute source: REGIONAL | `var(--accent2)` / `--st-wic-solid` | Same token as WIC_DONOR. `StatusBadge tone="wic" variant="outline"`. Light: `#06AED4`; dark: `#22CCEE`. |
| Live kiosk presence | pulsing dot | `--signal-live` (`pulse-live` CSS class in `index.css`) |
| Agent coverage FULL | `AGENT_MATCH_COLORS.FULL` | `var(--green)` / `--st-good-solid` |
| Agent coverage PARTIAL | `AGENT_MATCH_COLORS.PARTIAL` | `var(--warn)` / `--st-warn-solid` |
| Agent coverage NONE | `AGENT_MATCH_COLORS.NONE` | `var(--danger)` / `--st-crit-solid` |
| NPP site warning | border | `var(--danger)` / `--st-crit-solid` (hardcoded in component) |

---

## 4. UX Pain Points & Functional Constraints

### Active pain points

1. **No sidebar navigation grouping.** 21 flat links. Experienced users scan by memory; new users are lost. Suggested logical groups: WIC Operations | Shift Management | Leave & Absence | People | Reports & Tools.

2. **WicAttendance toolbar is a row of 4 unlabeled-by-context buttons.** "AL Planning", "Assign Agent", "Manual Check-in", "Find Substitute" — the first three open modals without clear visual distinction of what they do. Buttons are identical in style (card bg, 1px border, same size).

3. **Heatmap has no numeric data in cells.** Only color. The coverage count (e.g. "2/3 agents") is only visible on hover via `title`. Clicking a cell opens a Sheet drawer — but only for UNCOVERED/PARTIAL cells; clicking COVERED cells does nothing, with no affordance to distinguish clickable from non-clickable cells.

4. **Overview scrolling.** The page can be 3000+ pixels tall on busy days. There is no sticky section navigation or collapsed-by-default approach except the "WIC Detail Grid" which is collapsed.

5. **`alert()` / `confirm()` dialogs.** These produce native OS dialogs that break the visual context. They are jarring and can appear behind the fullscreen kiosk on Windows.

6. **Language inconsistency.** `SickLeave.tsx` is entirely in German in the UI code (button labels, modal titles). Other pages use `i18n` with DE/EN keys. The "bilingual inline" pattern used in WicAttendance (`"Save / Speichern"`) works but is visually noisy.

7. **AssignAgentModal date feedback.** When a date range spans closed WIC days, only the "closed day" banner and day-count badge communicate this. The submit button still says "X Tage zuweisen / Assign X days" — confusing if some days will be skipped.

### Hard constraints a redesign must NOT break

- **DE/EN localization**: Both languages must remain functional. The `react-i18next` system is the mechanism; don't replace or restructure the i18n key structure.
- **Color semantics**: All status colors listed in Section 3 must retain their meaning.
- **WicAttendance split-panel layout**: The 260px sidebar + right content structure is a deliberate choice for working with a selected location. The layout must stay navigable.
- **Sheet drawer**: The right-side substitute finder sheet is used in multiple places. Don't convert it to a modal.
- **Coverage heatmap clickability**: UNCOVERED and PARTIAL cells open the substitute Sheet. This behavior must be preserved.
- **Live presence indicators**: The pulsing green dot for kiosk-checked-in agents is a live operational signal. Must remain visually prominent.

---

## 5. Tech Constraints for the Redesigner

### React/TypeScript environment

| Item | Detail |
|---|---|
| React version | 19.2.6 |
| TypeScript | Yes, strict |
| Build tool | Vite 6 |
| Router | React Router 7 |
| Data fetching | TanStack Query 5 |
| Icons | Lucide React 1.16 |
| Map | React Leaflet 5 / Leaflet 1.9 |
| i18n | react-i18next 17, i18next 26 |
| Theme | next-themes 0.4 (dark/light class on `<html>`) |
| Charts | Recharts 3 (imported, may be unused on some pages) |
| **Component library** | **NONE** — no MUI, no shadcn, no Chakra |
| CSS approach | CSS custom properties in `index.css`; all component styles are inline `style={{}}` objects |
| Tailwind | Imported in `index.css` but utilities almost unused in components — Tailwind utility classes CAN be used in a redesign |

### How frontend is built and served

```
Frontend: C:\GSDDashboard\Frontend\
Build command: npm run build
  = tsc -b && vite build && cp -r ../Backend/wwwroot/. ../Backend/bin/Release/net8.0/wwwroot/
Output: C:\GSDDashboard\Backend\wwwroot\ (copied to bin/Release/... on build)
Served by: .NET 8 backend (UseStaticFiles) managed by Windows Task Scheduler task "GSDDashboard-Backend"
Deploy sequence: Stop task → npm run build → Start task
```

### Key files the redesigner will edit

```
Frontend shell & nav:
  Frontend/src/index.css          ← all CSS variables, global styles
  Frontend/src/App.tsx            ← sidebar, topbar, route definitions
  Frontend/src/main.tsx           ← React root

Pages (primary candidates):
  Frontend/src/pages/Overview.tsx
  Frontend/src/pages/WicAttendance.tsx
  Frontend/src/pages/SickLeave.tsx
  Frontend/src/pages/Employees.tsx
  Frontend/src/pages/Shifts.tsx
  Frontend/src/pages/WICShifts/index.jsx  (and AgentRow.jsx, LocationCard.jsx)

Components:
  Frontend/src/components/Sheet.tsx
  Frontend/src/components/CoverageBadge.tsx
  Frontend/src/components/CommandPalette.tsx
  Frontend/src/components/WicChatWidget.tsx

Modals:
  Frontend/src/pages/AssignAgentModal.tsx
  Frontend/src/pages/SickLeave.tsx (contains AddSickLeaveModal, DrillDownModal inline)
```

---

## 6. GSDDashboard Redesign Brief

The primary user is a **GSD team lead or operations manager** who checks the dashboard multiple times a day to monitor WIC coverage, track sick leave, and manage shift gaps. They know the data well; they need to get to the right information fast.

- **Make navigation purposeful.** Group the 21 nav items into 4–5 logical sections (WIC, Shifts, Leave & Absence, People, Tools). Add section headers in the sidebar. Consider collapsible sections if that helps density.
- **Promote the signal, suppress the noise.** The coverage heatmap and the WIC map are the two most-used views. Make them the visual focal point on Overview — not one item in a long scroll. Add coverage counts to heatmap cells; distinguish clickable cells visually.
- **Standardize typography and spacing.** Pick 3 font sizes: page headings (~18px), body (~13px), metadata/labels (~11px). Nothing in the main UI at 9–10px except truly supplementary captions. Increase table row heights by 2–4px.
- **Replace native dialogs.** Remove all `alert()` and `confirm()` calls and replace with in-page error states or styled confirmation modals that match the app's visual language.
- **Consolidate the language.** Adopt the i18n system everywhere. SickLeave.tsx modal text should use translation keys, not hardcoded German strings. The bilingual inline pattern ("Save / Speichern") should be eliminated in favor of the LangToggle mechanism.

---
---

# PART 2 — ShiftKiosk

## 1. Inventory of Screens / Windows

ShiftKiosk is a single Python file (`C:\ShiftKiosk\kiosk\main_kiosk.py`, 1037 lines) implementing two sequential UI phases and several dialogs.

### Phase 1 — Fullscreen Kiosk (`Phase1Kiosk` class, line 701)

The primary screen shown at startup and whenever no agent is on shift. It is a full-screen `tk.Tk()` window with `overrideredirect(True)` and `attributes("-fullscreen", True)`. The agent cannot Alt+Tab, Win, or Alt+F4 out (keyboard hook installed).

**Layout:**
- Top third of screen: Header frame
  - Company name label (28pt bold, white on `#0D1117`)
  - `USERNAME | HOSTNAME` label (15pt, grey `#8B949E`)
  - Date label (13pt, grey)
  - Clock label (46pt bold, white) — updates every second
  - Subtitle: "Bitte Schichtstatus wählen" / "Please select your shift status" (14pt, grey)
  - Holiday banner (optional): yellow `#FFD600` background, dark text — appears if today is a public holiday
- Bottom two thirds: Button zone
  - 4 buttons (200×120px each), centered horizontally with 20px gaps:
    1. **Start Shift** — `#00C851` green
    2. **Sick Leave** — `#FFD600` yellow (dark text)
    3. **Pause** — `#FF8C00` orange
    4. **Emergency** — `#FF1744` red
  - Feedback label (15pt bold, green or yellow) — appears briefly after action, positioned below buttons
  - Queue indicator label (11pt, yellow) at screen bottom — shows count of pending offline requests
- Auto-logout overlay (hidden by default): full-screen yellow `#FFD600` frame that slides over everything when the 9-hour timer expires; shows warning body text (26pt), large countdown number (64pt)

### Phase 2 — Floating Widget (`Phase2Widget` class, line 845)

Appears after "Start Shift" is confirmed. A compact 640×80px floating window positioned at the bottom-center of the screen, always on top (`attributes("-topmost", True)`).

**Layout:**
- 3px blue (`#2196F3`) accent bar at top
- Dark navy background (`#1a1a2e`)
- Left section (160px): Clock (20pt bold, white) + date (11pt, grey), cursor `fleur` — drag handle
- Thin separator (1px `#30363D`)
- Right section: 4 buttons in a grid row
  1. **Submit Sick Leave** — yellow `#FFD600`
  2. **Report Emergency** — red `#FF1744`
  3. **Pause / End Pause** — orange `#FF8C00` (toggles)
  4. **End Shift** — blue `#2196F3`

**Special behaviors:**
- Clicking the clock area (not dragging) toggles button visibility → widget shrinks to 165×80px (clock only)
- Dragging the clock area moves the widget anywhere on screen
- Every 3 seconds: widget lifts itself to top (`after(3000, keep_on_top)`)

### Location selection dialogs (shown when "Start Shift" is tapped)

Invoked by `_ask_work_location_v2()` → `_show_center_dialog()`:

**1-center dialog** (440×250px `tk.Toplevel`):
- Title: "Select Location"
- Prompt text (14pt bold, white on `#0D1117`)
- Info card (dark `#161B22` background): center name (13pt bold), role badge (color-coded: MAIN=green, BACKUP=blue, REGIONAL=purple)
- Buttons: Confirm (green), Home Office (blue), Cancel (dark grey)

**2+ centers dialog** (460×variable height `tk.Toplevel`):
- Prompt text (14pt bold)
- Radio buttons — one per center, with colored role badge label
- Separator line
- Home Office radio option
- Confirm + Cancel buttons

### Native dialogs (OS-provided)

- **Confirmation dialogs** (`messagebox.askyesno`): Used for all action confirmations (start shift, end shift, sick leave, pause). These are native Windows dialog boxes — blue/white Windows style, completely outside the app's visual language.
- **Emergency text input** (`simpledialog.askstring`): Standard Windows text input dialog.
- **Error dialogs** (`messagebox.showerror`): Native Windows error dialog (red icon, white background).

### Auto-logout overlay (Phase 2)

A full-screen `tk.Toplevel` over the widget. Same yellow `#FFD600` design as Phase 1's overlay. 30-second countdown shown in 64pt.

---

## 2. Current Visual State

### What works
- The fullscreen dark design (`#0D1117` background) reads cleanly in a kiosk environment — high contrast, white text.
- The 4 big colored buttons in Phase 1 are immediately legible from a distance and color-differentiated by action type.
- The company name + clock at large sizes is appropriate for a shared-screen kiosk.

### What looks bad today (honest assessment)

1. **Phase 2 buttons are far too small for a touchscreen.** At 9pt font on buttons that are roughly 110px wide × ~36px tall, these are mouse-sized targets, not finger-sized. On a touch kiosk, agents will mis-tap.

2. **The drag-and-toggle interaction on Phase 2 is invisible.** There is no visual hint that the clock area is draggable or that a click toggles the button panel. The cursor changes to `fleur` but only on hover — agents don't know to try clicking the clock.

3. **Native OS dialogs shatter the visual experience.** Every confirmation, error, and text input uses a native Windows dialog box (light grey/white, blue title bar, standard Windows icons). These appear over the custom dark kiosk and look completely unrelated to it.

4. **Phase 2 is cramped.** Four action buttons in 480px (640 - 160 for clock) at 9pt font. Long button labels like "Submit Sick Leave" and "Report Emergency" are truncated or wrapped (`wraplength=110`).

5. **No visual confirmation of selected WIC location after clock-in.** After an agent confirms their WIC center and Phase 2 appears, nothing in Phase 2 shows which location they clocked in at. The location is sent to the server but not displayed.

6. **Queue indicator is easy to miss.** The offline queue count label (`#FFD600` small text at screen bottom) is positioned far from the action buttons. Agents may not notice they're offline.

7. **The holiday banner uses an inconsistent background color.** The bright yellow `#FFD600` banner (same yellow as the Sick Leave button) can be confused with a Sick Leave state indicator.

8. **Auto-logout overlay is alarming by design but has no dismissal affordance in Phase 2.** In Phase 1, there is a "Stay Signed In" cancel button (actually the cancel label is defined in strings but not implemented as a cancel button — the overlay just runs the countdown and logs out). In Phase 2, the Toplevel overlay also just counts down.

---

## 3. Component & Pattern Catalog

ShiftKiosk has no reusable component abstraction. All UI is built inline in `Phase1Kiosk._build_ui()` and `Phase2Widget._build_ui()` using bare Tkinter widget constructors.

**Recurring patterns:**

| Pattern | Implementation | Locations |
|---|---|---|
| Action button | `tk.Button(relief="flat", bd=0, cursor="hand2")` — full color background, white text | Phase 1 main buttons, dialog buttons |
| Dark card/info box | `tk.Frame(bg="#161B22")` or `tk.Frame(bg="#21262D")` | Location dialog center info box |
| Separator line | `tk.Frame(bg="#30363D", height=1)` | Phase 2 clock/button divider, multi-center dialog |
| Role color badge | `tk.Label(bg=role_color)` — MAIN green, BACKUP blue, REGIONAL purple | Center selection dialogs |
| Queue indicator | `tk.Label(fg="#FFD600")` at screen bottom | Phase 1 |
| Feedback flash | `tk.Label(fg="#00ff88")` below buttons, auto-clears after 3s | Phase 1 |

**Color constants** (defined at module level, `main_kiosk.py` lines 247–254):
```python
BG_COLOR      = "#0D1117"   # very dark near-black
BTN_ON        = "#00C851"   # bright green — Start Shift
BTN_SICK      = "#FFD600"   # yellow — Sick Leave
BTN_OFF       = "#2196F3"   # blue — End Shift / Home Office
BTN_EMERG     = "#FF1744"   # red — Emergency
BTN_PAUSE     = "#FF8C00"   # orange — Pause
TEXT_COLOR    = "#FFFFFF"   # white
SUBTEXT_COLOR = "#8B949E"   # muted grey
```

These colors carry semantic meaning and must be preserved in any redesign.

---

## 4. UX Pain Points & Functional Constraints

### Active pain points

1. **Touchscreen usability.** The kiosk is used by agents clocking in at a shared workstation. If that station has a touch display, Phase 2 buttons (9pt font, ~36px height) are unusable. Phase 1 buttons (200×120px) are appropriately large.

2. **Native dialogs.** `messagebox.askyesno`, `messagebox.showerror`, `simpledialog.askstring` all render in native Windows style. A redesign should replace these with custom `tk.Toplevel` dialogs that match the dark kiosk aesthetic.

3. **No location indicator in Phase 2.** After clock-in the agent has no visual confirmation of their assigned location. If they clock in at the wrong location by mistake, there is no way to tell from Phase 2.

4. **Phase 2 toggle/drag discoverability.** The click-to-collapse interaction needs a visual affordance (e.g. a dedicated handle or toggle icon rather than a hidden click on the clock).

5. **Phase 1 has 4 buttons but "Sign Out" is absent.** The OFF action ("Sign Out") is only available in Phase 2. An agent who arrives at the kiosk (Phase 1 shown) but was previously clocked in from another terminal has no way to sign out from Phase 1.

### Hard constraints a redesign must NOT break

| Constraint | Why |
|---|---|
| Fullscreen `overrideredirect(True)` in Phase 1 | Prevents task-bar and window-decoration access — kiosk lock |
| Low-level keyboard hook (Win, Alt+Tab, Alt+F4 blocked) | Kiosk security — implemented in `_install_key_hook()` |
| Phase 2 `attributes("-topmost", True)` and `keep_on_top` loop | Widget must stay visible during agent's shift |
| `QUEUE_FILE` JSON offline queue + background flush thread | Offline resilience — if network drops, events are queued and replayed |
| DE/EN language auto-detection from Windows locale | Multi-language site (Germany + possibly other regions) |
| Supervisor exit shortcut `Ctrl+Shift+Alt+A` | Staff can exit kiosk mode without agent involvement |
| Single-instance mutex (`ShiftKioskMutex_v21`) | Prevents duplicate kiosk processes |
| `_agent_id` resolved from Windows username at startup | Passwordless identity — no login screen |

### Accessibility / readability

- Phase 1 buttons: adequate size (200×120px) and high contrast (colored bg, white text). Acceptable.
- Phase 1 clock: 46pt — highly readable from distance. Good.
- Phase 2 buttons: insufficient size and font for touch. Bad.
- Phase 2 date text (11pt, `#8B949E` on `#1a1a2e`): contrast ratio may be below 4.5:1. Verify.
- Phase 1 feedback label (`#00ff88` bright green): readable but disappears after 3s — agents at shared screens may miss it.

---

## 5. Tech Constraints for the Redesigner

### Python/Tkinter environment

| Item | Detail |
|---|---|
| Python version | 3.12 |
| GUI framework | `tkinter` (standard library tk 8.6) |
| ttk (themed widgets) | **NOT used** — all widgets are bare `tk.*` |
| Third-party UI libraries | **None** |
| Font system | Segoe UI (specified by name), standard Windows fonts only |
| Custom fonts | Cannot use web fonts; only fonts installed on the OS |
| Border radius | **Not possible** on `tk.Frame` or `tk.Button` — always rectangular |
| Shadows / gradients | **Not possible** in standard tkinter |
| Smooth animations | Not possible; only `after()` polling approximations |
| Images | `tk.PhotoImage` (GIF/PNG); images CAN be used in `tk.Label` or `tk.Button` |
| Canvas drawing | `tk.Canvas` can draw arcs, rounded rectangles (custom widget) |
| Scaling/DPI | No automatic DPI scaling; geometry must be set manually |
| Screen resolution | 1920×1080 assumed (Cloud PC; kiosk runs fullscreen) |

### What Tkinter CAN and CANNOT do

**CAN:**
- Set `bg`, `fg`, `font`, `relief`, `bd`, `cursor`, `wraplength` on any widget
- Use `tk.Canvas` to draw custom shapes (rounded rects, circles, progress bars)
- Embed PNG images in buttons/labels for icon-like elements
- Create `tk.Toplevel` dialogs with full custom styling (replacing native dialogs)
- Use `relief="flat"` for clean button appearance

**CANNOT:**
- CSS, class-based styling, cascading styles
- Border radius on Frame/Button (requires Canvas workaround)
- Box shadows, drop shadows
- Gradient fills
- Smooth CSS transitions (use `after()` loops for animation)
- Custom fonts without OS installation
- Arbitrary widget z-ordering within a frame (use `place()` geometry manager)

### Key file the redesigner will edit

```
C:\ShiftKiosk\kiosk\main_kiosk.py   ← entire UI is here (1037 lines)
```

Color constants are at lines 247–254. Phase 1 UI is built in `Phase1Kiosk._build_ui()` (line 725). Phase 2 UI is built in `Phase2Widget._build_ui()` (line 874). Dialogs are `_ask_work_location()` (line 381), `_show_center_dialog()` (line 444), `_ask_work_location_v2()` (line 576).

The server URL, API key, and language setting come from `C:\ShiftKiosk\kiosk\config.ini` — not relevant to redesign.

---

## 6. ShiftKiosk Redesign Brief

The users of this kiosk are **customer service agents** clocking in and out at a shared physical workstation at the start and end of their shifts, possibly in a hurry. Clarity and speed of interaction are everything.

- **Make Phase 2 touch-friendly.** The floating widget's 4 action buttons need to be at least 44px tall with 12pt+ font. Consider a taller widget (e.g. 640×100px or 640×120px) or a different layout. This is the most critical UX fix.
- **Replace all native dialogs.** Every `messagebox` and `simpledialog` call should become a custom `tk.Toplevel` styled to match the dark kiosk — dark background, large buttons, Segoe UI 14pt+. This is feasible in pure Tkinter using `tk.Toplevel` with custom widgets.
- **Add a location display to Phase 2.** After the agent confirms their WIC center, store and display the location name somewhere in the Phase 2 widget (even as a small text label above or below the buttons) so the agent can verify they checked into the right site.
- **Make Phase 2 interactions discoverable.** Add a visible drag handle icon (can be a small text symbol `⣿` or a Canvas-drawn grip) and a visible toggle button rather than relying on an invisible click-on-clock gesture.
- **Improve the offline indicator.** Move the queue-pending indicator to be adjacent to the action buttons or show it as a persistent badge, not a small footnote at screen bottom.
