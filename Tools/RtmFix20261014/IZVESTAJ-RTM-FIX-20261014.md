# RTM fix 2026-10-14 — izveštaj

Server day / datum izvršenja: **2026-10-14** (backend start 13.10.2026 20:58, `/health` 200 pre i posle).
Baza: `localhost\SQLEXPRESS` / `GSDDashboard` (Integrated Security) — ista baza koju koristi živi
`C:\GSDDashboard\Backend\bin\Release\net8.0\GSDDashboard.API.exe` (PID 27880).

**RTM KID S60028 nije upotrebljen ni u jednom upisu.** On ostaje samo kao zaštitni marker na
redovima koje je RTM ranije menjao. Nijedan test/probni upis nije rađen; svi upisi su
izvršni i pokriveni backup-om i `rollback.sql`.

---

## Rezultat po tačkama

| Tačka | Akcija | Broj redova |
|---|---|---|
| 1 — 9126883 `ShiftEntries` WIC_DUTY/WORKING 15.10.2026–28.02.2027 | UPDATE `ShiftStart/ShiftEnd` | **64** |
| 1 — upareni `WicShiftEntries.WorkingShift` | UPDATE | **0** (presek se ne menja — vidi dole) |
| 1 — redovi sa `ManualChangeBy='S60028'` | **netaknuti**, samo navedeni | **9** |
| 2 — 9125519 | **samo provera, nijedan upis** | 0 |
| 3 — 9047339 s-Hertogenbosch 14.10–30.10.2026 | INSERT `WicShiftEntries` (sync, `IsOnSite=1`) | **13** |

Ukupno upisano: **64 UPDATE + 13 INSERT**, u **dve transakcije** (jedna po tački), 0 guard skipova.

### Tačka 1 — zašto 64, a ne ceo opseg
- 15.10–30.10.2026 (9 dana, svi Mon–Thu): svi nose `ManualChangeBy='S60028'` (`SourceModule='ASSIGN'`)
  → **netaknuti**. Njihova vremena su već tačno `07:30-16:30`, tj. nema šta da se menja.
- 5 petaka (16.10, 23.10, 30.10 … ): `WORKING` redovi **bez** `ManualChangeBy`; nova smena za petak je
  `08:00-17:00`, a oni su **već** `08:00-17:00` → nema upisa (5 redova „already correct").
- 02.11.2026 – 26.02.2027 Mon–Thu: `SourceModule='SHIFT_FIX 2026-10-14'`, `ManualChangeBy` prazan →
  **dozvoljeno za izmenu**, a vrednost je bila `08:00-17:00` → prepravljeno na `07:30-16:30`.
  To je **64 reda**.
- 04.01.2027 i 05.01.2027 (`SourceModule='SHIFT_FIX …'`) → takođe prepravljeni.
- 06.01.2027 (`SourceModule='Roster'`, `ManualChangeBy` prazan, bilo `08:00-16:00`) → prepravljeno.
- Markacija izvršioca na izmenjenim redovima: `ManualChangeBy='S69307'` + `ManualChangeAt` +
  `ManualChangeNote` (isto pravilo koje je koristio `Tools/ShiftMismatchFix`, i nikad `S60028`).
  `SourceModule` je **sačuvan** (nije prepisan).

Po mesecima: 2026-11 **13**, 2026-12 **19**, 2027-01 **16**, 2027-02 **16** = 64.

### Tačka 1 — upareni `WicShiftEntries.WorkingShift`: 0 izmena (objašnjenje)
Pravilo je isto koje već koristi `Tools/ShiftMismatchFix` i `Backend/WicShiftService.cs`:
`WorkingShift = presek(smena, radno vreme centra za taj datum)`, a radno vreme centra se
razrešava kroz `WicHoursResolver` (`WicOpeningHours`, `ExceptionDate` pa `DayOfWeek` + `EffectiveFrom`).

- Centri ovog agenta (Halle, Markkleeberg) u `WicOpeningHours` za Mon–Thu imaju **`08:00-16:00`**
  (petak zatvoreno).
- Presek nove smene `07:30-16:30` sa `08:00-16:00` = **`08:00-16:00`**.
- Toliko (`08:00-16:00`) je već stajalo na **svih 64** uparenih redova → upis bi bio no-op, pa nije ni rađen.

Zato je broj 0, a ne zato što je korak preskočen: izračun je urađen za svaki red i vrednost je
već bila tačna. Petkom centri ne rade i nema uparenog `WicShiftEntries` reda.

### Tačka 3 — šta je upisano
13 dana bez `WicShiftEntries` reda: 14, 15, 16, 19, 20, 21, 22, 23, 26, 27, 28, 29, 30.10.2026.
Za svaki je postojao `ShiftEntries.WIC_DUTY` (`AgentTask='s-Hertogenbosch'`, `LocationId` je NULL).
Umetnuto je po jedan red: `IsOnSite=1`, `IsGSDDay=0`, `IsOffDay=0`, `Task='WIC'`,
`LocationCode='NL~5211 AK~s-Hertogenbosch~Willemsplein 4'`, `DayOfWeek` = stvarni dan,
`WorkingShift = presek(smena, radno vreme centra) = 08:00-17:00` (centar radi Mon–Fri `08:00-17:00`).

Preskočeno: **nijedan dan** — nije bilo AL/SL, nije bilo preklapajućeg on-site reda istog agenta
(nema ga uopšte za te dane), nije bilo zatvorenog centra i nije bilo praznog preseka.
Kontrola za drugog potvrđenog agenta **9074512**: 0 dana sa rupom u istom periodu (on je već
imao s-Hertogenbosch red za svaki dan), pa za njega ništa nije upisivano.

---

## Tačka 2 — lista: 9125519, redovi od 15.10.2026 koji NE odgovaraju pravilu i nisu ručno menjani

Pravilo: `07:30-16:30`, osim kad pokriva **Stade** → `08:00-17:00`.
Provereno 95 `WIC_DUTY` redova ≥ 15.10.2026:

- **18 redova odgovara pravilu** — svi su `AgentTask='Stade'`, `08:00-17:00` (npr. 02.11, 05.11, 09.11, 12.11, 16.11, 19.11, 23.11, 26.11, 30.11, 03.12, 07.12, 10.12, 14.12, 17.12, 21.12, 24.12, 28.12, 31.12.2026).
- **12 redova je ručno menjao RTM (`S60028`)** — 15.10, 16.10, 19.10, 20.10, 21.10, 22.10, 23.10, 26.10, 27.10, 28.10, 29.10, 30.10.2026, svi `Stadland`, `07:30-16:30` (odgovaraju pravilu i zaštićeni su).
- **65 REDOVA NE ODGOVARA PRAVILU I NIJE RUČNO MENJANO** (`ManualChangeBy` prazan,
  `SourceModule='SHIFT_FIX 2026-10-14'`), svi `AgentTask='Stadland'`, svi `08:00-17:00`
  umesto `07:30-16:30`:

**Novembar 2026 (12):** 03, 04, 06, 10, 11, 13, 17, 18, 20, 24, 25, 27 — (`ShiftEntryId` 58024, 58116, 58301, 58482, 58573, 58756, 58939, 59031, 59215, 59397, 59489, 59674)
**Decembar 2026 (13):** 01, 02, 04, 08, 09, 11, 15, 16, 18, 22, 23, 29, 30 — (59862, 59957, 60148, 60332, 60425, 60612, 60797, 60891, 61079, 61261, 61353, 61622, 61712)
**Januar 2027 (20):** 04, 05, 06, 07, 08, 11, 12, 13, 14, 15, 18, 19, 20, 21, 22, 25, 26, 27, 28, 29 — (70068–70087)
**Februar 2027 (20):** 01, 02, 03, 04, 05, 08, 09, 10, 11, 12, 15, 16, 17, 18, 19, 22, 23, 24, 25, 26 — (70088–70107)

12 + 13 + 20 + 20 = **65**.

> Napomena: u tački 2. **nije izmenjen nijedan red** (ni `ShiftEntries`, ni `WicShiftEntries`).
> Ovi redovi su isti oni koje je `SHIFT_FIX 2026-10-14` postavio na `08:00-17:00`; tačka 1 ih je
> ispravila samo za 9126883, jer se tačka 2 izričito traži kao provera.

Puna lista (mašinski čitljivo): `.tmp-rtmfix-20261014\p2-violations.csv`
(uz `p2-conforming.csv` i `p2-manual.csv`).

---

## Verifikacija posle upisa

| Provera | Rezultat |
|---|---|
| Tačka 1 — preostali redovi van cilja u opsegu (ne-zaštićeni) | **0** |
| Tačka 1 — `ManualChangeBy='S60028'` redovi | **9**, vremena nepromenjena |
| Tačka 3 — dana sa WIC_DUTY a bez `WicShiftEntries` reda | **0** |
| Tačka 2 — broj upisa | **0** |
| `GET /health` | **200** ×3 (`{"status":"ok"}`) |
| `GET /api/wic/open?date=2026-10-15` → s-Hertogenbosch `scheduledCount` | **2** (pre: 1) |
| `GET /api/wic/open?date=2026-10-20` → s-Hertogenbosch `scheduledCount` | **3** (pre: 2) |
| Ostali dani tačke 3 (14, 16, 19, 21, 23, 26, 30.10) | `scheduledCount` +1 svuda, `coverageStatus=COVERED`, `absentCount=0` |

---

## Backup i rollback

```
C:\ProgramData\PulseRollback\rtm-fix-20261014-105656\
    ShiftEntries-point1-pre.csv        64 puna reda pre izmene
    WicShiftEntries-point3-pre.csv     svi WicShiftEntries redovi 9047339 u periodu (pre)
    point1-plan.csv, point3-plan.csv   plan
    rollback.sql                       UNDO (NIJE izvršen)
    SHA256SUMS.txt
```

`rollback.sql` vraća tačno ove izmene, u jednoj transakciji:
`DELETE` za 13 umetnutih `WicShiftEntries` redova (guarded po `EmployeeId+ShiftDate+SupportLocation+IsOnSite+WorkingShift`)
i `UPDATE` za 64 `ShiftEntries` reda (guarded po `Id` **i** trenutnoj novoj vrednosti, pa ne može da pregazi
naknadnu ručnu izmenu).

---

## Napomene / odluke koje treba znati

1. **`WorkingShift` = presek, ne prepis smene.** Zato je u tački 1 bilo 0 izmena na `WicShiftEntries`:
   `07:30-16:30` ∩ centar `08:00-16:00` = `08:00-16:00`, a to je već stajalo. Da je traženo da
   `WorkingShift` bude „smena isečena na radno vreme centra" u drugom smislu, rezultat bi bio isti.
2. **Petak.** Nova smena za petak je `08:00-17:00`, ali Halle i Markkleeberg petkom ne rade
   (`IsClosed=1`), i nijedan petak nema upareni `WicShiftEntries` red — zato petkom nema WIC reda.
3. **`LocationId` je NULL** na ovim `ShiftEntries` redovima; naziv centra je u `AgentTask`
   (NVARCHAR(20)). Zato se poklapanje centra radi preko `AgentTask`, ne preko `LocationId`.
4. **Markacija izvršioca.** `ShiftEntries.ManualChangeBy` na 64 izmenjena reda sada je `S69307`
   (konvencija iz `Tools/ShiftMismatchFix`), nikad `S60028`. Time su ti redovi zaštićeni od
   automatike (`GuardManualShifts`) — što je i bio cilj, jer je prethodni `SYSTEM_*` batch
   pregazio upravo ovakve redove.
5. Tokom rada je jednom viđen `scheduledCount` za 19.10.2026 kao `1` pa `2`; ponovljeno merenje
   (2×) i stanje baze pokazuju da su ta dva reda (9074512 + 9132070) stabilno tu. Baza ima
   aktivne pozadinske pisce, pa su sve vrednosti pre/posle merene neposredno pre i posle upisa.
6. Tačka 3 je pokrila samo **9047339**; **9074512** je proveren i nije imao nijednu rupu
   (kontrolni upit: 0). Oba agenta su sada na s-Hertogenbosch za sve dane 14.10–30.10.2026.
