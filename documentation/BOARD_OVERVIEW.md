# WorkForce Pulse & ShiftKiosk — Overview for the Board

**Prepared 2026-10-11.** Every figure in this document was read from the live systems on that date
(read-only), and every figure names the system and the source table it came from. Nothing here is an
estimate and nothing is rounded. A companion document, [VIDEO_BRIEF.md](./VIDEO_BRIEF.md), turns this
into a whiteboard explainer; [VIDEO_NARRATION.md](./VIDEO_NARRATION.md) holds the same story as a
ready-to-read narration.

> ## ⚠ The single most important thing in this document
>
> **A supervisor sign-in switch that several documents describe is NOT IMPLEMENTED IN CODE as of
> 2026-10-11.** Nothing in the running workforce application reads it: there is no such setting in any
> source file or in any `appsettings*.json`, and the client class that would connect the workforce
> application to the kiosk's supervisor check does not exist. An operator who followed the current
> instructions would believe supervision had been switched on when nothing had changed. This is
> finding number 1 in [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md). Read it before acting on
> any old document.

---

## Sažetak na srpskom (za tebe)

Dva su sistema, jedan cilj: da se zna **ko treba da radi gde** i **ko je stvarno tu**.

- **WorkForce Pulse** je aplikacija za planiranje i pregled. Tu žive plan smena, raspored agenata po WIC
  centrima, bolovanja, godišnji odmori, lista ljudi, obuke i izveštaji. Na dan 2026-10-11: **122 aktivna
  radnika** (128 redova u tabeli `Employees`), **43 WIC lokacije** (41 Nemačka, 2 Holandija), **41.931 red
  u planu smena** (do 26.02.2027) i **122 od 122** aktivna radnika imaju smenu u narednih 14 dana.
- **ShiftKiosk** je evidencija prisustva. Na laptopu agenta radi mali program koji javlja početak smene,
  pauzu, hitan slučaj i kraj. **Kiosk ima svoju, odvojenu listu agenata: 123 aktivna od 139 redova** u
  tabeli `ShiftKioskDB.agents` (WIC 62, VOICE 32, VWIC 4, BO 25, nerazvrstanih 0). **To nije isti spisak
  kao Pulse radnici** — 122 radnika u Pulse-u i 123 kiosk agenta su dva različita sistema i nikada se ne
  smeju mešati.
- Do sada je zabeleženo **2.552 događaja prisustva**, **519 ručnih intervencija supervizora** i
  **71 registrovanih računarskih kredencijala**, a kiosk server je video **63 računara**: **44 hosta** se
  još prijavljuje deljenim „legacy" ključem, **19 hostova** već koristi novi klijent. **Migracija je u
  toku** — nije završena.
- **Dva nalaza koja moraš znati pre nego što bilo šta kažeš o bezbednosti:**
  1. Prekidač za prijavu supervizora („SupervisorMode") koji dokumentacija opisuje **nijedan red koda ne
     čita**, a deo koji bi ga povezao sa aplikacijom **ne postoji**. To je nalaz broj 1.
  2. Na živom serveru je **device binding u režimu „enforce"** i tako ostaje; profili `dan1/2/3.ini` ga
     još vode kao „shadow", pa dokumentacija zaostaje za stvarnim stanjem.
- **Šta treba da uradiš ti:** vidi poslednju sekciju ovog dokumenta.

---

## 1. The problem

Before this system, the answer to "who is working where today, and is every customer centre covered?"
lived in spreadsheets, e-mail and people's heads. Four consequences followed:

1. **No shared picture.** A planner, a team lead and a coordinator each held a different version of the
   same week.
2. **Coverage was discovered too late.** A customer centre could be left short because nobody compared
   the two lists in time.
3. **Absence handling was manual.** A sickness note had to be copied into the plan by hand, and the copy
   could be forgotten or applied twice.
4. **Attendance was invisible.** Nobody could say with confidence who had actually arrived, or when,
   except by asking around.

The system exists to replace all four: one place where the plan, the absences and the actual attendance
meet, built so that a human decision cannot be silently undone by automation.

---

## 2. The solution

Two products, deliberately separate:

- **WorkForce Pulse** plans the work. A planner generates a person's roster in one action, using each
  centre's own opening hours; the system skips weekends and public holidays, shows a preview before it
  writes, and keeps a snapshot so the run can be undone. Coverage is classified continuously as covered,
  partially covered, uncovered or closed. Sickness and annual leave are entered once and applied to the
  plan automatically. A back-office list, a bulk one-day entry tool, a training scheduler and a central
  export complete the picture. An in-app assistant answers questions from this system's own data —
  **no external artificial-intelligence service is called**.
- **ShiftKiosk** records what actually happened. A small program on the agent's laptop shows a fullscreen
  choice at the start of the day and then a compact floating panel. It records start, breaks,
  emergencies and the end of the shift, and it queues events when the network is down so nothing is
  lost. A shift closes automatically after **11 hours**.

The two meet in exactly one place: **the employee identifier**. Pulse decides who *should* be somewhere;
the kiosk records who actually checked in; the attendance page joins the two and shows the difference in
both directions. The kiosk's own free-text location is displayed but never used in that comparison.

---

## 3. The value

The value is in three places, and none of them is a new feature:

1. **The plan is complete and current.** On 2026-10-11 all 122 active Pulse employees had a shift within
   the next 14 days. That is a result of the generator plus a missing-roster warning, not a coincidence.
2. **A coverage gap is visible before it becomes a customer problem.** A centre at risk is flagged with
   ranked substitutes, not just a colour.
3. **Every manual change is attributable and protected.** A human edit is stamped with who and when, and
   is protected at three levels: automatic writers check and skip it, a save-time guard refuses to
   overwrite it, and the marker fields are concurrency tokens so a concurrent writer fails loudly instead
   of silently clobbering. That is what makes the data defensible in a review.

The system was built incrementally inside the team over roughly five months, using the SQL Server
instance and the team's own laptops that were already available. There was no procurement, no external
platform and no licence beyond the Microsoft stack already in place.

---

## 4. The state today

All figures below were read **read-only from the live databases on 2026-10-11**. Where two systems both
have a number, the system is named.

**WorkForce Pulse — database `GSDDashboard`**

| What | Number | Source |
|---|---|---|
| Employees | **128 rows, 122 active** | `Employees` (`IsActive=1`) |
| Active team leads | **6** | `Employees.TeamLeadName`, distinct active |
| WIC centres | **43** — 41 Germany, 2 Netherlands, all active | `WicLocations` |
| Shift-plan rows | **41,931**, 2026-01-01 → 2027-02-26 | `ShiftEntries` |
| Roster completeness | **122 of 122** active employees have a shift in the next 14 days | computed |
| WIC assignment rows | **23,945**, of which 9,137 on-site | `WicShiftEntries` |
| WIC assignment tiers | **162** — 52 main, 70 backup, 40 regional | `WicAgentAssignments.AssignmentType` |
| Opening-hours records | **417** | `WicOpeningHours` |
| Annual-leave bookings | **765** | `Vacations` |
| Sick-leave records | **350** | `SickLeaves` |
| Back-office list entries | **276** | `BoEntries` |
| Administrative accounts | **9** (1 developer, 2 RTM, 6 team leads) | `AppUserRoles` |
| Application tables | **52 physical** — 26 application/framework and 26 archive/backup copies | live schema |
| Newest features | Rollout page and Bulk RTM Entry — live but not yet used (0 rows) | `WicRolloutDays`, `WicRolloutWorkers` |

**ShiftKiosk — database `ShiftKioskDB`** (a **different** agent list from Pulse, never to be mixed up)

| What | Number | Source |
|---|---|---|
| Kiosk agents | **139 rows, 123 active** — WIC 62, VOICE 32, VWIC 4, BO 25, unclassified 0 | `agents` (`active=1`) |
| Attendance events | **2,552**, 2026-05-28 → 2026-10-10 | `status_log` |
| Supervisor manual actions | **519** — 331 check-ins, 188 log-outs | `manual_actions` |
| Enrolled computer credentials | **71** (67 active, 4 revoked) | `sec_devices` |
| Computers seen by the server | **63** — **44** on the legacy shared key, **19** on the new device credential | `sec_kiosk_registry.credential_type` |
| Security observation rows | **283,206** (allow 269,365 · would-block 13,788 · blocked 53) | `sec_observation_log` |

**Two honest readings of these numbers.**

- *Attendance volume (2,552 events over 4½ months) is low relative to the workforce.* Most events are
  recorded manually by supervisors today, not by agents at kiosks: **519 of the recorded actions are
  supervisor interventions**. The kiosk migration is **in progress** — 44 computers still use the legacy
  key and 19 use the new client — and it is not finished.
- *The two agent counts answer different questions.* Pulse's 122 active employees are the planning
  population; the kiosk's 123 active agents are the attendance system's own list. They are close in size
  but they come from different tables and are not the same list.

---

## 5. Security — stated plainly

> ### ⚠ Supervisor sign-in is NOT IMPLEMENTED IN CODE as of 2026-10-11
>
> Several documents describe a supervisor sign-in switch ("SupervisorMode") as an operating control with
> `off`, `shadow` and `enforce` states, and one describes a recovery procedure that depends on it.
> **None of it is implemented.** A repository-wide search of the workforce application finds no reader
> for the setting, no provider interface and no client class; the configuration file contains no such
> key. The kiosk server can verify a supervisor credential, but nothing in the workforce application
> asks it to. **Do not treat any document that instructs you to set this switch as a runbook step.**

**What is protected.** Sign-in to the workforce application is role-based, with four roles and a
server-side allow-list that scopes an agent to their own data. The most sensitive read — the attendance
feed — is reachable only through an authenticated server-side proxy: anonymous callers are refused
before any request leaves the application, and only three roles may pass. The API documentation page
returns "not found" to anyone who is not a developer. Administrative commands are developer-only.
Agents' identity at the kiosk is derived from the operating-system session by a privileged broker, not
from anything the client sends.

**What was found and fixed.** In early October a review found a real exposure: the kiosk server
published every agent's employee number on an endpoint that required no credential, and the workforce
application accepted a bare employee number as a sign-in. Together those two facts allowed anyone who
could reach the kiosk server to sign in **as an agent** — bounded to agent level, never an administrator
or a team lead. Both halves were closed: the workforce application now reaches kiosk data only through
its authenticated proxy, and the kiosk server's shared key can no longer be used on the session
endpoints, nor a session token on the attendance-write endpoint. The change required no action from
agents.

**What is still open — and the board should hear this without softening.** The kiosk server still issues
its session token to anyone who can reach it, and that token can still be used to **read** attendance
history, the audit log and the exports; closing that needs supervisor accounts first, or supervisors
would be locked out. The shared legacy key is still in daily use by **44 computers** and still signs
those tokens; its retirement criterion is thirty days with no legacy traffic, and that clock has not
started because the traffic is real. Two administrative command endpoints have no gate of their own
while the general authorisation switch is off. No retention or deletion rule has been agreed for the
attendance and observation data, which includes sickness information.

**One configuration fact the board should know.** On the live kiosk server, **device binding is
enforcing**: the control that ties a credential to one computer and one user is blocking in production.
The tracked profile files (`dan1.ini`, `dan2.ini`, `dan3.ini`) still record it as `shadow`
(observation-only). The live server is the authority; the documents must be read as behind reality.

**What we deliberately do not claim.** There is no penetration-test report. There is no alerting
pipeline — detection data is written to a database and read by a person. Historical attendance is
recorded as **not** valid evidence of individual performance, and that must be honoured in any reporting
built from it.

---

## 6. The plan

| Priority | Next step | Why now |
|---|---|---|
| 1 | **Make the supervisor sign-in documents match reality** | Finding number 1: two documents tell an operator to switch on a mode that no code reads, and the part that would connect it does not exist. Correct the documents (or build the missing connection) before anyone acts on them. |
| 2 | **Record the live device-binding state in the profile files** | The live server enforces while `dan1/2/3.ini` say `shadow`. Either write the live state into the profiles, or change the live server — but stop the document and the system disagreeing. |
| 3 | Provision the supervisor accounts and prove the recovery path | It is the prerequisite for the next two security stages; without it supervisors would be locked out |
| 4 | Finish the attendance-capture migration | 44 computers still use the legacy key; it cannot be retired until the rest move to the new client (19 today) |
| 5 | Complete the enforcement stages for anonymous identifiers and anonymous reads | Two of the nine kiosk controls are still observing |
| 6 | Close the two administrative endpoints that have no gate of their own | They rely on the general authorisation switch, which is off |
| 7 | Agree a data-retention rule for attendance and observation data | The observation log has 283,206 rows and grows with every request; sickness data is a special category |
| 8 | Close the remaining correctness gaps | The legal shift rules are only partly server-enforced, and two known rules have arithmetic limitations |
| 9 | Bring the remaining documentation into agreement with the code | Several older files state figures and behaviours that are no longer true |

**A note on pace.** The system is in daily operational use and the plan is complete. The work that
remains is less about new features and more about finishing the security stages and making the
documentation match reality — which is the correct order for a system that people now depend on.

---

## 7. What the owner needs to do

1. **Decide the fate of the supervisor sign-in documents.** Finding number 1 needs an owner decision,
   not more analysis: either build the missing connection or rewrite the affected documents so that no
   operator is told to use a switch that does nothing. This set now carries the warning "NOT IMPLEMENTED
   IN CODE as of 2026-10-11" wherever the switch is described; the older documents outside this set
   still describe it as an operating control and still carry personal names and machine names, so they
   should not be handed out.
2. **Confirm the live device-binding decision.** Device binding is enforcing on the live server and
   stays that way. The tracked profiles should be brought into line, and the profiles should stop
   recording it as observing-only.
3. **Approve the supervisor-account provisioning window.** It is the gate for the next two security
   stages and needs a short authorised maintenance window and a secure hand-off of one-time credentials.
4. **Get a data-protection decision on retention.** Two questions: how long attendance and observation
   data is kept, and written confirmation that historical attendance is not used as individual
   performance evidence.
5. **Approve the video storyline.** Review [VIDEO_BRIEF.md](./VIDEO_BRIEF.md) and
   [VIDEO_NARRATION.md](./VIDEO_NARRATION.md); both are whiteboard explainers and contain no screenshots,
   no names and no machine identifiers.
