# VIDEO_BRIEF — whiteboard explainer (Erklärvideo) for the board

**Prepared 2026-10-11.** This is the brief for a **whiteboard explainer**: one hand drawing on a
whiteboard, with a calm voice-over. It is **not** a screen recording and it is **not** a product demo.
There are no screenshots, no application capture and no film of anybody's desk. Everything the viewer
sees is drawn with simple shapes, icons and arrows.

The viewer is the board of the company. Assume **no technical knowledge at all**. The video tells the
story of **one real working day** — a Monday morning, a planner at her desk, an agent at a centre in a
different federal state, the same planner watching the afternoon — and lets the system's behaviour appear
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
   **11-hour automatic end of a kiosk session**. Both are written on the board as **11 h**. Dates appear
   only in the holiday example, **6 January**, and in the code-search scene, **11 Oct** in Scene 12. The
   "prepared on" line of the documents is not spoken.
4. **The kiosk is described only by what it does.** Every statement about the kiosk is one of its
   visible behaviours — the credential that belongs to the individual computer, the automatic end of
   a forgotten session after eleven hours, the blocked Windows keys, and the offline queue that sends
   again by itself. No volume of devices, no percentage and no date is ever shown.
5. **Real places, invented people.** The example centres are real: **Augsburg (Bavaria)**,
   **Essen (North Rhine-Westphalia)** and **Quickborn (Schleswig-Holstein)**. The people are invented and
   carry a **first name only** — Anna the planner, Murat the agent. No real employee name, no user name,
   no employee number, no computer name, no network address and no key or token ever appears.
6. **Say the hard thing.** The security part of the video states plainly that a supervisor sign-in
   switch described in several documents **is not implemented in the code**. It is not softened and
   it is not skipped.
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

### Scene 1 — Monday morning

- **On the board:** A simple desk, a lamp and a wall calendar. A small figure, "Anna", sits at the desk
  with a phone in one hand. Two speech bubbles float above her: *"Who is where?"* and *"Who is
  missing?"* Both are drawn with a question mark.
- **Key message:** The day starts with questions.
- **Source:** The problem statement in [BOARD_OVERVIEW.md](./BOARD_OVERVIEW.md) §1; the attendance
  comparison is implemented by the attendance page end to end (`Backend/KioskProxyService.cs`,
  `Frontend/src/pages/Attendance.tsx`).

### Scene 2 — Two systems, one picture

- **On the board:** Two large boxes side by side. The left box is labelled **PULSE**, carries a calendar
  icon and the word **plan**. The right box is labelled **KIOSK**, carries a clock with a tick and the
  words **what happened**. Arrows run from both boxes into one picture.
- **Key message:** Two systems, one picture.
- **Source:** The two products and their separate databases, [SYSTEM_ARCHITECTURE.md](./SYSTEM_ARCHITECTURE.md);
  the two sides meet on the employee identifier, never on a place name (`Backend/KioskProxyService.cs`,
  `Frontend/src/pages/Attendance.tsx`).

### Scene 3 — Building the plan

- **On the board:** A calendar grid is drawn, then filled with one touch of a pen — whole columns at
  once. Beside the grid a small card labelled **preview** appears, then a tick. Below, small map pins are
  drawn and labelled with their real names: **Augsburg**, **Essen**, **Quickborn**; the rest
  stay unlabelled — the board shows a crowd of pins, not a total.
- **Key message:** The plan is created in one step.
- **Source:** The Roster Generator (`Backend/RosterService.cs`); the centre list and its real cities and
  federal states are read live from `GSDDashboard.WicLocations` (columns `City`, `Bundesland`,
  `LocationCode`), 2026-10-11. The preview is `POST /api/roster/preview` (`Backend/RosterService.cs:1254`,
  `Frontend/src/pages/Roster.tsx:233-241`); the whole run is undone with `DELETE /api/roster/batches/{id}`
  (`Backend/RosterService.cs:1132-1245`, endpoint at `:1302`; `Frontend/src/pages/Roster.tsx:262-281`).

### Scene 4 — Only when the centre is open

- **On the board:** The filled calendar, with a closed sign drawn on some days and a holiday flag on
  another.
- **Key message:** People are planned only when a centre is open.
- **Source:** Weekend and holiday handling in the Roster Generator
  (`Backend/RosterService.cs:395-399` weekends, `:404-408` holidays) and the opening-hours resolver
  (`Backend/Services/WicHoursResolver.cs`); public-holiday matching through
  `Backend/Services/PublicHolidayResolver.cs`. On a weekday the centre is closed for, the generator
  writes a back-office (BO) row instead of a centre shift (`Backend/RosterService.cs:425-434`).

### Scene 5 — A holiday is not the same everywhere

- **On the board:** Two pins are drawn large, side by side. Left: **Augsburg (Bavaria)** with
  **6 January — closed** written under it and a small flag. Right: **Essen (NRW)** with the same date and
  **open** written under it.
- **Key message:** Same date, different state, different answer.
- **Source:** `PublicHolidays` holds **one row per affected federal state** for this date, with
  `IsNational = 0` and `Bundesland` set; verified live 2026-10-11 (`HolidayDate`, `Bundesland`,
  `IsNational`). The rule is applied by `Backend/Services/PublicHolidayResolver.cs` and by the
  coverage/forecast code in `Backend/Services/CoverageEvaluator.cs:86-100`. Augsburg is `Bayern`, Essen
  is `Nordrhein-Westfalen` in `GSDDashboard.WicLocations`.

### Scene 6 — Checked before it is saved

- **On the board:** Shift cards pass a gate; one stops at a small red sign, the next one gets a yellow
  warning sign.
- **Key message:** Pulse checks the law before saving.
- **Source:** `Backend/ShiftValidationService.cs` — the daily maximum (`:71-75`), the weekly maximum
  (`:132-145`), the consecutive-day maximum (`:147-158`) and the minimum rest (`:95-127`) are raised as
  **hard blocks**; Sunday and holiday work (`:78-81`, `:54-59`) and the student weekly limit
  (`:160-164`) are raised as warnings.

### Scene 7 — Eleven hours of rest

- **On the board:** A long horizontal time line drawn across the board. On the left a shift ends at
  **22:00**; on the right the next shift would start at **07:00**. A gap is measured with a small ruler
  and the ruler is too short. A red cross is drawn over the second shift, and a curved arrow moves the
  start to **09:00**, where a ruler labelled **11 h** fits and a tick is drawn.
- **Key message:** At least 11 hours of rest.
- **Source:** `Backend/ShiftValidationService.cs:95-127` — the rule is a hard block
  (`IsHardBlock: true`), cites **§5 ArbZG**, and compares the end of the previous day's shift with the
  start of this one and the end of this one with the next. The limitation that only the immediately
  adjacent calendar day is examined is recorded in
  [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) item **F2**.

### Scene 8 — Seeing the gap early

- **On the board:** A simple map outline with several pins on it, drawn in green, amber and one red.
  From the red pin an arrow leads to a short ranked list of small figures with a pencil beside each —
  first, second, third choice. One of them is ticked.
- **Key message:** Gaps are seen and filled early.
- **Source:** Coverage is classified as covered / partly covered / uncovered / closed by the single
  classifier in `Backend/Services/CoverageEvaluator.cs` (`Classify` at `:29-45`, `ClassifyByMinutes` at
  `:49-55`); the rolling look-ahead with its at-risk flag is `Backend/Services/ForecastService.cs:59-152`
  (the horizon is clamped to a bounded range at `:61` — the bound itself is a setting, not a figure for
  the board); the ranked substitutes are produced and ordered by `Backend/Services/SubstitutionService.cs`
  (`:546-549`, scoring at `:610-629`), served by `GET /api/wic/substitutes` (`:667-690`) and applied by
  `POST /api/wic/substitutes/accept` (`Backend/Modules/SubstitutionModule.cs:22`).

### Scene 9 — What really happens

- **On the board:** A laptop is drawn — the agent's own laptop, not a counter device. On its screen, big
  labelled buttons: **start**, **break**, **sick leave**, **emergency**, and, once the shift is running,
  **end shift**. A padlock is drawn on the keyboard. A dashed line runs from the laptop into a cloud, and
  beside the laptop a small stack of cards is labelled **waiting**. A clock is drawn with **11 h** written
  inside it.
- **Key message:** The kiosk records the real day, even offline.
- **Source:** The kiosk program `kiosk-ng\main_kiosk.py` (living copy
  `C:\ShiftKiosk_secure\kiosk-ng\main_kiosk.py`): the credential belongs to the individual computer and
  is held by the privileged broker — the installed device token is sent as `X-Device-Token`
  (`kiosk-ng\broker.py:132`) and there is **no login step**, identity comes from the Windows session
  alone (`main_kiosk.py:721`); the four start-screen buttons are Start Shift, Report Sick Leave, Pause and
  Report Emergency (`main_kiosk.py:995-1000`, `:137-140`), and the running panel adds **End Shift**
  (`main_kiosk.py:1249-1253`, `:144`); the Windows keys are blocked while the session runs — the low-level
  keyboard hook swallows the Windows key and Alt+F4 (`main_kiosk.py:771-788`); the event queue
  survives a dead network — an event that cannot be delivered is written to `pending_queue.json`
  (`main_kiosk.py:368-405`) and sent again by itself once the server answers
  (`main_kiosk.py:757-766`); and a forgotten session ends automatically after **11 hours** from the real
  shift start (`AUTO_LOGOUT_SECONDS = 11 * 3600` at `main_kiosk.py:816`, deadline computed at
  `main_kiosk.py:863`). The history of the 11-hour change is recorded in
  [CHANGE_HISTORY.md](./CHANGE_HISTORY.md) (2026-09-16, commit `a1b1f74`). The stale nine-hour copies that
  remain in the working trees — `C:\ShiftKiosk\deploy\main_kiosk.py:682` and
  `C:\ShiftKiosk_secure\deploy\main_kiosk.py:682`, plus two comments in
  `C:\ShiftKiosk\server\server.py` — are listed in
  [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) § **C1**.

### Scene 10 — Planned and actual

- **On the board:** Two columns of small figures. The left column is headed **planned**, the right
  **checked in**. Most figures are joined by a line from one column to the other; two are not.
- **Key message:** Plan and reality, side by side.
- **Source:** The attendance comparison joins the two systems on the employee identifier only
  (`Backend/KioskProxyService.cs`, `Frontend/src/pages/Attendance.tsx:86-99`); the kiosk's free-text
  location is displayed but never used as the match key.

### Scene 11 — A person's decision comes first

- **On the board:** One calendar cell is drawn large, with a pen mark on it and a padlock. Two arrows
  come towards the cell from two boxes labelled **generator** and **import**, and each arrow hits a small
  shield and falls away.
- **Key message:** A human decision comes first.
- **Source:** The manual marker and the save-time guard (`Backend/GSDContext.cs:25-35`,
  `GuardManualShifts` at `:56-72`, called from `SaveChanges`/`SaveChangesAsync` at `:74-90`); automatic
  writers that must preserve it are recorded in
  [ROSTER_AND_SCHEDULING.md](./ROSTER_AND_SCHEDULING.md) and [CLAUDE.md](../CLAUDE.md).

### Scene 12 — One switch that does not exist yet

- **On the board:** A large light switch is drawn in the centre of the board. Over it, a red circle with
  a diagonal line and the words **not built yet**. Under it the date **11 Oct**. From the switch a dotted
  line runs to nothing at all — it simply stops.
- **Key message:** One switch is described but not built.
- **Source:** Repository-wide search on 2026-10-11: `SupervisorMode` occurs **only in
  `documentation\*.md`**, in no `.cs` file; no `appsettings*.json` contains the key;
  `Backend/Services/SupervisorAuthClient.cs` does not exist. Full statement in
  [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) §0 and item **S14**, and in
  [SECURITY_MODEL.md](./SECURITY_MODEL.md) §4.

### Scene 13 — What is done, and what is not

- **On the board:** The board is divided in two by a vertical line. Left, under a tick: **own key per
  laptop** and **human changes protected**. Right, under an open padlock: **supervisor sign-in** and
  **data retention rule**.
- **Key message:** What is done and what is not.
- **Source:** [SECURITY_MODEL.md](./SECURITY_MODEL.md) §8 (what is closed) and §9 (what is open);
  [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) §1; the per-laptop credential is the device token
  (`kiosk-ng\broker.py:105-136`, installed per machine in `kiosk-ng\install_kiosk_ng.ps1:36`); the
  protection of a human edit is `GuardManualShifts` (`Backend/GSDContext.cs:56-72`); the owner's
  device-binding decision is item **S17** and **C26** (live `device_binding = enforce`, stays enforcing;
  the tracked profile files `dan1/2/3.ini` still say `shadow`).

### Scene 14 — Another Monday

- **On the board:** The same desk, lamp and calendar as Scene 1, drawn again but calmer. The map is
  mostly green, with one amber pin and a tick beside it. In the corner, a small flag reads **next**.
- **Key message:** One picture Anna trusts.
- **Source:** The completeness and coverage behaviour in
  [BOARD_OVERVIEW.md](./BOARD_OVERVIEW.md) §3; the security stages and the documentation work in
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
- **On-screen text:** words only, except the numbers **11 h** (Scene 7 and Scene 9) and the dates
  **6 January** (Scene 5) and **11 Oct** (Scene 12). Write those large and leave them visible long enough
  to read.
- **Never on the board:** any count of people, centres, kiosks, computers, tables or rows; any real
  employee name, user name, employee number, computer name or network address; any key, token or
  password; any screenshot or product capture; any individual sickness or incident story.
- **Two systems, never blurred:** whenever the workforce application and the kiosk are both on the
  board, the boxes are labelled and the voice says which one it means.
- **If filming happens later than 2026-10-11:** the 11-hour rule is policy and does not drift; the
  code-search date in Scene 12 must be **re-verified** and updated to the date of the new search, because
  it is a statement about the code as it stands on a given day.
