# IZVEŠTAJ — AgentTask: vraćanje tragova privremenih premeštaja (2026-10-15)

> Radni folder: `C:\GSDDashboard_secure` · Live: `C:\GSDDashboard` (Release, port 5000)
> U izveštaju **nema imena** — samo `EmployeeId`. RTM KID **`S60028` nije nigde upisan ni
> korišćen** (nijedan login, nijedan upis). Nijedan probni upis na živoj bazi nije izvršen:
> jedini upis je tačka 1 (vraćanje 19 redova), a svaka provera je `SELECT` ili `GET`.

---

## 1. Tačka 1 — vraćeno 19 redova `AgentTask` (9129428 ostaje očišćen)

Izvor: `C:\ProgramData\PulseRollback\bo-agenttask-20261015-164944\cleared-rows.csv`
(29 očišćenih redova). Vraćeno je **19 redova** koji nisu `EmployeeId 9129428`:

| EmployeeId | Broj redova | Dani |
|---|---:|---|
| `9124697` | 9 | 20.–23. i 26.–30.10.2026 |
| `9135517` | 5 | 22., 23., 26., 27., 28.10.2026 |
| `9121951` | 4 | 19., 20., 26., 27.10.2026 |
| `9119463` | 1 | 29.10.2026 |

**Redovi `9129428` (Pfaffenhofen, 16.–27.11.2026, 10 redova) NISU dirani** — ostaju
`AgentTask = NULL`, jer ih je RTM izričito prebacila u BO.

| # | Id | EmployeeId | Datum | Centar | RawValue | `/api/wic/open` **pre** | `/api/wic/open` **posle** |
|---:|---:|---|---|---|---|---|---|
| 1 | 57009 | 9121951 | 2026-10-19 | Osnabrück | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 2 | 57098 | 9121951 | 2026-10-20 | Münster | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 3 | 57461 | 9121951 | 2026-10-26 | Osnabrück | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 4 | 57553 | 9121951 | 2026-10-27 | Münster | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 5 | 57105 | 9124697 | 2026-10-20 | Helmstedt | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 6 | 57195 | 9124697 | 2026-10-21 | Helmstedt | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 7 | 57285 | 9124697 | 2026-10-22 | Helmstedt | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 8 | 57376 | 9124697 | 2026-10-23 | Helmstedt | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 9 | 57468 | 9124697 | 2026-10-26 | Helmstedt | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 10 | 57560 | 9124697 | 2026-10-27 | Helmstedt | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 11 | 57653 | 9124697 | 2026-10-28 | Helmstedt | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 12 | 57746 | 9124697 | 2026-10-29 | Helmstedt | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 13 | 57838 | 9124697 | 2026-10-30 | Helmstedt | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 14 | 57312 | 9135517 | 2026-10-22 | Neu-Isenburg | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 15 | 57403 | 9135517 | 2026-10-23 | Neu-Isenburg | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 16 | 57495 | 9135517 | 2026-10-26 | Neu-Isenburg | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 17 | 57587 | 9135517 | 2026-10-27 | Neu-Isenburg | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 18 | 57680 | 9135517 | 2026-10-28 | Neu-Isenburg | (prazno) | COVERED (1/1) | COVERED (1/1) |
| 19 | 57736 | 9119463 | 2026-10-29 | Mülheim | **BO** | UNCOVERED (0/0) | UNCOVERED (0/0) |

- `/api/wic/open` čita **signal 1** (`WicShiftEntries.IsOnSite`), a tačka 1 menja samo
  `AgentTask` (signal 2). Zato je rezultat **identičan pre i posle** — centri su pokriveni
  tačno onako kako su bili. 18/19 dana pokriva drugi agent (signal 1: `9133997`, `9129429`,
  `9126880`); Mülheim 29.10. je jedini nepokriven i to je tačno stanje i pre bo-agenttask
  čišćenja (red je BO → po pravilu 1 BO nikad nije WIC dužnost, pa centar nije lažno pokriven).
- `/api/wic/cards` (signal 2) sada ponovo prikazuje vraćenog agenta kao dodatnog čoveka na
  svih 18 ne-BO dana — **to je vraćeni trag privremenog premeštaja**. Red 19 (Mülheim, BO) se
  i dalje ne pojavljuje kao WIC dužnost, jer je BO.
- Dokazi: `coverage-before.json` i `coverage-after.json` (identični `openStatus`/`cardStatus`),
  `.tmp-agenttask-deploy/coverage-after-deploy.json`, `.tmp-agenttask-deploy/db-post-deploy.json`.
- Provereno i posle deploya/restarta: sva 19 redova imaju `AgentTask`, sva 19 i dalje nose
  `ManualChangeBy='S60028'` (marker nije prepisan), a 10 redova `9129428` (16.–27.11.) je i
  dalje `AgentTask=NULL`.
- Upis: **jedna transakcija**, per-row guard (`ShiftType='WORKING'` + `AgentTask IS NULL` +
  `Id`/`EmployeeId`/`ShiftDate`), backup trenutnog stanja **pre** upisa.
  Tool: `Tools/AgentTaskRestore20261015/Restore-AgentTask.ps1` (DryRun pa `-Apply`).

---

## 2. Tačka 2 — izmene koda

Pravilo: **brisanje imena centra iz `AgentTask` dozvoljeno je SAMO kada RTM ručno
(ćelija Shift Plana / Bulk RTM) promeni `WIC_DUTY` → `WORKING` ili BO.**
Skidanje on-site WIC reda pri `WIC_DUTY` → ne-WIC ostaje kako jeste (za svakog pisca).

| Fajl | Izmena |
|---|---|
| `Backend/GSDContext.cs` | novo polje `_manualRtmTypeEdits` + nova interna metoda `RecordManualRtmShiftEdit(row, actor)` (plain manual marker **i** dozvola za brisanje centra). Hook `LinkWicRowsToShiftTypeChanges` sada čisti `AgentTask` samo za redove deklarisane tom metodom i samo na prelazu u `WORKING`/`BO` (`ClearsCentreOnManualRtmTypeEdit`); skidanje on-site reda (`IsOnSite=false`) je nepromenjeno za sve pisce. Obe `SaveChanges` grane čiste novi set. |
| `Backend/Services/WicAttendanceLink.cs` | nova `ClearsCentreOnManualRtmTypeEdit(before, after)` (samo `WIC_DUTY` → `WORKING`/`BO`) + `BackOfficeShiftType`; dokumentacija hook-a proširena. |
| `Backend/ShiftService.cs` | `UpdateShiftAsync` (ćelija Shift Plana, `PATCH /api/shifts/{id}[/manual]`) i `AssignShiftAsync` (nova ćelija + Bulk RTM AL/OFF/CD/NIGHT, `POST /api/shifts/assign[/manual]`) sada zovu `RecordManualRtmShiftEdit` umesto `RecordManualShiftEdit`. |
| `CLAUDE.md` | nova sekcija „Temporary moves and `AgentTask`". |

Ko **ne** briše (zadržavaju plain marker ili nikakav): roster generator, `ShiftSyncService`
(sync/revert odsustva, rekoncilijacija), `RolloutService`, substitution, `WicShiftService`
(Assign Agent / New Shift / move-to-GSD), startup. `WIC_DUTY` centar A → `WIC_DUTY` centar B
nije prelaz van WIC dužnosti (`LeavesWicDuty` je `false`), pa se ništa ne briše.

---

## 3. Tačka 3 — testovi

Novi fajl `Backend.Tests/TemporaryMoveAgentTaskTests.cs` (9 testova):

| Test | Pokriva |
|---|---|
| `RtmShiftPlanCell_WicDutyToWorking_ClearsTheCentreNameAndStandsTheWicRowDown` | RTM ćelija `WIC_DUTY`→`WORKING` briše centar **i** sklanja WIC red |
| `RtmManualEdit_WicDutyToBo_ClearsTheCentreNameAndStandsTheWicRowDown` | isto za BO (`WORKING` + `RawValue="BO"`) |
| `RtmManualEdit_WicDutyToOff_KeepsTheCentreName_ButStillStandsTheWicRowDown` | samo `WORKING`/BO briše |
| `AbsenceSync_WicDutyToSickLeave_KeepsTheCentreName_AndStandsTheWicRowDown` | sync odsustva **ne** briše |
| `AbsenceSync_WicDutyToAnnualLeave_KeepsTheCentreName_AndStandsTheWicRowDown` | sync odsustva **ne** briše |
| `AutomaticWriterShapedChange_WicDutyToBo_KeepsTheCentreName` | automatski pisac (BO oblik) **ne** briše |
| `ManualWritersThatAreNotTheRtmCellOrBulkPaste_KeepTheCentreName` | rollout / WIC pisci (plain marker) **ne** brišu |
| `TemporaryMove_CentreAToBAndBack_KeepsTheCentreNameAndNeverClearsAnything` | A→B→A (TemporaryMove) prolazi |
| `WicDutyToBoForTwoWeeks_ThenAutomaticReturnToTheHomeCentre` | 2 nedelje BO pa automatski povratak u matični centar |

Izmenjen `Backend.Tests/BoAgentTaskWicTests.cs`: teorija koja je tvrdila da hook briše za
**svakog** pisca zamenjena je sa `Hook_AutomaticWriter_LeavingWicDuty_KeepsTheCentreName`
(automatski pisac čuva ime) i `ClearsCentreOnManualRtmTypeEdit_OnlyForWorkingOrBo`
(granice pomoćne funkcije).

**Cela suita: 402 prošlo, 0 palo, 2 preskočena** (`dotnet test`, Release, net8.0) — ista dva
namerna live-DB testa ostaju preskočena (`LiveShiftUnfixAcceptanceTests`,
`LiveManualShiftAcceptanceTests`).

---

## 4. Tačka 4 — deploy po `documentation/LIVE_DEPLOY_RUNBOOK.md`

| Korak | Dokaz |
|---|---|
| Precondition (stari live DLL) | `0D096412ACD10E5EC5AC3346575CBF1719DD7FAEB29203EAE9D34570DF120380` — proveren **pre** ijednog koraka ✓ |
| Snapshot (binarni rollback) | `C:\ProgramData\PulseRollback\agenttask-restore-deploy-20261015-171510` — 221 fajl, `hash-manifest.json`, svi hashovi 1:1 prema živim originalima + `backend.json` |
| Sentinel | `C:\HealthCheck\DEPLOY_IN_PROGRESS` — kreiran pre stop-a, uklonjen **posle** 82/82 |
| Stop | vlasnik porta 5000 bio PID **9884** `GSDDashboard.API.exe` = živi Release exe; zaustavljen samo on |
| Build | radni tree `C:\GSDDashboard_secure`, SDK **10.0.204**; `PS1_19_FinalBuildVerify.ps1 -Mode Build` (preservation: 202 live / 262 candidate) |
| Copy | `copy-live.ps1` — binarni + static u live Release i oba static root-a; `appsettings*.json` sačuvani (hash nepromenjen) |
| **Novi DLL SHA256 (live i kandidat)** | **`AF87B6D70E54C0ED01FED5734BF51FAE763EE914CCE8A977792A5101D4984407`** |
| Static | `index-DBHFkHxs.js` / `index-Ded7oazT.css` (nepromenjeni — nema frontend izmena) |
| **82/82 — ručno pokrenut novi build** | **82/82 x200 lokalno I 82/82 x200 na tunelu**, login 200/DEV (`.tmp-agenttask-deploy/routes-manual.json`) |
| **82/82 — proces koji drži watchdog** | **82/82 x200 lokalno I 82/82 x200 na tunelu** (`.tmp-agenttask-deploy/routes-watchdog.json`) |
| Watchdog posle | task `GSDDashboard-Backend` = **Running**; port 5000 drži `GSDDashboard.API.exe` PID **20936**, roditelj **10612** = `watchdog_gsd_backend.ps1`; sentinel uklonjen; `/health` **200** |
| KID | samo DEV `S69307`. **RTM KID `S60028` nije korišćen.** |
| Commit | `security/kiosk-hardening`, **bez push-a** |

Nijedna ruta nije bila ne-200, pa rollback nije pokrenut.

---

## 5. Tačka 5 — stanje posle deploya (GET, bez upisa)

| Provera | Rezultat |
|---|---|
| Pfaffenhofen, Binodovi BO dani (16.–20. i 23.–27.11.2026) | `COVERED` na svih 10 dana; u `/api/wic/cards` **samo `9135516`**; `9129428` se ne pojavljuje → **PASS** |
| Helmstedt 2026-10-16 | `COVERED` (agent `9129429`) |
| Centri iz tačke 1 | 18/19 dana `COVERED` (1/1) i posle deploya; Mülheim 29.10. `UNCOVERED` i pre i posle — identično stanju pre prethodnog zadatka (BO red, signal-1 bez on-site reda). Trag (signal 2) je vraćen na 18 dana. |
| `9129428` (Pfaffenhofen 16.–27.11.) | i dalje `AgentTask=NULL` (10 redova) |
| `/health` | **200** lokalno i na `d2jn94qg-5000` |
| Receipt | `.tmp-agenttask-deploy/wic-permission-check.json` → `PASS: true` |

---

## 6. Rollback putanje

| Vrsta | Putanja | Sadržaj |
|---|---|---|
| **Podaci (tačka 1)** | `C:\ProgramData\PulseRollback\agenttask-restore-20261015-170513\` | `ShiftEntries-pre.csv` (stanje pre vraćanja), `restored-rows.csv`, **`rollback.sql`** (ponovo čisti `AgentTask` samo dok red nosi tačno vraćenu vrednost; jedna transakcija), `SHA256SUMS.txt`, `REPORT.txt` |
| **Podaci (prethodni zadatak)** | `C:\ProgramData\PulseRollback\bo-agenttask-20261015-164944\` | `cleared-rows.csv` (izvor vraćenih vrednosti), `ShiftEntries-pre.csv`, `rollback.sql` — netaknuto |
| **Binarni (deploy)** | `C:\ProgramData\PulseRollback\agenttask-restore-deploy-20261015-171510\` | `release\`, `release-wwwroot\`, `wwwroot\`, `backend.json`, `hash-manifest.json` (221 fajl) |
| Priprema podataka | `Tools\AgentTaskRestore20261015\Restore-AgentTask.ps1` | bez `-Apply` = samo `SELECT`; sa `-Apply` = backup + `rollback.sql` + jedna transakcija |
| Deploy skripte | `Tools\AgentTaskRestore20261015\Deploy\` | `snapshot-and-stop.ps1`, `copy-live.ps1`, `start-manual.ps1`, `verify-routes.ps1`, `verify-wic-duty.ps1` |

---

## 7. Fajlovi dirani

```
Backend/GSDContext.cs                                        hook: samo ručni RTM WIC_DUTY->WORKING/BO
Backend/Services/WicAttendanceLink.cs                        ClearsCentreOnManualRtmTypeEdit
Backend/ShiftService.cs                                      dva ručna pisca deklarišu RTM type edit
Backend.Tests/BoAgentTaskWicTests.cs                         hook testovi po novom pravilu
Backend.Tests/TemporaryMoveAgentTaskTests.cs                 nov — 9 testova
CLAUDE.md                                                    pravilo zapisano
Tools/AgentTaskRestore20261015/**                            restore tool, capture tool, deploy skripte, ovaj izveštaj
Tools/pulse-preservation-manifest.json                       regenerisana samo Candidate polovina (5 promena)
```

Nije menjano: frontend (nijedan korisnički string), baza sem tačke 1, tunnel, watchdog,
ACL-ovi, `WicShiftEntries` redovi.
