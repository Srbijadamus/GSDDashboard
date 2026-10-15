# IZVEŠTAJ — HAL/WIC 2026-10-15

> Radni folder: `C:\GSDDashboard_secure` · Live: `C:\GSDDashboard` (Release, port 5000)
> Namerno: u izveštaju **nema imena** — samo `EmployeeId`. RTM KID `S60028` nije nigde upisan;
> naše izmene nose `SourceModule='RTM_RULE 2026-10-15'`, `ManualChangeBy` je `NULL`.
> Nijedan probni upis na živoj bazi nije izvršen (svi upisi su isključivo A2/B4 iz zadatka;
> svaka provera je SELECT ili GET).

---

## 1. Tačka 3 — svi pisci koji menjaju `ShiftEntries` i sada automatski sklanjaju WIC

**Rešenje:** jedno centralno mesto, `Backend/GSDContext.cs:113` —
`LinkWicRowsToShiftTypeChanges()`, pozvano iz `SaveChanges` (`GSDContext.cs:77`) i
`SaveChangesAsync` (`GSDContext.cs:87`). Svaki EF Core pisac prolazi kroz `SaveChanges`, pa
nijedan pisac ne može biti izostavljen. Pravilo: čim red `ShiftEntries` **prestane** da bude
`WIC_DUTY` (bilo koji drugi tip), njegovi **on-site** `WicShiftEntries` redovi za taj
`EmployeeId`+`ShiftDate` se u istom `SaveChanges` sklanjaju (`IsOnSite=false`). Istorijski red
i njegov `WorkingShift` ostaju (bezbedno za schedule/izveštaje) — samo prestaje da se računa
kao pokrivenost; `WicPresenceResolver` čita isključivo `IsOnSite=1`.

Deljena definicija: `Backend/Services/WicAttendanceLink.cs` (`LeavesWicDuty`, `StandDownAsync`).

### 1.1 Pisci (fajl:linija) — ulazna tačka → mesto gde nastaje promena tipa

| # | Fajl:linija | Pisac / ruta |
|---|---|---|
| 1 | `Backend/ShiftService.cs:235` (`UpdateShiftAsync`) | **Shift Plan ćelija** — `PATCH /api/shifts/{id}/manual` |
| 2 | `Backend/ShiftService.cs:304` (`AssignShiftAsync`) | Shift Plan nova ćelija + **Bulk RTM** AL/OFF/CD/NIGHT — `POST /api/shifts/assign/manual` |
| 3 | `Backend/ShiftService.cs:375` (`SyncWicDutyFromShiftPlanAsync`), stand-down na `:391` | postojeći Shift-Plan sync (eksplicitni stand-down + WIC upsert) |
| 4 | `Backend/WicShiftService.cs:515` (`CreateShiftAsync`) | + New Shift / „Assign to gap” — `POST /api/wic/shifts` |
| 5 | `Backend/WicShiftService.cs:846` (`CreateAssignmentAsync`) | **Assign Agent** + Bulk RTM WIC — `POST /api/wic/assignments` |
| 6 | `Backend/WicShiftService.cs:1176` (`MoveToGsdAsync`) | move-to-GSD — `POST /api/wic/move-to-gsd` |
| 7 | `Backend/ShiftSyncService.cs:29` (`SyncSickLeaveAsync`) | **Bulk RTM SL** → `POST /api/sickleave` |
| 8 | `Backend/ShiftSyncService.cs:62` (`SyncVacationAsync`) | **Bulk RTM AL** / odsustva → `POST /api/vacations` |
| 9 | `Backend/ShiftSyncService.cs:99` (`RevertSickLeaveAsync`) | revert SL |
| 10 | `Backend/ShiftSyncService.cs:128` (`RevertVacationAsync`) | revert AL/OL |
| 11 | `Backend/ShiftSyncService.cs:189` (`ReconcileFutureVacationsAsync`, start) | startup rekoncilijacija budućih godišnjih |
| 12 | `Backend/RosterService.cs:1119` (`ApplyGenerated`; pozivi `1190`, `1220`, `1254`, `1775`) | Roster generator / undo batch-a |
| 13 | `Backend/RolloutService.cs:508` (kreiranje `:500`) | Rollout dan |
| 14 | `Backend/Modules/SubstitutionModule.cs:112` (kreiranje `:134`) | Substitute accept |
| 15 | `Backend/TrainingService.cs:194` | Training (TRAINING red) |
| 16 | `Backend/WicScheduleService.cs:351` | brisanje WIC_DUTY redova (zatvoreni dan) |

**BO dan** u `ShiftEntries` je `ShiftType='WORKING'` + `RawValue='BO'` (`RosterService.cs:1116`),
pa ga pokriva isti prelaz (WIC_DUTY → WORKING) — bez posebnog koda.
Napomena: `BoEntries` (BO Lista / Bulk RTM BO preko `POST /api/bo-list`) **ne piše**
`ShiftEntries`, pa nije pisac u ovom smislu i nije menjan.

### 1.2 Centar A → centar B

`CreateAssignmentAsync` (`Backend/WicShiftService.cs:929-1030`): novo mesto se upisuje po
`SupportLocation` (`:1005-1030`), a preklapajuća posvećenost na starom centru ide u `toReplace`
(`:1002`) — dakle A se uklanja, B se dodaje u istom `SaveChanges`. Test:
`AssignAgent_CentreA_To_CentreB_MovesTheOnSiteWicRow`.
Shift-Plan ćelija NAMERNO ne sklanja drugi centar (multi-centre pravilo od 2026-10-15,
`WicMultiCentreDayTests`) — ona imenuje jedan od centara dana.

---

## 2. Tačka 4 — postojeći podaci od danas (2026-10-15) nadalje

Kriterijum (kako je zadato): `WicShiftEntries` **on-site** red gde je `ShiftEntries` za isti
`EmployeeId`+`ShiftDate` **ručno promenjen** (`ManualChangeBy='S60028'`) u ne-WIC tip.
`WicShiftEntries` nema kolonu sa vremenom, pa „WIC red je stariji od izmene” važi po
konstrukciji: `CreateAssignmentAsync` bi takav red pretvorio u `WIC_DUTY`, pa ne-WIC
`ShiftEntries` uz WIC red znači da je WIC red nastao pre te izmene.

| Grupa | Broj obrisanih WIC redova |
|---|---:|
| **Ukupno u tački 4** | **29** |
| — on-site, `ManualChangeBy='S60028'`, ne-WIC tip | 19 |
| — **Binod / Pfaffenhofen, novembar** (`EmployeeId 9129428`, 16.–27.11.) | **10** |
| Ostali slučajevi — **samo navedeni** (nisu dirani) | 41 |

19 on-site redova (bez imena): `EmployeeId 9121951` ×4 (19/20/26/27.10.), `9124697` ×9
(20.–30.10.), `9119463` ×1 (29.10.), `9130648` ×5 (02.–06.11.). Binodovih 10 su
`EmployeeId 9129428` (16.,17.,18.,19.,20.,23.,24.,25.,26.,27.11.).
41 „ostalih” su svi `IsOnSite=false`, sa centrom u `SupportLocation`, a `ShiftEntries` im je
AL (13), WORKING (22), OFF (3), SL (3) — spisak: `StaleCentredWic-OtherCases.csv`.
Tu je i `EmployeeId 9129428` za 2026-10-16 (`SL`, WIC red `IsOnSite=false`) i još par sličnih
koji nisu obuhvaćeni zadatim kriterijumom (nisu on-site) — namerno nedirani.

---

## 3. Tačka A2 — Helmstedt 2026-10-16 (hitno)

| | |
|---|---|
| `EmployeeId` | **9129429** |
| `ShiftEntries` `Id 56939` | `ShiftType='HALF_AL'` (HAL), `ShiftStart='08:00'`, `ShiftEnd='13:00'` → radni deo od početka smene do zatvaranja centra; `IsWicDuty=0`; **`ManualChangeBy=NULL`**, `SourceModule='RTM_RULE 2026-10-15'` |
| `WicShiftEntries` `Id 64431` | `SupportLocation='Helmstedt'`, `IsOnSite=1`, `WorkingShift='08:00-13:00'` (= presek radnog dela 08:00–13:00 i radnog vremena centra 08:00–13:00), `Task='WIC'` |
| Merlin (`EmployeeId 9124697`, SL 16.10.) | WIC red za Helmstedt 16.10 (`Id 66645`, `IsOnSite=0`) **obrisan** — SL ima prednost |

Radno vreme centra je pročitano iz živog `WicOpeningHours` reda za taj datum (petak
08:00–13:00); nijedan sat nije pretpostavljen.

### Stanje posle deploya (provera tačke 7)

| Provera | Rezultat |
|---|---|
| `/api/wic/open?date=2026-10-16` — Helmstedt | `COVERED`, `scheduled=1`, `absent=0`, `effective=1/1`, intervali `08:00-13:00` (lokalno **i** tunel) |
| `/api/wic?...location=Helmstedt` 16.10. | jedan red: `9129429`, `IsOnSite=true`, `WorkingShift='08:00-13:00'` → „do 13:00” |
| `/api/wic/cards?date=2026-10-16` — Helmstedt | `COVERED`; `9129429` `ws=08:00-13:00`, `match=FULL 300/300 min` |
| HR export (ShiftPlan, 16.10., lokalno i tunel) | `9129429,...,2026-10-16,HALF_AL,08:00,13:00,Helmstedt,` → pola AL + pola rada |
| Pfaffenhofen, Binodovi BO dani (16.–20. i 23.–27.11.) | `COVERED`, `scheduled=1`, `effective=1/1` na svih 10 dana; u `/api/wic` na tim danima **samo `9135516` (Mehmet)** |
| `/api/wic/conflicts?from=2026-10-15&to=2026-11-30` | **0** (lokalno i tunel); za 16.10. **0** |
| `/health` | **200** lokalno i na `d2jn94qg-5000` |

Napomena (pošteno, bez uljepšavanja): `/api/wic/cards` za Pfaffenhofen 16.11. prikazuje i
`EmployeeId 9129428` preko **signal-2** (`ShiftEntries` je `WORKING`, a `AgentTask` i dalje
nosi ime centra — dokumentovani „flipped ShiftType” obrazac iz `AvailabilityResolver.IsWicDutyAt`,
koji se namerno čita kao WIC duty da centri ne bi lažno izgledali nepokriveni). To je stanje
`ShiftEntries` reda, ne WIC reda; zadatak ga nije ovlastio da menja, pa je **naveden, ne diran**
(tačka 4: „ostale slučajeve samo navedi”). `/api/wic/open`, `/api/wic` i conflicts na tim danima
pokazuju samo `9135516`.

---

## 4. Tačka 1 — HAL dan dozvoljava WIC

- `AvailabilityResolver.BlockingAbsenceTypes` (`Backend/Services/AvailabilityResolver.cs:36`)
  više ne sadrži `HALF_AL`. Pun AL/SL/UL/PH/LPH/OL/OFF/OFF_WEEKEND/RESIGNED i dalje blokira.
- `CreateAssignmentAsync` (`WicShiftService.cs:697-712`, `:840-860`): ako je dan `HALF_AL`,
  red **ostaje** `HALF_AL` (ne prepisuje se u `WIC_DUTY`, vreme se ne dira — to je radni deo), a
  `WicShiftEntries.WorkingShift = WicWorkingShift.Slice(radni deo, prozor centra)` — presek.
- `CreateShiftAsync` (`WicShiftService.cs:511-533`, `:596`) i Shift-Plan sync
  (`ShiftService.cs:375-470`) rade isto za `HALF_AL` (novi parametar `wicDutyRequested`).
- Pokrivenost: `AvailabilityResolver.GetOnSiteWicContribution`
  (`AvailabilityResolver.cs:143-171`) — `HALF_AL` **sa on-site WIC redom** = 1.0 (agent je na
  WIC dužnosti za svoj radni deo, a red je već presečen na preklop sa centrom); bez on-site reda
  ostaje generičkih 0.5. Primenjeno na sve signal-1 čitaoce: `WicShiftService.cs:299`,
  `WicPresenceResolver.cs:138`, `OverviewService.cs:101`, `BackupService.cs:165`.
  Bez toga bi Helmstedt 16.10. sa pola dana bio `0.5 → floor 0 → UNCOVERED`.

---

## 5. Tačka 5 — testovi

Novi fajl `Backend.Tests/HalWicAndWicAttendanceLinkTests.cs` (15 testova):

| Test | Pokriva |
|---|---|
| `HalfAlDay_AssignAgent_IsAllowed_AndTheWicRowIsTheIntersectionOfWorkingPartAndCentre` | HAL + WIC do 13:00 dozvoljeno, `WorkingShift` = presek |
| `HalfAlDay_WorkingPartLongerThanTheCentre_IsCutToTheCentre` | presek (10:00–18:00 ∩ 08:00–13:00 = 10:00–13:00) |
| `FullAlDay_IsStillBlocked_AndNoWicRowIsWritten` | **AL blokira** |
| `HalfAlDay_OpenReportsTheCentreCovered` | `/api/wic/open` = COVERED |
| `HalfAlContribution_IsFullWhenThereIsAnOnSiteRow_AndHalfWithout` | pravilo doprinosa |
| `HalfAlIsNoLongerABlockingAbsenceType` | blokirajuća lista |
| `WicDuty_To_BoWorkingDay_StandsDownTheOnSiteWicRow` | **WIC_DUTY → BO** sklanja WIC red |
| `WicDuty_To_Working_ThroughTheShiftPlanCell_...` | Shift Plan ćelija |
| `WicDuty_To_SickLeave_ThroughTheLeaveSync_...` | Bulk RTM SL |
| `WicDuty_To_Off_ThroughTheAssignRoute_...` | `POST /api/shifts/assign/manual` |
| `WicDuty_To_AnnualLeave_ThroughTheVacationSync_...` | AL |
| `WicDuty_IsNotStoodDownWhenTheWriterKeepsWicDuty` | negativan slučaj |
| `AssignAgent_CentreA_To_CentreB_MovesTheOnSiteWicRow` | **A → B premešta** |
| `LeavesWicDuty_OnlyTrueForTheRealTransition` | granice pravila |
| `StandDownAsync_SparsTheNamedCentre` | helper |

**Cela suita: 371 prošlo, 0 palo, 2 preskočena** (`LiveShiftUnfixAcceptanceTests`,
`LiveManualShiftAcceptanceTests` — live-DB testovi koji i treba da ostanu preskočeni).

---

## 6. Tačka 6 — deploy (ručno, po `documentation/LIVE_DEPLOY_RUNBOOK.md`)

| Stavka | Vrednost |
|---|---|
| Precondition — stari live DLL | `BA3BE6C7EDE55A27B13113C6F698BF86E30E3A204CF8BE2775AEC5CD6B6C2BB5` — proveren **pre** ijednog upisa ✓ |
| Precondition — live | task `GSDDashboard-Backend` = Running; port 5000 = `GSDDashboard.API.exe` PID 20988, roditelj watchdog `powershell.exe` PID 10612; sentinel nije postojao |
| SDK / alat | `dotnet 10.0.204`, `node v24.15.0`, `npm 11.12.1` |
| Preservacija | `Tools/MultiCentre20261014/Update-PreservationCandidate.ps1` — samo Candidate polovina: 9 promenjenih/dodatih, 0 uklonjenih (202 live / 260 candidate) |
| Build | `PS1_19_FinalBuildVerify.ps1` — frontend `tsc -b && vite build`, `dotnet build -c Release` (0 grešaka) |
| Snapshot (binarni rollback) | `C:\ProgramData\PulseRollback\hal-wic-deploy-20261015-162607` — 222 fajla, `hash-manifest.json`, svi hashovi 1:1 prema živim originalima |
| Sentinel | `C:\HealthCheck\DEPLOY_IN_PROGRESS` — kreiran pre stop-a, uklonjen **posle** 82/82 |
| Stop → copy | zaustavljen samo provereni PID 20988; `copy-live.ps1` (appsettings sačuvani; oba static root-a zamenjena); bundle `index-DBHFkHxs.js` nepromenjen |
| **Novi live DLL SHA256** | **`98AB98C526170B9B5DA2E91226208E375B89C47991AC1A7091E9C4D3480868E6`** |
| Novi live EXE SHA256 | `D89953ABE45856DF190D14AB41555609285819EB59610D75265E61A4DCCEA490` |
| **Rute — ručno pokrenut novi build** | **82/82 x200 lokalno I 82/82 x200 na tunelu**, login DEV |
| **Rute — posle preuzimanja porta od watchdog-a** | **82/82 x200 lokalno I 82/82 x200 na tunelu**, login DEV |
| Watchdog posle | task = **Running**; port 5000 drži `GSDDashboard.API.exe` PID **23492**, roditelj watchdog PID 10612; sentinel uklonjen |
| Commit | `security/kiosk-hardening`, **bez push-a** |

Nijedna ruta nije bila ne-200, pa rollback nije pokrenut.

---

## 7. Rollback putanje

| Vrsta | Putanja | Sadržaj |
|---|---|---|
| **Podaci (A2 + B4)** | `C:\ProgramData\PulseRollback\hal-wic-20261015-162300\` | `before.json` (pune before-slike svih diranih redova), **`rollback.sql`** (133 linije; vraća `ShiftEntries #56939` tačno i ponovo ubacuje svih 30 obrisanih WIC redova sa `SET IDENTITY_INSERT`, u jednoj transakciji), `b4-onsite.csv` (19), `b4-binod.csv` (10), `StaleCentredWic-OtherCases.csv` (41) |
| **Binarni (deploy)** | `C:\ProgramData\PulseRollback\hal-wic-deploy-20261015-162607\` | `release\`, `release-wwwroot\`, `wwwroot\`, `backend.json`, `hash-manifest.json` (222 fajla) |
| Priprema podataka (dry run pa apply) | `Tools\HalWic-20261015\Apply-HalWic.ps1` | bez `-Apply` = samo SELECT; sa `-Apply` = backup + `rollback.sql` + transakcioni upis |

Dokazi: `.tmp-halwic-deploy\` (`verify-routes-manual.log`, `verify-routes-watchdog.log`,
`routes-manual.json`, `routes-watchdog.json`, `post-deploy-state.json`, `postcheck.json`,
`postcheck.txt`, `copy-receipt.json`, `hr-export-*.zip/csv`).

---

## 8. Šta je diran kod (fajl → šta)

| Fajl | Promena |
|---|---|
| `Backend/Services/WicAttendanceLink.cs` | **nov** — jedna definicija prelaza WIC_DUTY→nešto drugo i `StandDownAsync` |
| `Backend/GSDContext.cs` | hook u `SaveChanges`/`SaveChangesAsync` (`:113`) |
| `Backend/Services/AvailabilityResolver.cs` | `HALF_AL` van blokirajuće liste; `GetOnSiteWicContribution` |
| `Backend/ShiftService.cs` | Shift Plan: HAL ostaje HAL; stand-down ostaje; `wicDutyRequested` |
| `Backend/WicShiftService.cs` | Assign Agent / +New Shift: HAL handling; `/api/wic/open` signal-1 doprinos |
| `Backend/Services/WicPresenceResolver.cs`, `Backend/OverviewService.cs`, `Backend/BackupService.cs` | signal-1 doprinos kroz isti helper |
| `Backend.Tests/HalWicAndWicAttendanceLinkTests.cs` | **nov** — 15 testova |
| `Tools/HalWic-20261015/**` | apply/postcheck/deploy skripte i ovaj izveštaj |
| `Tools/pulse-preservation-manifest.json` | regenerisana samo Candidate polovina |

Nije menjano: frontend (nijedan korisnički string se ne menja), `BoListService`, `RosterService`
(zadržava zaštitu HAL reda od prepisivanja), baza sem A2/B4, tunnel, watchdog, ACL-ovi.
