# GSDDashboard / WorkForce Pulse - context for next session
Updated: 2026-09-30

## System
- C:\GSDDashboard - ASP.NET Core 8 backend (port 5000) + React/Vite frontend
- SQL Server Express localhost\SQLEXPRESS, database GSDDashboard
- Public: https://d2jn94qg-5000.euw.devtunnels.ms (devtunnel "gsd-dashboard-v2")
- Separate Shift Kiosk: Python/FastAPI port 8000, database ShiftKioskDB,
  tunnel "shift-kiosk" = https://ssr7tm2l-8000.euw.devtunnels.ms
  Kiosk client on agent PCs: C:\ShiftKiosk\kiosk\main_kiosk.py, scheduled task "ShiftKiosk"
- Backend runs as scheduled task GSDDashboard-Backend; deploy sentinel C:\HealthCheck\DEPLOY_IN_PROGRESS
- Rebuild: stop task -> dotnet build -c Release -> robocopy wwwroot to bin\Release\net8.0\wwwroot -> start task
  (npm cp step fails on Windows; server needs ~25s to answer /health after start)

## Auth
- KID-only login, no password. Roles: AGENT (own data, read-only), TEAM_LEAD, RTM, DEV
- RTM: S60028 Silvija Angelova, Y6525 Yiting Qiang. DEV: S69307 (Nebojsa, primary; S69367 secondary)
- Verified working: S69307 -> DEV, S60028 -> RTM, invalid KID -> 401
- Auth:EnforceAuthorization is still FALSE - backend blocks nobody except the new
  coverage-gaps endpoint. Turn on after browser verification.

## Done 2026-09-29/30
1. Vacation-vs-roster root cause: ShiftSyncService.ReconcileFutureVacationsAsync() +
   startup call in Program.cs. Imported leave never reached ShiftEntries; 82 days / 14 people fixed.
2. WIC coverage gaps feature:
   - Backend/WicCoverageGapService.cs -> GET /api/wic/coverage-gaps?days= (default 14), RTM/TEAM_LEAD/DEV only
   - Overview.tsx banner: red UNCOVERED_ABSENCE, amber UNCOVERED_ASSIGNED, collapsed NOT_RECORDED
   - Each row shows assigned agents with real task labels (WIC <centre>, BO, VWIC, Training, GSD,
     "GSD (available)" for PATTERN_FILL rows with no task) + available REGIONAL floaters
   - Assign button opens existing NewShiftModal prefilled, goes through ShiftValidationService
   - WICShifts/index.jsx got ?date= deep link
3. Roster priority rule built into RosterService (permanent):
   absence > WIC duty at own open centre > Training/VWIC > BO on closed days > GSD
   MAIN first, then BACKUP in order - backup now auto-fills when MAIN unavailable.
   One agent may hold two non-overlapping WIC duties same day (Demmin pair).
4. Roster extended 2027-01-01 to 2027-02-28 (4402 rows). 2027 holidays seeded
   (Neujahr national, Hl. Drei Koenige BW/BY/ST only).
5. Mojibake fix: PublicHolidays.Bundesland had Baden-WÃ¼rttemberg / ThÃ¼ringen (10 rows, 2 years).
   Backend/Services/Mojibake.cs + PublicHolidayResolver.cs; matching now normalises both sides.
6. Validation: R4 Sunday and R7 public holiday are warnings, never blocks. Weekly hours net of breaks.
   No auto CD for holiday work - stays manual by design.

## Gap counts
- 90-day (verified via GET /api/wic/coverage-gaps?days=90 on 2026-09-30 after the
  Hamburg fix below): 1 critical / 0 warning. Remaining: Essen - BP1 2026-12-24
  (Angelika Weber BACKUP is on CD; unrelated, not investigated this session).
  Fuerstenwalde 2026-10-05 is no longer a gap (Krishnendu Das now on-site there;
  out of scope, not this session's work) and Hamburg 2026-12-15/16/17/22/23 is fixed
  (see Done below) — CONTEXT's old "1 critical (Fuerstenwalde) / 5 warning (Hamburg)"
  expectation was stale; trust the endpoint over this file when they disagree.

## Shift Kiosk incident 2026-09-30 - SOLVED
Agent e26842 on DESKTOP-SGJVCI9 could not reach the kiosk backend. Worked for all 31 other agents.
Cause: that machine was dual-homed (Ethernet 192.168.1.97 + Wi-Fi 192.168.1.119, same subnet).
The Ethernet path silently dropped HTTPS to devtunnels.ms - TCP and TLS completed, HTTP never answered.
Disabling Wi-Fi did not help; disabling Ethernet did - HTTP 200 immediately over Wi-Fi.
Fix: keep Ethernet disabled on that machine. If a cable is plugged back in, the fault returns.
Note: Zscaler intercepts TLS for everyone (issuer "EON Internal TLS Zscaler CA 2026") - that is normal,
not the cause. Do not raise a Zscaler ticket for this.

## Done 2026-09-30 (Hamburg BACKUP-walk fix)
- Root cause: RosterService.GenerateLocationCoverageAsync (the MAIN/BACKUP walk) was
  never wired to any endpoint or caller — a one-off script (CreatedByKid=SYSTEM_BACKUPFIX,
  RosterBatches 194-223) had run an earlier, uncommitted revision of this same method
  directly against an EARLIER build and produced zero result for Hamburg (and Hannover);
  the fix already present in today's source was never re-run. Wired
  POST /api/roster/generate-location-coverage (RosterService.cs, RosterEndpointMapper)
  so it's reachable from the running app going forward.
- Also fixed: GenerateLocationCoverageAsync and WicCoverageGapService each had their own
  copy of "is this date covered" (drift risk = the exact "run said 1, endpoint said 6"
  failure mode). Extracted WicCoverageGapService.ComputeCoveredDates/GetOpenDates as the
  single shared definition; GenerateLocationCoverageAsync now also calls
  WicCoverageGapService.GetStillGapCountAsync after writing and reports THAT number
  (logs a loud mismatch warning if it ever disagrees with its own tally).
- Data: backed up Oender Arslan's 5 pre-fix ShiftEntries rows to
  ShiftEntries_bak_hamburgfix (Ids 60819/60913/61007/61283/61375), then ran the fixed
  generator for Hamburg 2026-12-15..2026-12-23 (batch 225, CreatedByKid=HAMBURGFIX).
  Verified via GET /api/wic/coverage-gaps?days=90: Hamburg no longer listed.
- Test: RosterServiceTests.GenerateLocationCoverage_AssignsFreeBackup_WhenMainIsUnavailable
  (Backend.Tests/RosterServiceTests.cs) — reproduces MAIN-unavailable/BACKUP-free and
  asserts the BACKUP gets assigned; confirmed it fails if the overlap-unavailability
  branch in IsAvailable is broken.
- Note: RosterBatches identity counter now shows a small gap around 224 (a rolled-back
  diagnostic transaction consumed it) — SQL Server IDENTITY gaps after rollback are
  normal and harmless, no action needed.
- Pending: the new endpoint is only in source, not yet built into the deployed
  wwwroot/bin — a rebuild is needed before /api/roster/generate-location-coverage is
  reachable on the tunnel. Not done this session (never deploy without asking).

## Backups / undo
Tables: ShiftEntries_bak_2027ext, _bak_wicfix, _bak_backupfix, PublicHolidays_bak_mojibake,
  ShiftEntries_bak_hamburgfix (Oender Arslan, Hamburg, 2026-12-15/16/17/22/23)
Roster batches: 40-150 (2027 extension), 151-193 (MAIN fix), 194-223 (BACKUP fix, SYSTEM_BACKUPFIX),
  225 (Hamburg fix, HAMBURGFIX) — 224 is a harmless identity gap, no batch row exists there

## Open items
- Essen - BP1 2026-12-24: real UNCOVERED_ABSENCE gap, confirmed pre-existing (not caused
  by the coverage-gaps refactor). MAIN Holger Kuhlmann and Mark Bachmann both on AL,
  BACKUP Angelika Weber on CD; BACKUP Kevin Heynen is free (plain WORKING, no task) that
  day and could be manually assigned via the Assign button - nobody has auto-generated
  it yet.
- Auth:EnforceAuthorization -> true after verifying S69307 login in a private window
- 24 employees have no Bundesland (message to Yiting drafted, not confirmed sent)
- 7 employees skipped in the 2027 extension (long absence during both reference weeks)
- Abdulrahman Aldera is MAIN for Stadland AND Stade, hours overlap Mon+Thu, 43 days - needs a decision
- Berlin - Brueckenstrasse: standing E.ON arrangement, not a real gap; needs an exception list
- Pascal Dutz and Rene Raoul Aboikoni have no Azure account - deactivate if confirmed gone
- 6 past AL-on-public-holiday cases await team-lead confirmation
- 5 RosterServiceTests fail (pre-existing, test-only, neither caused by nor fixed this
  session): 4 on EF InMemory not supporting transactions (Generate_CreatesWorkingRows_
  TaggedWithBatch, Generate_ExistingRow_IsSkipped_NeverOverwritten, DeleteBatch_
  RemovesOwnedRows_SoftDeletesBatch, DeleteBatch_DetachedRows_AreLeftUntouched), plus 1
  new one found this session: Preview_RegionalHoliday_OnlySkippedForThatBundesland throws
  because Mojibake.cs calls Encoding.GetEncoding(1252) and the test project never
  registers CodePagesEncodingProvider — needs Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
  somewhere test-reachable (or a reference to System.Text.Encoding.CodePages + registration
  in a test startup fixture).
- GitHub PAT still embedded in the git remote - not revoked
- Devtunnels run as manual processes; should be scheduled tasks so they survive reboot

## How to work with me
- Deliver everything as copy-paste PowerShell blocks. I do not type.
- Read the existing code and documentation before changing anything.
- Fix the cause, never just the data.
- Do not hallucinate. If a tool call fails twice, stop and report.
- Verify against real output, not against your own counters. Show me evidence.
- No git stash in this repo - use git show HEAD:path.
- Never deploy. Tell me when a rebuild is needed and I run it.
- Do not touch C:\HealthCheck or ports 5016 and 8000.
- WIC is the core of this project, not a side feature.
