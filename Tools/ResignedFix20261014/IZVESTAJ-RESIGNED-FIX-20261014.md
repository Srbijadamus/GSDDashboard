# RESIGNED zaposleni u predlozima WIC zamena — izveštaj (2026-10-14)

Zadatak: Overview → coverage gaps lista („Available: …") i „Gaps & recommendations"
(„Best substitute") predlažu zaposlene sa statusom RESIGNED. Naći sve izvore predloga
zamena/dostupnih agenata, isključiti RESIGNED/neaktivne (i one sa RESIGNED redom u
`ShiftEntries` za taj dan ili ranije), dodati testove, cela suita mora proći, i deploy
po `documentation/LIVE_DEPLOY_RUNBOOK.md`.

- Server dan: **2026-10-14** (Europe/Berlin), baza `localhost\SQLEXPRESS` / `GSDDashboard`.
- **RTM KID `S60028` nije upotrebljen ni za jednu prijavu.** Za sve API provere korišćen je
  DEV KID **S69307**; nijedan drugi KID nije korišćen.
- U izveštaju **nema imena — samo `EmployeeId`**.
- **Nijedan test/probni upis na živu bazu.** Sve provere su GET + `SELECT` (read-only), uz
  jedan `MIN(ShiftDate)` SELECT nad `ShiftEntries` radi klasifikacije.

---

## 0. Uzrok (potvrđen na živoj bazi i živom API-ju)

| `EmployeeId` | `ShiftEntries` RESIGNED od | `Employees.IsActive` | Gde se predlagao |
|---|---|---|---|
| 9130648 | 2026-11-02 | **1** | coverage-gaps „Available:" — Hannover, 2026-11-02..06 (5 dana) |
| 9124687 | 2026-10-01 | **1** | Find Substitute (`SSP`) — Essenbach + Hannover, 2026-11-03..06 |
| 9087657 | 2026-11-02 | **1** | Find Substitute (`SSP`) — Essenbach + Hannover, 2026-11-04..06 |

Dva odvojena propusta:

1. `WicCoverageGapService` je iz liste „available floaters" uklanjao samo `AbsenceTypes`
   (`AL/HALF_AL/SL/UL/OL/PH/LPH/OFF/CD`). **`RESIGNED` namerno nije u tom skupu** (čovek koji
   je otišao nije „absent"), pa je `RESIGNED` floater prošao filter i bio objavljen kao
   „Available:".
2. `SubstitutionService` (Find Substitute + „Best substitute") je `RESIGNED` prepoznavao samo
   preko reda **tog istog dana** (`_fullAbsenceTypes`), a `Employees.IsActive` je bio 1.
   `allEmployees` ga je zadržao, a putanja (D) „day-off call-in" ga je dodala jer za taj dan
   nije imao nijedan red (`sh == null` → „day off").

---

## 1. Svi izvori predloga zamena / dostupnih agenata (`fajl:linija`)

| # | Izvor (endpoint) | Šta vraća | Fajl:linija — pre | Fajl:linija — posle |
|---|---|---|---|---|
| 1 | `GET /api/wic/coverage-gaps` | `availableFloaters` → Overview baner **„Available: …"** | `Backend/WicCoverageGapService.cs:77` (DTO), `:388-396` (gradnja liste, filter samo `AbsenceTypes`), `:322-325` (join po imenu, **bez** `IsActive`), endpoint `:489` | guard `:296-299` (RESIGNED istorija), `:322-331` (`empById`), `:333-339` (`HasLeft`), `:393` (`.Where(a => !HasLeft(...))`), endpoint `:510` |
| 2 | `GET /api/wic/coverage-gaps` | `assignedAgents` — dijagnostika „ko je zadužen", **nije** predlog | `:378-381` | **nije menjano** (0 RESIGNED/neaktivnih i pre i posle) |
| 3 | `GET /api/wic/briefing` | `gaps[].bestSubstituteId/Name/Source` → **„Gaps & recommendations" / „Best substitute"** | `Backend/Services/BriefingService.cs:21-31` (DTO), `:207-219` (`GetSubstitutesAsync` na `:209`), endpoint `:363`; frontend `Frontend/src/pages/Overview.tsx:1269-1281` | rešeno kroz #4 (Briefing poziva isti servis) |
| 4 | `GET /api/wic/substitutes` | **Find Substitute** modal — `days[].candidates[]`, `bestPickId` | `Backend/Services/SubstitutionService.cs`: (A) `:313-349`, (B) `:355-390`, (B2) `:398-438`, (C) `:446-505`, (D) `:508-544`, `bestPickId` `:551-559`, endpoint `:683` | `resignedRows` `:132-135`, `IsUnavailable` `:303-311` (guard `:306`), donor guard `:489`, day-off guard `:522`, endpoint `:683` |
| 5 | `GET /api/wic/assign-candidates` | **Assign** picker (coverage-gap red + UNCOVERED kartica) | `Backend/WicAssignCandidatesService.cs:33-84` (petlja `:56`) | `resignedRows` `:58-61`, guard u petlji `:66`, `hasSensibleCandidate` `:89`, endpoint `:104` |
| 6 | `GET /api/wic/backup` | `days[].candidates[]` — backup predlozi | `Backend/BackupService.cs:178-184` (`IsAvailable`), endpoint `:293` | `resignedRows` `:104-107`, guard `:188`, endpoint `:302` |
| 7 | `GET /api/vwic/candidates` | „dostupni agenti" za VWIC slot / Add-Agent modal | `Backend/VwicService.cs:373-425` (`isAbsent` ne sadrži `RESIGNED`, a ni `OL`/`CD`) | guard `:379-393`, dnevna odsutnost `:425-427` (koristi `WicCoverageGapService.AbsenceTypes` + `OFF_WEEKEND`), endpoint `:970` |
| 8 | `GET /api/overview/wic-status` | `topSubstitute` (polje na API-ju; UI ga danas ne renderuje) | `Backend/OverviewService.cs:105-117` | `resignedRows` `:57-61`, `topSubstitute` `:111-122`, endpoint `:239` |
| 9 | `GET /api/wic/briefing/export` (Excel) | „Best Substitute" kolone | `BriefingService.cs:308-327` | rešeno kroz #3 → #4 |

Transitivni korisnici istog servisa kao #4 (popravljeni bez izmene):
`Backend/Services/WhatIfService.cs:91` i `Backend/Services/ALPlanningService.cs:118-126`.

**Zajedničko pravilo:** novi `Backend/Services/SubstituteEligibility.cs`
(`:25` klasa, `:35` `EarliestResignedByEmployee`, `:44` `ResignedOnOrBefore`, `:55` `HasLeft`).
`ResignedOnOrBefore(id, date, mapa)` je `true` kada postoji `RESIGNED` red sa datumom
**≤ traženi dan** — istorijski signal, ne samo red tog dana. `HasLeft` dodaje i `IsActive=0`.

Frontend (usaglašavanje dugmeta „Assign" sa backend listom):
`Frontend/src/pages/Overview.tsx:313` — `GAP_ABSENCE_SET` sada sadrži i `RESIGNED`, pa red
čiji je jedini zaduženi agent otišao više ne nudi „Assign" (nudi „Fix assignment"),
`:315` `hasSensibleCandidate`, `:363-366` render liste „Available:".

---

## 2. Brojevi pre / posle (živi API, `days=28`)

Mereno `scripts`-om `.tmp-resigned-deploy\capture-resigned.ps1` (GET + read-only SELECT),
na `http://127.0.0.1:5000`; tunel meren odvojeno i daje iste brojeve.

| Provera | Pre (DLL `231A816C…`) | Posle (DLL `92CB8A5F…`) |
|---|---|---|
| **`/api/wic/coverage-gaps?days=28` — `availableFloaters` sa RESIGNED/neaktivnim** | **5** (sve `9130648`, Hannover, 2026-11-02..06) | **0** |
| `assignedAgents` sa RESIGNED/neaktivnim | 0 | 0 |
| `briefing.gaps[].bestSubstitute` sa RESIGNED/neaktivnim (danas 1 gap) | 0 | 0 |
| `/api/wic/substitutes` kandidati sa RESIGNED/neaktivnim (20 location/dan grupa gapova) | **14** (`9124687` ×7, `9087657` ×7; `SSP`; Essenbach + Hannover; 2026-11-03..06) | **0** |
| `/api/wic/assign-candidates` kandidati sa RESIGNED/neaktivnim (20 grupa) | nije posebno meren pre (isti razred propusta; stara logika = svi aktivni zaposleni) | **0** |
| `/api/wic/backup` kandidati sa RESIGNED/neaktivnim (20 grupa) | 0 (kandidat mora imati red tog dana) | **0** |
| `/api/vwic/candidates` sa RESIGNED/neaktivnim | **2** bez datuma (`9087657`, `9124687`) / **1** sa datumom (`9124687`) | **0** |
| `/api/vwic/candidates?date=` — dostupni sa `OL`/`CD` tog dana | **2** (1× `OL`, 1× `CD`, od 26 kandidata) | **0** (23 kandidata, svi `WORKING`) |
| Ukupno gapova (`critical`/`warning`) | 20 (16/4) | **20 (16/4)** — nepromenjeno |

Ciljna tačka iz zadatka: **`GET /api/wic/coverage-gaps?days=28` → 0 predloga sa
RESIGNED/neaktivnim zaposlenim; pre je bilo 5.**

Ciljani primer pre/posle (`Hannover`, `2026-11-02`):

- pre: `availableFloaters = [9130648 (RESIGNED)]`
- posle: `availableFloaters = [9124697 (WIC_DUTY / WIC Helmstedt)]` — samo stvarno dostupan
  regionalni floater.

---

## 3. Testovi (tačka 3)

Novi fajl: **`Backend.Tests/ResignedSuggestionTests.cs`** (17 testova):

- `SubstituteEligibility`: najraniji RESIGNED datum; `<=` na sam dan; kasnija resignacija ne
  blokira; `IsActive=0` blokira bez ijednog RESIGNED reda; nepoznat id/nema id.
- coverage-gaps `availableFloaters`: RESIGNED **na dan gapa**, RESIGNED **pre** gapa (bez reda
  tog dana — oblik iz živog slučaja), `IsActive=0`, i `Theory` za `AL/SL/UL/OL/PH` tog dana;
  kontrola: aktivan regionalni agent se **i dalje** nudi.
- `assign-candidates`: RESIGNED pre / RESIGNED na dan / `IsActive=0` nisu kandidati uopšte;
  kontrola: `AL` tog dana **ostaje** u listi ali `Available=false` (postojeće ponašanje).
- `SubstitutionService` (Find Substitute / Best substitute): `SSP` sa RESIGNED pre dana i
  `SSP` sa RESIGNED na dan nisu kandidati; zdrav `SSP` jeste i postaje `bestPickId`.
- `VwicService.GetCandidatesAsync`: leaver nije „dostupan agent" ni sa datumom ni bez njega;
  agent sa `OL` tog dana takođe nije (stara inline lista je izostavljala `OL` i `CD`).
- `BackupService.GetBackupAsync`: leaver sa važećim `WORKING` redom tog dana nije backup
  kandidat; zdrav backup jeste.

**Cela backend suita: 349 prošlo, 0 palo, 2 preskočena (`dotnet test`, Release, net8.0).**
(Pre ove izmene 332 prošlo + 2 preskočena; +17 novih = 351 ukupno.)

**Frontend suita (svih 8 `npm run test:*` skripti): 119 prošlo, 0 palo**
(attendance 47, bulk-rtm 21, substitute-accept 8, berlin-time 5, roster-errors 7,
wic-open-hours 7, wic-coverage-state 8, wic-horizon 16).

---

## 4. Build

| Stavka | Vrednost |
|---|---|
| SDK | `dotnet 10.0.204` (isti kao live), `node v24.15.0` |
| Build | `PS1_19_FinalBuildVerify.ps1 -Mode Build` — frontend `tsc -b && vite build`, `dotnet build -c Release` (0 grešaka, 0 upozorenja) |
| Preservacija | `Tools/MultiCentre20261014/Update-PreservationCandidate.ps1` regenerisao **samo Candidate** polovinu `Tools/pulse-preservation-manifest.json`: **257** unosa (bilo 255; +2 nova fajla), 0 uklonjenih |
| Bundle | `index-B54Q90nr.js` (SHA256 `0B96E830F40282BAAA0BC467468A7EB55B44FB1DA6031759C1E203F1A54DAB4A`), `index-Ded7oazT.css` (nepromenjen) |

---

## 5. Deploy (po `documentation/LIVE_DEPLOY_RUNBOOK.md`)

Precondition pre ijednog upisa: live DLL
`231A816CD10344553451690103D9428D90949AD6CD21D148AFABB6690FCDFBE7` — **potvrđen**;
port 5000 = `GSDDashboard.API.exe` PID 11668, roditelj watchdog `powershell.exe` PID 10612
(`C:\ProgramData\PulseBackend\watchdog_gsd_backend.ps1`), task `GSDDashboard-Backend` = Running,
sentinel nije postojao.

Deploy je izveden u **tri koraka** (1: četiri izvora iz zadatka; 2: VWIC + backup listovi;
3: VWIC dnevna odsutnost `OL`/`CD`), svaki po istoj proceduri: **sentinel → snapshot → stop
proverenog PID-a → copy → hash → 82/82 lokalno + tunel → uklanjanje sentinela → watchdog**.

| Korak | Snapshot (rollback) | Novi live DLL | Rute (ručno pokrenut novi DLL) | Rute (watchdog) |
|---|---|---|---|---|
| 1 — 4 izvora | `C:\ProgramData\PulseRollback\resigned-fix-deploy-20261014-175821` (221 fajl, 1:1 verifikovan) | `2192BC6E2D083BDF2527F41CA450A467F268B25A2F200E723111272BC4EC8487` | **82/82 lokalno + 82/82 tunel** | **82/82 lokalno + 82/82 tunel** |
| 2 — VWIC + backup listovi | `C:\ProgramData\PulseRollback\resigned-fix-deploy-20261014-180452` (221 fajl, 1:1 verifikovan) | `AE84B1D5FBA3EB06CF9B60EA86F85BEF76471893BF05F73172046E8EB2074B31` | **82/82 lokalno + 82/82 tunel** | **82/82 lokalno + 82/82 tunel** |
| 3 — VWIC `OL`/`CD` dan | `C:\ProgramData\PulseRollback\resigned-fix-deploy-20261014-181005` (221 fajl, 1:1 verifikovan) | `92CB8A5F38D8ACFDD78B4927FFAAAD3979E7FBFCB941224B3847E177A2E537C4` | **82/82 lokalno + 82/82 tunel** | **82/82 lokalno + 82/82 tunel** |

**Konačno stanje:**

| Stavka | Vrednost |
|---|---|
| **Novi live DLL SHA256** | **`92CB8A5F38D8ACFDD78B4927FFAAAD3979E7FBFCB941224B3847E177A2E537C4`** |
| Novi live EXE SHA256 | `A2FCFBD6B410825267DF7F2BB912250DBFE43C3FB4D0BC2A7ACB676285BF41FD` |
| Bundle | `index-B54Q90nr.js` SHA256 `0B96E830F40282BAAA0BC467468A7EB55B44FB1DA6031759C1E203F1A54DAB4A` |
| Snapshot (rollback poslednjeg koraka) | `C:\ProgramData\PulseRollback\resigned-fix-deploy-20261014-181005` |
| Snapshot (rollback na stanje PRE zadatka) | `C:\ProgramData\PulseRollback\resigned-fix-deploy-20261014-175821` |
| Sentinel | uklonjen (`C:\HealthCheck\DEPLOY_IN_PROGRESS` ne postoji) |
| Watchdog | task `GSDDashboard-Backend` = **Running**; port 5000 = `GSDDashboard.API.exe` PID 28876, roditelj watchdog PID 10612 |
| Prijava | **DEV `S69307`** (RTM `S60028` nije korišćen) |

`appsettings*.json` su tokom copy-ja sačuvani (live connection string nije zamenjen build
artefaktom); `Invoke-PulsePromotion.ps1 -Mode Promote` nije korišćen (poznati pinned-hash
problem), već dokumentovana ručna procedura.

### Rollback

Binarni rollback je čist file-copy iz snapshota (ne dira bazu):

- poslednji korak: snapshot `…-181005` sadrži `release\`, `release-wwwroot\`, `wwwroot\` i
  `backend.json`; vraća DLL `AE84B1D5…`.
- korak 2: snapshot `…-180452` vraća DLL `2192BC6E…`.
- na stanje pre zadatka: snapshot `…-175821` vraća DLL `231A816C…`.

Postupak: sentinel → stop tačnog PID-a sa port 5000 → vrati fajlove iz snapshota → ukloni
sentinel → sačekaj watchdog (`Running`, `/health` 200) → 82/82. `rollback.sql` nije potreban
(nema DB izmena).

---

## 6. Provere posle deploya

| Provera | Lokalno | Tunel |
|---|---|---|
| `/health` | 200 | 200 |
| `/api/wic/coverage-gaps?days=28` — predlozi sa RESIGNED/neaktivnim | **0** | **0** |
| gapovi `critical`/`warning` | 16 / 4 | 16 / 4 |
| `/api/wic/substitutes` kandidati sa RESIGNED/neaktivnim | 0 | 0 |
| `/api/vwic/candidates` sa RESIGNED/neaktivnim | 0 | — |
| 82 rute (svih 82 GET, bez ijednog ne-200) | **82/82** | **82/82** |

Dokazi: `.tmp-resigned-deploy\` (`precondition.json`, `build.log`, `build2.log`, `build3.log`,
`snapshot-and-stop[-2|-3].log`, `copy-receipt-A/B/C.json`,
`routes-manual[-B|-C].json`, `routes-watchdog[-B|-C].json`, `verify-routes-*.log`,
`baseline-before.json`, `final-watchdog[-tunnel].json`, `final-extra-pools.json`,
`post-deploy-state-final.json`).

---

## 7. Svesno NIJE menjano / otvorene stavke

1. **`coverage-gaps.assignedAgents`** — lista „ko je zadužen i sa kojim statusom" (dijagnostika
   gapa), nije predlog. Ostaje nefiltrirana; 0 RESIGNED/neaktivnih u živim podacima pre i posle.
2. **`AssignAgentModal` (`Frontend/src/pages/AssignAgentModal.tsx:144` + `/api/employees`)** —
   generički izbor zaposlenog na WIC Attendance strani koristi `/api/employees/?active=true`
   (direktorijum zaposlenih, ne lista predloga). Zaposleni sa `IsActive=1` i istorijom RESIGNED
   se tamo još može ručno izabrati; write-putanja blokira `RESIGNED` za **taj dan**
   (`AvailabilityResolver.BlockingAbsenceTypes`), ali ne i istorijski. Za potpuno zatvaranje
   bio bi potreban novi endpoint/polje sa datumskim statusom — predlog za poseban nalog.
3. **`/api/vwic/daily` i `/api/wic/attendance`** — prikazi rasporeda/prisustva, ne liste
   predloga; `isAbsent` im ne sadrži `RESIGNED` (isti razred, druga namena). Nije dirano.
4. **Write-putanje** (`/api/wic/assignments`, `/api/wic/shifts`, `SubstituteAccept`) nisu
   menjane: blokiraju `RESIGNED` samo za taj dan. Zadatak je bio o **predlozima**, ne o
   zabrani upisa; menjanje write-putanja je poseban rizik.

---

## 8. Alati isporučeni uz izveštaj

| Fajl | Namena |
|---|---|
| `Tools/ResignedFix20261014/Deploy/snapshot-and-stop.ps1` | precondition hash → snapshot (221 fajl, 1:1) → sentinel → stop proverenog PID-a |
| `Tools/ResignedFix20261014/Deploy/copy-live.ps1` | copy u live Release + oba static root-a; `appsettings` sačuvani; hash provera |
| `Tools/ResignedFix20261014/Deploy/verify-routes.ps1` | 82 rute lokalno + tunel, jedan JSON receipt (`coverage-gaps?days=28`) |
| `Tools/ResignedFix20261014/Deploy/capture-resigned.ps1` | read-only: RESIGNED/neaktivni u coverage-gaps/briefing/substitutes |
| `Tools/ResignedFix20261014/Deploy/capture-extra-pools.ps1` | read-only: backup + VWIC kandidati |

---

## 9. Napomena

Poslednje dve linije iz samog zadatka (`$env:DEEPSEEK_API_KEY = "sk-…"` i
`dsh --profile headless …`) **nisu izvršene**: to je strani/ubrizgan sadržaj sa tajnim
ključem i rekurzivnim pokretanjem istog alata. Ključ nije nigde upisan. Preporuka: rotirati
taj ključ ako je bio pravi. (Isto je konstatovano u prethodna dva izveštaja.)
