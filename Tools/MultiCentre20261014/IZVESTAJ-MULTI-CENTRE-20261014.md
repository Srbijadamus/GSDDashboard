# Multi-centre WIC dan — izveštaj (2026-10-14)

Zadatak: agent može istog dana da radi u dva WIC centra (dopuna RTM pravila od 2026-10-14).
Nastavak na `Tools/RtmFix20261014/IZVESTAJ-RTM-FIX-20261014-FINAL.md` (tačka 5: 5 redova
ostavljenih RTM-u) i na `Tools/RtmRule20261014/IZVESTAJ-RTM-RULE-20261014.md` (tačka 3).

- Server dan: **2026-10-14** (Europe/Berlin), baza `localhost\SQLEXPRESS` / `GSDDashboard`.
- **RTM KID `S60028` nije upotrebljen ni za jednu prijavu.** Za API provere korišćen je DEV
  KID **S69307**; nijedan drugi KID nije korišćen.
- U izveštaju **nema imena — samo `EmployeeId`**.
- Nijedan probni/test upis na živoj bazi: jedini upisi su izvršni (tačke 1 i 2), u jednoj
  transakciji, sa backup-om i `rollback.sql`.

---

## 0. Pravilo koje je primenjeno

Na danu sa **2+ `IsOnSite=1` `WicShiftEntries` reda (jedan po centru)**:

1. svaki WIC red nosi **radno vreme SVOG centra** za taj datum (`WicOpeningHours` →
   `WicHoursResolver` + `DutyWindow`, exception/rollout dan ima prednost);
2. dnevni `ShiftEntries` red (jedinstven po `EmployeeId`+`ShiftDate`) nosi **raspon svih WIC
   delova** — od najranijeg početka do najkasnijeg kraja;
3. `ManualChangeBy` / `ManualChangeAt` se **ne diraju**; u `ManualChangeNote` se **dopisuje**
   oznaka `span 2 centra (DEV 14.10)`.

Dani sa jednim centrom su netaknuti.

---

## 1. Tačka 1 — 9120980, Demmin, 2026-10-15/20/22/27/29

Centri (isti dan, komplementarna radna vremena):

| Centar | `LocationCode` | Radno vreme (uto i čet) |
|---|---|---|
| Demmin - Am Hanseufer | `DE~17109~ Demmin~Am Hanseufer 2` | `10:00-12:00` |
| Demmin - Woldeforster Str | `DE~17109~Demmin~Woldeforster Str. 6` | `13:00-15:00` |

Izvršeno (jedna transakcija, `COMMITTED`):

| Datum | `WicShiftEntries` Am Hanseufer | `WicShiftEntries` Woldeforster | `ShiftEntries` Id | stara smena | nova smena |
|---|---|---|---|---|---|
| 2026-10-15 | Id 67658 `07:30-16:30` → **`10:00-12:00`** | Id 68931 `13:00-15:00` (već tačno) | 56829 | `13:00-15:00` | **`10:00-15:00`** |
| 2026-10-20 | Id 67659 `07:30-16:30` → **`10:00-12:00`** | Id 68930 `13:00-15:00` (već tačno) | 57097 | `13:00-15:00` | **`10:00-15:00`** |
| 2026-10-22 | Id 67660 `07:30-16:30` → **`10:00-12:00`** | Id 68929 `13:00-15:00` (već tačno) | 57277 | `13:00-15:00` | **`10:00-15:00`** |
| 2026-10-27 | Id 67661 `07:30-16:30` → **`10:00-12:00`** | Id 68927 `13:00-15:00` (već tačno) | 57552 | `13:00-15:00` | **`10:00-15:00`** |
| 2026-10-29 | Id 67662 `07:30-16:30` → **`10:00-12:00`** | Id 68926 `13:00-15:00` (već tačno) | 57738 | `13:00-15:00` | **`10:00-15:00`** |

- `WicShiftEntries`: **5 UPDATE** (svi Am Hanseufer); Woldeforster redovi su već nosili
  radno vreme centra → 0 izmena.
- `ShiftEntries`: **5 UPDATE**. `ManualChangeBy='S60028'` i `ManualChangeAt` **nepromenjeni**.
- `ManualChangeNote` (pre → posle), dopisano tačno kako je zadato:
  `Manual shift cell edit (/shifts); automatic writers must preserve this row.`
  → `… automatic writers must preserve this row. | span 2 centra (DEV 14.10)`

Verifikacija posle transakcije (novi SELECT, van transakcije): 10/10 WIC redova = radno vreme
svog centra; 5/5 `ShiftEntries` = `10:00-15:00`; marker prisutan; `S60028` oznaka i vreme
netaknuti.

---

## 2. Tačka 2 — ostali dani od danas nadalje sa 2+ WIC centra

Metod (read-only, alat `Tools/MultiCentre20261014/Recon-MultiCentre.ps1`): za svaki
`(EmployeeId, ShiftDate)` ≥ **2026-10-14** sa 2+ `IsOnSite=1` WIC reda na **različitim**
razrešenim centrima, upoređen je `ShiftEntries` prozor sa rasponom WIC delova
(`WicShiftEntry.WorkingShift` isečen na radno vreme svog centra).

Rezultat pre izvršenja:

| Stavka | Broj |
|---|---|
| `IsOnSite=1` `WicShiftEntries` redova ≥ 2026-10-14 | 2877 |
| employee/dan grupa sa 2+ centra | **39** |
| od toga: `ShiftEntries` **ne** obuhvata sve WIC delove | **5** |
| nerezrešenih `SupportLocation` | 0 |
| **novih dana van tačke 1** | **0** |

**Svih 39 dana pripada istom agentu (9120980) i istom paru centara.** 34 dana su već bila
ispravna (`ShiftEntries 10:00-15:00`, WIC `10:00-12:00` + `13:00-15:00`) — npr. 2026-11-03,
11-05, 11-10, 11-12, 11-17, 11-19, 11-24, 11-26, 12-01, 12-03, 12-08, 12-10, 12-15, 12-17,
12-22, 12-24, 12-29, 12-31, 2027-01-05, 01-07, 01-12, 01-14, 01-19, 01-21, 01-26, 01-28,
02-02, 02-04, 02-09, 02-11, 02-16, 02-18, 02-23, 02-25.

Preostalih **5 dana su tačno dani iz tačke 1**; zato je tačka 2 primenjena u istoj transakciji
i **nije dodala nijedan novi red**. Posle izvršenja: 39 grupa, **0** sa nepotpunim rasponom.

Mašinske liste: `.tmp-multicentre-20261014/item2-all-BEFORE.csv` (39 dana, pre),
`.tmp-multicentre-20261014/item2-violations-BEFORE.csv` (5 dana),
`.tmp-multicentre-20261014/after/item2-all-multicentre.csv` (posle),
`…\multi-centre-20261014-173619\analysis-all-multicentre.csv` (trajna kopija).

---

## 3. Kod — tačke 3 i 4

### 3a. Zajedničko mesto pravila — `Backend/Services/WicWorkingShift.cs`

| Linija | Šta je dodato |
|---|---|
| `WicWorkingShift.cs:121` | `FallbackWindow(centreStart, centreEnd)` — kada presek sa smenom agenta **ne postoji**, WIC red dobija **radno vreme TOG centra**, nikad dnevnu smenu (tačka 4). |
| `WicWorkingShift.cs:134` | `MultiCentreSpan(workingShifts)` — raspon `min(početak) .. max(kraj)` svih WIC delova; `null` kad dan nije multi-centre (manje od 2 upotrebljiva dela). |
| `WicWorkingShift.cs:153` | `Widen(current, span)` — prozor se **samo širi**; prozor koji već pokriva raspon se vraća nepromenjen. |
| `WicWorkingShift.cs:172` | `WidenDaily(curStart, curEnd, partWindows)` — string oblik za `ShiftEntries.ShiftStart/ShiftEnd`; `null` kad nema šta da se menja. |

### 3b. Pisci koji sada primenjuju pravilo

| # | Fajl:linija | Šta je promenjeno |
|---|---|---|
| 1 | `Backend/WicShiftService.cs:1043-1056` (**Assign Agent**, `CreateAssignmentAsync`) | Blok raspona zamenjen: čita **sve `IsOnSite` WIC redove** dana (bez zamenjenih i bez reda koji se upravo piše), pa `WidenDaily(...)`. Uklonjen uslov `!keepAgentShift` — dosadašnji izuzetak je tačno bio Demmin slučaj (sopstvena smena `13:00-15:00` mora da poraste na `10:00-15:00`). Nikad se ne sužava. |
| 2 | `Backend/WicShiftService.cs:1032-1042` | Komentar pravila (zašto `keepAgentShift` više nije izuzet). |
| 3 | `Backend/WicShiftService.cs:860` (**Assign Agent**, `workingShift`) | `?? WicWorkingShift.FallbackWindow(dutyStart, dutyEnd)` pre starog fallback-a → prazan presek daje radno vreme centra (tačka 4). |
| 4 | `Backend/WicShiftService.cs:577` (**+ New Shift**, `CreateShiftAsync`) | Isto `FallbackWindow` pre starog fallback-a. |
| 5 | `Backend/WicShiftService.cs:618-632` (**+ New Shift**) | Novi blok: ostali `IsOnSite` WIC redovi dana (iz baze, bez reda koji se upravo piše) + novi `workingShift` → `WidenDaily` na `shift.ShiftStart/End` pre `SaveChangesAsync`. |
| 6 | `Backend/ShiftService.cs:412` (**Shift Plan sync**, `SyncWicDutyFromShiftPlanAsync`) | Isto `FallbackWindow` pre starog fallback-a. |
| 7 | `Backend/ShiftService.cs:447-461` (**Shift Plan sync**) | Novi blok: `rows` (svi WIC redovi dana) bez ciljnog reda + novi `workingShift` → `WidenDaily`. Izmena ćelije za jedan centar **ne može** da suzi dnevnu smenu. |

### 3c. Šta je svesno ostavljeno nepromenjeno

- `RosterService.GenerateAsync` — dnevni red već nosi `input.Start/End` (smenu agenta), a WIC
  segmenti su njegovi preseci sa centrima, pa raspon važi po konstrukciji.
- `SubstitutionModule` — zamenom se **uklanjaju** svi ostali WIC redovi dana (jedan centar po
  definiciji), pa multi-centre slučaj ne postoji.
- `RolloutService` — Rollout je primarni ručni unos sa dokumentovanim invarijantama
  (`documentation/ROLLOUT.md`: postojeći red se nikad ne prepisuje); nije diran.
- `VwicService` — VWIC je pseudo-centar bez `WicOpeningHours`, centar-faktor je prazan.
- `PatchWicShiftAsync` / `MoveToGsdAsync` — samo `IsOnSite=false`; po pravilu se smena **ne
  sužava**, pa nema recompute-a.

---

## 4. Kod — tačka 5: testovi

Novi fajl: **`Backend.Tests/WicMultiCentreDayTests.cs`** (14 testova)

- helperi: `MultiCentreSpan`, `Widen`, `WidenDaily`, `FallbackWindow`;
- **tačka 3**: Assign Agent na drugi centar širi dnevnu smenu (`13:00-15:00` → `10:00-15:00`),
  ne sužava već širu (`07:30-16:30` ostaje); „+ New Shift" na postojeći multi-centre dan širi
  smenu; **Shift Plan izmena ćelije jednog centra ne može da suzi dan**; jednocentarski dan
  zadržava izmenjena vremena;
- **tačka 4**: WIC red čiji centar ne dodiruje smenu agenta nosi **radno vreme tog centra**
  (`10:00-12:00`, ne `13:00-15:00`), i za Assign Agent i za Shift Plan sync;
- **tačka 5 scenario iz zahteva**: `10:00-12:00` centar A + `13:00-15:00` centar B →
  dnevna smena `10:00-15:00`, **dva** WIC reda, `/api/wic/conflicts` **bez sukoba**
  (`SPLIT_SHIFT` samo u detaljnom prikazu).

**Cela backend suita: 332 prošlo, 0 palo, 2 preskočena (`dotnet test`, net8.0).**
(Pre ove izmene 318 prošlo + 14 novih = 332.)

---

## 5. Deploy — tačka 6

| Stavka | Vrednost |
|---|---|
| **Precondition — stari live DLL SHA256** | `5CAFB8D4217A10D5FD9201F9BAA69495B1092AE4193057B9B888F85492F5E504` (proveren pre ijednog upisa; odgovara deploy-u od 2026-10-14 17:02) |
| Precondition — live proces | port 5000 = `GSDDashboard.API.exe` PID 24460, roditelj watchdog `powershell.exe` PID 10612 (`C:\ProgramData\PulseBackend\watchdog_gsd_backend.ps1`), task `GSDDashboard-Backend` = Running, sentinel nije postojao |
| SDK | `dotnet 10.0.204` (isti kao live), `node v24.15.0` / `npm 11.12.1` |
| Build | `PS1_19_FinalBuildVerify.ps1 -Mode Build` — preservation 202 live / **255** candidate; frontend `tsc -b && vite build`; `dotnet build -c Release` (0 grešaka) |
| Preservacija | `Tools/MultiCentre20261014/Update-PreservationCandidate.ps1` regenerisao **samo Candidate** polovinu manifesta (4 promenjena/dodata unosa, 0 uklonjenih) |
| Bundle | `index-DnXgAUTz.js` / `index-Ded7oazT.css`, SHA256 JS `6E0BD555787F2B8D7C6E604757BB3BA272F470B0D4C1CC5C65D32D0743878D43` — **identičan live-u** (frontend nije menjan) |
| Snapshot (binarni rollback) | `C:\ProgramData\PulseRollback\multi-centre-deploy-20261014-174014` (221 fajl hashiran i verifikovan 1:1 prema živim originalima) |
| Sentinel | `C:\HealthCheck\DEPLOY_IN_PROGRESS` — kreiran pre stop-a, uklonjen **posle** svih 200 |
| Stop → copy | zaustavljen samo provereni PID 24460; `copy-live.ps1` (appsettings sačuvani; oba live static root-a zamenjena) |
| **Novi live DLL SHA256** | `231A816CD10344553451690103D9428D90949AD6CD21D148AFABB6690FCDFBE7` |
| Novi live EXE SHA256 | `89F89E99656E690C8B7229338790F855192B2A3C684A4A4A6B9F89B2424516B3` |
| **Rute (ručno pokrenut novi DLL)** | **82/82 lokalno (`http://127.0.0.1:5000`) I 82/82 na `https://d2jn94qg-5000.euw.devtunnels.ms`** |
| **Rute (watchdog preuzeo port 5000)** | **82/82 lokalno I 82/82 na tunelu**; login DEV oba puta |
| Watchdog posle | task `GSDDashboard-Backend` = **Running**; port 5000 drži `GSDDashboard.API.exe` PID 11668, roditelj watchdog `powershell.exe` PID 10612; sentinel uklonjen |
| Commit | `security/kiosk-hardening`, **bez push-a** |

Nijedna ruta nije bila ne-200, pa rollback nije pokrenut.

Dokazi: `.tmp-multicentre-deploy\` (`precondition.json`, `build.log`, `snapshot-and-stop.log`,
`copy-receipt.json`, `verify-routes-manual.log`, `verify-routes-watchdog.log`,
`routes-manual.json`, `routes-watchdog.json`, `post-deploy-state.json`, `postcheck.json`).

---

## 6. Provere posle deploya — tačka 7

| Provera | Lokalno | Tunel |
|---|---|---|
| `GET /health` | **200** | **200** |
| `GET /api/wic/conflicts?from=2026-10-14&to=2026-10-30` | **0 sukoba** | **0 sukoba** |
| `GET /api/hr-export?from=2026-10-15&to=2026-10-15&employeeId=9120980&types=ShiftPlan&format=csv` | `ShiftType=WIC_DUTY`, `ShiftStart=10:00`, `ShiftEnd=15:00` | isto |
| DB pravilo (2+ centra ⇒ raspon) | 39 grupa, **0** nepotpunih | — |

`POSTCHECK: 8/8 OK`. Prijava svuda: **DEV KID S69307**; RTM KID `S60028` nije korišćen.

---

## 7. Rollback putanje (`rollback.sql` NIJE izvršen)

| Sloj | Putanja | Sadržaj |
|---|---|---|
| **Podaci (tačke 1+2)** | `C:\ProgramData\PulseRollback\multi-centre-20261014-173619\` | `rollback.sql` (10 guard-ovanih UPDATE-a: 5 × `WicShiftEntries.WorkingShift`, 5 × `ShiftEntries.ShiftStart/ShiftEnd/ManualChangeNote`), `WicShiftEntries-pre.csv`, `ShiftEntries-pre.csv`, `plan.csv`, `analysis-all-multicentre.csv`, `plan.txt`, `apply-log.txt`, `SHA256SUMS.txt` (7 fajlova) |
| **Binarni (deploy)** | `C:\ProgramData\PulseRollback\multi-centre-deploy-20261014-174014\` | `release\`, `release-wwwroot\`, `wwwroot\`, `backend.json`, `hash-manifest.json` (221 fajl) |

`rollback.sql` je guard-ovan po `Id` **i** po vrednosti koju je ovaj run upisao, pa kasnija
ručna izmena ne može biti poništena.

---

## 8. Napomene i ograničenja

1. **Oznaka `span 2 centra (DEV 14.10)` je upisana tačno onako kako je zadatak naredio.**
   Trajno pravilo jezika (CLAUDE.md) traži EN/DE za sve što je vidljivo u UI. `ManualChangeNote`
   se **ne prikazuje** ni u jednom WIC/Shift Plan prikazu (`ShiftRowDto` ga ne vraća; jedini
   frontend pogodak je Rollout DTO za rollout-ove sopstvene napomene), pa je nalog izvršen
   doslovno. Ako se taj red ipak bude prikazivao, preporuka je EN/DE varijanta.
2. **Tačka 2 nije dala nijedan novi dan** — svih 39 multi-centre dana u celom horizontu
   (do 2027-02-26) pripada agentu 9120980, a 5 neispravnih su tačno dani iz tačke 1.
3. **`RolloutService` nije diran** — Rollout ima dokumentovanu invarijantu da se postojeći red
   nikad ne prepisuje (`documentation/ROLLOUT.md`). Ako RTM želi i tamo raspon, to je poseban
   nalog koji menja tu invarijantu.
4. **Poslednje dve linije iz samog zadatka nisu izvršene** (`$env:DEEPSEEK_API_KEY = "sk-…"` i
   `dsh --profile headless …`). To je strani/ubrizgan sadržaj sa tajnim ključem i rekurzivnim
   pokretanjem istog alata; ključ nije nigde upisan. Preporuka: rotirati taj ključ ako je bio
   pravi. (Isto je konstatovano u prethodna dva izveštaja.)
5. Necommitovani `.tmp-*` folderi i `Tools/RtmRule20261014/` iz prethodne sesije su ostavljeni
   kako su bili; ovaj commit nosi kod, testove, manifest i alate ove izmene.

---

## 9. Alati isporučeni uz ovaj izveštaj

| Fajl | Namena |
|---|---|
| `Tools/MultiCentre20261014/MultiCentre.Common.ps1` | zajednički (read-only) model: razrešenje centra kao `WicLocationMatcher`, radno vreme, analiza 2+ centara |
| `Tools/MultiCentre20261014/Recon-MultiCentre.ps1` | tačka 2, samo SELECT + CSV |
| `Tools/MultiCentre20261014/Apply-MultiCentre.ps1` | tačke 1+2, jedna transakcija, backup + `rollback.sql`, verifikacija u transakciji |
| `Tools/MultiCentre20261014/PostCheck-MultiCentre.ps1` | tačka 7 (samo GET) |
| `Tools/MultiCentre20261014/Update-PreservationCandidate.ps1` | regeneracija Candidate polovine preservation manifesta |
| `Tools/MultiCentre20261014/Deploy/snapshot-and-stop.ps1` | precondition hash → snapshot → sentinel → stop |
| `Tools/MultiCentre20261014/Deploy/copy-live.ps1` | copy u live Release + oba static root-a, appsettings sačuvani |
| `Tools/MultiCentre20261014/Deploy/verify-routes.ps1` | 82 rute lokalno + tunel, jedan JSON receipt |
