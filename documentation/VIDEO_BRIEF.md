# VIDEO_BRIEF — whiteboard explainer (Erklärvideo) for the board

**Prepared 2026-10-11.** This is the brief for a **whiteboard explainer**: one hand drawing on a
whiteboard, with a calm voice-over. It is **not** a screen recording and it is **not** a product demo.
There are no screenshots, no application capture and no film of anybody's desk. Everything the viewer
sees is drawn with simple shapes, icons and arrows.

The viewer is the board of the company. Assume **no technical knowledge at all**. The video tells the
story of **one real working day** — a Monday morning, a planner at her desk, an agent at a centre in a
different federal state, a team lead watching the afternoon — and lets the system's behaviour appear
inside that story. It is a story, not a feature list.

**Related documents:** [VIDEO_NARRATION.md](./VIDEO_NARRATION.md) (the full spoken script) ·
[BOARD_OVERVIEW.md](./BOARD_OVERVIEW.md) (the written version for the board) ·
[OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) (the full risk list).

---

## 1. Rules this video follows

These rules are binding. They were written because the first version of this brief broke them.

1. **No counting numbers on the board.** The video contains **no figure** for the number of agents,
   agent groups, locations, centres, kiosks, computers, tables, rows, routes, pages, versions or
   months. The size of the system is shown by **drawing a crowd, a map and a stack**, never by writing a
   number. "We are not going to put a number on the board here" is itself a legitimate line.
2. **Concepts may be named; they are not numbers.** WIC, VOICE, vWIC, BO, OFF, CD are **categories** —
   words the business already uses. They appear as words in a labelled box, never as a count.
3. **The only numbers allowed** are (a) a **rule** the viewer must remember, and (b) a **date** inside a
   worked example. Exactly two rules carry a number: the **11-hour minimum rest between shifts** and the
   **11-hour automatic end of a kiosk session**. Dates appear only in the holiday example and the
   "prepared on" line of the documents.
4. **Migration is a process, not a figure.** The kiosk client replacement is described as
   "in progress", with no count of devices and no percentage and no end date.
5. **Real places, invented people.** The example centres are real: **Augsburg (Bavaria)**,
   **Essen (North Rhine-Westphalia)** and **Quickborn (Schleswig-Holstein)**. The people are invented and
   carry a **first name only** — Anna the planner, Murat the agent. No real employee name, no user name,
   no employee number, no computer name, no network address and no key or token ever appears.
6. **Say the hard thing.** The security part of the video states plainly that a supervisor sign-in
   switch described in several documents **is not implemented in the code**, and that the kiosk client
   migration is unfinished. It is not softened and it is not skipped.
7. **Two systems are never mixed up.** The workforce application and the kiosk have separate lists.
   Whenever the two are compared, the video says which system it means.
8. **Plain language.** Short sentences. One idea per scene. If a word would need explaining, it is
   replaced with a word the viewer already owns.

---

## 2. The scene list

Each scene below gives the **title**, **what the hand draws on the board**, the **one message** the
viewer must keep, and the **source in code or in the database** for every statement the scene makes.
The spoken words are in [VIDEO_NARRATION.md](./VIDEO_NARRATION.md).

---

### Scene 1 — Monday, seven in the morning

- **On the board:** A simple desk, a lamp and a wall calendar with one week circled. A small figure,
  "Anna", sits at the desk with a phone in one hand and a printed sheet in the other. Two speech bubbles
  float above her: *"Who is where today?"* and *"Who did not arrive?"* Both are drawn with a question
  mark.
- **Key message:** The day starts with questions that used to have no single answer.
- **Source:** The problem statement in [BOARD_OVERVIEW.md](./BOARD_OVERVIEW.md) §1; the attendance
  comparison is implemented by the attendance page end to end (`Backend/KioskProxyService.cs`,
  `Frontend/src/pages/Attendance.tsx`).

### Scene 2 — One plan, one truth

- **On the board:** Two large boxes side by side. The left box carries a calendar icon and the word
  **PLAN**. The right box carries a clock and a checkmark and the word **REALITY**. A single thick arrow
  runs between them, and on the arrow the word **EMPLOYEE** is written once. Under the boxes, a queue of
  small figure icons is drawn, to suggest the size of the workforce without writing it.
- **Key message:** Two products, joined in exactly one place — the employee.
- **Source:** The two products and their separate databases, [SYSTEM_ARCHITECTURE.md](./SYSTEM_ARCHITECTURE.md);
  the comparison key is the employee identifier, never a place name (`Backend/KioskProxyService.cs`,
  `Frontend/src/pages/Attendance.tsx`).

### Scene 3 — Anna builds a fortnight

- **On the board:** A calendar grid is drawn square by square. A small wand or pen touches it and whole
  columns fill in at once. Above the grid a small card appears labelled **preview**, then a tick.
  Beside the grid, three small map pins are drawn and labelled with their real names: **Augsburg**,
  **Essen**, **Quickborn**. A fourth small pin is drawn far away with the label "also on the map" — the
  board shows a crowd of pins, not a total.
- **Key message:** A whole roster is produced by one action, from each centre's own rules.
- **Source:** The Roster Generator (`Backend/RosterService.cs`); the centre list and its real cities and
  federal states are read live from `GSDDashboard.WicLocations` (columns `City`, `Bundesland`,
  `LocationCode`), 2026-10-11.

### Scene 4 — The rules are already in the plan

- **On the board:** The filled calendar, with a magnifying glass over one week. Two days are crossed out
  with a small **weekend** label, and one day has a small **holiday** flag. A shield is drawn at the
  corner of the calendar with the word **rules**.
- **Key message:** The generator does not invent days — it follows the centre's own opening pattern.
- **Source:** Weekend and holiday handling in the Roster Generator
  (`Backend/RosterService.cs:395`, `:860`) and the opening-hours resolver
  (`Backend/Services/WicHoursResolver.cs`); public-holiday matching through
  `Backend/Services/PublicHolidayResolver.cs`.

### Scene 5 — A holiday is not the same everywhere

- **On the board:** Two pins are drawn large, side by side. Left: **Augsburg** with a small church icon
  and **closed** written under the date **6 January**. Right: **Essen** with an open door and
  **working** written under the same date. A calendar strip at the top shows the same day touched by
  both pins, and a small flag sits only over Augsburg.
- **Key message:** The same date can be a rest day in one federal state and a working day in another,
  and the plan treats it that way.
- **Source:** `PublicHolidays` holds **one row per affected federal state** for this date, with
  `IsNational = 0` and `Bundesland` set; verified live 2026-10-11 (`HolidayDate`, `Bundesland`,
  `IsNational`). The rule is applied by `Backend/Services/PublicHolidayResolver.cs` and by the
  coverage/forecast code in `Backend/Services/CoverageEvaluator.cs:86-100`. Augsburg is `Bayern`, Essen
  is `Nordrhein-Westfalen` in `GSDDashboard.WicLocations`.

### Scene 6 — The plan is checked before it is saved

- **On the board:** A stack of small shift cards is lifted towards a doorway. The doorway has a gate
  with a small guard figure and a stop sign. One card is pushed out of the stack and bounces off the
  gate; the rest pass through.
- **Key message:** The system refuses a shift that would break a working-time rule, and tells the
  planner why.
- **Source:** `Backend/ShiftValidationService.cs` — the daily maximum (`:71-75`), the weekly maximum
  (`:132-145`), the consecutive-day maximum (`:147-158`) and the minimum rest (`:95-127`) are raised as
  **hard blocks**; Sunday and holiday work (`:78-81`, `:54-59`) and the student weekly limit
  (`:160-164`) are raised as warnings.

### Scene 7 — Eleven hours of rest

- **On the board:** A long horizontal time line drawn across the board. On the left a shift ends at
  **22:00**; on the right the next shift would start at **07:00**. A gap is measured with a small ruler
  and labelled **11 h**. A red cross is drawn over the second shift, and a curved arrow pushes it to the
  right, past the ruler, to **09:00**.
- **Key message:** Nobody is scheduled with less than eleven hours between two shifts; the system stops
  the booking and the planner moves it.
- **Source:** `Backend/ShiftValidationService.cs:95-127` — the rule is a hard block
  (`IsHardBlock: true`), cites **§5 ArbZG**, and compares the end of the previous day's shift with the
  start of this one and the end of this one with the next. The limitation that only the immediately
  adjacent calendar day is examined is recorded in
  [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) item **F2**.

### Scene 8 — The gap is seen before the customer feels it

- **On the board:** A simple map outline with several pins on it. Most pins are green. Two pins turn
  amber and one turns red. From the red pin an arrow leads to a short ranked list of small figures with
  a pencil beside each — first, second, third choice. A small eye icon sits above the map.
- **Key message:** Coverage is checked continuously, so a shortfall is visible early and arrives with
  named options rather than a colour alone.
- **Source:** Coverage is classified as covered / partly covered / uncovered / closed by the single
  classifier in `Backend/Services/CoverageEvaluator.cs` (`Classify`, `ClassifyByMinutes`); the rolling
  look-ahead with its at-risk flag is `Backend/Services/ForecastService.cs:59-152` (the horizon is
  clamped to a bounded range at `:61` — the bound itself is a setting, not a figure for the board);
  ranked substitutes are produced by `Backend/Services/SubstitutionService.cs`.

### Scene 9 — What really happened

- **On the board:** A laptop is drawn on a counter, at the centre. On its screen, big labelled buttons:
  **start**, **break**, **emergency**, **end**. A dashed line runs from the laptop into a cloud, and
  beside the laptop a small stack of cards is labelled **queue**. A clock is drawn with **11 h** written
  inside it.
- **Key message:** The kiosk records the real day where the work happens, keeps working when the network
  does not, and closes a forgotten session on its own.
- **Source:** The kiosk client states and the offline queue (`C:\ShiftKiosk\deploy\main_kiosk.py`);
  the automatic end of session after **11 hours** from the shift start
  (`AUTO_LOGOUT_SECONDS = 11 * 3600` at `main_kiosk.py:784` in the live packages under `C:\ShiftKiosk\kiosk\`
  and `C:\ShiftKiosk\KIOSK_PACKAGE\Files\`). The history of this change is recorded in
  [CHANGE_HISTORY.md](./CHANGE_HISTORY.md) (2026-09-16, commit `a1b1f74`). The stale nine-hour copies that
  remain in the working trees — `C:\ShiftKiosk\deploy\main_kiosk.py:682` and
  `C:\ShiftKiosk_secure\deploy\main_kiosk.py:682`, plus two comments in
  `C:\ShiftKiosk\server\server.py` — are listed in
  [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) § **C1**.

### Scene 10 — Planned and actual, side by side

- **On the board:** Two columns of small figures. The left column is headed **EXPECTED**, the right
  **CHECKED IN**. Two arrows cross between the columns, one in each direction. Under them a small note:
  *matched by employee*.
- **Key message:** The two systems are allowed to disagree, and seeing that disagreement honestly is the
  whole point.
- **Source:** The attendance comparison joins the two systems on the employee identifier only
  (`Backend/KioskProxyService.cs`, `Frontend/src/pages/Attendance.tsx`); the kiosk's free-text location
  is displayed but never used as the match key.

### Scene 11 — A person's decision outranks the machine

- **On the board:** One calendar cell is drawn large, with a pen mark and a stamp on it. Three arrows
  come towards the cell from three boxes labelled **roster**, **import** and **cleanup**, and each arrow
  hits a small shield and falls away. A padlock is drawn on the cell.
- **Key message:** Once a human has changed a shift, no automatic job may quietly undo it.
- **Source:** The manual marker and the save-time guard (`Backend/GSDContext.cs:25-35`,
  `GuardManualShifts` at `:56-72`, called from `SaveChanges`/`SaveChangesAsync` at `:74-90`); automatic
  writers that must preserve it are recorded in
  [ROSTER_AND_SCHEDULING.md](./ROSTER_AND_SCHEDULING.md) and [CLAUDE.md](../CLAUDE.md).

### Scene 12 — The switch that is not connected

- **On the board:** A large light switch is drawn in the centre of the board. Over it, a red circle with
  a diagonal line. Under it, in block letters: **NOT IMPLEMENTED IN CODE — 2026-10-11**. Beside the
  switch, a document icon with a warning triangle, and a dotted line that leads from the switch to
  nothing at all — it simply stops.
- **Key message:** This is the most serious documentation defect we found: several documents tell an
  operator to switch on a supervisor sign-in mode, and **no line of code reads it**. Following those
  instructions would change nothing while looking like a safety measure had been turned on.
- **Source:** Repository-wide search on 2026-10-11: `SupervisorMode` occurs **only in
  `documentation\*.md`**, in no `.cs` file; no `appsettings*.json` contains the key;
  `Backend/Services/SupervisorAuthClient.cs` does not exist. Full statement in
  [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) §0 and item **S14**, and in
  [SECURITY_MODEL.md](./SECURITY_MODEL.md) §4.

### Scene 13 — Closing the loop

- **On the board:** The board is divided in two by a vertical line. Left, under a shield with a tick:
  **closed**. Right, under an open padlock: **still open**. Under "still open" the hand draws three
  small icons and labels them in words, never in numbers: *a session can still be obtained without
  signing in*, *the old shared key is still in daily use*, *an unfinished client migration*, and
  *no retention rule yet*.
- **Key message:** Some things are fixed and some are not, and the board is told which is which.
- **Source:** [SECURITY_MODEL.md](./SECURITY_MODEL.md) §8 (what is closed) and §9 (what is open);
  [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) §1; the owner's device-binding decision is item
  **S17** and **C26** (live `device_binding = enforce`, stays enforcing; the tracked profile files
  `dan1/2/3.ini` still say `shadow`).

### Scene 14 — One more Monday, later

- **On the board:** The same desk, lamp and calendar as Scene 1, drawn again but calmer. Anna now has a
  single screen: the map with green pins, one amber pin already being handled, and a short note under
  it. The question marks from Scene 1 are gone. In the corner, a small horizon line with a flag —
  **next** — and beside it two words only: *finish the migration*, *make the documents true*.
- **Key message:** The day now has one shared picture, and the remaining work is finishing what is
  already started.
- **Source:** The completeness and coverage behaviour in
  [BOARD_OVERVIEW.md](./BOARD_OVERVIEW.md) §3; the migration status and the documentation work in
  [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) §5 (recommended order of work).

---

## 3. Production notes

- **Length:** fourteen scenes. At a calm reading pace this lands at roughly **three to five minutes** of
  speech, with a short pause after each scene while the drawing finishes. Scene 7 and Scene 12 are the
  two to rehearse most: one carries the only number the viewer must remember, the other carries the
  hardest message.
- **Drawing style:** one continuous hand, black lines, at most one accent colour. Circles stand for
  people, rectangles for systems, arrows for flows, a padlock for protection, a clock for time. No
  screenshots, no logos, no glass-and-gradient "tech" imagery.
- **On-screen text:** words only, except the two rule numbers (11 h in Scene 7 and Scene 9) and the two
  example dates (6 January in Scene 5, and the "2026-10-11" date stamp of the code search in Scene 12).
  Write those large and leave them visible long enough to read.
- **Never on the board:** any count of people, centres, kiosks, computers, tables or rows; any real
  employee name, user name, employee number, computer name or network address; any key, token or
  password; any screenshot or product capture; any individual sickness or incident story.
- **Two systems, never blurred:** whenever the workforce application and the kiosk are both on the
  board, the boxes are labelled and the voice says which one it means.
- **If filming happens later than 2026-10-11:** the two rule numbers are policy and do not drift; the
  code-search date in Scene 12 must be **re-verified** and updated to the date of the new search, because
  it is a statement about the code as it stands on a given day.
