# WIC gap „Available" — POSLEDICA premeštanja (owner odluka 2026-10-16)

Zadatak: u listi WIC gapova kolona **„Available" ostaje savet za RTM** — niko se ne skriva i
ništa se ne dodeljuje automatski. Dodaje se **samo posledica premeštanja**: za svakog
predloženog agenta stanje njegovog **izvornog centra** posle premeštanja (`prisutno−1` vs
`MinRequired`), i to samo u delu vremena koji se preklapa sa gapom i u radnom vremenu
izvornog centra (`WicHoursResolver`).

Sve u ovom izveštaju je iz radnog stabla `C:\GSDDashboard_secure`, SDK **10.0.204**, prema
živoj bazi `GSDDashboard` na `localhost\SQLEXPRESS`. Nijedan test ni probni upis nije izvršen
nad živom bazom. Za svaki login korišćen je **isključivo DEV operatorski KID**; **RTM KID
(S60028) nije korišćen ni za jedan login**, niti je bilo koji KID upisan u `ManualChangeBy`.
Imena ne pojavljuju — samo `EmployeeId`.

---

## 1. Izmene (fajl:linija)

| Fajl | Linije | Izmena |
|---|---|---|
| `Backend/WicCoverageGapService.cs` | **73–82** | `WicGapAgent` DTO dobio 4 nova polja: `SourceLocation`, `SourcePresentAfter`, `SourceRequired`, `SourceBelowMinimum` (nullable/`false` = premeštanje nema merljivu posledicu). |
| `Backend/WicCoverageGapService.cs` | **294** | `WicLocations` se čita `OrderBy(l => l.Id)` — determinističan izbor izvornog centra; redosled gapova se ne menja (sortira se kasnije). |
| `Backend/WicCoverageGapService.cs` | **393–406** | Keš prisustva sada čuva **ceo `WicPresence`** (`PresenceDetailFor`), ne samo broj — da bi se oduzela tačno sopstvena kontribucija predloženog agenta, a ne „naslepo −1". |
| `Backend/WicCoverageGapService.cs` | **426–484** | Novo: `SourceCentreFor` (izvorni centar = centar gde agent stvarno drži WIC dužnost tog dana, kroz `AvailabilityResolver.IsWicDutyAt` — isti izvor kao `ComputeTaskLabel`/`WicPresenceResolver`) i `WithMoveConsequence` (presek intervala gapa i izvornog centra preko `WicHoursResolver.Intervals`; ako preseka nema → bez upozorenja; inače `posle = floor(prisutno − sopstvena kontribucija)`, `MinRequired` iz istog izvora kao gap). |
| `Backend/WicCoverageGapService.cs` | **495–509** | `BuildGap`: lista predloga (REGIONAL floaters) **nepromenjena** — isti filteri, isti `OrderBy(FullName)`; svaki element samo prolazi kroz `WithMoveConsequence`. |
| `Frontend/src/pages/Overview.tsx` | **353–364** | `WicGapAgent` interfejs dobio `sourceLocation`, `sourcePresentAfter`, `sourceRequired`, `sourceBelowMinimum`. |
| `Frontend/src/pages/Overview.tsx` | **439–463** | Prikaz „Available": kada `sourceBelowMinimum` → uz ime `… (WIC Helmstedt → 0/1)` u tonu reda (crveno za kritičan, narandžasto za nepopunjen) + tooltip; kada ne pada ispod minimuma → **tačno kao do sad**. |
| `Frontend/src/i18n/locales/en/common.json` | **180–181** | `availableLabel: "Available:"`, `availableWarningHint: "Moving this agent would leave {{centre}} below its minimum."` |
| `Frontend/src/i18n/locales/de/common.json` | **180–181** | `availableLabel: "Verfügbar:"`, `availableWarningHint: "Ein Verschieben dieses Agenten würde {{centre}} unter das Minimum bringen."` |
| `Backend.Tests/WicGapMoveConsequenceTests.cs` | **1–305** (novo) | 10 testova — vidi §3. |
| `Tools/pulse-preservation-manifest.json` | — | regenerisana **samo `Candidate` polovina** (270 unosa; `Live` netaknut, 202 unosa). |
| `Tools/WicGapMove20261016/**` | — | deploy skripte, receipts, dokazi, ovaj izveštaj. |

**Logika liste predloga se ne menja:** isti agenti, isti redosled, isti broj; `Assign` dugme
ostaje. Dokaz nad živim podacima: lista od **111** predloga (centar + datum + `index` +
`EmployeeId` + `TaskLabel`) je **identična pre i posle** deploya — 111/111, 0 razlika
(`Deploy/proposals-before-after.json`).

Pravila koja ispadaju iz implementacije:
* agent je **sam** u izvornom centru → upozorenje (`0/1`);
* izvorni centar posle premeštanja ostaje **≥ Min** → bez upozorenja;
* izvorni centar **zatvoren** (ili mu se radno vreme ne preklapa sa gapom) → bez upozorenja;
* **BO** dan i **„rostered, no task"** → nema centra → bez upozorenja;
* agent koji je **već na gap centru** → nije premeštanje → bez upozorenja;
* upozorenje nosi **isključivo** lista „Available" (`AvailableFloaters`); `AssignedAgents` ga nikad ne dobija.

---

## 2. Testovi

Novi `Backend.Tests/WicGapMoveConsequenceTests.cs` (10 testova): `0/1` upozorenje, `0/2`,
centar ostaje ≥ Min, centar zatvoren tog dana, centar otvoren van gap prozora, BO agent,
„rostered, no task", agent već na gap centru, lista predloga (isti agenti/redosled/broj,
upozorenje samo na jednom), `AssignedAgents` bez upozorenja.

| Suita | Rezultat |
|---|---|
| `dotnet test Backend.Tests` (Release, net8.0) | **483 passed, 0 failed, 2 skipped** (485 ukupno; 2 preskočena su postojeći live-DB acceptance testovi) |
| Frontend `npm run test:*` (8 skripti) | **119 passed, 0 failed** (47 + 21 + 8 + 5 + 7 + 7 + 8 + 16) |
| `npm run build` (`tsc -b` + vite) | prošao |

---

## 3. Deploy (po `documentation/LIVE_DEPLOY_RUNBOOK.md`, ručni promote)

| Korak | Dokaz |
|---|---|
| **Precondition** | živi DLL `7F344D0F7E8E97383F97CCAA7DEBF5DD4774DE7C4AED504BA44D4E80C2B104AB` — potvrđen OK pre ijednog upisa |
| **Snapshot** | `C:\ProgramData\PulseRollback\wicgapmove-deploy-20261016-083406` — 221 fajl, svaki kopirani fajl heš-verifikovan 1:1 prema živim originalima + `backend.json` |
| **Sentinel** | `C:\HealthCheck\DEPLOY_IN_PROGRESS` kreiran **pre** stopa, uklonjen **posle** svih 82/82 provera |
| **Stop** | vlasnik porta 5000 **PID 22524** `GSDDashboard.API.exe` = živi Release exe; zaustavljen |
| **Build** | radno stablo `C:\GSDDashboard_secure`, SDK 10.0.204; `PS1_19_FinalBuildVerify.ps1 -Mode Build` (uključ. preservation assert: 202 live / 270 candidate unosa) |
| **Copy** | build → živi Release + **oba** statička root-a; `appsettings*.json` sačuvani (hash nepromenjen `220CD54B945F2BEB104C286073E206519FC533AADD95F8B0B464D5353E01806C`) |
| **Novi DLL hash (kandidat = živi)** | **`6F33ABA3AA9432DC9755ECB2B8B98C55B983CE098B1EAC132F7D021E9E24B855`** |
| **Static** | `index-BqD9ThSW.js` / `index-DziOci1q.css` (CSS nepromenjen); u bundle-u prisutni i EN i DE stringovi |
| **82/82 — ručno pokrenut novi build** | **82/82 ×200 lokalno + 82/82 ×200 na tunelu**, login 200/DEV (`Deploy/routes-manual-82.json`) |
| **82/82 + postojeće rute (83/83)** | `Deploy/routes-manual-all.json` — 83/83 lokalno i na tunelu (82 + `2026-10-16 /api/wic/al-tracker-conflicts`) |
| **82/82 — proces koji drži watchdog** | **82/82 ×200 lokalno + 82/82 ×200 na tunelu** (`Deploy/routes-watchdog-82.json`) |
| **83/83 — proces koji drži watchdog** | `Deploy/routes-watchdog-all.json` |
| **Watchdog** | sentinel uklonjen → task `GSDDashboard-Backend` **Running**; port 5000 PID **18304**, parent **10612** = `watchdog_gsd_backend.ps1`; `/health` **200** |
| **KID** | samo DEV operatorski. RTM KID **nije** korišćen; ništa nije upisano u bazu |

Nove rute: **nema ih** — izmena je samo proširenje postojećeg `GET /api/wic/coverage-gaps`
DTO-a, pa je dokumentovani set ostao **82/82** (i 83/83 sa rutom od 2026-10-16).

---

## 4. Prihvat posle deploya — brojevi

Receipts: `Deploy/gapmove-acceptance-manual.json` (ručno pokrenut build) i
`Deploy/gapmove-acceptance-watchdog.json` (watchdog proces) — oba `pass: true`, provereno
**lokalno i na `https://d2jn94qg-5000.euw.devtunnels.ms`**. Baseline pre deploya:
`Deploy/gapmove-baseline-before.json`.

| Traženo | Rezultat |
|---|---|
| **Hannover 2026-11-02…06 → predlog 9124697 sa upozorenjem „Helmstedt 0/1"** | **POTVRĐENO za svih 5 dana.** `/api/wic/coverage-gaps?days=90` vraća za Hannover (present 0 / required 1, `UNCOVERED_ABSENCE`) floatera `9124697`, `taskLabel = "WIC Helmstedt"`, `sourceLocation = "Helmstedt"`, `sourcePresentAfter = 0`, `sourceRequired = 1`, `sourceBelowMinimum = true` → prikaz `WIC Helmstedt → 0/1`. Podloga (`Deploy/hannover-helmstedt-2026-11-02-06.txt`): Helmstedt je tih dana `COVERED` sa **tačno 1** agentom (9124697, `WIC_DUTY` Helmstedt 08:00–17:00; `WicAgentAssignments` 110 = MAIN Helmstedt, 200 = REGIONAL Hannover) → posle premeštanja Helmstedt je **0/1**. |
| **Ukupan broj predloga u 90 dana = 111** | **111** (nepromenjeno; window 2026-10-16…2027-01-13, 79 gap redova: 30 kritičnih, 49 nepopunjenih). Lista od 111 predloga je **identična** pre i posle deploya (`Deploy/proposals-before-after.json`). |
| **Broj predloga sa upozorenjem = 5** | **5** — tačno pet Hannover dana 02.–06.11. Nijedan drugi predlog ne pada ispod minimuma svog centra. |
| **Regensburg 20.10 i dalje prisutan + sukob prikazan** | **Delimično — razlika objašnjena, nije posledica ove izmene.** Slučaj je **i dalje prisutan**: `ShiftEntries` 57122 (9130657, 2026-10-20, `WIC_DUTY`, `AgentTask=Regensburg`, ručni RTM upis) i `WicShiftEntries` 68923 (isti agent/dan, `IsOnSite=1`, 08:00–16:00); `/api/wic/open?date=2026-10-20` → Regensburg `COVERED`, scheduled 1, effective 1, min 1. **Sukob (AL Tracker vs Shift Plan) se više ne prikazuje** jer red `Vacations` **Id 1377** (9130657, 2026-10-20) **više ne postoji**: u 08:29–08:32 (pre deploya) bio je prisutan i `/api/wic/al-tracker-conflicts` ga je vraćao (`alTrackerEntryId 1377`); u 08:44 ga nema (`al total = 0`). Dokazi (`Deploy/evidence-readonly-sql-20261016.txt`): `Vacations` 1376 (2026-10-19) i 1378 (2026-11-09) **jesu** tu, red 1377 **nije**; u celoj tabeli **nema** nijednog reda za 9130657 koji pokriva 2026-10-20. Ova izmena to nije mogla prouzrokovati: diff je isključivo čitanje (`WicCoverageGapService` DTO + `Overview.tsx` + i18n) i nijedan skript ovog zadatka ne poziva `DELETE /api/vacations/{id}` (jedini kod koji briše red iz `Vacations`); nad tabelom nema triggera. Zaključak: unos u AL Trackeru je uklonjen **izvan ovog zadatka** — upravo ona korekcija na koju upozorenje „AL Tracker vs Shift Plan" i poziva. Posledica: `/api/wic/cards?date=2026-10-20` sada čita `COVERED`, a dan zadržava WIC pokrivenost. |
| **`/health`** | **200** lokalno i na tunelu (oba procesa: ručno pokrenut i watchdog) |

---

## 5. Rollback putanje

| Opseg | Putanja / komanda |
|---|---|
| **Kod** (kompletan pre-deploy Release + oba statička root-a + `backend.json`, 221 heš-verifikovan fajl) | `C:\ProgramData\PulseRollback\wicgapmove-deploy-20261016-083406` |
| **Stari (pre-deploy) DLL** | `7F344D0F7E8E97383F97CCAA7DEBF5DD4774DE7C4AED504BA44D4E80C2B104AB` |
| **Novi (deployovani) DLL** | `6F33ABA3AA9432DC9755ECB2B8B98C55B983CE098B1EAC132F7D021E9E24B855` |
| **Postupak rollback-a** | sentinel → stop tačnog PID-a sa porta 5000 → vrati fajlove iz snapshota → ukloni sentinel → sačekaj watchdog (`Running`, `/health` 200) → 82/82 |
| **Baza** | nije potrebna: ovaj zadatak **nije** menjao podatke (nema rollback SQL-a) |

---

## 6. Commit

Commit napravljen **bez push-a** (vidi §7 fajlove). `.pulse-build-receipt.json` i
`Backend/wwwroot/` su gitignored i nisu deo commita.
