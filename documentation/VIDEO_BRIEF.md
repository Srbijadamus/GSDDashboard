# VIDEO_BRIEF — a suggested board storyline

**Prepared 2026-10-11.** This is a proposal for how to tell the story on camera, not a script to read
out. Every number below was verified against the live systems on **2026-10-11** and cites its source.
Every screen is named with its route and menu location so it can be found quickly.

**Two rules that make this video credible:**

1. **Say the hard things.** The security section includes what is still open. A board that discovers a
   gap after being told everything is fine loses trust in the whole report.
2. **Do not film the mixed-language pages.** Two pages still show German in the English view or English
   in the German view and will look unfinished on camera. They are named in the "do not show" list at
   the end.

---

## 1. Chapters and the message each one carries

| # | Chapter | Duration (suggested) | The one message |
|---|---|---|---|
| 1 | The problem | 60 s | Four questions nobody could answer reliably before: who is working where, is every centre covered, what is today's absence picture, and who actually turned up. |
| 2 | What we built | 60 s | One system that plans the work, and a second that records what really happened; they meet on the employee. |
| 3 | The plan | 120 s | The roster is generated, not typed, and the plan is complete: 122 of 122 people have a shift in the next fortnight. |
| 4 | Coverage | 120 s | A coverage gap is visible before it becomes a customer problem; the map, the cards and the report cannot disagree. |
| 5 | Attendance | 120 s | Who really turned up, compared against who was expected — in both directions. |
| 6 | Trust | 90 s | Every human change is attributable and protected; nothing automation does can silently undo a person's decision. |
| 7 | Security | 120 s | What is closed, what was fixed, what is still open — and what we deliberately do not claim. |
| 8 | What is next | 60 s | Nine prioritised actions with owners; the first two are documentation and configuration corrections, not features. |

Total: roughly 12–13 minutes. Chapter 7 is the one to rehearse most.

---

## 2. What to show on screen — the shot list

Fifteen items, each with the exact place to find it and the reason to show it.

| # | Show this | Where | Why it earns screen time |
|---|---|---|---|
| 1 | The command centre with its five figures and the coverage map | **Overview** — the landing page at `/` | One screen that answers "what does today look like". The map's 43 markers are the clearest visual proof of scope. |
| 2 | Point at one at-risk marker and open the substitute panel | **Overview** → click a marker | Shows that a gap is not just flagged, it comes with ranked names. |
| 3 | The roster completeness figure | **Overview** — the missing-roster banner area, deliberately empty | "122 of 122" is the strongest single number in the whole story. An empty warning is the good outcome. |
| 4 | The shift-plan grid with its colour coding and the coverage-per-hour bar | **Shift Plan** — Planning menu, `/shifts` | Proves this is a real planning tool, not a dashboard over a spreadsheet. |
| 5 | The roster preview: rows, skipped weekends, skipped holidays, collisions | **Roster Generator** — Planning menu, `/roster`; press Preview and stop | The single most convincing feature on camera: it tells you exactly what it is about to do before it does it. |
| 6 | The batch list with a delete-and-restore | **Roster Generator** — scroll to the batches table | "Everything is reversible" made visible. |
| 7 | The WIC Shifts card grid, covered / partial / uncovered | **WIC Shifts** — Planning menu, `/wic-shifts` | The daily operational view; shows the three coverage states at a glance. |
| 8 | The WIC Attendance header — Expected, Checked in, Not yet in — and the per-centre split | **WIC Attendance** — WIC Operations menu, `/wic-attendance` | The heart of the story: expectation against reality, with the arithmetic that cannot produce three disagreeing numbers. |
| 9 | The same feed in the read-only remote view | **Remote Attendance** — Operations menu, `/remote-attendance` | Shows the deliberate separation of on-site and remote, and that the remote view has no buttons at all. |
| 10 | The WIC assignment directory: main, backup and regional tiers | **WIC Assignments** — WIC Operations menu, `/wic-coverage` | Shows the system knows *who* can cover, not just *that* someone is missing. |
| 11 | The central export: pick people, a date range, data types and a format | **HR Export** — Tools menu, `/export` | One click replaces the monthly report assembly. |
| 12 | The shift cell change with its validation dialog, and the manual marker afterwards | **Shift Plan** — edit a cell, trigger the validation, then hover the edited cell | Makes "manual changes are protected" concrete rather than a claim. |
| 13 | The kiosk screen itself: the fullscreen start, then the floating panel | **Kiosk client** on an agent machine, or a recording supplied by the owner | The physical reality of attendance capture. |
| 14 | The kiosk supervisor dashboard and its eight views | **Kiosk dashboard** — port 8000, `/dashboard` → the Live view, then Hours Report | The second product, and the source of the attendance truth. |
| 15 | The security observation figures and a decision breakdown | **Security observation data** — shown as an aggregate chart or table, not the raw rows | Turns "we observe before we enforce" into evidence. Show totals only — the raw log contains personal data. |

**If you have time for only five shots:** items **3**, **5**, **8**, **12** and **15**.

---

## 3. Key figures, each with its date and source

Use these exact numbers. Every one was read on **2026-10-11**.

| Figure | Value | Source |
|---|---|---|
| Active employees | **122** (128 records including leavers) | `GSDDashboard.Employees` |
| Active team leads | **6** | distinct team-lead values |
| WIC centres | **43** — 41 in Germany, 2 in the Netherlands | `WicLocations` |
| Shift-plan rows | **41,931** | `ShiftEntries` |
| Plan horizon | **2026-01-01 → 2027-02-26** | `ShiftEntries` min/max date |
| Roster completeness | **122 of 122** active employees have a shift within 14 days | computed, same date |
| WIC assignment rows | **23,945**, of which **9,137** on-site | `WicShiftEntries` |
| WIC tiers | **49** main · **60** backup · **39** regional | `WicAgentAssignments` |
| Opening-hours records | **417** | `WicOpeningHours` |
| Annual-leave bookings | **765** total — 764 in 2026 (2,117 net days) plus 1 in 2027 | `Vacations` |
| Sick-leave records | **350** | `SickLeaves` |
| Attendance events stored | **2,552** since **2026-05-28** | `status_log` |
| Supervisor manual actions | **519** — 331 check-ins, 188 log-outs | `manual_actions` |
| Registered computers | **71** (67 active, 4 revoked) | `sec_devices` |
| Computers seen by the kiosk | **63** | `sec_kiosk_registry` |
| Requests observed by the security layer | **282,745** | `sec_observation_log` |
| Observation decisions | **268,952** allowed · **13,740** "would have blocked" · **53** blocked | `sec_observation_log` |
| Client versions in use | **2.1.1** (legacy) and **3.0.0-ng** (current) | `status_log` |
| Administrative accounts | **9** — 1 developer, 2 RTM, 6 team leads | `AppUserRoles` |
| Application tables | **35** functional (52 physical, 17 of them historical copies) | live schema |
| Shift auto-logout | **11 hours** from the recorded shift start | kiosk client |
| Newest features | Rollout and improved Bulk RTM — **live, not yet used** (0 rows) | `WicRolloutDays`, `WicRolloutWorkers` |

**Two figures to narrate carefully.**

- **519 supervisor interventions against 2,552 events** means most attendance is still recorded by a
  supervisor on someone's behalf. That is the honest reason the kiosk rollout matters: it moves the
  recording to the agent.
- **13,740 "would have blocked"** rows are the security layer doing its job in observation mode. They
  are *not* blocked requests. Say that explicitly on camera or the number will be misread.

---

## 4. The security chapter, in the order to say it

1. **The model.** Two products, two independent sign-in worlds: the workforce application uses a
   role-based session; the kiosk uses a per-computer device credential, with the agent's identity taken
   from the operating system rather than from anything the client sends.
2. **What we found and fixed.** A review found that the kiosk server published every agent's employee
   number without requiring a credential, and that the workforce application accepted a bare employee
   number as a sign-in. Together those allowed anyone who could reach the kiosk server to sign in as an
   agent — bounded to agent level, never an administrator or a team lead. Both halves are closed, and
   agents had to do nothing.
3. **What is closed today.** The attendance feed is reachable only through an authenticated proxy that
   refuses anonymous callers before any request leaves the application. Device binding, credential
   scoping and the documentation page closure are live. Every request is recorded with the rule that
   decided it.
4. **What is still open.** Anonymous callers can still obtain a session credential and use it to read
   attendance history, the audit log and the exports. The shared legacy key is still used by roughly
   sixty machines and still signs those tokens. Device binding is enforcing while the recorded plan says
   it is only observing. And two administrative commands currently have no gate of their own.
5. **What we do not claim.** No penetration-test report. No alerting pipeline. No retention rule for the
   attendance and observation data, which includes sickness information. Historical attendance is
   recorded as *not* valid evidence of individual performance.
6. **The documentation defect an operator must not act on.** A supervisor enforcement switch that
   several documents describe does not exist in the code, and the part that would connect it to the
   application is not implemented. Say this plainly; an operator following the current instructions
   would believe supervision had been enforced when nothing had changed.

---

## 5. Do not show, and do not say

**Do not film these screens** (language rule not met — they will look unfinished):

- **The BO List page** — German-only, while its menu label is English.
- **The WIC Schedule page** — German empty-state text and column headers inside the English view.
- **The Shift Plan's bilingual warning line** — if a validation warning appears in the recording, re-take
  the shot or crop above it.
- **Any page in the German view that falls back to English** — about eleven pages are largely
  hardcoded English, so switch the recording to English and keep it there.

**Do not say or show:**

- No agent names, user names, machine names, network addresses, device identifiers or internal
  identifiers. Blur or crop any account badge, session identifier or device identifier.
- No individual sickness, incident or device story. Speak only about the general controls.
- No secret values, configuration contents or credentials.
- No individual performance claims based on attendance data — that is explicitly recorded as invalid
  use.
- Do not describe the supervisor sign-in as protecting the workforce application; today it does not.

---

## 6. Preparation checklist before recording

1. Confirm which build is being filmed and verify it on the **served** address, not a development
   server.
2. Populate the Rollout page with a clearly fictional example if you want to show it — it currently has
   no data, and an empty new feature is worse than no shot.
3. Prepare the observation-data visual as an **aggregate** chart or table, never the raw log.
4. Check the language switch on every screen you intend to film.
5. Have the two "still open" security sentences written down and in front of you. Improvising them is
   how a credible report becomes a comfortable one.
6. Confirm the figures on the day of filming if the recording is more than a day after 2026-10-11, and
   update this brief's numbers accordingly.
