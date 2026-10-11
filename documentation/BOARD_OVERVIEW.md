# WorkForce Pulse & ShiftKiosk — Overview for the Board

**Prepared 2026-10-11.** Every figure in this document was read from the live systems on that date.
Nothing here is an estimate, and nothing is rounded up. A companion document,
[VIDEO_BRIEF.md](./VIDEO_BRIEF.md), turns this into a suggested narrative for a board video.

---

## Sažetak na srpskom (za tebe)

Dva su sistema, jedan cilj: da se zna **ko treba da radi gde** i **ko je stvarno tu**.

- **WorkForce Pulse** je aplikacija za planiranje i pregled. Tu žive plan smena, raspored agenata po WIC
  centrima, bolovanja, godišnji odmori, lista ljudi, obuke i izveštaji. Danas: **122 aktivna radnika**,
  **43 WIC lokacije** (41 Nemačka, 2 Holandija), **41.931 red u planu smena** koji seže do 26.02.2027, i
  **122 od 122** aktivna radnika imaju smenu u narednih 14 dana — plan je potpun.
- **ShiftKiosk** je evidencija prisustva. Na laptopu agenta radi mali program koji javlja početak smene,
  pauzu, hitan slučaj i kraj. Server to čuva i prikazuje supervizoru. Do sada: **2.552 događaja**,
  **519 ručnih intervencija supervizora**, **71 registrovan računar**.
- Pulse **čita** podatke iz kioska preko autentifikovanog posrednika — nikad direktno iz pregledača.
- **Ručne izmene su zaštićene**: ko je šta promenio i kada, i nijedan automatski proces ne sme to da
  pregazi.
- **Bezbednost je poštena priča**: lanac zloupotrebe koji je nađen u oktobru je **zatvoren**, ali
  anonimno čitanje istorije i izveštaja sa kiosk servera **još nije zatvoreno** (to je predviđeni
  sledeći korak), a deljeni „legacy" ključ još koristi oko 60 računara. To ne treba ublažavati u videu.
- **Dva nalaza koja moraš znati pre nego što bilo šta kažeš o bezbednosti:**
  1. Dokumentacija opisuje prekidač za prijavu supervizora koga **nijedan red koda ne čita**, a deo koji
     bi ga povezao sa aplikacijom **ne postoji**. Operater bi mislio da je nešto uključio, a nije.
  2. U živoj konfiguraciji je **device binding u režimu „enforce"**, dok ga svi profili i oba glavna
     dokumenta vode kao „shadow" — dakle kontrola blokira u produkciji iako po zapisanom procesu nije
     promovisana.
- **Šta treba da uradiš ti:** vidi poslednju sekciju ovog dokumenta.

---

## 1. The problem we set out to solve

Before this system, the answer to "who is working where today, and is every customer centre covered?"
lived in spreadsheets, e-mail and people's heads. Four consequences followed, and all four are the
reason the system exists:

1. **No shared picture.** A planner, a team lead and a coordinator each had a different version of the
   same week.
2. **Coverage was discovered too late.** A customer centre could be left short because the person who
   knew that had not compared two lists.
3. **Absence handling was manual.** A sickness note had to be copied into the plan by hand, and the copy
   could be forgotten or applied twice.
4. **Attendance was invisible.** Nobody could say with confidence who had actually arrived, or when,
   except by asking around.

The system replaces all four. It is one place where the plan, the absences and the actual attendance
meet, and it is deliberately built so that a human decision cannot be silently undone by automation.

---

## 2. Who uses it, and for what

| User | What they get |
|---|---|
| **Agents** | A single kiosk screen to start a shift, take a break, report an emergency or finish. A personal read-only page shows their own shifts, leave and balance. |
| **Team leads (6 active)** | Their team's attendance, leave and coverage, grouped by team lead — the grouping field the whole application sorts on. |
| **RTM / coordination** | The operating cockpit: who is on site, who is checked in, who is missing, move someone to back-office work, fill a gap with a ranked list of substitutes, plan annual leave inside a coverage view. |
| **Planning / workforce management** | The shift plan, the roster generator, the WIC schedule, the forecast, the break planner, the virtual-centre rotation, and exports. |
| **Administrators / developers** | People records, WIC master data, opening hours, roles, and the maintenance commands. |

**Roughly 122 active employees** are visible in the system today, alongside **128 total records**
(the difference is leavers who are kept for history).

---

## 3. What the system does

### Planning and rostering
A planner generates an employee's whole roster in one action — either a plain working pattern or a WIC
pattern where the system uses each centre's own opening hours, sends the person to the centre on open
days and to back-office work on the others, and skips weekends and public holidays automatically. The
action shows a preview before it writes, records the run as a batch, and can be undone: the batch keeps
a snapshot of anything it replaced. Opening hours are held as **417** dated records across the 43
centres, so a one-off day — such as a centre opened specially for new workers — can be stated once and
is then honoured by every screen.

### Coverage and attendance
Each WIC centre is classified continuously as covered, partially covered, uncovered or closed, based on
who is assigned and the centre's hours for that date. The attendance page then joins that expectation
with what the kiosks actually recorded — **matched on the employee, never on a location name** — and
shows the difference in both directions: present but not rostered, and rostered but not present. The
three headline numbers are built so that they can never contradict each other.

### Absence and leave
Sickness and annual leave are entered once. The system applies them to the shift plan automatically,
remembers what the original state was, and reverses itself correctly when a sickness ends or a leave
booking is deleted. Annual-leave balances are held per person **and per year**.

### Worklists and reporting
A back-office list, a bulk entry tool for one-day changes, a training scheduler, a special-event
(pipeline) register, and a central HR export that produces a workbook from any combination of shift
plan, sickness, annual leave, attendance, balance and training data.

### An internal assistant
A question-and-answer page suggests who can substitute, what leave is coming and where coverage is at
risk. **It uses no external artificial-intelligence service** — every answer is computed from this
system's own data.

### Attendance capture (ShiftKiosk)
A small program on the agent's own laptop shows a fullscreen choice at the start of the day and then a
compact floating panel with the elapsed shift time. It records the start, breaks, emergencies and the
end of the shift, and it queues events when the network is unavailable so nothing is lost. Shifts
close automatically after **11 hours**.

---

## 4. The numbers, with their source

All figures read from the live databases on **2026-10-11**.

| What | Number | Source |
|---|---|---|
| Active employees | **122** (128 records total, 6 leavers) | `GSDDashboard.Employees` |
| Active team leads | **6** | distinct team-lead values |
| WIC centres | **43** — 41 in Germany, 2 in the Netherlands, all active | `GSDDashboard.WicLocations` |
| Shift-plan rows | **41,931**, covering 2026-01-01 → **2027-02-26** | `ShiftEntries` |
| Roster completeness | **122 of 122** active employees have a shift in the next 14 days | computed, same date |
| WIC assignment rows | **23,945**, of which **9,137** on-site | `WicShiftEntries` |
| WIC assignment tiers | 49 main · 60 backup · 39 regional | `WicAgentAssignments` |
| Opening-hours records | **417** across 43 centres | `WicOpeningHours` |
| Annual-leave bookings | **765** total — 764 in 2026 (2,117 net days) and 1 in 2027 | `Vacations` |
| Sick-leave records | **350** | `SickLeaves` |
| Back-office list entries | **276** | `BoEntries` |
| Pipeline events | **3** | `WicPipeline` |
| Accepted substitutions | **49** | `SubstitutionHistory` |
| Attendance events stored | **2,552** (since 2026-05-28) | `ShiftKioskDB.status_log` |
| Supervisor manual actions | **519** (331 check-ins, 188 log-outs) | `manual_actions` |
| Enrolled computer credentials | **71** (67 active, 4 revoked) | `sec_devices` |
| Computers seen by the kiosk | **63** | `sec_kiosk_registry` |
| Security observation rows | **282,745** | `sec_observation_log` |
| Client versions in use | **2.1.1** (legacy) and **3.0.0-ng** (current) | `status_log` |
| Administrative accounts | 9 — 1 developer, 2 RTM, 6 team leads | `AppUserRoles` |
| Application tables | 35 functional (52 physical, of which 17 are historical copies) | live schema |
| Newest features | Rollout page and improved Bulk RTM Entry — **both live but not yet used** (0 rows) | `WicRolloutDays`, `WicRolloutWorkers` |

**Two honest readings of these numbers:**

- *Roster completeness at 122 of 122 is a result, not a coincidence.* It is the direct outcome of having
  a generator plus a missing-roster warning that sits on the front page.
- *Attendance volume (2,552 events over 4½ months) is low relative to the workforce.* That is because
  most events are recorded manually by supervisors today, not by agents at kiosks: **519 of the
  recorded actions are supervisor interventions**. The kiosk migration is under way — 18 computers have
  moved to the new client — but it is not finished.

---

## 5. Why it is trustworthy (the part that matters most)

Four design decisions make the data defensible rather than merely convenient:

1. **Manual decisions are protected, structurally.** When a human changes a shift, the row is stamped
   with who and when, and it is protected at three levels: every automatic process checks and skips it,
   a save-time guard refuses any operation that would overwrite it, and the marker fields are optimistic
   locks so a concurrent writer fails rather than clobbers. If a future change forgets one of those
   checks, the result is a loud error, not a silent overwrite.
2. **Nothing is irreversible.** Roster generation keeps a snapshot of what it replaced; deleting the run
   restores it. Deployments keep a rollback snapshot, and flag, code, database-schema and
   application rollbacks are four separate, documented operations.
3. **Failures are visible.** If the attendance feed cannot be reached, the page shows a specific error
   rather than an empty list — a timeout, an outage and a malformed response are three different
   statuses. `/health` returning 200 is explicitly not treated as proof that a deployment works.
4. **The numbers are computed once.** Coverage classification, opening-hours resolution and the
   attendance counting identity each live in exactly one place, so a map, a card and a report cannot
   disagree about the same day.

---

## 6. Security — stated plainly

**What is protected.** Sign-in is role-based, with four roles and a server-side allow-list that scopes
an agent to their own data. The most sensitive read — the attendance feed — is reachable only through
an authenticated server-side proxy: anonymous callers are refused before any request leaves the
application, and only three roles may pass. The API documentation page returns "not found" to anyone
who is not a developer, so the system's internal shape is not published. Administrative commands are
developer-only. Agents' identity at the kiosk is derived from the operating-system session by a
privileged broker, not from anything the client sends.

**What was found and fixed.** In early October a review found a real exposure: the kiosk server
published every agent's employee number on an endpoint that required no credential, and the workforce
application accepted a bare employee number as a sign-in. Together those two facts allowed anyone who
could reach the kiosk server to sign in **as an agent** — bounded to agent level, never an
administrator or a team lead. Both halves were closed: the application now reaches kiosk data only
through its authenticated proxy, and the kiosk server's shared key can no longer be used on the session
endpoints, nor a session token on the attendance-write endpoint. The change required no action from
agents.

**What is still open — and the board should hear this without softening.** The kiosk server still issues
its session token to anyone who can reach it, and that token can still be used to **read** attendance
history, the audit log and the exports. Closing that is the next planned stage and requires provisioning
supervisor accounts first, because the alternative is locking supervisors out. The shared "legacy" key
is still in daily use by roughly sixty machines; its retirement criterion is thirty days with no legacy
traffic, and that clock has not started because the traffic is real. Device binding and rate limiting
are currently observing only, not blocking. And no retention or deletion rule has been agreed for the
attendance and observation data, which includes sickness information.

**What we deliberately do not claim.** There is no penetration-test report. There is no alerting
pipeline — detection data is written to a database and read by a person. Historical attendance is
recorded as **not** valid evidence of individual performance, and that must be honoured in any reporting
built from it.

---

## 7. What it cost, and what it replaced

The system was built incrementally inside the team over roughly five months, using the tools already
available: the existing SQL Server instance, the team's own laptops as kiosk clients, and internal
developer time. There was no procurement, no external platform and no licence beyond the Microsoft
stack already in place.

It replaced: the quarterly shift spreadsheet, the manual absence copy-into-the-plan step, the ad-hoc
coverage checks, and the "ask around" method of finding out who was in.

**The value is in three places:** the plan is complete and current; a coverage gap is visible before it
becomes a customer problem; and every manual change is attributable and protected, which is what makes
the data usable for planning and defensible in a review.

---

## 8. Where it is going

| Priority | Next step | Why now |
|---|---|---|
| 1 | **Reconcile the supervisor sign-in documentation with reality** | Two documents tell an operator to switch on a supervisor mode that **no code reads**; the Pulse-side integration it describes does not exist. An operator would believe supervision had been enforced when nothing had changed. This is the most serious documentation defect found and it must be corrected before anyone acts on it. |
| 2 | **Decide the live security-configuration mismatch** | Device binding is **enforcing** in the live configuration while every tracked profile and both canonical documents record it as observing-only, pending a fourteen-day evidence gate. Either promote it formally or turn it back — but stop the document and the system disagreeing. |
| 3 | Provision the supervisor accounts and prove the recovery path | It is the prerequisite for the next two security stages; without it supervisors would be locked out |
| 4 | Finish the attendance capture migration | 18 of the machines use the new client; the legacy key cannot be retired until the rest follow |
| 5 | Complete the enforcement stages for anonymous identifiers and anonymous reads | Two of the nine controls are still observing; the evidence base to promote them is already being recorded |
| 6 | Close the two administrative endpoints that have no gate of their own | They rely entirely on the general authorisation switch, which is off, so they are reachable without signing in |
| 7 | Agree a data-retention rule for attendance and observation data | The observation log has 282,745 rows and grows with every request; sickness data is a special category |
| 8 | Close the remaining correctness gaps | The legal shift rules are only partly server-enforced, and two known rules have arithmetic limitations |
| 9 | Bring the remaining documentation into agreement with the code | Several documents state figures and behaviours that are no longer true; the board should not be shown those |

**A note on pace.** The system is in daily operational use and the plan is complete. The work that
remains is less about new features and more about finishing the security stages and making the
documentation match reality — which is the correct order for a system that people now depend on.

---

## 9. What you need to do

1. **Decide which numbers are quoted publicly.** The documentation currently disagrees with itself:
   employee counts of 57, 123, 129 and 130 appear in different files, and the table count is variously
   21 and 52. This document uses the verified figures — **122 active employees, 43 centres, 35
   application tables**. Use these.
2. **Do not hand the existing documentation to the board as-is.** Several files contain people's names,
   machine names and internal addresses, and several contain outdated facts. Point the board at this
   overview and the video brief instead.
3. **Approve a short documentation-and-configuration reconciliation.** Two items need an owner's
   decision rather than more analysis: the supervisor-mode switch that no code reads, and the live
   device-binding setting that contradicts its own documented promotion gate. Both are described in
   section 8, priorities 1 and 2.
4. **Approve the supervisor-account provisioning window.** It is the gate for the next two security
   stages and it needs a short, authorised maintenance window and a secure hand-off of one-time
   credentials.
5. **Get a data-protection decision on retention.** Two questions: how long attendance and observation
   data is kept, and written confirmation that historical attendance is not used as individual
   performance evidence.
6. **Fix the two screens before any German-language demonstration.** Two pages still mix or omit
   languages; on screen they will look unfinished.
7. **Agree the video storyline.** Review [VIDEO_BRIEF.md](./VIDEO_BRIEF.md) and confirm which of the
   suggested screens you are comfortable showing, given the honesty requirements in section 6.
