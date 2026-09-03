# WIC Assignment Conflicts — OVERLAP type

**Generated:** 2026-09-03  
**Period:** 2026-06-01 to 2026-09-30  
**Filter:** `GET /api/wic/conflicts?from=2026-06-01&to=2026-09-30` (default: OVERLAP + CLOSED_LOCATION only)  
**Type shown:** OVERLAP (13 entries) — hours of two or more assigned locations overlap on the same day

---

## Summary

| Conflict | Count |
|----------|-------|
| OVERLAP (real conflicts) | 13 |
| CLOSED_LOCATION (agent on-site, location closed) | 1 — Angelika Weber 2026-06-09, Wesel (excluded from table below; separate record) |
| SPLIT_SHIFT (Demmin dual-location, intentional) | 14 — hidden by default, use `includeSplitShifts=true` to see |

Agents involved: Angelika Weber (3), Kaan Arslan (5), Kavinraj Pathmanathan (1), Burak Kurtulmaz (2), Aakash Som (1), Erik Goecks (1)

---

## Conflicts table

| # | Date | Agent | Location | Hours | Role | EntryId |
|---|------|-------|----------|-------|------|---------|
| 1 | 2026-06-09 | Angelika Weber | Arnsberg | 10:00–14:00 | REGIONAL | 60690 |
|   |            |                | Dortmund | 08:00–16:30 | BACKUP | 60691 |
|   |            |                | Essen - BP1 | 09:00–16:30 | BACKUP | 60692 |
|   |            |                | Essen - TK | 08:00–16:00 | BACKUP | 60693 |
| 2 | 2026-06-22 | Kaan Arslan | Essen - BP1 | 09:00–16:30 | NONE | 53461 |
|   |            |             | Essen - TK | 08:00–16:00 | MAIN | 60730 |
| 3 | 2026-06-22 | Kavinraj Pathmanathan | Essen - TK | 08:00–16:00 | BACKUP | 53885 |
|   |            |                       | Essen - BP1 | 09:00–16:30 | BACKUP | 60735 |
| 4 | 2026-06-23 | Burak Kurtulmaz | Emmerthal | 08:30–15:30 | NONE | 48798 |
|   |            |                 | Saffig | 08:30–16:00 | MAIN | 60709 |
| 5 | 2026-06-23 | Kaan Arslan | Essen - BP1 | 09:00–16:30 | NONE | 53462 |
|   |            |             | Essen - TK | 08:00–16:00 | MAIN | 60731 |
| 6 | 2026-06-24 | Kaan Arslan | Essen - BP1 | 09:00–16:30 | NONE | 53463 |
|   |            |             | Essen - TK | 08:00–16:00 | MAIN | 60732 |
| 7 | 2026-06-25 | Kaan Arslan | Essen - BP1 | 09:00–16:30 | NONE | 53464 |
|   |            |             | Essen - TK | 08:00–16:00 | MAIN | 60733 |
| 8 | 2026-06-26 | Kaan Arslan | Essen - BP1 | 09:00–12:00 | NONE | 53465 |
|   |            |             | Essen - TK | 08:00–16:00 | MAIN | 60734 |
| 9 | 2026-06-30 | Burak Kurtulmaz | Emmerthal | 08:30–15:30 | NONE | 48805 |
|   |            |                 | Saffig | 08:30–16:00 | MAIN | 61081 |
| 10 | 2026-07-28 | Angelika Weber | Grafenrheinfeld | 07:30–16:00 | BACKUP | 47773 |
|    |            |                | Arnsberg | 10:00–14:00 | REGIONAL | 60938 |
| 11 | 2026-07-30 | Angelika Weber | Grafenrheinfeld | 07:30–16:00 | BACKUP | 47775 |
|    |            |                | Wesel | 08:00–12:00 | MAIN | 60940 |
| 12 | 2026-07-31 | Aakash Som | Brokdorf | 07:00–13:00 | NONE | 46504 |
|    |            |            | Salzgitter | 08:00–13:00 | BACKUP | 60782 |
| 13 | 2026-08-11 | Erik Goecks | Berlin - Brückenstrasse | 09:00–17:00 | MAIN | 62771 |
|    |            |             | Potsdam | 08:00–12:00 | NONE | 63183 |

---

## Notes

- **Do not set `IsOnSite=0` automatically.** The detector does not know which location entry is correct. Manual review required per entry.
- **Kaan Arslan (rows 2, 5–8):** Essen-TK is their MAIN; Essen-BP1 has no assignment (NONE) on five consecutive days (2026-06-22–26). The Essen-BP1 entries (ids 53461–53465) are likely erroneous imports.
- **Burak Kurtulmaz (rows 4, 9):** MAIN at Saffig; Emmerthal NONE — two separate dates. The Emmerthal entries (ids 48798, 48805) are likely erroneous imports.
- **Angelika Weber (rows 1, 10, 11):** MAIN at Wesel; appears at multiple BACKUP/REGIONAL locations on three separate days. The extra location entries from the June 2026 import period are the likely source.
- **Aakash Som (row 12):** BACKUP at Salzgitter; Brokdorf NONE — Brokdorf entry (id 46504) is likely erroneous.
- **Erik Goecks (row 13):** MAIN at Berlin-Brückenstrasse; Potsdam NONE — the Potsdam entry (id 63183) is likely erroneous.
- **EntryId** is the `WicShiftEntries.Id` column — use `PATCH /api/wic/shifts/{id}` to correct `IsOnSite` or `SupportLocation` after manual review.
