# ROSTER_AND_SCHEDULING — where shifts come from and how they are protected

**Verification basis:** `C:\GSDDashboard_secure` (backend and database), read **read-only** on
**2026-10-11**. Every rule cites the file that implements it.

**Related documents:** [PULSE_FEATURES.md](./PULSE_FEATURES.md) · [DATA_MODEL.md](./DATA_MODEL.md) ·
[GLOSSARY.md](./GLOSSARY.md) · [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md)

---

## 1. The one-sentence version

A planner's roster is a set of rows in one table (`ShiftEntries`, one row per employee per date per
source). Anything a human changes is marked as manual and is then protected from every automatic
process; everything else can be regenerated. Coverage is computed from those rows against each WIC
centre's opening hours, and absences are applied to the same table by a synchronisation service rather
than kept in a parallel world.

---

## 2. The shift categories

The canonical list is the `ShiftTypes` constant class in the backend, 16 values. Live usage on the
2026-10-11 read is shown too, because a category with no writer is worth knowing about.

| Code | Meaning | Written by (verified) | Live rows (all time) |
|---|---|---|---|
| `WORKING` | Normal GSD shift day | Roster Generator plain mode and WIC-mode back-office days; the manual shift-assign endpoint; move-to-GSD; swap creates/clears; Bulk RTM "NIGHT" writes `WORKING` | 11,508 in the last 90 days |
| `WIC_DUTY` | On WIC duty | Roster Generator WIC days; WIC shift creation; WIC assignment; substitute acceptance; rollout projection | 4,968 (90 d) |
| `AL` | Annual leave | Vacation service through the sync service; startup vacation reconciliation; manual entry | 1,246 (90 d) |
| `HALF_AL` | Half-day annual leave | Vacation service when the request is a half day | 41 (90 d) |
| `SL` | Sick leave | Sick-leave service through the sync service | 528 (90 d) |
| `UL` | Unpaid leave | **No writer found in this repository** | 14 |
| `OFF` | Day off | Manual entry only | 206 (90 d) |
| `OFF_WEEKEND` | Weekend off | **No writer found in this repository** (only an uncalled parser branch) | 3,445 (90 d) |
| `PH` | Public holiday (national) | **No writer found** — read and displayed only | 10 |
| `LPH` | Local public holiday | **No writer found** | 3 |
| `CD` | Compensation day | Bulk RTM / manual entry | 18 (90 d) |
| `OL` | Other leave | Vacation service for the "other leave" kind | 88 (90 d) |
| `CO` | Compensation off (legacy wording) | No writer; selectable in the grid | 0 |
| `TRAINING` | Formal training | Training confirmation | 14 (90 d) |
| `RESIGNED` | Agent has resigned | **No writer found**; deactivating an employee sets `IsActive=false` instead | 40 (90 d) |
| `EMPTY` | No shift data | Swap soft-delete | — |

**Important distinctions that are easy to get wrong:**

- **`BO` is not a shift type.** Back-office work is either a `WORKING` row with `RawValue = "BO"`
  (written by the Roster Generator) or an entry in the separate `BoEntries` table (written by the Bo
  List page). Bulk RTM's "BO" category uses the second.
- **`NIGHT` is not a shift type.** Bulk RTM's "NIGHT" category writes a `WORKING` row. `NIGHT` exists
  as an agent *shift pattern* (`Employees.ShiftPattern`) with default times 22:00–07:00.
- **`WIC`, `VWIC`, `Voice`, `Backlog`, `Dispatcher`, `SME`, `SSP` are task badges, not shift types.**
  They live in `AgentTask` and are validated against a fixed list.

**Live distribution over the last 8 weeks** (2026-08-16 → 2026-10-11): WORKING 2,474 · WIC_DUTY 1,273 ·
OFF_WEEKEND 752 · AL 435 · SL 322 · OFF 120 · OL 28 · HALF_AL 28 · RESIGNED 15 · TRAINING 14 · CD 11 ·
UL 9 · LPH 3. The `ShiftEntries` table holds **41,931** rows and reaches from 2026-01-01 to
**2027-02-26**.

---

## 3. Where the data comes from

| Source | In this repository? | Notes |
|---|---|---|
| Roster Generator (plain and WIC mode) | Yes | The supported way to set up a new employee or a new WIC agent |
| Shift Plan grid cell edits | Yes | The human manual path |
| Bulk RTM Entry | Yes | One name per line, whole day, no time |
| Rollout page | Yes | A centre opened on one date for new workers |
| Sick-leave and vacation entry | Yes | Propagates into the shift table automatically |
| Training confirmation | Yes | Writes training rows |
| Substitute acceptance | Yes | Writes a WIC duty row |
| Coverage walk (generate location coverage) | Yes | Fills gaps from MAIN then BACKUP assignments |
| **The June Excel shift-plan import** | **No** | The writer is not in this repository. `CLAUDE.md` records the June Excel import as dead and never to be re-run. The script that produced the `EXCEL` and `AL_IMPORT` rows is external. |
| Historical ad-hoc SQL/PowerShell imports | No | The repository contains many one-off scripts from that era; they are not part of the running application. |

> **Honest statement for the board:** the quarterly Excel import is **no longer the mechanism**, and
> the Roster Generator is. But the historical rows still carry their original source labels, and no
> importer exists in the repository to re-run.

---

## 4. The Roster Generator, in detail

### 4.1 Two modes

**Plain mode** — employee, date range, start/end times, and which weekdays to work. Every selected
weekday becomes a `WORKING` row. Weekends are always skipped regardless of the selection.

**WIC mode** — employee plus a WIC centre, or a centre that is discovered automatically: if the
request leaves the centre blank but the employee has an active MAIN assignment in the WIC assignment
directory, the service **silently switches to WIC mode**. In WIC mode the requested working days and
task are ignored; instead, for every weekday in the range:

- if the centre is **open** that date → a `WIC_DUTY` row whose times come from the centre's own
  opening hours (a two-interval centre spans its break), plus a `WicShiftEntries` row marked on-site;
- if the centre is **closed** → a `WORKING` row with `RawValue = "BO"`.

The centre's hours are resolved by a single resolver whose precedence is: **a dated exception row
wins over the weekday pattern**, and among exceptions the most recently edited wins. A rollout day is
implemented as exactly such an exception row, tagged with the ownership marker `ROLLOUT`.

WIC mode does **not** silently write the employee's permanent MAIN assignment row. An agent who is
MAIN at another centre keeps that centre (the response carries
`MAIN_UNCHANGED: agent already has main centre X` and only the shifts for the selected period are
generated); an agent with no MAIN at all gets one only when the caller explicitly confirms it
(`confirmCreateMain=true`, sent by the UI's "This will set &lt;centre&gt; as the agent's permanent main
WIC" dialog). A generate call without that confirmation reports `MAIN_NOT_CREATED` and writes shifts
only. This used to be the one code path that created a MAIN row, and it did so silently.

### 4.2 Weekends and public holidays

- Weekends are skipped in **both** modes and are counted separately.
- Public holidays are skipped and counted separately — **no row is created**, so a holiday is a gap
  in the roster rather than a `PH` row. This is a deliberate choice, not an omission.
- Order of checks per day is: weekend first, then the selected-weekday filter, then the holiday. A
  holiday falling on a weekend therefore counts as a skipped weekend; a holiday on a non-selected
  weekday is not counted at all.
- The range is capped at **366 days**.

### 4.3 Preview before write

Generating is a two-step operation. The preview is read-only and returns: the rows that would be
created, the skipped weekends and holidays, the collisions, the overwrite replacements, and whether
each collision is *protected*. The generate call then **rebuilds the plan inside a transaction**, so a
row created between the preview and the generate is evaluated by the same rules. This is what makes
the preview trustworthy rather than advisory.

### 4.4 Overwrite mode — off by default

With overwrite off, **any** existing row on that date is a collision and is left alone (unless the
generation is precisely meant to fill that day). With overwrite on, the row is replaced **unless it is
protected**. Protected means:

1. any row carrying a manual marker, whatever its type;
2. any type in the protected-absence set: **AL, HALF_AL, SL, UL, PH, LPH, OFF, OFF_WEEKEND, OL,
   RESIGNED, CD**;
3. `TRAINING`;
4. a `WORKING` row whose task badge is `VWIC`;
5. a WIC-shaped row (a `WIC_DUTY` row, or a `WORKING` row whose task names a centre) aimed at a
   **different** centre than the one being generated.

Note that the “absence” lists differ between code paths in this repository — there are at least four
variants. The protected-overwrite list above is the roster's own list and is the authoritative one for
this feature; the discrepancy is recorded in [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md).

### 4.5 Batch, snapshot and delete-with-restore

Every generation creates a **batch** row recording the employee, range, mode, counts and creator, and
each generated row points back at the batch. Before overwriting anything, the original row — including
its WIC entry — is serialised into the batch's snapshot column.

Deleting a batch:

- deletes only rows owned by that batch **and** not manually marked;
- counts rows that another module has since taken over as "detached" and leaves them;
- removes the WIC entries that match the batch's employee, centre and deleted duty dates;
- **restores each snapshotted row, but only into an unoccupied employee-and-date slot** — if something
  new now occupies the date, the restore is skipped and counted rather than forced;
- soft-deletes the batch so the audit trail survives.

### 4.6 Missing-roster check

`GET /api/roster/missing` (window 1–90 days, default 14) answers three questions at once:

1. which **active employees have no shift at all** in the window;
2. which **shift rows belong to an employee id with no active employee record** (and whether an
   inactive record exists, which distinguishes "leaver" from "typo");
3. per employee, which specific dates are missing.

The Overview page shows this as a warning banner with deep links, and the endpoint is behind the same
RTM/TEAM_LEAD/DEV gate. On 2026-10-11 **all 122 active employees had at least one shift in the next
14 days** — the banner is therefore empty in normal operation, which is the intended state.

### 4.7 Coverage walk

`POST /api/roster/generate-location-coverage` walks the WIC assignment directory MAIN-first and then
BACKUP to fill uncovered dates. It shares the covered-date signal with the coverage-gap service, skips
anyone who is unavailable, writes through the same assignment service as a manual assignment would,
creates one batch per agent that receives rows, and finally compares its own tally against the
"still-gap" count and reports a mismatch rather than claiming success blindly.

---

## 5. Conflict and overlap rules

One predicate — `start1 < end2 && start2 < end1` — appears in five places, and **it does not mean the
same thing in all of them.** This is the single most important thing to understand about
"conflicts" in this system.

| Place | Effect |
|---|---|
| WIC assignment creation | **Blocks.** A second same-day assignment at overlapping times is refused and returned as "skipped" with a reason, and nothing is saved. Assignments at *non-overlapping* times are allowed — this is what lets one agent cover two centres in a day. Only on-site rows are considered; historical off-site rows are ignored. |
| Conflict detector (`GET /api/wic/conflicts`) | **Report only.** Never blocks anything. Returns OVERLAP, SPLIT_SHIFT (a hidden-by-default type) and CLOSED_LOCATION findings. |
| Coverage walk availability | **Blocks silently.** An agent with an overlapping commitment elsewhere is treated as unavailable for that date. |
| Rollout projection | **Warning only, never a block** — by explicit design rule. |
| Break scheduling | Uses the same predicate on minutes, for break collisions. |

**Other hard blocks in the shift system:** assigning the same shift type twice for an employee and
date returns a conflict; swapping refuses when either side is a protected manual row, or when the swap
would collide at the same WIC location; sick leave refuses a date range that overlaps another record
for the same person; vacation creation refuses any overlap with a sick leave.

**Duplicate rows are normal.** An employee and date can legitimately carry more than one row (one per
source, or one per shift type). Most services collapse duplicates by taking the **highest id** (the
latest write). Three places do something different, and that is the source of the confusion:

- the Shift Plan grid collapses duplicates by *last row in query order*, not highest id;
- the roster plan builder takes the *first row as materialised*;
- a documentation claim that a database unique index enforces one row per employee and date is
  **wrong** — no such index exists. The real unique indexes are per
  (employee, date, source) and (employee, date, type).

---

## 6. Legal and rest rules

Seven rules are implemented, each with a rule id, a quoted legal basis and a hard-or-soft flag. Only
the hard ones make a shift invalid.

| Rule | Check | Hard block? |
|---|---|---|
| R1 | Minimum **11 hours** rest between shifts — compares only the immediately previous and next calendar day | **Yes** |
| R2 | Maximum **10 hours** per day | **Yes** |
| R3 | Maximum **48 hours** per week | **Yes** |
| R4 | **Sunday** work | No — warning |
| R5 | Maximum **6 consecutive** days | **Yes** |
| R6 | **Students** maximum 20 hours per week | No — warning |
| R7 | Work on a **public holiday** | No — warning |

**Break deduction:** a shift longer than 6 hours is treated as net gross-minus-one-hour. This is
applied **only to the weekly total (R3)**; the daily maximum and the rest rule use gross hours. The
code comment that introduces the deduction (`Backend/ShiftValidationService.cs:129`) sits in the middle
of the validator and does not say which totals it feeds, so it reads as if the deduction applied to
every total — that reading is stale, and the live behaviour is the weekly-only one above.

**Two real limitations of these rules**, both verifiable:

1. The rest rule loads a ±7-day window but only tests the adjacent calendar day, so a late finish two
   days earlier is invisible, and two shifts on the same day are never compared.
2. The weekly rule computes the week as Monday-to-Sunday, but for a **Sunday** shift the arithmetic
   lands on the *following* Monday, so a Sunday shift is measured against the next week and the
   current week's hours are ignored.

**Where the rules actually apply.** The validator is called from exactly two places: WIC shift
creation (which refuses with a blocked response) and the explicit validate endpoint. The Shift Plan
grid calls the validate endpoint first and blocks the confirm button for hard violations — so on that
route R1/R2/R3/R5 are **enforced by the user interface, not by the server**. The bulk assign routes
and the Roster Generator do not call the validator at all. This is a real gap and is listed as such in
[OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md).

---

## 7. Agent grouping — what actually groups people

The requirement mentions `agent_group`. Here is the honest position.

| Field | Where it lives | Does it group anything? |
|---|---|---|
| `Employees.PrimaryRole` | Pulse database | **Buckets**, not groups. Used for task defaults, coverage-per-hour buckets, the break pools and the VWIC candidate pool. Live values include WIC, Voice, SSP, VWIC, SME, Dispatcher, Chat, Chat CRO, Booking Tool, Bulk PWs, Trainer. |
| `Employees.TeamLeadName` | Pulse database | **This is the field that actually drives grouping in Pulse.** The dashboard's team-lead summary, the AL calendar, and the default ordering on Shifts, Attendance, Sick Leave, WIC Schedule and the missing-roster list all group or order by it. **6** distinct active team leads. |
| `Employees.Category` | Pulse database | A legacy free-text label used only as a search filter. Live values: empty (107), null (20), `Management` (1). **It is not the agent group.** |
| `Employees.SecondaryRole` | Pulse database | Used for VWIC/Voice membership only. |
| `Employees.GroupRegion` | Pulse database | Display and patching only. No grouping logic uses it. |
| `WicAgentAssignments.AssignmentType` | Pulse database | The real grouping **inside WIC scheduling**: MAIN (49 rows), BACKUP (60), REGIONAL (39). Joined by employee *name string*, not by id. |
| **`agent_group`** | **ShiftKioskDB only** | Values `WIC` / `VOICE` / `VWIC` / `BO` (live: WIC 62, VOICE 33, BO 25, VWIC 7, blank 12). It is **not a column in the Pulse database**. Pulse receives it opaquely from the kiosk feed and uses it only in the browser, to split the WIC Attendance view from the Remote Attendance view. |

**Conclusion to state plainly:** "agent group" as a scheduling concept is split across two systems.
The kiosk owns the four-value grouping; Pulse owns the roles, the team lead and the WIC assignment
tier. They are not synchronised by any code, and a value present in one is not guaranteed in the
other.

---

## 8. Manual versus automatic — the protection rules

This is the mechanism that makes the data defensible, so it is worth stating precisely.

**The marker.** A manual change stamps three fields on the row: **who** (`ManualChangeBy`), **when**
(`ManualChangeAt`) and a note. A row is "manual" if the actor is non-blank. The same three fields are
also EF **concurrency tokens**, so a writer that loaded the row before the human edit fails rather
than clobbering it.

**Two ways the actor is determined — and they behave differently:**

| Route family | Actor rule | Consequence |
|---|---|---|
| Shift Plan writes (`/api/shifts/{id}`, `/api/shifts/assign`, `/api/shifts/assign/manual`) | The session must be authenticated, in DEV/RTM/TEAM_LEAD, and carry a non-empty KID. Otherwise 401/403. | Cannot be reached without a session. |
| WIC writes (`/api/wic/shifts`, `/api/wic/assignments`, `/api/wic/move-to-gsd`) and rollout writes | Prefer the session KID; **fall back to the fixed tag `UI-MANUAL`** when the session has none. | **While the global RBAC flag is off, these routes have no 401/403 and every caller is stamped as a human.** |
| Bulk assign (`/api/shifts/assign/bulk`) | Session gate, but **no actor is passed**. | Treated as an automatic writer: a protected row is refused with 409. |

**The guard.** At the point of saving, the context inspects every modified or deleted shift row. If
the row was manual in the database and the current operation did not register itself as a human edit,
the save throws instead of proceeding. The only permitted deletion of a manual row is a rollout-owned
row, and even that requires an explicit registration naming the rollout module.

**Every automatic writer also checks first.** Sickness sync, vacation sync and revert, startup
vacation reconciliation, roster generation and its overwrite path, the coverage walk, training
confirmation, the closed-day cleanup, WIC creation and assignment, substitute acceptance, and both
raw-SQL startup steps all skip manual rows. Two raw-SQL maintenance commands — the DEV-only demo-data
delete and the WIC orphan cleanup — **bypass the guard entirely** because they do not go through EF.

**Known gaps in the mechanism**, stated without softening:

1. The `UI-MANUAL` fallback described above.
2. Substitute acceptance writes its inserted row as auto-generated and does **not** stamp a manual
   marker, so a manually accepted substitution is afterwards protected from nothing.
3. Two write paths mutate WIC entry rows without consulting the marker at all: the WIC shift patch and
   the VWIC assignment.
4. An automatic writer that deletes rows (a sick-leave revert that removes a row the sync itself
   created) cannot report how many rows vanished — the API returns nothing about it.

---

## 9. Automatic writers, listed

| Writer | Trigger | Writes | Can it touch a manual row? |
|---|---|---|---|
| Startup `IsWicDuty` cleanup | Every startup | Flags only | No — excludes manual rows |
| Startup future-vacation reconciliation | Every startup | Future weekday rows → AL; deletes matching WIC entries | No — skips manual rows |
| Sick-leave sync and revert | Sick-leave create/edit/end | SL rows; revert restores or deletes | No |
| Vacation sync and revert | Vacation create/delete | AL or OL rows; revert restores or deletes | No |
| Roster generation (plain and WIC) | RTM action | Shift rows, WIC entries, MAIN assignment | No — protected rows are skipped |
| Roster overwrite | RTM action with overwrite on | Replaces and snapshots | No |
| Coverage walk | RTM action | Assignments via the assignment service | No |
| Roster batch delete | RTM action | Deletes batch-owned rows; restores snapshots | No — manual rows are "detached" |
| Training confirmation | Training confirm | Inserts a TRAINING row, or sets the task badge on an existing row | No |
| Substitute acceptance | Substitutes accept | WIC duty row + WIC entries | Refuses a manual row with a conflict |
| Closed-day cleanup | Opening-hours change (explicit endpoint) | Deletes WIC entries and WIC-duty shift rows on now-closed days | Protected **only if** the shift row has a manual marker — a hand-made duty row with no marker is deleted |
| VWIC assignment | VWIC page | WIC entries only | **No check** |
| WIC shift patch | WIC Shifts page | WIC entry fields | **No check** |
| Rollout projection | RTM rollout create/edit/delete | Exception hours row, assignment rows, one shift row | It *is* a stamping writer; it never rewrites a pre-existing row |
| Demo-data delete (DEV only) | Manual admin call, raw SQL | Deletes DEMO-sourced rows in every table | **Bypasses the guard** |
| WIC orphan cleanup (DEV only) | Manual admin call, raw SQL | Deletes null-location WIC entries | No marker exists on that table |
| Startup coverage seed | Every startup (idempotent) | WIC locations, assignments, reachable cities, employee KIDs | Does not touch shift rows |

---

## 10. The three things a board should take from this document

1. **The roster is generated, not typed.** One action produces a whole employee's plan, with a preview
   before it writes, and a snapshot that makes it reversible.
2. **Human decisions outrank automation, structurally.** The marker, the guard and the concurrency
   tokens mean a missed check produces a loud failure, not a silent overwrite.
3. **Coverage is a calculation over one source of truth for opening hours.** Every surface that says
   "open", "closed", "covered" or "at risk" resolves hours and staffing through the same resolver and
   the same coverage classifier — which is why the numbers agree across pages.
