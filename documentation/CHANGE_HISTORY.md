# CHANGE_HISTORY — how the project got here

**Source:** the git history of both repositories, read on 2026-10-11. This is a **thematic** history,
not a commit list: commits are grouped by what they changed and ordered by date. Repository totals:
**89 commits** in `C:\GSDDashboard_secure` (first commit 2026-05-28) and **45 commits** in
`C:\ShiftKiosk_secure` (first commit 2026-05-29).

Where a theme has a first-appearance date for its main file, that date is given. Commit hashes are
shortened and included so any claim here can be re-checked with `git show`.

---

## 1. Foundations (May – June 2026)

| Date | Milestone | Evidence |
|---|---|---|
| 2026-05-28 | Pulse starts as a single dashboard for E.ON GSD: shift plan, WIC coverage, absence tracking, Excel import. | `252cd0f` "Initial commit - EON GSD Dashboard v1.0" |
| 2026-05-29 | ShiftKiosk v2.0 begins as a separate product: a fullscreen Python/tkinter kiosk client plus a FastAPI server and an embedded supervisor dashboard. | `0717d87` "Shift Kiosk System v2.0 - Infosys E.ON" |
| 2026-06-01 | The kiosk is packaged for deployment (PSAppDeployToolkit structure, v2.1). | `ed9cc77`, `f7ca053` |
| 2026-06-05 | Legal shift validation, public holidays and Bundesland, leave-availability and coverage bars, and the WIC Shifts card-grid redesign land in one day. | `affaef6`, `52f879a`, `1ea1d32`, `b05f3a9`, `25815e0`, `97a0340` |
| 2026-06-11 – 06-15 | Attendance correctness work and the training scheduler with coverage/impact ranking. | `aeb191b`, `3308fbb` |
| 2026-06-16 – 06-19 | VWIC page, WIC dashboard major update (heatmap, substitute accept, AL planning, manual check-in), VWIC rotation planner, vacation and sick-leave imports. | `666e4f6`, `9147f4b`, `96362e7` |
| 2026-06-22 – 07-02 | VWIC 24/7 coverage with all Voice agents, then the Break Planner with VWIC rotation awareness and the `VwicRotationSlots` table. | `14e2429`, `d1a1e00` |

## 2. Data hygiene and operational hardening (July 2026)

| Date | Milestone | Evidence |
|---|---|---|
| 2026-07-06 | Sessions of employee-data cleanup: `IsActive` leaks, duplicate/incorrect sick records, renaming, DB integrity, encoding. | `e40dec0`, `b84fa06`, `5632904` |
| 2026-07-22 | Stabilisation pass: error handling, race conditions, validation, dead code; the operational PowerShell/SQL tooling is committed. | `015c0e3`, `fccd62c` |
| 2026-07-28 | **Bulk RTM Entry page** added; NPP site and agent flags with the NPP badge; the WIC Assistant module with a 10-domain router. | `4771630`, `995ce62` |
| 2026-07-29 | Sick-leave category handling, a shared fuzzy name matcher, and the SL/AL merge bug fix. | `d9c2414`, `60f4f19` |
| 2026-07-31 | Self-service WIC opening-hours editor, manual WIC shift entry with opening-hours defaults, swap collision guard, sick-leave overlap protection with a filtered unique index. | `ffddc53`, `1c60a86`, `4d8b6b6`, `66c5ffb` |

## 3. The WorkForce Pulse rebrand and the design system (July – September 2026)

| Date | Milestone | Evidence |
|---|---|---|
| 2026-08-01 | Design-token system introduced, the shell extracted into `layout/`, 17 navigation i18n keys added. | `1a42580`, `552b864`, `2887fe8` |
| 2026-09-01 | Design-token migration completed; coverage fixes; the Pulse Assistant arrives. | `59102ff` |
| 2026-09-03 | **Rebrand to WorkForce Pulse**; WIC conflict detector; assistant coverage table. WIC Coverage display name becomes "WIC Assignments". | `d57b65c` |
| 2026-09-14 | Map basemap switched to CARTO with an API key; `Frontend/.env` untracked. | `60a6da1` |
| 2026-09-15 | GRC fix in the live-attendance duration calculation (use the check-out time for completed agents instead of "now"); documentation and task backup aligned with the live watchdog setup. | `00a8feb`, `bcd9b6e`, `abc1480`, `895ff13` |
| 2026-09-16 | Kiosk: live clock removed in favour of elapsed shift time; the auto-logout threshold moved from 9 h to **11 h**; 3 h / 6 h / 8 h shift reminders; the Phase 2 widget became a compact non-topmost window. | `a1b1f74`, `6e3ddc2` |
| 2026-09-17 – 09-18 | Per-employee per-year AL eligible days; VWIC/primary-role dropdown options; the WIC move-to-GSD action extended to a full date range; WIC assignment uses the agent's working shift and date-resolved opening hours. | `f079ad1`, `83be702`, `fde8411`, `417897d`, `652f4bd` |

## 4. Authentication, RBAC and the Roster Generator (24 September 2026)

The single most consequential day in the Pulse history.

- **KID login + RBAC** introduced: an `AppUserRoles` login table, four roles (`AGENT`, `TEAM_LEAD`,
  `RTM`, `DEV`), a session cookie, a rate-limited login endpoint and a role middleware behind a feature
  flag.
- **Roster Generator** with a WIC mode — the supported way to create a new employee's roster.
- **Missing-roster warning** on the Overview page, with a server-side gate.
- **Deploy sentinel** `C:\HealthCheck\DEPLOY_IN_PROGRESS` introduced so the watchdog cannot relaunch the
  old binary mid-deploy.
- Coverage fix for absent agents (an absent agent stays on the card but is excluded from the coverage
  calculation).

Evidence: `51cf9d2`. Follow-ups: `b0d8ae5` (public holiday is a warning, not a block), `4b1c992`
(break subtracted from shifts over 6 h; Sunday is a warning, not a block), `bfd0623` (WIC coverage
gaps, roster BACKUP walk, shared covered-date logic).

## 5. The ShiftKiosk security layer (2 October 2026)

| Date | Milestone | Evidence |
|---|---|---|
| 2026-10-02 | Additive security migration (flag profiles, observation log, kiosk registry); supervisor accounts with password + TOTP and a device-token admin CLI; the new **NG client** (`3.0.0-ng`) with a SYSTEM broker, device token and durable queue. | `f3ad09e`, `2a27316`, `ffb0d91` |
| 2026-10-02 | Independent review: 10 findings fixed; staged profiles; live tooling; a test fence with a preflight guard. | `9ef5910`, `d916aae` |
| 2026-10-02 | The attack identified in the review is closed at the **dan1** profile; Team-Lead scoping implemented; a dead flag removed. | `5bc7b09` |
| 2026-10-03 | NG packaged for production, one-command laptop installation, PowerShell 5.1 rollout flow, prepared production package. | `d1bd15f`, `ad57c9e`, `e6338c3`, `88e6231`, `9fa2cac` |
| 2026-10-03 | Attendance-row correctness: resolve events per agent before choosing the current status; prevent multi-host joins from duplicating agents; retry transient broker-pipe acquisition before falling back to legacy. | `103617c`, `2a20d81`, `3c1045e` |
| 2026-10-05 | One-click launcher package with one hostname per run; **wall-clock auto-logout** so a suspended laptop still logs out on time. | `ee026ad`, `3f247f5` |
| 2026-10-06 | Duplicate attendance rows fixed and per-group counters added; the actually-installed kiosk template recorded; install/uninstall scripts aligned with what the packages carry. | `375aadc`, `b258083`, `8442e8e`, `8501b3c` |
| 2026-10-07 | Group UI text localised (EN/DE); blank agent username rejected. | `ab0410c` |

Note: the history contains a candid record that a template/package drift once shipped an older client
version in three packages built after a cut-off time; the packaging tool now compares per-file hashes
against the template before adding a package to the distribution list
(`KIOSK_NG_ROLLOUT.md:12-20`).

## 6. Closing the kiosk credential chain and routing kiosk data through Pulse (2 – 5 October 2026)

This is the security story a board should hear, in order:

1. **The exposure (found 2026-10-02).** The kiosk server published each agent's `employee_id` on an
   endpoint that required no credential at all. Because Pulse's KID-only sign-in accepted a bare
   employee id, anyone who could reach the kiosk server could sign in to Pulse **as any agent**. The
   exposure was bounded to `AGENT` level — a cross-database check showed no kiosk identity mapped to a
   `DEV`, `RTM` or `TEAM_LEAD` role. Evidence: `auth_rbac.md:255-269`.
2. **Pulse side (2026-10-02, `8bf15fc`, `14d07cf`).** An authenticated server-side proxy
   (`GET /api/kiosk/attendance`) with an always-on role gate: anonymous callers get **401**,
   authenticated `DEV`/`RTM`/`TEAM_LEAD` are allowed, every other role gets **403** — independently of
   the global RBAC flag. On the kiosk side, supervisor accounts (password + TOTP) and the admin CLI to
   create them were added. **Correction added 2026-10-11:** the *interface* between the two was never
   built. A repository search of Pulse finds no supervisor-mode setting, no provider interface and no
   client class, and no Pulse configuration key selects a supervisor mode, so the kiosk's verification
   endpoint has no caller. The supervisor sign-in is usable on the kiosk dashboard only; several
   documents describe it as protecting Pulse, and that is wrong. See
   [SECURITY_MODEL.md](./SECURITY_MODEL.md) §4 and [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md)
   item **S14**.
3. **Kiosk side (dan1, 2026-10-02).** The legacy key was restricted to the six kiosk endpoints, and
   session tokens were made unable to call them — closing the anonymous-session → `POST /api/status`
   impersonation chain without requiring agents to authenticate.
4. **Routing (2026-10-04, `bbe7bff`, `d627df2`).** All kiosk browser traffic, including the agent list
   and the manual check-in/log-out modal, now goes through Pulse's authenticated `/api/kiosk/*`
   routes; the embedded frontend key and tunnel URL were removed and their absence checked in the
   built JavaScript.
5. **Cutover (2026-10-05).** The owner-authorised cutover and manual kiosk acceptance are live; the
   backend runs from a protected copy under SYSTEM. Verified backups, a code/config/import rollback
   snapshot and read-only forensic comparisons are retained. Evidence: `LIVE_DEPLOY_RUNBOOK.md:29-95`.
6. **Still open.** At dan1 an anonymous caller can still obtain a session token and use it to read
   history, audit-log and exports until **dan3**. `anon_identifiers`, `session_auth` and
   `supervisor_auth` remain in shadow. This is the single most important honest caveat in the whole
   security story — see [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md).

## 7. Group separation, EN/DE-only UI, and the newest features (6 – 9 October 2026)

| Date | Milestone | Evidence |
|---|---|---|
| 2026-10-06 | **WIC Attendance and Remote Attendance separated into two pages** so that an on-site WIC view and a remote/work-from-anywhere view can never be confused. | `baeb310` |
| 2026-10-06 | Sources aligned with the live deployment (no behaviour change) and the tests/tooling/documentation that are *not* on the live deployment recorded. | `58a39ae`, `5827adc` |
| 2026-10-07 | **EN/DE-only UI enforced** as a permanent rule; Remote Attendance localised; a team lead can move an agent to GSD work, and the manual actor is resolved from the session (`ResolveManualActor`, `ManualChangeBy`/`At`). | `e0fe75a` |
| 2026-10-08 | One agent may cover several **non-overlapping** centres in one day. | `99d1e79` |
| 2026-10-09 | **Rollout page**: a WIC centre opened on a single date for new workers, entered by hand, stored in two new additive tables, honoured by coverage because it projects one `WicOpeningHours` row with `ChangeNote='ROLLOUT'`. | `7387352` |
| 2026-10-09 (uncommitted) | **Bulk RTM Entry** reworked: one agent name per line, whole day, no time; a typo is never auto-corrected. Present in the working tree only — see the note below. | working tree: `Frontend/src/pages/BulkRtm.tsx`, `bulkRtmParse.ts`, `Backend.Tests/BulkRtmPasteTests.cs` |

> **Working-tree caveat.** On 2026-10-11 the `GSDDashboard_secure` working tree contains
> **uncommitted** changes: `Backend/BoListService.cs`, `Backend/ShiftService.cs`, `CLAUDE.md`,
> `Frontend/package.json`, both locale files, `BulkRtm.tsx`, `resolveEmployee.ts`, several PowerShell
> scripts and `Tools/Read-LiveTableBaseline.ps1`, plus untracked files for the Bulk RTM feature and
> four `.tmp-*deploy*` folders. Those changes are **not** part of this documentation task and were
> deliberately left untouched. Any statement in this documentation set about Bulk RTM describes the
> rule recorded in `CLAUDE.md`, and is marked where it depends on uncommitted code.

## 8. Themes worth naming in a board narrative

1. **From Excel to a system of record.** The original workflow was Excel files, e-mail and manual
   reconciliation. Today the shift plan, WIC assignments, absences and attendance all live in one
   database with one UI, and the June Excel import is dead.
2. **From invisible exposure to measured control.** The kiosk credential exposure was found, bounded,
   fixed on both sides, and is now governed by nine explicit flags with an observation log — promoted
   on evidence rather than on opinion.
3. **Manual edits are protected.** Every human change carries an actor and a timestamp, and automatic
   writers are not allowed to overwrite them. This is what makes the data defensible.
4. **Operational discipline learned the hard way.** Watchdog, sentinel, staged profiles, packaging
   hash checks, wall-clock timers, and the rule that `/health` returning 200 proves nothing.
5. **A deliberately reversible path.** Batch snapshots, rollback snapshots, verified backups and
   separate rollback scopes for flags, code, schema and Pulse.
