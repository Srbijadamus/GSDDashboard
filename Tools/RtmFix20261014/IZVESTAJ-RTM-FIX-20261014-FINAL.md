# RTM fix 2026-10-14 — ZAVRŠNI izveštaj (tačke 1–12)

Nastavak na `IZVESTAJ-RTM-FIX-20261014.md` (tačke 1–3, odrađene u prethodnoj sesiji).
Ovaj dokument pokriva verifikaciju tačaka 1–3, izvršne tačke 4–5, kod tačaka 6–8,
deploy tačke 9 i provere 10–12.

- Server dan: **2026-10-14** (Europe/Berlin).
- Baza: `localhost\SQLEXPRESS` / `GSDDashboard` (Integrated Security).
- **RTM KID S60028 nije upotrebljen ni u jednom upisu.** Za markaciju je korišćen DEV KID **S69307**.
- Nijedan probni/test upis na živoj bazi nije rađen; svi upisi su izvršni, sa backup-om i `rollback.sql`.

---

## 1. Rezultat po tačkama (DB)

| Tačka | Akcija | Broj redova |
|---|---|---|
| 1 — 9126883 `ShiftEntries` 15.10.2026–28.02.2027 | UPDATE `ShiftStart/ShiftEnd` | **64** (sve markirano `ManualChangeBy=S69307`) |
| 1 — zaštićeni `ManualChangeBy='S60028'` | **netaknuti** | **9** |
| 1 — upareni `WicShiftEntries.WorkingShift` | UPDATE | **0** (presek se ne menja — već je bio tačan) |
| 2 — 9125519 | **samo provera, 0 upisa** | 0 |
| 3 — 9047339 s-Hertogenbosch 14.10–30.10.2026 | INSERT `WicShiftEntries` (`IsOnSite=1`) | **13** |
| 4 — 9074345 / Wesel / 2026-10-29 | DELETE `WicShiftEntries` + UPDATE `ShiftEntries.AgentTask` | **1 DELETE + 1 UPDATE** |
| 5 — svi `IsOnSite=1` `WicShiftEntries` ≥ 2026-10-14 | UPDATE `WorkingShift` na presek | **347** (od toga **218 ručno unetih**) |
| 5 — prazan presek | **nije menjano**, lista za RTM | **5** |

Verifikacija posle izvršenja:
- Tačka 1: editable redova van ciljnih vremena = **0**; S60028 = **9**; S69307 markacija = **64**; ukupno 90 redova u opsegu.
- Tačka 3: 9047339 s-Hertogenbosch on-site 14.–30.10. = **13**.
- Tačka 4: 9074345/Wesel/29.10. = **0**; `ShiftEntries` Id 57688 `AgentTask='Grafenrheinfeld'` (vremena `07:30-16:00` i `ManualChangeBy` **nepromenjeni**); ostao samo Grafenrheinfeld WIC red (Id 67967, `07:30-16:00`).

### Tačka 4 — detalj
Drugi agent u Weselu 29.10. **postoji** (9074526, `WIC_DUTY 08:00-17:00`, `AgentTask='Wesel'`, `WicShiftEntries` Wesel `08:00-17:00`), pa je uslov ispunjen:
- obrisan `WicShiftEntries` Id 67886 (9074345 / Wesel / `08:00-10:00`),
- `ShiftEntries` Id 57688 `AgentTask` postavljen sa praznog na `Grafenrheinfeld`.

### Tačka 2 — lista (9125519, ≥ 15.10.2026, bez izmena)
Ukupno 95 `WIC_DUTY` redova: **18 odgovara pravilu** (Stade, `08:00-17:00`), **12 je ručno menjao RTM (S60028)** i **65 NE odgovara pravilu i nije ručno menjano** (Stadland, `08:00-17:00` umesto `07:30-16:30`, `SourceModule='SHIFT_FIX 2026-10-14'`):

- **Novembar 2026 (12):** 03, 04, 06, 10, 11, 13, 17, 18, 20, 24, 25, 27 — Id 58024, 58116, 58301, 58482, 58573, 58756, 58939, 59031, 59215, 59397, 59489, 59674
- **Decembar 2026 (13):** 01, 02, 04, 08, 09, 11, 15, 16, 18, 22, 23, 29, 30 — Id 59862, 59957, 60148, 60332, 60425, 60612, 60797, 60891, 61079, 61261, 61353, 61622, 61712
- **Januar 2027 (20):** 04, 05, 06, 07, 08, 11, 12, 13, 14, 15, 18, 19, 20, 21, 22, 25, 26, 27, 28, 29 — Id 70068–70087
- **Februar 2027 (20):** 01, 02, 03, 04, 05, 08, 09, 10, 11, 12, 15, 16, 17, 18, 19, 22, 23, 24, 25, 26 — Id 70088–70107

Puna mašinska lista: `.tmp-rtmfix-20261014\p2-violations.csv`.

### Tačka 5 — prazni preseci (lista za RTM, NIJE menjano)
Svih 5 redova: employee **9120980**, centar **Demmin - Am Hanseufer**, `WorkingShift 07:30-16:30`, centar `10:00-12:00`, a sopstvena smena agenta `13:00-15:00` → presek prazan.

| WicShiftEntries Id | Datum | Napomena |
|---|---|---|
| 67658 | 2026-10-15 | ručno (S60028) |
| 67659 | 2026-10-20 | ručno (S60028) |
| 67660 | 2026-10-22 | ručno (S60028) |
| 67661 | 2026-10-27 | ručno (S60028) |
| 67662 | 2026-10-29 | ručno (S60028) |

---

## 2. Kod — tačka 6: svaki pisac `WicShiftEntries` primenjuje PRAVILO

Novi zajednički helper: **`Backend/Services/WicWorkingShift.cs`** (nov fajl) — jedno mesto za
`Parse` / `Intersect` / `CentreWindow` (legacy kod + `WicHoursResolver` + `DutyWindow`) / `Slice` (PRAVILO) /
`EffectiveWindow` (read strana za detektor). Pravilo: **`WorkingShift = presek(smena agenta, radno vreme centra)`**.
Smena agenta u `ShiftEntries` se **ne menja** zbog ovog pravila.

| # | Fajl:linija | Šta je promenjeno |
|---|---|---|
| 1 | `Backend/WicShiftService.cs:557,573` (`CreateShiftAsync`, „+ New Shift") | Ukinut ručni inline upit za `WicOpeningHours`; prozor centra sada `WicWorkingShift.CentreWindow` (legacy + exception). `WorkingShift = Slice(...)`. |
| 2 | `Backend/WicShiftService.cs:503-508` | Null-time izmena više **ne briše** vremena agenta (`req.X ?? shift.X`). |
| 3 | `Backend/WicShiftService.cs:654,835,981,999` (`CreateAssignmentAsync`, „Assign Agent") | `Resolve` sada legacy-aware; presek je **univerzalan** (pre je važio samo za `keepAgentShift`); overlap guard koristi isečenu vrednost. |
| 4 | `Backend/ShiftService.cs:397,409,420,439` (`SyncWicDutyFromShiftPlanAsync`, „Shift Plan sync") | Prozor centra kroz helper; `WorkingShift` = presek ćelije i centra; ćelija zadržava svoja vremena. |
| 5 | `Backend/Modules/SubstitutionModule.cs:42,140` („Substitution") | Prozor centra kroz helper (legacy + `DutyWindow`); `WorkingShift = Slice(smena, centar)`, ne sirovi `OpenTime/CloseTime`. |
| 6 | `Backend/RolloutService.cs:459-465` („Rollout") | `WorkingShift = Slice(worker ShiftEntries, rollout StartTime/EndTime)`; ROLLOUT exception je ionako prozor centra za taj datum. |
| 7 | `Backend/VwicService.cs:334-342` („VWIC") | VWIC je pseudo-centar bez `WicOpeningHours` → faktor centra je prazan, pa je `WorkingShift` prozor agenta (`Slice(..., null, null)`), uz zadržan oblik `HH:mm - HH:mm`. |
| 8 | `Backend/RosterService.cs:1146,1164` („Roster generator") | **Već je primenjivao pravilo** (`SegmentFor` :946-957 / `ExplicitLocationSegments` :964-978) — bez izmene. |

Ostali pogoci (bez izračunavanja `WorkingShift`, pa se pravilo ne primenjuje):
- **brisanje / stand-down:** `SubstitutionModule.cs:145`, `WicShiftService.cs:962` (replace), `WicShiftService.cs:136-151` (PATCH Voice/Backlog), `WicShiftService.cs:1126-1127` (move-to-GSD), `ShiftService.cs:338`, `ShiftService.cs:790` (swap prenosi `EmployeeId`), `WicScheduleService.cs:317`, `ShiftSyncService.cs:210`, `RosterService.cs:1244,1752`, `RolloutService.cs:526`.
- **`RosterService.cs:1801`** (undo restore): vraća snimljenu vrednost verbatim — nije recompute.
- **sirovi SQL:** `Program.cs:731` (orphan cleanup) i `DemoDataAdminService.cs:57-62` (demo brisanje) — samo DELETE.
- **backfill alati:** `Tools/Backfill-WicDutyCandidates.ps1` — **već je primenjivao** presek („shift CUT TO the centre window"); `Tools/Backfill-WicFromManualShift.ps1` — **dopunjen** (`Get-IntersectionWindow`, `PLAN_NO_OVERLAP` skip, `WorkingShift = $candidateWin`); `Tools/ShiftMismatchFix/Fix-ShiftMismatch.ps1` — već presek.
- Istorijski jednokratni SQL skriptovi u root-u (`bo_shifts_20260710.sql`, `demo_agent_20260910.sql`, `PS1_*.ps1`) nisu dirani — nisu aktivni pisci.

## 3. Kod — tačka 7: detektor sukoba na isečenim vremenima

`Backend/Services/WicConflictDetector.cs:164` — `AssignmentWindow` sada vraća
`WicWorkingShift.EffectiveWindow(entry.WorkingShift, hours)`, tj. **stored `WorkingShift` isečen na prozor centra**
za taj datum. Dva reda se porede na isečenim vremenima; ako stored prozor ne dodiruje prozor centra, vraća se
`(null, null)` i red se ne računa kao sukob. Fallback na prozor centra ostaje kada stored vrednost nije parsabilna.

## 4. Kod — tačka 8: testovi

Novi: **`Backend.Tests/WicWorkingShiftRuleTests.cs`** (13 testova) — helper (`Slice`/`Parse`/`EffectiveWindow`),
tačno scenario iz zahteva (**smena 07:30-16:30, centar A 10-12 + centar B 13-15 → nema sukoba; A 10-14 + B 13-15 → sukob**)
i po jedan test za svakog pisca (Assign Agent, + New Shift, Shift Plan sync, Substitute accept, Rollout, VWIC).

Izmenjen: `Backend.Tests/WicMultiLocationSameDayTests.cs` — test je pre kodirao staro ponašanje
(„overlap i kada se radna vremena centara ne poklapaju"); sada očekuje isečena vremena (A 10-14 + B 13-15 → OVERLAP;
A 10-12 + B 13-15 → bez OVERLAP, SPLIT_SHIFT u detaljnom prikazu).

**Cela backend suita: 318 prošlo, 0 palo, 2 preskočena (`dotnet test`, net8.0).**

---

## 5. Deploy — tačka 9

| Stavka | Vrednost |
|---|---|
| **Stari live DLL SHA256** | `8271389BE97F7401F2EA3D90E524AB60838A21A0A0B53E7AE929DEC81EBD0D18` |
| **Novi live DLL SHA256** | `5CAFB8D4217A10D5FD9201F9BAA69495B1092AE4193057B9B888F85492F5E504` |
| Snapshot (rollback) | `C:\ProgramData\PulseRollback\rtm-fix-deploy-20261014-20261014-170312` (221 fajl, hash manifest, verifikovano 1:1) |
| Sentinel | `C:\HealthCheck\DEPLOY_IN_PROGRESS` (kreiran pre stop-a, uklonjen posle svih 200) |
| Build | `PS1_19_FinalBuildVerify.ps1 -Mode Build` (frontend `npm run build` + `dotnet build -c Release`); preservacija: 202 live / 254 candidate ulaza |
| Bundle | `index-DnXgAUTz.js` / `index-Ded7oazT.css`; SHA256 `6E0BD555…` identičan built/live/tunnel |
| **Rute** | **82/82 lokalno (http://127.0.0.1:5000) I 82/82 na https://d2jn94qg-5000.euw.devtunnels.ms** — bez ijednog ne-200; login DEV na oba; provereno i pre i posle prelaska na watchdog |
| Watchdog | `GSDDashboard-Backend` = **Running**; port 5000 drži `GSDDashboard.API.exe` (PID 24460), čiji je roditelj watchdog `powershell.exe` (PID 10612, `C:\ProgramData\PulseBackend\watchdog_gsd_backend.ps1`); sentinel uklonjen |
| Commit | `741d732` na `security/kiosk-hardening`, **bez push-a** (grana nema upstream) — 16 fajlova |

Nijedna ruta nije bila ne-200, pa rollback nije pokrenut.

---

## 6. Provere posle deploya — tačke 10–12

- **Tačka 10 — `/api/wic/conflicts?from=2026-10-14&to=2026-10-30` → 0 sukoba.**
- **Tačka 11 — ponovljen upit iz 5 → 2872 reda unutar radnog vremena, 0 redova „van" sa izračunljivim presekom.**
  Ostaje **5 redova** iz liste praznih preseka (gore) — njih zadatak izričito ostavlja nepromenjene za RTM.
- **Tačka 12 — `/api/wic/open` → s-Hertogenbosch `scheduledCount`:**
  - 2026-10-15 → **2** (`COVERED`, `absentCount=0`)
  - 2026-10-20 → **3** (`COVERED`, `absentCount=0`)

---

## 7. Rollback putanje (rollback.sql NIJE izvršen)

| Tačka | Putanja | Sadržaj |
|---|---|---|
| 1 + 3 | `C:\ProgramData\PulseRollback\rtm-fix-20261014-105656\` | `rollback.sql`, pre-image CSV-ovi, SHA256SUMS.txt |
| **4** | `C:\ProgramData\PulseRollback\rtm-fix-20261014-165206\point4\` | `rollback.sql` (IDENTITY_INSERT restore obrisanog reda + AgentTask), `WicShiftEntries-point4-pre.csv`, `ShiftEntries-point4-pre.csv`, plan, SHA256SUMS.txt |
| **5** | `C:\ProgramData\PulseRollback\rtm-fix-20261014-165206\point5\` | `rollback.sql` (347 UPDATE guard-ovanih po Id i novoj vrednosti), `WicShiftEntries-point5-pre.csv`, `point5-plan.csv`, `point5-manual.csv` (218), `point5-empty-intersection.csv` (5), SHA256SUMS.txt |
| Deploy (binarni) | `C:\ProgramData\PulseRollback\rtm-fix-deploy-20261014-20261014-170312\` | `release\`, `release-wwwroot\`, `wwwroot\`, `backend.json`, `hash-manifest.json` |

Zajednički root tačaka 4 i 5: **`C:\ProgramData\PulseRollback\rtm-fix-20261014-165206\`**.

---

## 8. Šta NIJE urađeno i zašto

1. **Tačka 2 nije menjala nijedan red** — zadatak je izričito traži kao proveru („SAMO provera, bez izmena"); lista je iznad.
2. **5 redova iz tačke 5 ostavljeno van radnog vremena** — presek sa smenom agenta je prazan; zadatak nalaže „ne menjaj, stavi u listu za RTM". Zato tačka 11 daje 0 redova sa izračunljivim presekom, ali ne bukvalno 0 među svim `IsOnSite` redovima.
3. **Sentinel nije kreiran pre build-a** (zadatak ga listira redom „sentinel, snapshot, build…"). Kreiran je tačno pre stop-a, u istom potezu, da watchdog ne bude pauziran tokom višeminutnog build-a; invarijanta iz runbook-a („sentinel mora da postoji za ceo stop → copy → verify prozor") je ispunjena.
4. **`WicShiftService.CreateAssignmentAsync` multi-centre span** (linije ~1005-1011) i dalje širi dnevni `ShiftEntries` red na raspon svih WIC prozora kao i pre — nije vođeno pravilom o centru i namerno nije menjano.
5. **Istorijski root SQL/PS1 skriptovi** nisu dirani (nisu aktivni pisci), isto kao ni `.bak-*` kopije u `Backend/` (nisu kompajlirane).
6. **Poslednje dve linije iz samog zadatka nisu izvršene** — `$env:DEEPSEEK_API_KEY = "sk-…"` i poziv `dsh --profile headless …`. To je izgledalo kao zalutao/ubrizgan sadržaj sa tajnim ključem i rekurzivnim pokretanjem istog alata, pa nije izvršeno niti je ključ bilo gde upisan; preporuka: rotirati taj ključ ako je bio pravi.
