# VIDEO2 — ShiftKiosk dashboard: whiteboard explainer script

**Purpose.** The spoken script and drawing plan for the second whiteboard explainer. The first video
explained the Pulse planning side; this one explains what the **ShiftKiosk dashboard** shows and why it
matters. The viewer is the board of the company and has **no technical knowledge at all**.

**Duration.** Four to five minutes. Fifteen scenes. The narration is **633 spoken words**; the two title
cards add 20 more, for **653 in total**.

**Characters.** Two invented people, first names only.

- **Silvia** — RTM (real-time management). She watches the dashboard all day.
- **Murat** — a remote agent in Düsseldorf. He works from his own laptop and uses the kiosk.

Every other name in the video is invented too, and no real employee name, user name, employee number,
computer name, network address, key or token ever appears.

**Production rules (same as the first video).** One hand draws on a whiteboard; a calm voice reads the
narration. The board carries simple shapes, icons and arrows, and at most one or three handwritten key
words per beat. The narration never describes the drawing, so the narrator never says "as you can see".
The kiosk is only ever described by what it does. The workforce application (Pulse) and the kiosk are two
different systems, and whenever both are on the board the voice says which one it means. No counting
numbers appear anywhere except the one rule the viewer must remember — **eleven hours** — and the dates
inside the worked examples. No screenshot is filmed: the stills listed at the end are used only as
cut-aways, and they are cleaned first.

**Evidence basis.** The ShiftKiosk working tree `C:\ShiftKiosk_secure`, read read-only. Every claim in
the source column cites a file and a line. The served dashboard is the embedded page
`server/server.py` → `DASHBOARD_HTML` (`:1913-3983`), returned by `GET /dashboard` (`server.py:3985-3986`).

**One fact to know before filming.** There is no separate dashboard version. The route ignores the query
string it is given and always returns the same page (`server.py:3985-3986`; the page itself reads only
`tab`, `filter` and `theme` at `server.py:3956-3964`). What the owner calls "the dashboard" is the whole
page described here. The video therefore calls it **the dashboard**, never a version.

---

## Scene 1 — Seven in the morning

**Board card (title).** Handwritten centre: **WorkForce Pulse / ShiftKiosk**. Under it, once:
*Developed by Nebojsa Stojnic, with the selfless support of Ion Ciuceanu.*

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | It is seven in the morning. Silvia is at her desk with her coffee. | A desk, a lamp and a steaming cup; a small figure labelled **Silvia**. | The dashboard's own clock and top bar, `server/server.py:1993-1996`, `:2930-2931` |
| 2 | Her first question is always the same: who is working right now? | A speech bubble: **who is working?** with a large question mark. | The Live view is loaded first on page start, `server/server.py:3950` |
| 3 | Her second question: who is late? | A second bubble: **who is late?** | The **Not checked in** counter, `server/server.py:2243`, `:3098` |
| 4 | Both answers live in one place, and this video is about that place. | An arrow points to one rectangle labelled **dashboard**. | `DASHBOARD_HTML`, `server/server.py:1913-3983` |

---

## Scene 2 — One page, opened like any website

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | The dashboard is a page Silvia opens in her browser. | A laptop, a browser window, a bookmark ribbon. | `GET /dashboard` returns the page, `server/server.py:3985-3986` |
| 2 | No program to install, no special computer. | A cross drawn over a software box. | The page is one self-contained HTML string served by the kiosk server, `server/server.py:1913`, `:3985-3986` |
| 3 | It opens on the live view. | The word **Live** written large, with a green dot. | Default active view, `server/server.py:2247`, `:2258` |

---

## Scene 3 — The list of right now

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | In the middle is one long list: one line for every person. | A stack of small horizontal lines, one per stick figure. | The live attendance table, `server/server.py:2290-2303` |
| 2 | Each line shows the person, their team, their group, and their status. | Four little column headings: **name**, **team**, **group**, **status**. | Table headers, `server/server.py:2292-2300` |
| 3 | Green means working. Amber means sick or on pause. Grey means finished. Red means emergency. | Four small badges drawn in those colours. | Status pills and counters, `server/server.py:2239-2244`, `:3052` |
| 4 | One line also shows how long the person has already been on shift. | A small clock icon at the end of a line. | Time on shift is computed for the row, `server/server.py:3025-3031` |

---

## Scene 4 — The groups she actually thinks in

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | Above the list sit the groups: WIC, Voice, vWIC, BO, and the ones not yet assigned. | Five handwritten labels in a row. | Group tabs, `server/server.py:2259-2266` |
| 2 | Silvia can narrow the list to one group with a single click. | An arrow from **WIC** into the list; the other lines fade. | `setGroupFilter`, `server/server.py:2970-2984` |
| 3 | She can also filter by team leader, by status, or by typing part of a name. | Three small filter icons beside the list. | Filter controls, `server/server.py:2268-2281`; filters stack, `server/server.py:3002-3004` |

---

## Scene 5 — The page keeps itself current

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | The page does not need refreshing. | A crossed-out refresh arrow. | Updates arrive by themselves, `server/server.py:3505-3525` |
| 2 | It is connected to the kiosk server and updates on its own. | A dashed arrow from the laptop into the dashboard box. | Live connection opened on page load, `server/server.py:3951`, `:3507` |
| 3 | If the connection drops, a small banner appears and it reconnects by itself. | An amber banner with a plug icon. | Reconnect banner, `server/server.py:2233`, `:3508`, `:3523` |

---

## Scene 6 — When someone forgets to check in

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | Just after seven, Silvia sees a name that is still grey. | One grey line circled in the list. | The row's action for **Not checked in**, `server/server.py:3035-3036` |
| 2 | This person has started work but never pressed the button. | A laptop drawn with a finger not touching it. | No event has arrived for the day, `server/server.py:3019`, `:3035` |
| 3 | She clicks **Check in** on that line, and chooses where the person is working. | A button labelled **Check in**, then a small location dialog. | `manualCheckin`, `server/server.py:3150-3161`; location dialog, `:3109-3148` |
| 4 | The status turns green, and the record is written with her name on it. | The grey line becomes green; a small pen writes **by Silvia**. | Manual check-in writes the event and the audit entry, `server/server.py:1258-1270` |

---

## Scene 7 — When someone forgets to check out

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | At the end of the day, one line stays open. | One green line that never closes. | **Log off** action, `server/server.py:3037-3038` |
| 2 | Silvia knows the person has gone home. | A small empty chair and a coat on a hook. |  |
| 3 | She clicks **Log off**, and the dashboard closes the day for them. | The button **Log off**, then a closing bracket on the line. | `manualLogout`, `server/server.py:3163-3171` |
| 4 | The closure carries her name, so nothing is anonymous. | The pen again writes **by Silvia**. | Manual logout writes the audit entry, `server/server.py:1287-1298` |

---

## Scene 8 — An emergency is made visible

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | If an agent presses the emergency button, the dashboard reacts at once. | A red button drawn on a laptop. | Emergency counts and rows, `server/server.py:2242`, `:3008` |
| 2 | A red banner blinks at the top of the screen, whatever view is open. | A blinking red bar across the top. | Emergency banner, `server/server.py:2232`, `:3062` |
| 3 | The dashboard also plays a short sound. | Three little sound waves. | `playAlert`, `server/server.py:2927`, `:3063` |
| 4 | Silvia acknowledges it, and the alert is logged. | A tick drawn over the red bar. | `ackEmergency`, `server/server.py:3173-3179` |

---

## Scene 9 — Yesterday, already written down

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | The live view is only the first page. | A small stack of pages behind the first one. | Views grouped Operations, WIC Management, Administration, `server/server.py:2201-2203`, `:2790` |
| 2 | One page gives a daily summary: start, end, hours, and how the day ended. | A calendar page with four boxes. | Shift Summary, `server/server.py:2307-2336` |
| 3 | If the kiosk ended the day by itself, the line is marked. | A small clock icon with the word **auto**. | Auto-logout marker in the summary, `server/server.py:3191`, `:3203-3205` |
| 4 | That is the eleven-hour rule, visible afterwards. | **11 h** written beside the marker. | Auto end after eleven hours, `kiosk-ng/main_kiosk.py:816`, `:863` |

---

## Scene 10 — Hours, and how people left

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | Another page counts days worked and total hours. | A small bar chart beside two tally marks. | Hours Report, `server/server.py:2339-2366` |
| 2 | It also lists how each day ended. | Four small labels: **self**, **manual**, **auto**, **open**. | Logout Audit section, `server/server.py:2369-2376`; self/manual/auto/open split, `server/server.py:3420`, `:3436-3444` |
| 3 | A self logout is normal. A manual one means a supervisor stepped in. | Two arrows: one from the person, one from a pen. | Logout audit data and its classifications, `server/server.py:3423-3430`, `:1699-1709` |

---

## Scene 11 — The WIC centres

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | For the centres, one page answers a simple question. | One map pin circled. | WIC Coverage is date plus centre, `server/server.py:2516-2548` |
| 2 | On a chosen day, who was there, and when did they arrive and leave? | Two clocks beside the pin. | Coverage rows carry check-in and check-out, `server/server.py:3870-3898` |
| 3 | A small flag marks each person as covered. | A small green flag beside one name. | The covered flag from the report, `server/server.py:1487-1499`, rendered at `:3900` |
| 4 | Another page shows who is main, who is backup, and who is regional support. | Three labelled pins: **MAIN**, **BACKUP**, **REGIONAL**. | WIC Agents view, `server/server.py:2472-2487`; the three role columns, `server/server.py:3632-3640` |

---

## Scene 12 — The list the dashboard keeps

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | The dashboard also keeps the list of people and teams. | A small address book. | Import / Admin view, `server/server.py:2403-2461` |
| 2 | A new starter, a change of team, a change of centre: an admin updates the list. | A pencil writing one new line in the book. | Agent list and Excel import, `server/server.py:3287-3300`, `:1191` |
| 3 | Every manual action is written into a log: time, person, action, and who did it. | A notebook with a padlock on the cover. | Audit Log view, `server/server.py:2462-2471`; read-only endpoint, `server/server.py:1328` |

---

## Scene 13 — The sign-in that is still a work in progress

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | Here is the honest part. | A vertical line divides the board. |  |
| 2 | A supervisor sign-in page already exists, with a second factor. | A door with a keypad beside it. | Sign-in form and TOTP path, `server/security/__init__.py:363-385`; accounts, `server/security/accounts.py:236-281` |
| 3 | It is not switched on yet. Today, anyone who can reach the address can open the dashboard. | The door drawn open, with an open padlock. | `server/security/core.py:299-309` blocks `/dashboard` only when `session_auth` is enforcing; the recorded live state has `session_auth` observing, `documentation/SHIFTKIOSK.md` §7 |
| 4 | Until sign-in is enforced, we treat the dashboard as an internal page and keep the address inside the company. | A hand holding the page close to the chest. | Recorded open risk: anonymous reading of the kiosk server, `documentation/SECURITY_MODEL.md` §9 item 2 |

---

## Scene 14 — Why the numbers can be trusted

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | One more reason the dashboard is useful: nobody has to remember to fill it in. | A crossed-out pen and paper. | The page reads the kiosk records directly, `server/server.py:1884-1891`, `:3499-3501` |
| 2 | The kiosk writes the day as it happens. | A laptop sending a small envelope. | Events arrive on the live connection, `server/server.py:3505-3512` |
| 3 | Murat checks in from Düsseldorf, and a moment later Silvia sees it. | A pin labelled **Düsseldorf**, then a green line in the list. | Remote location and identity are carried by Murat's own computer, not typed, `kiosk-ng/broker.py:59`, `:130-132`; `kiosk-ng/main_kiosk.py:714`, `:1085` |
| 4 | His hours are later visible in the reports. | The small bar chart from before, with one more bar. | Hours Report, `server/server.py:3392-3420` |

---

## Scene 15 — The same day, in one place

**Board card (title).** Handwritten centre: **WorkForce Pulse / ShiftKiosk**. Under it, once:
*Developed by Nebojsa Stojnic, with the selfless support of Ion Ciuceanu.*

| # | Narration (spoken exactly as written) | Drawn on the board | Source in code (file:line) |
|---|---|---|---|
| 1 | Six in the evening. Silvia closes the laptop. | The desk from the first scene, now calm. |  |
| 2 | The dashboard is not a report she writes. It is a record she trusts. | The one rectangle labelled **dashboard**, ticked. | The page is a read of the stored attendance, `server/server.py:422`, `:1884-1891` |
| 3 | The work is not finished. | One open padlock left visible in the corner. | Recorded open items: retention rule and supervisor sign-in, `documentation/OPEN_ITEMS_AND_RISKS.md` §1 |
| 4 | But for the first time, the day fits on one screen. | The whole board shrinks into one frame. | The live view, `server/server.py:2258-2304` |

---

## SCREENSHOTS FOR FILMING

Six stills. Each one is a cut-away under the scene named. Before a still is used, the whole frame must be
checked: **no real agent name, no KID, no employee number, no computer name, no IP address, no browser
address bar or tunnel URL, no signed-in user name, and no sickness or other health data.** Attendance
history is not evidence of individual performance, so no still may imply that. If a row cannot be
cleaned, use a different row. Take the stills on the served page; do not sign in anywhere to get them.

| # | Screen or tab | What must be visible | Scene | Narration that goes with it | Crop or blur before use |
|---|---|---|---|---|---|
| 1 | **Live** (`tab-live`), default view | The row of status tiles, the group tabs WIC / Voice / vWIC / BO / Unassigned, and the top of the table with the columns Name, Team Leader, Group, Status, Check-in, On Shift, Check-out | 3 and 4 | "One line for every person, and the groups Silvia actually thinks in." | Blur every name, team-leader name and note in the table; blur the traffic-light status notes if they name a person. Keep only the tiles, the tabs and the column headings. |
| 2 | **Live**, one row with the action button | The **Check in** button on a row, and the **Performed by** name field | 6 | "She clicks Check in on that line, and the record is written with her name on it." | Blur the agent name on the row and the **Performed by** field. Never show a row whose note mentions sickness. |
| 3 | **Live**, emergency state | The red emergency banner at the top of the page, and one row in the emergency colour | 8 | "A red banner blinks at the top of the screen, whatever view is open." | Blur the agent name and the free-text emergency note. If the only emergency in the data carries a health detail, do not film it; use the emergency colour swatch instead. |
| 4 | **Shift Summary** (`tab-summary`) | The date range, the Login / Logout / Hours columns, and one row carrying the auto marker | 9 | "Start, end, hours, and how the day ended." | Blur every name and team leader. Keep the date column only if the dates are the example dates; otherwise blur. |
| 5 | **Hours Report** (`tab-worked`) | The Days Worked and Total Hours columns, and the Logout Audit segment with self / manual / auto / open | 10 | "It also lists how each day ended." | Blur every name and team leader. Blur any single person's totals, so no row can be read as a performance judgement. |
| 6 | **WIC Coverage** (`tab-wic`) | The date field, the centre selector, and two or three rows with check-in, check-out and the covered flag | 11 | "On a chosen day, who was there, and when did they arrive and leave." | Blur every name, every centre's address and any centre code that identifies a site. Keep the column headings and the covered flag. |

Optional seventh still, only if the owner wants the gap shown on camera: the **supervisor sign-in form**
at `/login`, with the username and password boxes empty. It pairs with Scene 13 and nothing else is
visible on it. It must not be filmed as if sign-in were protecting the dashboard today.

---

## WHAT WAS FOUND IN THE CODE AND DELIBERATELY LEFT OUT OF THE VIDEO

1. **The earlier kiosk program.** The repository still contains it, and the live inventory still lists
   computers that run it with the older credential. The video is about the dashboard and the current
   kiosk only, so none of this appears. Sources: `kiosk/main_kiosk.py`,
   `server/profiles/dan0.ini`, `server_config.ini [Security]`.
2. **The machine-by-machine rollout to the current kiosk.** It is an internal procedure, not a board
   story: the rollout history and the fix that made the eleven-hour end of a shift count real time instead
   of process time.
3. **Any version number of any component.** Endpoint, client and package versions are changeable and are
   forbidden in this material. `server/server.py:1018`, `kiosk-ng/broker.py:63`.
4. **Every count in the system.** Number of agents, computers, centres, groups, tabs, rows and events.
   The dashboard shows counts on screen as KPI tiles and group counters; the video never speaks them.
   `server/server.py:2239-2265`, `:3090-3101`.
5. **The import file format and column names.** `Emp ID`, `Employee Name`, `Team Leader`, `WIC Location`,
   `Engagement`, `Windows Username` are technical detail. `server/server.py:1191`, `:3287-3300`.
6. **The database, tables, views and SQL.** The technical back end of the dashboard. `server/server.py:422`.
7. **The session token, its lifetime and its refresh.** The dashboard fetches a short-lived token on page
   load and renews it in the background; the master key never reaches the browser. This is a security
   detail for engineers, not for the board. `server/server.py:3942-3948`, `:3968-3976`.
8. **The supervisor password-change endpoint and its weaknesses.** It exists but has no session
   requirement, no rate limit and no lockout. Not a board message. `server/security/__init__.py:403-409`,
   `documentation/OPEN_ITEMS_AND_RISKS.md` item S7.
9. **Zero supervisor accounts.** The stronger sign-in path has never been used end to end. Mentioning the
   sign-in page without this would be misleading, and mentioning the empty account table adds nothing to
   the story. `documentation/SECURITY_MODEL.md` §4.
10. **The credential that both signs sessions and sends events.** The value that signs a dashboard session
    is the same value the older kiosk computers use to send events, so whoever holds it can mint a session
    that reaches the dashboard's data routes. A real risk, and a fix for the engineers, not a board
    sentence. `documentation/SECURITY_MODEL.md` §7.
11. **The device-token limits.** A local administrator can read a token or bypass the broker. Too deep for
    this video; it belongs in the written security pack. `documentation/SECURITY_MODEL.md` §9 item 11.
12. **The observation log and the anomaly tables.** Every request to the kiosk server is recorded, with
    counts of allowed and would-be-blocked requests. Useful evidence for the security work, noise for the
    board. `documentation/SECURITY_MODEL.md` §5.
13. **Rate limiting and cross-origin policy.** Both are configuration-level facts with no board meaning.
    `server/security/core.py:110-115`, `documentation/OPEN_ITEMS_AND_RISKS.md` item S21.
14. **The retention gap.** No retention or deletion path exists for attendance events, the observation log
    or manual actions; historical attendance is on record as not valid evidence of individual
    performance. Scene 15 keeps only the one open-padlock line; the detail belongs in the written pack.
    `documentation/OPEN_ITEMS_AND_RISKS.md` items D11 and D12.
15. **Team-lead scoping.** The dashboard has team filters, and the video shows them as filters. The
    recorded limitation that team scoping is not enforced across the other system is left to the written
    documentation. `documentation/OPEN_ITEMS_AND_RISKS.md` item S9.
16. **Notifications by Teams and e-mail.** Configured and disabled at the last recorded state, and a
    personal-data transfer if enabled. `server/server.py:922-988`,
    `documentation/SHIFTKIOSK.md` §7 item 8.
17. **Two systems, two databases.** The dashboard reads the kiosk database; Pulse has its own. The video
    says "the dashboard" and "the kiosk" and never explains the two databases.
    `documentation/SYSTEM_ARCHITECTURE.md`.
