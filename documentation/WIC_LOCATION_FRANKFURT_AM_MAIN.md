# WIC Location — Frankfurt am Main

**Added 2026-11-05.** New WIC location, ready for work from 2026-11-09.

| Field | Value |
|---|---|
| LocationCode | `DE~65929~Frankfurt am Main~Ludwigshafener Straße 4` |
| DisplayName | Frankfurt am Main |
| City / Postal / Country / Bundesland | Frankfurt am Main / 65929 / DE / Hessen |
| Coordinates | 50.110922,8.682127 |
| Operating period | 2026-11-09 → 2027-03-31 (closed from 2027-04-01) |
| Opening hours | Mon–Fri 07:30–16:00; Sat & Sun closed |
| Minimum agents | 2 per working day |
| MAIN agents | 2 (EmployeeId `9135517`, `9140993`) |

## Writes performed (2026-11-05)

| Database | Table | Rows |
|---|---|---|
| GSDDashboard | WicLocations | 1 |
| GSDDashboard | WicOpeningHours | 14 (7 effective 2026-11-09: Mon–Fri open, Sat/Sun closed; 7 effective 2027-04-01: all closed — end of operation) |
| GSDDashboard | WicAgentAssignments | 2 (MAIN) |
| ShiftKioskDB | wic_centers | 1 (id 45) |
| ShiftKioskDB | agents | 1 (EmployeeId `9140993`, id 289) |
| ShiftKioskDB | wic_agent_roles | 2 (MAIN) |

## Shifts (Roster Generator)

Generated via `POST /api/roster/generate` (WIC mode, overwrite=true), RosterBatches **291** and **292**:

- `9135517` — 99 WIC days (2026-11-09 → 2027-03-31)
- `9140993` — 89 WIC days (2026-11-23 → 2027-03-31); the 10 days 2026-11-09..20 remain `TRAINING`

## Backups / rollback

- `C:\PulseOps\backup\20261105-093315_frankfurt_write.sql` / `_rollback.sql` (Pulse)
- `C:\PulseOps\backup\20261105-093315_kiosk_write.sql` / `_rollback.sql` (ShiftKioskDB)
- `C:\PulseOps\backup\20261105-093315_frankfurt_shifts_rollback.sql` (shifts; replaced WORKING rows restore via `DELETE /api/roster/batches/291`)
- `C:\PulseOps\backup\20261105-093315_*_before.csv` (pre-write table snapshots)

## Follow-up

- The second MAIN agent (`9140993`) has no `windows_username` in ShiftKioskDB `agents`; the kiosk client
  resolves identity by Windows username, so it must be set (agent import) before that laptop can check in.
- `9140993` is on `TRAINING` 2026-11-09..20, so the location starts with one rostered agent and reaches
  the two-agent minimum from 2026-11-23 unless the training is revised.
