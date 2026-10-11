# OPEN_ITEMS_AND_RISKS — what is unfinished, what is contradictory, what to do next

**Verification basis:** both repositories, both live databases and the existing documentation, read
**read-only** on **2026-10-11**. Every item is something that exists in the code, the data or a
document — nothing here is invented or rounded off.

**Related documents:** [SECURITY_MODEL.md](./SECURITY_MODEL.md) · [OPERATIONS.md](./OPERATIONS.md) ·
[CHANGE_HISTORY.md](./CHANGE_HISTORY.md) · [DATA_MODEL.md](./DATA_MODEL.md) ·
[PULSE_FEATURES.md](./PULSE_FEATURES.md)

**Severity key.** *High* = a live risk to data, access or the board's understanding of it.
*Medium* = a real defect with a bounded effect. *Low* = hygiene, clarity or consistency.

---

## 0. Finding number 1 (read this before anything else)

> ## ⚠ Supervisor sign-in is NOT IMPLEMENTED IN CODE as of 2026-10-11
>
> Several documents — in this set and in the older corpus — describe a supervisor sign-in switch
> (`Auth:SupervisorMode`) as an operating control with `off`, `shadow` and `enforce` states, and one
> describes a recovery procedure that depends on it. **None of that exists in the running code.**
> Re-verified 2026-10-11:
>
> - A repository-wide search of `C:\GSDDashboard_secure` finds `SupervisorMode` **only in `documentation\`
>   files** — there is no occurrence in any `.cs` file.
> - No `appsettings.json` or `appsettings.Development.json` (working tree, release output or deployment
>   copy) contains the key. The `Auth` section holds exactly one setting, `EnforceAuthorization`.
> - `Backend/Services/SupervisorAuthClient.cs` **does not exist**. No provider interface and no client
>   class for it exists anywhere in the repository.
>
> An operator following the old instructions would believe supervision had been switched on when nothing
> had changed. **Do not treat any document that instructs you to set this switch as a runbook step.**
> This is finding number 1 of this document and it is item **S14** below.

---

## 1. Security items still open

| # | Item | Severity | Evidence |
|---|---|---|---|
| S14 | **A documented enforcement switch does not exist in the code — THE NUMBER ONE FINDING.** Several documents instruct an operator to set a supervisor mode to off / shadow / enforce. A repository-wide search of the application finds **no reader for it**, no provider interface and no client class, and no `appsettings*.json` contains the key. Re-verified 2026-10-11: `SupervisorMode` appears only in `documentation\*.md`; `SupervisorAuthClient.cs` does not exist. The seam the documentation says Pulse uses to verify a supervisor credential is **not implemented**, and the endpoint that would serve it has no caller. An operator following those instructions would believe supervision had been enforced when nothing had changed. | **High** | Pulse code search; configuration key inventory (re-verified 2026-10-11) |
| S1 | **Anonymous read on the kiosk server.** Anyone who can reach it can obtain a session token and read history, the audit log and the exports. Closed only at the `dan3` profile, which is not applied. | **High** | Recorded live state and the `session_auth` flag in shadow |
| S2 | **Anonymous identifiers.** The control that strips employee ids, KIDs and e-mail addresses from anonymous responses is still in shadow. | **High** | `anon_identifiers` = shadow; six fields named in the stripper |
| S3 | **The shared legacy key.** Still used by **44 hosts** (`sec_kiosk_registry.credential_type='legacy_key'`, 2026-10-11 — a further 19 hosts use the new device credential, 63 in total), readable on those machines by design, and present in git history in three early commits. The kiosk client migration is **in progress**; the legacy count is the reason the key cannot yet be retired. Retirement criterion: no legacy inventory for 30 days; the clock has not started because legacy clients are still sending events (438 events tagged `2.1.1` in the last 90 days, `status_log`). | **High** | Live `sec_kiosk_registry`; live `status_log` |
| S4 | **Zero supervisor accounts exist.** The password + TOTP path and the break-glass recovery codes are prepared but unprovisioned, so supervisor authentication cannot be enforced. | **High** | Live `sec_accounts` = 0 rows |
| S5 | **Device tokens are not user authentication.** A local administrator can read a token or bypass the broker — stated in the code's own comment. | Medium | Broker module comment |
| S6 | **A credential-shape bypass exists on the kiosk server.** The identifier-stripping rule protects the attendance and agent endpoints, but an equivalent CSV export of the same agent data is reachable with a credential that anyone can mint. Read the finding as a design gap in the endpoint-by-endpoint shield, not just a missing field. | **High** | Endpoint inventory of the kiosk server |
| S7 | **The supervisor password-change endpoint has no session requirement and no rate limit or lockout.** It verifies the old password directly, unlike the sign-in path. | Medium | Kiosk security layer |
| S8 | **`/swagger` map.** Correctly gated to DEV with a 404 for everyone else — listed here only so the board knows the endpoint map is *not* publicly readable. | — (closed) | Backend startup |
| S9 | **Team-lead scoping exists in the kiosk dashboard but not across Pulse.** Do not read one as the other. | Medium | Recorded in the deployment note |
| S10 | **Tunnel transport.** TLS terminates at the tunnel; the legacy client disables certificate verification on every call; the tunnel permits anonymous connections at the port level because the kiosk has no corporate account. | Medium | Kiosk client and documentation |
| S11 | **Notification payloads are not escaped** before being placed in an e-mail body, so a free-text note can influence the rendering. Notifications were disabled at the last recorded state. | Medium | Kiosk server notification code |
| S12 | **The legacy key is committed in git history.** The repository must stay private. | Medium | Kiosk documentation |
| S13 | **A live-looking device-token file exists in the kiosk working tree.** It is gitignored and its presence is not proof of any deployment, but it should not be in a repository folder at all. | Low | Working tree |
| S15 | **The shared legacy key is also the session-token signing key.** The rule restricting the raw key to six endpoints does not restrict tokens *derived* from it on any other route, so a holder of the key can mint a token reaching history, audit, exports and imports. The key's scope restriction is defeated by the key itself. Separating the signing secret is recorded as a required future change and has not been done. | **High** | Kiosk token issuance and scope rules |
| S16 | **Two administrative command endpoints have no gate of their own.** They rely entirely on the general authorisation middleware, which is switched off, so both are reachable without signing in. Every other protected family carries an independent gate. | **High** | Pulse endpoint inventory; middleware configuration |
| S17 | **Device binding is enforcing while the tracked profiles say it is observing — and the owner has decided it stays enforcing.** The live kiosk configuration (`C:\ShiftKiosk\server\server_config.ini`) sets `device_binding = enforce`, while `C:\ShiftKiosk_secure\server\profiles\dan1.ini`, `dan2.ini` and `dan3.ini` all record `device_binding = shadow`. **Owner decision 2026-10-11: the live server stays `enforce`; nothing in code or configuration is changed. The profiles are the side that must be corrected.** | **High** | Live `server_config.ini` versus tracked profile files |
| S18 | **Zero supervisor accounts exist, so the stronger sign-in path has never been exercised end to end.** | Medium | Live `sec_accounts` |
| S19 | **A second-factor code can be replayed within its 30-second time step**; there is no replay prevention. | Low | Kiosk crypto module |
| S20 | **The rate-limit bucket that would protect bulk reads excludes the attendance, agent-list and team-list endpoints** — the largest and most sensitive surface stays unlimited even if rate limiting is enforced. Supervisor lockout is per account, not per address. | Medium | Kiosk security layer |
| S21 | **Cross-origin policy on the kiosk server allows any origin**, and its middleware reflects any origin back on its own refusal responses. | Medium | Kiosk server configuration |

---

## 2. Data and correctness items

| # | Item | Severity | Evidence |
|---|---|---|---|
| D1 | **`ShiftEntries` unique-index claim — CORRECTED against the live database.** The earlier review recorded that a unique index on (employee, date) "does not exist". **That conclusion was wrong.** Re-verified on 2026-10-11 against live `sys.indexes`: **`UQ_ShiftEntries_EmpDate` EXISTS** — active (`is_disabled=0`), unique, non-clustered, on `(EmployeeId, ShiftDate)`, with **0 duplicate `(EmployeeId, ShiftDate)` pairs across all 41,931 rows**. Two further unique indexes sit beside it: `UQ_ShiftEntries_EmpDateSheet` (a unique **constraint** on `(EmployeeId, ShiftDate, SourceSheet)`) and `UQ_ShiftEntries_EmpDateType` (a unique **index** on `(EmployeeId, ShiftDate, ShiftType)`). No document may say the (employee, date) index is absent or is to be introduced later; the places that did are corrected in this task. | **High** | Live `sys.indexes` + duplicate-count query, 2026-10-11 |
| D2 | **Three different duplicate tie-breaks in code.** Most services take the highest id; the Shift Plan grid takes the last row in query order; the roster plan builder takes the first row materialised. **Note (2026-10-11):** with `UQ_ShiftEntries_EmpDate` active (see **D1**), two `ShiftEntries` rows for the same employee and date cannot exist, so this divergence cannot be triggered on that table. The resolver code is retained as defensive/legacy behaviour and should be read as such. | Low | Service and page code; live `sys.indexes` |
| D3 | **At least four different “absence type” lists.** The canonical resolver's full-absence set, its blocking set, the roster's protected-overwrite set and the coverage-gap set all differ (one includes `OL`, another adds `CD` and drops `OFF_WEEKEND`/`RESIGNED`). Each is defensible in isolation; together they make "absent" mean different things on different screens. | Medium | Five named locations in code |
| D4 | **Two different opening-hours resolution paths.** The designated single resolver orders dated exceptions by last-edited time; the coverage evaluator re-implements the same query without that tie-break. Two exception rows for the same day can resolve differently on the two paths. | Medium | Resolver versus evaluator |
| D5 | **The VWIC assignment path looks up a WIC entry by employee and date only**, ignoring the centre, and can repoint a real centre row to "VWIC". The same class of bug was already found and fixed in the WIC assignment service. | Medium | VWIC service |
| D6 | **`DailyAttendance` has 0 rows** and is written only by an external job that is not in this repository, yet the Daily Attendance page presents it as history. | Medium | Live data |
| D7 | **`LeaveQuotas` is a reserved table** that no code reads or writes, while one document describes it as written by quota endpoints. | Low | Code versus documentation |
| D8 | **An unmanaged column dependency on the kiosk side.** One column is written by an endpoint but created by no migration in the repository; if it is absent on a fresh database the endpoint fails. | Medium | Kiosk schema inventory |
| D9 | **A destructive startup step on the kiosk side.** When a legacy column is detected in one table, the whole table is dropped and recreated empty. The migration script is described as additive. It has not been observed to fire, but it is in the code. | Medium | Kiosk `db_init` |
| D10 | **26 archive/backup tables live inside the production database** with no documented retention, making the table count ambiguous: **52 physical base tables = 26 application/framework tables + 26 archive/backup copies**. | Low | Live `INFORMATION_SCHEMA.TABLES`, 2026-10-11 |
| D11 | **No retention or deletion path anywhere:** attendance events, the observation log (**283,430 rows** on a re-read of `sec_observation_log` on 2026-10-11; **283,206** at the earlier reading the same day — the table grows with every request) and manual actions grow without bound. Named sickness data is a special category. | **High** | Live row counts; no cleanup code |
| D12 | **Historical attendance is not valid evidence of individual performance.** Recorded as a data-protection position; must be honoured in any reporting built from it. | **High** | Recorded in the kiosk documentation |
| D13 | **The kiosk client migration is in progress, not finished.** On 2026-10-11 the live registry lists 63 computers: **44 hosts on the legacy shared key** (`credential_type='legacy_key'`) and **19 hosts on the new device credential** (`credential_type='device_token'`). The legacy count is the reason the shared key cannot yet be retired. | Medium | Live `sec_kiosk_registry.credential_type`, 2026-10-11 |

---

## 3. Functional gaps and defects

| # | Item | Severity | Evidence |
|---|---|---|---|
| F1 | **The legal shift rules are only partly server-enforced.** Minimum rest, daily maximum, weekly maximum and consecutive days are blocked on the WIC creation route and by the UI on the grid, but the bulk-assign routes and the Roster Generator never call the validator. | **High** | Validator call sites |
| F2 | **The rest rule tests only the adjacent calendar day** despite loading a ±7-day window, and never compares two shifts on the same day. | Medium | Validator code |
| F3 | **The weekly-hours rule computes the wrong week for a Sunday shift** (it lands on the following Monday), so a Sunday shift is measured against the next week. | Medium | Validator arithmetic |
| F4 | **Break deduction is applied only to the weekly total**, while the model comment claims it also applies to the daily span. | Low | Validator code |
| F5 | **The EN/DE language rule is not fully met.** Two pages violate it seriously: one is entirely in German while its menu label is English, and one shows German empty-state text and column headers inside the English view. A third shows a bilingual warning in one line. Eleven further pages are largely hardcoded English, so the German view shows English there. | Medium | Frontend inventory |
| F6 | **No 404 route** in the SPA; unknown paths render the shell. | Low | Router |
| F7 | **Dead code and dead assets in the source tree:** a stale backup of a page file, an unimported mock-data file containing centre data that could be mistaken for live data, an unused shift-type parser, a no-op reorder endpoint that reports success without writing, and at least four Serbian-named backup copies of backend files that still appear in searches. | Low | Grep results |
| F8 | **The reorder endpoint returns success without persisting anything** while being exposed as a real API. | Low | Service code |
| F9 | **Training confirmation half-applies when a row already exists:** it sets only the task badge and does not change the shift type or the ownership fields, unlike the insert path. | Medium | Training service |
| F10 | **Substitute acceptance marks its own inserted row as auto-generated and stamps no manual marker**, so a human acceptance is afterwards protected from nothing — the opposite of the Bulk RTM rule for the same kind of decision. | Medium | Substitution module |
| F11 | **The closed-day cleanup deletes a hand-made duty row that carries no manual marker.** Only rows with the marker are protected. | Medium | Cleanup service |
| F12 | **Two DEV-only maintenance commands bypass the manual-edit guard entirely** because they use raw SQL rather than the context. | Medium | Admin endpoints |
| F13 | **Serbian comments and file names remain in the backend source tree** — harmless for users, contrary to the repository's own language rule, and they make searches noisy. | Low | Grep results |
| F14 | **A "RosterBatchRows" table is implied by one document but does not exist**; replaced rows live in a JSON column instead. | Low | Schema |
| F15 | **The Rollout feature is live but unused.** Both rollout tables have 0 rows, so the feature has never been exercised in production. | Low | Live data |
| F16 | **The `SubstitutionHistory` table has no creation script in the repository**; the service degrades gracefully when it is missing. | Low | Code inventory |

---

## 4. Documentation contradictions to resolve

These are places where **existing documentation disagrees with the code or with itself**. They are
listed rather than silently edited, because a reader who trusts the older document will be wrong.

| # | Contradiction | Which is right |
|---|---|---|
| C1 | **9-hour versus 11-hour auto-logout.** The threshold is **11 hours** (`AUTO_LOGOUT_SECONDS = 11 * 3600`). Four stale 9-hour copies remain in the kiosk working trees and are listed with their exact paths below; they are **not changed by this task**. | **11 hours** |
| C2 | **The kiosk described as QR/card terminals at WIC locations that are "not yet deployed"** versus laptop applications in daily use (44 hosts on the legacy key, 19 on the device credential). One Pulse document still contains the former. | The laptops are real |
| C3 | **"Nothing was implemented" proposals** versus three enforcement flags actually live. | The proposals are historical |
| C4 | **Team-lead role rows described as placeholders that can never authenticate** versus nine active accounts with real identifiers. | The later reading |
| C5 | **"The project has no audit log or authentication system"** in the data-model document versus a live login table, role middleware and a manual-action audit. | Stale and actively misleading |
| C6 | **Table counts**: one document documents 21 tables; the live database has 52 physical base tables (26 application/framework + 26 archive/backup). | Use "52 physical; 26 application/framework" |
| C7 | **Employee headcount**: documents say 57, ~123, ~130 and 129 in different places; the live figure is **122 active** (128 rows) in `GSDDashboard.Employees`. The kiosk separately has **123 active** (139 rows) in `ShiftKioskDB.agents` — a different system's list. | 122 active (Pulse); 123 active (kiosk, separate list) |
| C8 | **WIC location count**: 40, 43 and 44 all appear. Live: **43** (41 DE, 2 NL). | 43 |
| C9 | **Forecast horizon**: documented as both "1–30" and "1–62". | Both appear in code paths; verify per endpoint |
| C10 | **Substitute-accept and WIC-schedule paths** are documented with an older prefix in one file and the current one in another. | The current prefixes |
| C11 | **The `theme.py` file size** is stated as 1281 bytes in a deployment document; the file is 2902 bytes. | 2902 bytes |
| C12 | **PAUSE is missing from the documented status tables** even though both clients send it, the server accepts it, the dashboard filters on it and it appears in the live data. | PAUSE exists |
| C13 | **Phase 1 and Phase 2 button counts** are documented as three; both screens have four. | Four |
| C14 | **The Phase 2 widget size** is documented as 520×80; the code uses 560×92. | The code |
| C15 | **A documentation file referenced from the kiosk source does not exist in the kiosk repository** (it exists only in the Pulse repository). The same applies to two other referenced files and a break-glass text file. | The references are broken |
| C16 | **`DailyAttendance` described as written by the attendance service** in one document and as having zero write operations in another. | Zero writes in this repository |
| C17 | **Maximum stack versions**: README says .NET 8 and React 19; the machine reports SDK 10 and the live binary was built with it. | The machine |
| C18 | **The kiosk server's own endpoint table is missing 19 existing routes** and its line-number column points at nothing current, though no phantom route was found. | The route list |
| C19 | **A database view's own header says it is "not yet applied"** while two code paths depend on it. | Unresolved — needs a live check |
| C20 | **Two tables are documented as "written by" endpoints that do not exist** in the repository. | The code |
| C21 | **The Excel shift import is documented as the writer of the shift plan** in the data-model document while another document declares it dead and forbids re-running it. | Dead; the Roster Generator is the mechanism |
| C22 | **⚠ NOT IMPLEMENTED IN CODE as of 2026-10-11: a supervisor enforcement mode is documented as an operating switch, an interface and a client class — none of which exist in the application code.** Two documents instruct an operator to set it, and one documents a recovery procedure that depends on it. Re-verified 2026-10-11: `SupervisorMode` occurs only in `documentation\*.md`, no `appsettings*.json` contains the key, and `SupervisorAuthClient.cs` does not exist. | The documentation is wrong; the setting has no reader |
| C23 | **Two canonical deployment documents disagree about whether the Pulse proxy cutover is live** — one says it is not deployed, the other records it as live on a later date. | The later record; the earlier state table is stale |
| C24 | **A configuration key for a removed control is still present in a test configuration**, which makes it look like a control exists. | The flag was removed and is silently ignored |
| C25 | **A configured cross-origin allow-list in the application's configuration file is never read**; the running policy is fully open. | The configuration file misleads; the code is authoritative |
| C26 | **Device binding: the live server and the tracked profiles disagree.** Owner decision 2026-10-11: **device binding is `enforce` on the live server and stays that way; nothing is changed in code or configuration.** The tracked profile files `C:\ShiftKiosk_secure\server\profiles\dan1.ini`, `dan2.ini` and `dan3.ini` still record `device_binding = shadow`, so the profiles are the side that is wrong. | The live configuration (`server_config.ini`: `device_binding = enforce`); the profiles must be corrected |
| C27 | **The escape procedure in the documentation points at a file that is deliberately not in the repository** (it exists only on the host). | Intentional for secrets, but the reference should say so |

### C1 evidence — the four stale 9-hour code copies, with exact paths

The auto-logout threshold is **11 hours**. The following four places still say **9 hours**. They are
**not changed by this task** (no code may be modified), but they must not be quoted, and they must be
corrected by whoever owns the kiosk repository:

| File | Line | Stale text |
|---|---|---|
| `C:\ShiftKiosk\deploy\main_kiosk.py` | 682 | `AUTO_LOGOUT_SECONDS = 9 * 3600` |
| `C:\ShiftKiosk_secure\deploy\main_kiosk.py` | 682 | `AUTO_LOGOUT_SECONDS = 9 * 3600` |
| `C:\ShiftKiosk\server\server.py` | 514 | comment: "can base the 9h AUTO_LOGOUT timer on the real shift start" |
| `C:\ShiftKiosk\server\server.py` | 1519 | comment: "so the 9h AUTO_LOGOUT timer can be based on it instead of process start" |

The live client code in use sets `AUTO_LOGOUT_SECONDS = 11 * 3600`:
`C:\ShiftKiosk\kiosk\main_kiosk.py:784` and `C:\ShiftKiosk\KIOSK_PACKAGE\Files\main_kiosk.py:784`, and the
same value in the per-machine packages under `C:\ShiftKiosk\PAKETI\*`. The `deploy\main_kiosk.py` copies,
by contrast, are the old ones — the two listed above — so the `deploy\` folder must not be read as the
live client. **None of these files is changed by this task** (no code may be modified); the correction
belongs to whoever owns the kiosk repository.

---

## 5. Recommended next steps, in order

These follow from the findings above. They are *recommendations*, not authorisations — nothing here was
executed.

**Before the next board demonstration**

1. Decide which number is quoted for employees (122 active) and tables (52 physical: 26
   application/framework + 26 archive/backup) and use
   it everywhere.
2. Do not show the two pages that violate the language rule during a German-language demo, or fix them
   first.
3. Prepare the two or three screens that best tell the story (see
   [VIDEO_BRIEF.md](./VIDEO_BRIEF.md)) and verify them on the served build, not on a development
   server.

**Short term — correctness**

4. Fix the misleading "unique index" claim (**D1**) by correcting the comments, not the schema.
5. Unify the absence-type lists (**D3**) or document explicitly which list each surface uses.
6. Fix the rest-rule window and the Sunday week arithmetic (**F2**, **F3**) or state the limitation in
   the UI.
7. Call the shift validator on the bulk-assign routes and in the roster generator (**F1**).
8. Correct the kiosk documentation's 9-hour figure in all five places (**C1**) and bring the endpoint
   table up to date (**C18**).

**Short term — data protection**

9. Agree a retention and deletion rule for attendance events, the observation log and manual actions
   (**D11**), with the responsible data-protection function.
10. Confirm in writing that historical attendance is not used as individual performance evidence
    (**D12**).
11. Remove the stray device-token file from the kiosk working tree (**S13**).

**Medium term — security**

12. Complete the prerequisite order recorded in the runbook: deploy the Pulse proxy build and service
    credential, provision the supervisor accounts and prove recovery, then apply the next kiosk
    profile, then the one after it (**S1**, **S2**, **S18**).
13. **Fix the supervisor-mode documentation before anyone acts on it** (**S14**, **C22**): either
    implement the Pulse-side integration, or remove the switch from the runbook and state plainly that
    supervisor sign-in is kiosk-dashboard-only. Do not leave an operator with a switch that does
    nothing.
14. **Reconcile the live flag state with the tracked profiles** (**S17**, **C26**): either promote
    device binding formally with the evidence gate, or turn it back to observing. Whichever is chosen,
    the live configuration and the documents must agree — today a control is blocking in production
    that the process says was never promoted.
15. Close the credential-shape bypass on the kiosk server (**S6**) before enforcing that profile, or it
    gives false assurance.
16. **Add an always-on gate to the two administrative command endpoints** (**S16**), so they do not
    depend on a switch that is off.
17. **Separate the session-token signing secret from the shared API key** (**S15**), then migrate the
    remaining legacy clients so the retirement clock can start (**S3**, **D13**).
18. Add a session requirement, rate limiting and lockout to the supervisor password-change endpoint
    (**S8**-adjacent) and add second-factor replay protection (**S19**).

**Medium term — hygiene**

16. Decide the fate of the 26 archive tables and record it (**D10**).
17. Remove the dead files and the no-op endpoint, or mark them clearly as dead (**F7**, **F8**).
18. Fix or make explicit the broken documentation cross-references (**C15**) and the two unmanaged
    schema dependencies (**D8**, **C19**).
19. Add an automated check that every language key exists in both locale files and that no page
    reintroduces a hardcoded string (**F5**).

---

## 6. What is explicitly NOT a risk

For balance, these are things the review found to be in good order and that a board should not be
alarmed about:

- The manual-edit protection chain is genuinely implemented at three levels (marker, save-time guard,
  concurrency token) and every automatic writer checks it.
- The deploy sentinel and the two-tier watchdog are real and were verified.
- The kiosk proxy's error mapping distinguishes timeout, outage and malformed data instead of
  returning an empty success.
- The attendance counting identity is stated once, unit-tested and cannot produce three numbers that
  disagree.
- Roster generation is reversible by construction.
- Four of the nine kiosk security flags are enforced, including the ones that close the credential
  chain that was actually exploited-class in the October review.
