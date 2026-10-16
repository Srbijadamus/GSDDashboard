# Shift Plan — ko odlazi UNUTAR prikaza ostaje u planu (2026-10-16)

Zadatak: u `ShiftService.GetShiftsAsync` (`ShiftService.cs:156`) zaposleni se isključuje **samo
ako je otišao PRE PRVOG dana prikaza** — `HasLeft(e, from, …)` umesto `HasLeft(e, to, …)`.
Ko odlazi unutar prikaza ostaje u planu; dani pre izlaska ostaju vidljivi, a dani od izlaska
pokazuju `RESIGNED` red kao i do sad. Ostale površine (`assign-candidates`,
`/api/employees?date=`) se **ne menjaju**.

Sve iz radnog stabla `C:\GSDDashboard_secure`, SDK **10.0.204**. Nijedan test ni probni upis
nije izvršen nad živom bazom (svi pozivi su `GET` / čitanje). Za svaki login korišćen je
**isključivo DEV operatorski KID `S69307`**; **RTM KID (`S60028`) nije korišćen**, niti je bilo
koji KID upisan u `ManualChangeBy`. Imena ne pojavljuju — samo `EmployeeId`.

---

## 1. Izmene (fajl:linija)

| Fajl | Linija | Izmena |
|---|---|---|
| `Backend/ShiftService.cs` | **151** | `RESIGNED` redovi se učitavaju sa `ShiftDate < from` (bilo `<= to`) — rečnik zna samo za izlaske **pre** prvog dana prikaza. |
| `Backend/ShiftService.cs` | **159** | `HasLeft(e, from, resignedFrom)` (bilo `e, to, …`) — zaposleni se isključuje samo ako je otišao pre prvog dana; izlazak tačno na `from` je **unutar** prikaza i ostaje. |
| `Backend/ShiftService.cs` | **144–149, 154–157** | Komentari prepisani da opisuju novo pravilo (izlazak unutar prikaza ostaje; `RESIGNED` redovi od dana izlaska se prikazuju kao i do sad). |

Ne menjaju se: `SubstituteEligibility.HasLeft` (deljena definicija), `WicAssignCandidatesService`,
`EmployeeService` (`/api/employees?date=`), `WicCoverageGapService`, `SubstitutionService`,
`BackupService`, `OverviewService`. `GetShiftsAsync` ima samo dva pozivaoca — `GET /api/shifts`
(`ShiftService.cs:758`) i Excel izvoz Shift Plan-a (`ShiftService.cs:620`), oba ista površina.

**Živi oblik podatka (read-only, pre deploya):** `9106138` je **aktivan** (`IsActive=1`, WIC) i
nosi `RESIGNED` `ShiftEntries` prvi put **2026-10-26** (kasnije i 10-29, 11-02, 11-03). Zato:
`2026-10-20…10-26` → izlazak je **unutar** prikaza → mora biti u planu; prozor koji **počinje
2026-10-27** → otišao je pre njega → ne sme biti u planu.

---

## 2. Testovi

| Fajl | Linije | Test |
|---|---|---|
| `Backend.Tests/AssignCandidateFilterTests.cs` | **221** | `ShiftPlan_LeaverInsideTheWindow_StaysWithTheDaysBeforeTheExit` — izlazak `Date+3` u prozoru `Date..Date+6`: zaposleni **vidljiv**, dani pre izlaska (`WORKING` `Date+1`, `Date+2`) prisutni, `RESIGNED` na dan izlaska prisutan. |
| `Backend.Tests/AssignCandidateFilterTests.cs` | **245** | `ShiftPlan_LeaverBeforeTheWindow_IsNotAPlanRow` — izlazak pre `from` → **nije vidljiv**. |
| `Backend.Tests/AssignCandidateFilterTests.cs` | **259** | `ShiftPlan_ResignedOnTheFirstShownDay_IsStillAPlanRow` — `RESIGNED` tačno na `from` nije „pre prikaza" → ostaje. |
| `Backend.Tests/AssignCandidateFilterTests.cs` | **273** | `ShiftPlan_ActiveEmployeeWithoutExit_IsAPlanRow` — aktivan bez izlaska → **vidljiv**. |
| `Backend.Tests/AssignCandidateFilterTests.cs` | **286, 298, 314** | Neaktivna `Employees` vrsta nikad nije red; `FUT1` ostaje dok prozor ne **počne** posle izlaska; `IsOnDemand` zadržava mesto. |

Stari testovi koji su pinovali suprotno ponašanje (`ShiftPlan_InactiveEmployee_AndLeaverInsideTheWindow_AreNotPlanRows`,
`ShiftPlan_FutureExitDate_LeavesTheAgentInThePlanUntilItPasses`) zamenjeni su gornjim.

| Suita | Rezultat |
|---|---|
| `dotnet test Backend.Tests` (Release, net8.0) | **497 passed, 0 failed, 2 skipped** (499 ukupno; 2 preskočena su postojeći live-DB acceptance testovi) |
| `PS1_19_FinalBuildVerify.ps1 -Mode Build` (`npm run build` + `dotnet build`) | prošao; bundle nepromenjen (`index-CIY1cV6-.js` / `index-DziOci1q.css`) |
| `Assert-PulsePreservation.ps1` | 202 live / 271 candidate unosa; regenerisana **samo `Candidate` polovina** manifesta (2 izmenjena fajla) |

---

## 3. Deploy (po `documentation/LIVE_DEPLOY_RUNBOOK.md`, ručni promote)

| Korak | Dokaz |
|---|---|
| **Precondition** | živi DLL `CA18785F28325EC68A1BD1349CECB0D935A3D9F52DE14B3E7301CD0A2ACE033B` — potvrđen OK pre ijednog upisa |
| **Snapshot** | `C:\ProgramData\PulseRollback\leaverwindow-deploy-20261016-092754` — **221 fajl**, svaki kopirani fajl heš-verifikovan 1:1 prema živim originalima + `backend.json` |
| **Sentinel** | `C:\HealthCheck\DEPLOY_IN_PROGRESS` kreiran **pre** stopa, uklonjen **posle** svih 82/82 provera |
| **Stop** | vlasnik porta 5000 **PID 35256** `GSDDashboard.API.exe` = živi Release exe; zaustavljen |
| **Copy** | build → živi Release + **oba** statička root-a; `appsettings*.json` sačuvani (hash nepromenjen `220CD54B945F2BEB104C286073E206519FC533AADD95F8B0B464D5353E01806C`) |
| **Novi DLL hash** | **`E2708DCFE3BFD166FCA4933B692BEE1964543717F942286BF3DE6B0087CC5420`** (na disku i u procesu) |
| **Static** | `index-CIY1cV6-.js` / `index-DziOci1q.css` — **nepromenjeni** (backend-only izmena) |
| **82/82 — ručno pokrenut novi build** | **82/82 ×200 lokalno + 82/82 ×200 na tunelu**, login 200/DEV (`Deploy/routes-manual-82.json`) |
| **82/82 — proces koji drži watchdog** | **82/82 ×200 lokalno + 82/82 ×200 na tunelu** (`Deploy/routes-watchdog-82.json`); + postojeće parametrizovane rute **87/87** (`Deploy/routes-watchdog-all.json`) |
| **Watchdog** | sentinel uklonjen → task `GSDDashboard-Backend` **Running**; port 5000 PID **10116**, parent **10612** = `watchdog_gsd_backend.ps1`; `/health` **200** |
| **KID** | samo DEV operatorski. RTM KID **nije** korišćen; ništa nije upisano u bazu |

Nove rute: **nema ih** — `/api/shifts` je već u dokumentovanom 82-setu.

---

## 4. Prihvat posle deploya — brojevi

Receipts: `Deploy/leaver-window-before.json` (staro, živo ponašanje),
`Deploy/leaver-window-manual.json` (ručno pokrenut build) i
`Deploy/leaver-window-watchdog.json` (watchdog proces) — oba poslednja `pass: true`,
provereno **lokalno i na `https://d2jn94qg-5000.euw.devtunnels.ms`**.

| Traženo | Pre deploya (staro) | Posle deploya |
|---|---|---|
| `/api/shifts?from=2026-10-20&to=2026-10-26` **sadrži `9106138`** | **NE** (824 reda, `contains=False`) | **DA** — 831 red; `9106138` prisutan sa `RESIGNED 2026-10-26` i danima pre izlaska |
| `/api/shifts?from=2026-10-27&to=2026-11-02` **ne sadrži `9106138`** | NE (tačno, ali iz pogrešnog razloga) | **NE** — 818 redova, `contains=False` |
| `/api/shifts?from=2026-10-27&to=2026-10-27` **ne sadrži `9106138`** | NE | **NE** — 120 redova, `contains=False` |
| `/api/shifts?from=2026-10-19&to=2026-10-26` sadrži `9106138` (kontrola) | NE | **DA** — 949 redova |
| `/health` | 200 | **200** lokalno i na tunelu (oba procesa: ručno pokrenut i watchdog) |

Razlika u broju redova nastaje tačno od zadržanih zaposlenih koji odlaze unutar prikaza
(10-20…26: +7 redova za `9106138`); prozor posle izlaska ostaje bez njega.

---

## 5. Rollback putanje

| Opseg | Putanja / komanda |
|---|---|
| **Kod** (kompletan pre-deploy Release + oba statička root-a + `backend.json`, 221 heš-verifikovan fajl) | `C:\ProgramData\PulseRollback\leaverwindow-deploy-20261016-092754` |
| **Stari (pre-deploy) DLL** | `CA18785F28325EC68A1BD1349CECB0D935A3D9F52DE14B3E7301CD0A2ACE033B` |
| **Novi (deployovani) DLL** | `E2708DCFE3BFD166FCA4933B692BEE1964543717F942286BF3DE6B0087CC5420` |
| **Postupak rollback-a** | sentinel → stop tačnog PID-a sa porta 5000 → vrati fajlove iz snapshota → ukloni sentinel → sačekaj watchdog (`Running`, `/health` 200) → 82/82 |
| **Baza** | nije potrebna: ovaj zadatak **nije** menjao podatke (nema rollback SQL-a) |

---

## 6. Commit

Commit napravljen **bez push-a**. `.pulse-build-receipt.json` i `Backend/wwwroot/` su
gitignored i nisu deo commita.
