# WorkForce Pulse & ShiftKiosk — Overview for the Board

**Prepared 2026-10-11.** This is a board paper, not a technical paper. It has three parts: a short
summary in Serbian for the owner, the story in plain English, and a clear statement of what still has to
be decided. Specific counts of people, centres, computers and data rows have deliberately been left out
of this document — the size of the system is described in words, and the exact figures with their
sources live in [DATA_MODEL.md](./DATA_MODEL.md) and in the companion briefs
[VIDEO_BRIEF.md](./VIDEO_BRIEF.md) and [VIDEO_NARRATION.md](./VIDEO_NARRATION.md).

---

## Sažetak na srpskom (za tebe)

**Dva sistema, jedan cilj:** da se uvek zna **ko treba da radi gde** i **ko je stvarno tu**.

- **WorkForce Pulse** je aplikacija za planiranje i pregled. Tu žive plan smena, raspored agenata po WIC
  centrima, bolovanja, godišnji odmori, spisak ljudi, obuke i izveštaji. Plan se ne kuca ćelija po
  ćelija — generiše se jednom akcijom, po radnom vremenu svakog centra, uz pregled pre upisa i mogućnost
  vraćanja na prethodnu verziju.
- **ShiftKiosk** je evidencija prisustva. Na laptopu agenta radi mali program koji javlja početak smene,
  pauzu, hitan slučaj i kraj. Prijava nije potrebna — identitet dolazi iz Windows sesije, a pristup nosi
  **token vezan za taj računar**. Radi i kad mreža padne — događaji čekaju u redu i šalju se kasnije, sami.
  Dok smena traje, Windows tasteri su blokirani, a zaboravljena smena se sama zatvara posle **11 sati**.
- **Spojna tačka je samo jedna: identifikator zaposlenog.** Ne ime zgrade, ne adresa — čovek. Zato se
  plan i stvarno stanje mogu pouzdano porediti.

**Dva nalaza koja moraš znati pre nego što bilo šta kažeš o bezbednosti:**

1. **Prekidač za prijavu supervizora („SupervisorMode") NIJE IMPLEMENTIRAN U KODU na dan 2026-10-11.**
   Dokumentacija ga opisuje kao operativni prekidač, ali ga **nijedan red koda ne čita**, u
   `appsettings*.json` ga nema, a klasa koja bi ga povezala sa aplikacijom ne postoji. Ovo je **nalaz
   broj 1**. Ako neko prati ta uputstva, misliće da je nadzor uključen — a ništa se neće promeniti.
2. **Device binding je na živom serveru u režimu `enforce` i tako ostaje** (odluka vlasnika). Profili
   `dan1.ini`, `dan2.ini`, `dan3.ini` ga još vode kao `shadow` — dakle profili su ti koji zaostaju za
   stvarnim stanjem, i njih treba ispraviti, a ne server.

**Šta treba da uradiš ti:** vidi poslednju sekciju ovog dokumenta („What the owner needs to do"). Ukratko:
odluči sudbinu dokumenata o supervizoru, potvrdi device-binding odluku i uskladi profile, odobri termin
za naloge supervizora, donesi odluku o rokovima čuvanja podataka, i pogledaj scenario za video.

---

## ⚠ The single most important thing in this document

> **A supervisor sign-in switch that several documents describe is NOT IMPLEMENTED IN CODE as of
> 2026-10-11.**
>
> Nothing in the running workforce application reads it: there is no such setting in any source file or
> in any `appsettings*.json`, and the client class that would connect the workforce application to the
> kiosk's supervisor check does not exist. An operator who followed the current instructions would
> believe supervision had been switched on when nothing had changed.
>
> In plain terms: **someone could do exactly what the document says, tick the box, and be no safer than
> before.** This is finding number 1 in [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md). Read it
> before acting on any older document.

---

## 1. The problem

Before this system, the answer to "who is working where today, and is every customer centre covered?"
lived in spreadsheets, in e-mail and in people's heads. Four consequences followed.

1. **No shared picture.** A planner, a team lead and a coordinator each held a different version of the
   same week.
2. **Coverage was discovered too late.** A customer centre could be left short because nobody compared
   the two lists in time.
3. **Absence handling was manual.** A sickness note had to be copied into the plan by hand, and the copy
   could be forgotten or applied twice.
4. **Attendance was invisible.** Nobody could say with confidence who had actually arrived, or when,
   except by asking around.

The system exists to replace all four: one place where the plan, the absences and the actual attendance
meet — built so that a human decision cannot be silently undone by automation.

---

## 2. The solution

Two products, deliberately separate.

- **WorkForce Pulse plans the work.** A planner generates a person's roster in one action, using each
  centre's own opening hours; the system skips weekends and public holidays, shows a preview before it
  writes, and keeps a snapshot so the run can be undone. Coverage is classified continuously as covered,
  partly covered, uncovered or closed. Sickness and annual leave are entered once and applied to the plan
  automatically. A back-office list, a bulk one-day entry tool, a training scheduler and a central export
  complete the picture. An in-app assistant answers questions from this system's own data — **no external
  artificial-intelligence service is called**.
- **ShiftKiosk records what actually happened.** A small program on the agent's laptop shows a fullscreen
  choice at the start of the day and then a compact floating panel. The agent never signs in: identity is
  taken from the Windows session and the program carries a **token that belongs to that individual
  computer**, so no shared credential sits on the desks. It records start, breaks, emergencies and the end
  of the shift, and it queues events when the network is down so nothing is lost — the queue sends them
  again by itself. While a session is running the Windows keys are blocked, so the screen cannot be left
  by accident. A session closes itself automatically after **11 hours**, so a forgotten sign-off still
  produces an honest record.

The two meet in exactly one place: **the employee identifier**. Pulse decides who *should* be somewhere;
the kiosk records who actually checked in; the attendance page joins the two and shows the difference in
both directions. The kiosk's own free-text location is displayed but never used in that comparison —
which matters, because the same building gets written down in several different ways, and a comparison
built on spelling would be wrong.

---

## 3. The value

The value is in three places, and none of them is a new feature.

1. **The plan is complete and current.** The roster generator, plus a warning when somebody has no
   roster, is what keeps the plan whole. Completeness is measured, not assumed.
2. **A coverage gap is visible before it becomes a customer problem.** A centre at risk is flagged
   together with ranked substitutes and the reason for the ranking — not just a colour.
3. **Every manual change is attributable and protected.** A human edit is stamped with who and when, and
   is protected at three levels: automatic writers check and skip it, a save-time guard refuses to
   overwrite it, and the marker fields act as concurrency tokens so a concurrent writer fails loudly
   instead of silently clobbering. That is what makes the data defensible in a review.

The system was built incrementally inside the team over roughly five months, using the SQL Server
instance and the team's own laptops that were already available. There was no procurement, no external
platform and no licence beyond the Microsoft stack already in place.

---

## 4. How a working day looks

This is the part to picture. It is one real day, told in order.

**Early morning.** The planner builds the plan for the coming period in one action. The generator reads
each centre's own opening hours and its own location, so the plan respects weekends and holidays per
centre, and the planner sees a preview before anything is written.

**Late morning.** The planner edits a shift by hand. The system refuses a shift that would leave a person
less than eleven hours of rest, or that would break the daily, weekly or consecutive-day limits — and it
says which rule was broken. Some rules are hard blocks; a few, such as work on a Sunday or a public
holiday, are warnings the planner may accept deliberately.

**Afternoon.** The planner watches coverage rather than typing. Centres are classified continuously; a
centre that is short turns amber or red early, and the screen offers ranked people who could fill it. The
customer never sees the gap.

**During the shift.** At the centre, an agent presses one of four buttons on the counter laptop: start,
break, emergency, end. If the network is down, the event waits in a queue and is sent later. If the
agent forgets to sign off, the session closes itself after eleven hours.

**Next day.** The team lead opens the attendance page: who was expected on the left, who checked in on
the right, matched by employee. Where the two disagree, that is exactly what the page is for.

**Holidays in that day.** A holiday is a property of the **centre's federal state**, not of the date
alone. On 6 January, for example, a centre in Augsburg (Bavaria) is closed, while a centre in Essen
(North Rhine-Westphalia) is an ordinary working day. The plan and the coverage view apply the rule for
the centre's own state.

---

## 5. Security — stated plainly

**What is protected.** Sign-in to the workforce application is role-based, with four roles and a
server-side allow-list that scopes an agent to their own data. The most sensitive read — the attendance
feed — is reachable only through an authenticated server-side proxy: anonymous callers are refused before
any request leaves the application, and only three roles may pass. The API documentation page returns
"not found" to anyone who is not a developer. Administrative commands are developer-only. Agents'
identity at the kiosk is derived from the operating-system session by a privileged broker, not from
anything the client sends.

**What was found and fixed.** In early October a review found a real exposure: the kiosk server
published every agent's employee number on an endpoint that required no credential, and the workforce
application accepted a bare employee number as a sign-in. Together those two facts allowed anyone who
could reach the kiosk server to sign in **as an agent** — bounded to agent level, never an administrator
or a team lead. Both halves were closed: the workforce application now reaches kiosk data only through
its authenticated proxy, and the kiosk's own credential no longer opens the paths it was not meant to
open — the session endpoints answer to the credential of the computer that was enrolled, and the
attendance-write endpoint to a session. The change required no action from agents.

**What is still open — and the board should hear this without softening.** The kiosk server still issues
its session token to anyone who can reach it, and that token can still be used to **read** attendance
history, the audit log and the exports; closing that needs supervisor accounts first, or supervisors
would be locked out. That server-side session credential is not tied to a person yet: each computer
carries its own device credential under the device-binding control described below, and tying the
session itself to a supervisor account is what the missing supervisor accounts are needed for. Two
administrative command endpoints have no gate of their own while the general authorisation switch is
off. No retention or deletion rule has been agreed for the attendance and observation data, which
includes sickness information.

**One configuration fact the board should know.** On the live kiosk server, **device binding is
enforcing**: the control that ties a credential to one computer and one user is blocking in production.
The tracked profile files (`dan1.ini`, `dan2.ini`, `dan3.ini`) still record it as `shadow`
(observation-only). **The owner's decision is that the live server stays enforcing and nothing is changed
in code or configuration; the profiles are the side that must be corrected.** Until they are, the
documents are behind reality.

**What we deliberately do not claim.** There is no penetration-test report. There is no alerting
pipeline — detection data is written to a database and read by a person. Historical attendance is
recorded as **not** valid evidence of individual performance, and that must be honoured in any reporting
built from it.

---

## 6. The plan

| Priority | Next step | Why now |
|---|---|---|
| 1 | **Make the supervisor sign-in documents match reality** | Finding number 1: documents tell an operator to switch on a mode that no code reads, and the part that would connect it does not exist. Correct the documents (or build the missing connection) before anyone acts on them. |
| 2 | **Bring the tracked device-binding profiles into line with the live server** | The live server enforces while `dan1/2/3.ini` say `shadow`. The owner has decided the server stays as it is, so the profiles must be corrected. |
| 3 | Provision the supervisor accounts and prove the recovery path | It is the prerequisite for the next two security stages; without it supervisors would be locked out |
| 4 | **Confirm that the new kiosk runs in production the way it does in the code** | The device token belongs to the computer that was enrolled, and it is the only credential the new kiosk is meant to use |
| 5 | Complete the enforcement stages for anonymous identifiers and anonymous reads | Some kiosk controls are still observing while others already enforce |
| 6 | Close the two administrative endpoints that have no gate of their own | They rely on the general authorisation switch, which is off |
| 7 | Agree a data-retention rule for attendance and observation data | The observation log grows with every request; sickness data is a special category |
| 8 | Close the remaining correctness gaps | The legal shift rules are only partly server-enforced, and two known rules have arithmetic limitations |
| 9 | Bring the remaining documentation into agreement with the code | Several older files state figures and behaviours that are no longer true |

**A note on pace.** The system is in daily operational use and the plan is complete. The work that
remains is less about new features and more about finishing the security stages and making the
documentation match reality — which is the correct order for a system that people now depend on.

---

## 7. What the owner needs to do

1. **Decide the fate of the supervisor sign-in documents.** Finding number 1 needs an owner decision,
   not more analysis: either build the missing connection or rewrite the affected documents so that no
   operator is told to use a switch that does nothing. This documentation set now carries the warning
   "NOT IMPLEMENTED IN CODE as of 2026-10-11" wherever the switch is described; older documents outside
   this set still describe it as an operating control and some still carry personal names and machine
   names, so they should not be handed out.
2. **Confirm the live device-binding decision and have the profiles corrected.** Device binding is
   enforcing on the live server and stays that way; the tracked profiles should stop recording it as
   observing-only.
3. **Approve the supervisor-account provisioning window.** It is the gate for the next two security
   stages and needs a short authorised maintenance window and a secure hand-off of one-time credentials.
4. **Get a data-protection decision on retention.** Two questions: how long attendance and observation
   data is kept, and written confirmation that historical attendance is not used as individual
   performance evidence.
5. **Approve the video storyline.** Review [VIDEO_BRIEF.md](./VIDEO_BRIEF.md) and
   [VIDEO_NARRATION.md](./VIDEO_NARRATION.md). Both are written for a non-technical audience, contain no
   screenshots, no real names and no machine identifiers, and neither one puts a count of people,
   centres or computers on the screen.
