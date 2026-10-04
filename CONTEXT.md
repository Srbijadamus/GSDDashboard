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
- 90-day, GET /api/wic/coverage-gaps?days=90 (verified 2026-09-30, after both the
  Hamburg fix and the Essen - BP1 fix below): **0 critical / 0 warning / 0
  NOT_RECORDED** across all 42 active locations. Raw: `criticalCount=0,
  warningCount=0, gaps=[], notRecorded=[]`.
  Fuerstenwalde 2026-10-05 is not a gap (Krishnendu Das on-site there; out of scope,
  not this session's work). CONTEXT's old "1 critical (Fuerstenwalde) / 5 warning
  (Hamburg)" expectation was stale; trust the endpoint over this file when they
  disagree. This is a point-in-time snapshot, not a guarantee - the 10-employee
  Nov/Dec roster gap (Open items) means new gaps can reappear at those 3 locations
  any time between now and whenever that roster gap is fixed.
- GET /api/wic/forecast (feeds the Overview Coverage Risk strip + KPI cards) used to
  disagree with coverage-gaps because it lacked the ShiftEntries fallback signal - see
  Done below. First fix attempt (bump 0->1 when covered) was NOT enough: it missed
  PARTIAL locations needing >1 agent (Essen - BP1 min=3, Essenbach min=2) where only
  SOME of the required agents are missing their on-site row. Corrected version verified
  read-only against the real DB (ForecastService makes no writes): Essen - BP1
  2026-10-01 now present=3/min=3 COVERED, Essenbach now present=2/min=2 COVERED - not
  yet confirmed via the live endpoint, pending a second rebuild (the first rebuild only
  had the incomplete 0->1 version).

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

## Done 2026-09-30 (Essen - BP1 fix + IsAvailable correction + /api/roster/missing extension)
- IsAvailable's "no ShiftEntries row" case changed from `return true` (treated as
  available) to `return false` (unknown is not free) - a real behavior change, not
  just a comment fix. Reason: Erdal Coskun (MAIN, Essen - BP1) had zero rows at all
  on 2026-12-24 and was being picked as "available" on a guess; an absence never
  imported can't be ruled out from a missing row. Test:
  RosterServiceTests.GenerateLocationCoverage_NoRecordAtAll_IsTreatedAsUnavailable_NotAssigned.
- After the fix, re-ran the dry run (rolled back) for all 42 active locations, 90
  days: only Essen - BP1 2026-12-24 still needed an assignment, correctly landing on
  Kevin Heynen (BACKUP, free that day) instead of Erdal Coskun.
- Deployed after 3 failed rebuild attempts where the DLL timestamp never actually
  moved (watchdog log showed zero new cycles) - 4th attempt confirmed via DLL mtime
  newer than RosterService.cs's edit time, plus a new watchdog cycle in the log.
- Wrote Essen - BP1 2026-12-24 via the live POST /api/roster/generate-location-coverage:
  Kevin Heynen (9074526, BACKUP), **batch 228**, daysStillGap=0, countMismatch=false.
  His pre-write row (Id 61398, plain WORKING/PATTERN_FILL) was already captured in
  ShiftEntries_bak_coverage90 from the earlier backup. Undo:
  ```sql
  BEGIN TRAN;
  UPDATE se SET se.ShiftType=bak.ShiftType, se.RawValue=bak.RawValue, se.AgentTask=bak.AgentTask,
      se.ShiftStart=bak.ShiftStart, se.ShiftEnd=bak.ShiftEnd, se.IsWicDuty=bak.IsWicDuty,
      se.SourceModule=bak.SourceModule, se.SourceId=bak.SourceId, se.SourceSheet=bak.SourceSheet
  FROM ShiftEntries se JOIN ShiftEntries_bak_coverage90 bak ON bak.Id = se.Id
  WHERE se.Id = 61398;
  DELETE w FROM WicShiftEntries w WHERE w.EmployeeId='9074526' AND w.ShiftDate='2026-12-24' AND w.SupportLocation='Essen - BP1';
  UPDATE RosterBatches SET DeletedAt = SYSUTCDATETIME() WHERE Id = 228;
  COMMIT;
  ```
  Verified via GET /api/wic/coverage-gaps?days=90: critical 1->0, warning 0->0,
  notRecorded 0->0 (see Gap counts).
- GET /api/roster/missing extended: new `dateGapsByEmployee` field (per active
  employee, individual dates in the window with no ShiftEntries row at all - the old
  `missingRoster` field only caught employees with ZERO rows in the whole window and
  missed sparse cases like Erdal Coskun's). `days` clamp raised from 60 to 90. Test:
  RosterServiceTests.GetMissingRoster_ReportsPerEmployeeDateGaps_ForSparseCoverage.
  Raw call at days=90 found 110 employees with >=1 gap date; 100 of those are just
  2026-12-25 (Christmas, a national holiday on a Friday - correctly no row; not a
  bug - 2026-12-26, also a holiday, lands on a Saturday so WEEKEND_FILL covers it
  anyway, independent of the holiday check). The other 10 are the real signal - see
  Open items.

## Done 2026-09-30 (Forecast/Overview false-positive fix + AvailabilityResolver rule change)
- Root cause: the Overview "Coverage Risk" day strip + KPI cards come from a THIRD,
  separate implementation - GET /api/wic/forecast -> ForecastService.cs - sharing zero
  code with WicCoverageGapService/RosterService. It only read on-site WicShiftEntries
  rows, with no fallback to ShiftEntries.ShiftType=WIC_DUTY, so it showed UNCOVERED for
  locations coverage-gaps correctly considered covered.
- Data scale: read-only count for the next 90 days found **521 date+location rows**
  where ShiftEntries has WIC_DUTY but WicShiftEntries has no on-site row, across 36
  employees and 37 of 42 locations. 35 of the 36 distinct RosterBatches are
  CreatedByKid=SYSTEM_WICFIX (Ids 151-192, created 2026-09-30 06:09-06:10, before this
  session) - a systemic gap in that one script across nearly every WIC location, not
  specific to Hamburg/Essen-BP1/Augsburg. Only 1 row traces elsewhere (batch 26,
  CreatedByKid=S60028).
- Fix: ForecastService now reuses WicCoverageGapService.ComputeCoveredDates's building
  block (WicCoverageGapService.MatchesAgentTask, internal, same assembly) to also count
  ShiftEntries-only WIC_DUTY agents as coverage, deduplicated against on-site agents so
  nobody is double-counted. First attempt (bump effectiveCoverage 0->1 when the shared
  boolean said "covered") was insufficient - missed PARTIAL locations needing more than
  1 agent (Essen - BP1 min=3, Essenbach min=2) where only some of the required agents
  lacked an on-site row; corrected to reconstruct the actual headcount from both
  signals. Verified read-only against the real DB (ForecastService makes no writes):
  Essen - BP1 and Essenbach 2026-10-01 both now COVERED at full headcount.
- AvailabilityResolver.GetWicContribution: "no ShiftEntry row" changed from 1.0
  (assumed present - a previously documented invariant, technical.md now updated) to
  0.0 (unknown is not present), same rule as RosterService.IsAvailable. Affects all 5
  real callers - an on-site WicShiftEntries agent with NO paired ShiftEntries row now
  counts as absent/not-present in each, where it previously counted as present:
  - BackupService.cs:157 - such an agent now lands in absentIds/absentNames (shown
    absent in backup planning) instead of counted present.
  - OverviewService.cs:95 - reduces that location's effectiveCoverage; can flip
    COVERED->PARTIAL/UNCOVERED for whatever widget this feeds.
  - WicShiftService.cs:290 (feeds WicCardsService / WICShifts cards) - increments
    fullAbsentCount instead of presentDouble.
  - ForecastService.cs:126 - combined with the fallback fix above.
  - BriefingService.cs:180 and :242 (today's gap list + 3-day at-risk lookahead) - can
    surface new gaps/at-risk days for on-site agents lacking a ShiftEntries row.
  Risk noted, not investigated: this is the mirror-image gap of what was just fixed -
  watch Backup/Overview/Cards/Briefing for new false positives (an agent genuinely
  on-site but whose ShiftEntries row hasn't been imported yet would now wrongly count
  as absent in those 4 places).
- Tests: ForecastServiceTests.Forecast_UsesSharedFallback_WhenOnSiteRowMissingButShiftEntryIsWicDuty,
  AvailabilityResolverTests.GetWicContribution_NoShiftEntry_ReturnsZero_NotPresent. Ran
  RosterServiceTests + these two: same 5 pre-existing unrelated failures, nothing new
  broken.
- Confirmed live after a second rebuild (headcount reconstruction, not just a 0->1
  bump): forecast?horizon=28 and coverage-gaps?days=28 agreed on all 28 dates.

## Done 2026-09-30 (coverage-gaps vs forecast: correctly un-coupled, not merged)
- Correction to the fix above: coverage-gaps and forecast are two DIFFERENT features
  (coverage-gaps: assignment-structure gap classification with an Assign button,
  documented in WicCoverageGapService.cs's header + this file's item 2 under "Done
  2026-09-29/30"; forecast: headcount-vs-MinRequired risk number, documented in
  technical.md "14-day coverage forecast" / "Authoritative for risk forecasting").
  ForecastService calling WicCoverageGapService.MatchesAgentTask/ComputeCoveredDates
  wrongly coupled them. Reverted: ForecastService now has its own private
  MatchesLocationAgentTask, calls nothing on WicCoverageGapService. Same Fuerstenwalde/
  Essen-BP1/Essenbach/Hamburg-missing-row fix, now fully independent. Verified both
  endpoints still agree after the revert.
- Banner bug: Overview.tsx's WicCoverageGapsBanner had `if (data.gaps.length === 0)
  return null`, hiding the whole banner AND the "View 90/14 days" toggle whenever
  there were zero gaps. Fixed: banner always renders - green "No WIC coverage gaps
  (next N days)" line + toggle when clear, red gap list otherwise.
- Text bug: ExceptionList.tsx showed i18n key overview.exceptionList.allCovered ("All
  locations covered for the next {{n}} days") whenever the list filtered to a
  SELECTED day was empty - even while the strip still showed other red/amber days
  unfiltered for other dates. Added a new key `dayCovered` ("No coverage gaps on
  {{date}}") used specifically when a day is selected; `allCovered` now only fires
  when nothing is filtered.
- Frontend rebuilt (npm run build) and verified live on port 5000 same session (no
  backend restart needed for frontend-only changes, Rule 3). Backend rebuilt via the
  normal Rule 7 cycle for the ForecastService.cs revert.
- Data fill: backed up Bishal Maharjan + Oender Arslan's ShiftEntries rows 2026-10-14
  .. 2026-10-27 to ShiftEntries_bak_hamburg_oct (28 rows); confirmed Arslan had no
  existing WicShiftEntries row at Hamburg in that range (nothing extra to back up);
  wrote via the live POST /api/roster/generate-location-coverage: Oender Arslan
  (BACKUP), **batch 239**, 10 days, daysStillGap=0, countMismatch=false. Verified via
  both endpoints: coverage-gaps shows 0 Hamburg rows, forecast shows COVERED/1/1 for
  all 10 dates (2026-10-14/15/16/19/20/21/22/23/26/27).

## Done 2026-09-30 (dayCovered double-escaping fix + Hamburg 10-28..30 fill)
- Frontend: ExceptionList.tsx's new `dayCovered` t() call showed i18next's default
  HTML-escaping literally (e.g. "Thu 01/10" -> "Thu 01&#x2F;10") because React already
  escapes text content itself - double-escaping. Fixed by passing
  `interpolation: { escapeValue: false }` on that specific call. Checked the other new/
  existing interpolated key (`allCovered`'s `{{n}}`) - it's a plain number, never
  affected. No global i18next config change made (scoped fix, as asked). Rebuild
  needed (`cd Frontend && npm run build`, no backend restart) - not run this turn,
  left for the user.
- Read-only 90-day sweep (coverage-gaps?days=90) after the earlier Hamburg 10-14..27
  fill found 4 remaining gaps: Hamburg 2026-10-28/29/30 (Bishal Maharjan's AL block
  runs past the 10-27 boundary the earlier fill stopped at) and Berlin - Bruckenstrasse
  2026-10-13 (UNCOVERED_ASSIGNED - the known standing E.ON arrangement noted in Open
  items, NOT written this session, held back pending a decision since the generator
  would happily roster MAIN Erik Goecks there even though the standing arrangement may
  mean he shouldn't be).
- Backed up all active MAIN/BACKUP agents' ShiftEntries (5412 rows) AND WicShiftEntries
  (6167 rows) for the full 90-day window into ShiftEntries_bak_coverage90_v2 /
  WicShiftEntries_bak_coverage90_v2 (current state, supersedes the staler
  ShiftEntries_bak_coverage90 for anyone touched since).
- Dry-run confirmed (all 42 active locations, rolled back): only Hamburg 10-28/29/30
  (Oender Arslan, BACKUP) and Berlin - Bruckenstrasse 10-13 (Erik Goecks, MAIN) needed
  anything; 0 gaps nobody could fill.
- Wrote ONLY Hamburg via the live endpoint (Berlin - Bruckenstrasse deliberately held
  back): Oender Arslan (BACKUP), **batch 247**, 3 days (2026-10-28/29/30),
  daysStillGap=0, countMismatch=false. Verified via coverage-gaps?days=90:
  criticalCount 3->0, warningCount unchanged at 1 - only Berlin - Bruckenstrasse
  2026-10-13 remains, exactly as expected.

## Done 2026-10-01 (Berlin - Brückenstrasse 2026-10-13 phantom UNSTAFFED - root cause)
- Symptom: coverage-gaps?days=90 reported 0 critical / **1 warning** - 2026-10-13
  UNCOVERED_ASSIGNED at Berlin - Brückenstrasse, the gap row itself showing Erik Goecks
  (MAIN) task='Berlin - Brückenstra'. forecast agreed (present=0/min=1 UNCOVERED). The
  Coverage % KPI read 100.0% at the same time - NOT a contradiction, see below.
- Root cause (data): ShiftEntries **#56615** (Erik Goecks, 2026-10-13) is an internally
  inconsistent row - ShiftType=**WORKING** but RawValue=NULL, ShiftStart/End 08:00-17:00
  and AgentTask='Berlin - Brückenstra' (the 23-char DisplayName cut to the NVARCHAR(20)
  column). Written by **RosterBatch 155** (CreatedByKid=SYSTEM_WICFIX, "WIC Priority
  Fix", 2026-09-30 06:09, overwrite mode: RowCount=0 / ReplacedExisting=74). The other
  14 Tuesdays that same batch wrote at that centre are all WIC_DUTY. The Program.cs
  startup cleanup then zeroed its IsWicDuty flag (that cleanup only ever sets 1->0 where
  ShiftType != WIC_DUTY). Not reachable from current RosterService code - that batch ran
  an earlier uncommitted revision, and the same batch's 2026-10-27+ rows are correct.
- Root cause (code - the part actually fixed): every reader gated the ShiftEntries cover
  signal on `ShiftType == WIC_DUTY` alone, so ONE wrong field on a row that is in every
  other respect a complete WIC-duty row produced a gap nobody could explain from the UI
  (the task cell showed the centre name while the day read UNSTAFFED).
- **The location name was NOT the problem.** 'Berlin - Brückenstra' (20) is a correct
  prefix of 'Berlin - Brückenstrasse' (23) and the truncated-prefix match already handled
  it. Truncation happens in the WRITERS (RosterService.GenerateAsync and
  WicShiftService.CreateAssignmentAsync both write `DisplayName[..20]`), not in the import
  or the UI. No absence, holiday, opening-hours, date-range or timezone issue on that date.
- Fix - one shared definition, replacing four byte-for-byte copies of the matcher:
  - `WicLocationMatcher.MatchesAgentTask` + `AgentTaskMaxLength` (new): the single
    AgentTask-vs-centre rule. Removed the private copies from WicCoverageGapService,
    ForecastService, RosterService.IsProtectedFromOverwrite and WicScheduleService. Also
    now matches LocationCode/LocationCodeLegacy and repairs double-encoding; the prefix
    branch still fires ONLY at the 20-char column limit (that guard is what stops a short
    value from matching an unrelated centre).
  - `AvailabilityResolver.IsWicDutyAt(sh, loc)` + `GetWicContributionAt(isSick, sh, loc)`
    (new): the single "is this row WIC duty here" definition. WIC_DUTY counts; WORKING
    counts when AgentTask names the centre (AgentTask only ever holds a centre name on the
    WIC_DUTY encoding - on a WORKING row it is a task badge or "GSD", never a centre);
    absence/away types NEVER count.
  - `AgentTasks.NonLocationValues` / `IsNonLocationValue` (new): the 7 badges + "GSD",
    i.e. how a reader tells a task badge from a centre name without the WicLocations table.
  - WicCoverageGapService + ForecastService: candidate rows are now selected by AgentTask
    (badges excluded in SQL) and the ShiftType call belongs to IsWicDutyAt. The two stay
    UN-coupled - neither calls the other; they share only the neutral primitives, which is
    what the 2026-09-30 revert was actually about (not sharing a string matcher).
  - RosterService.IsProtectedFromOverwrite: a WORKING row whose AgentTask is not a badge
    is now protected from a regenerate aimed at ANOTHER centre, same as a WIC_DUTY row.
    This is the guard that would have stopped the overwrite run behind #56615.
  - Program.cs: report-only startup warning counting flipped-ShiftType rows. Never
    mutates - ShiftType is roster truth and only an RTM may change it.
- Decision (step 4, per the docs): **"rostered, no task" does NOT count as covered**, for
  BACKUP or anyone. A raw WORKING row only says the agent is clocked in somewhere
  (WicCoverageGapService header; AvailabilityResolver WORKING -> 0.0). Krishnendu Das
  (BACKUP) and Hamyaz Pathan (REGIONAL floater) were already treated that way and are
  unchanged. A WORKING row naming THIS centre is a different shape, not "no task".
- KPI vs banner: all five Overview KPI cards read `lf.todayStatus` - **today only** -
  while the banner and day strip cover the whole horizon. Brückenstrasse is a Tuesday-only
  centre and was CLOSED on 2026-10-01, so 31/31 open locations covered = 100.0% was
  correct. Fixed the label, not the number: `overview.kpi.coveragePct` is now
  "Coverage % Today" / "Belegung % heute", plus a comment in Overview.tsx warning not to
  widen those cards to the horizon without relabelling them.
- Before -> after (verified read-only against the real DB, no deploy):
  coverage-gaps?days=90 criticalCount 0->0, warningCount **1->0**, gaps 1->0,
  notRecorded 0->0; forecast?horizon=28 totalAtRiskDays **1->0**; Berlin - Brückenstrasse
  2026-10-13 **0/1 UNCOVERED -> 1/1 COVERED**.
- Blast radius, measured not assumed: exactly 3 future rows have this shape DB-wide. Erik
  2026-10-13 (gap cleared) and **Merlin Voss 2026-10-01/02 at Helmstedt**, which went
  present 1->2 against min=1 (stays COVERED, no status change). Merlin is the
  corroboration that the shape is real: he carries the same flipped row AND a
  WicShiftEntries IsOnSite=1 row at Helmstedt, and used to be discarded as 0.0 despite
  being on site. Every other future row with a centre name in AgentTask is an AL/SL row
  and is still correctly excluded - that is what keeps the Hamburg AL gaps detectable.
- Tests: `Backend.Tests/WicDutyRecognitionTests.cs`, 19 cases incl. the location-name
  mismatch case (20-char prefix vs 23-char DisplayName), the double-encoded variant, the
  short-value guard, the other-Berlin-centre guard, the flipped row at both the
  ComputeCoveredDates and ForecastService seams, the Merlin/Helmstedt on-site case, and
  the absence-row-with-stale-AgentTask guard in both directions. Full suite: 91 passed /
  **the same 5 pre-existing failures** (4 EF-InMemory transactions + the
  CodePagesEncodingProvider one), nothing new broken.
- Added `InternalsVisibleTo(GSDDashboard.Tests)` to GSDDashboard.API.csproj so
  WicCoverageGapService.ComputeCoveredDates - internal on purpose - is directly testable.
- **Rebuild needed, not done (never deploy):** backend for the logic, frontend
  (`npm run build`) for the KPI label. Row #56615 was deliberately NOT rewritten - the
  readers now interpret it correctly and only an RTM may change a ShiftType. If you want
  the data itself clean, regenerating that location rewrites it (same-centre rows are not
  overwrite-protected).

## Done 2026-10-01 (five-surface coverage disagreement - one shared presence resolver)
- Symptom: banner said "No WIC coverage gaps (next 14 days)", Coverage Risk said "All
  locations covered for the next 28 days", KPIs at-risk 0 / 100% - while **Gaps &
  Recommendations listed 19 open centres at 0/1 agents with a Find Substitute button**
  and the WIC Locations page showed UNCOVERED (19) / COVERED (12). Pre-existing and
  independent of the 2026-10-13 fix.
- Five surfaces, five implementations. Mapping (worth keeping):
  | surface | endpoint | service |
  |---|---|---|
  | Overview banner | /api/wic/coverage-gaps | WicCoverageGapService |
  | Coverage Risk strip + all 5 KPI cards | /api/wic/forecast | ForecastService |
  | Gaps & Recommendations | /api/wic/briefing | BriefingService |
  | WIC Locations page | /api/wic/cards | WicCardsService |
  | (nothing - dead code) | /api/overview/wic-status | OverviewService.GetWicSummaryAsync |
- Root cause: **BriefingService and WicCardsService built presence from on-site
  WicShiftEntries rows ONLY** - no ShiftEntries fallback at all. The cross-check was
  decisive: forecast reported scheduledCount=0 but effectiveCoverage=1 for all 19, i.e.
  all coverage came from the signal those two lacked. Every one of the 19 traced to
  RosterBatches 153-192 (CreatedByKid=SYSTEM_WICFIX) - the already-documented 521-row
  import defect where the ShiftEntries side was written and the WicShiftEntries side
  never was.
- Classification of all 43 active locations on 2026-10-01 (read-only, reconciles exactly
  with the Locations page 12/12/19): **0 real gaps**, 12 closed/not required, 19 "duty
  recorded in ShiftEntries, on-site row missing", 12 covered on both signals.
- Fix (A) `Backend/Services/WicPresenceResolver.cs` - NEW. The single definition of "who
  is covering this location on this date, and how do we know": unions both cover signals,
  deduplicated by employee, returning EffectiveCoverage, CheckInNotRecordedCount and
  `WicCoverageEvidence` = NONE | ON_SITE | ROSTER_ONLY. ForecastService's inline union is
  deleted and all four real surfaces now call it. Coverage-gaps and forecast stay
  UN-coupled (neither calls the other) - they share a neutral resolver, which is what the
  2026-09-30 revert was actually about.
- Fix (B) ROSTER_ONLY is treated as **staffed**: it cannot reach the briefing gap list
  (headcount now >= min, plus an explicit belt-and-braces `continue`), so no Find
  Substitute is ever offered for a centre that is actually rostered; the lookahead
  at-risk scan skips it too, so the Closure Risk KPI stops counting record-keeping as
  risk. On the cards the roster-only agents now also join the agent list AND the
  minute-based coverage maths using their ShiftEntries times - previously the card showed
  UNCOVERED with an empty agent list.
- Fix (C) New card badge, **visible on the card itself** (not a tooltip):
  `wicShifts.checkInNotRecorded.badge` = "Staffed - check-in not recorded" /
  "Besetzt - Check-in nicht erfasst", plus a `.tooltip` key on the title attribute.
  Driven by `WicCardDto2.CheckInNotRecordedCount > 0`; deliberately independent of
  Evidence, so a centre with 2 of 3 agents confirmed still shows the missing check-in
  without its status changing.
- Fix (D) NOT_RECORDED window bug: `hasFutureCover` tested `coveredDates.Any(d > from+7)`
  regardless of whether the centre is even OPEN past that cutoff. Landshut, Mülheim,
  Trier and Wesel are **Thursday-only** - in a 14-day window their only open days are
  day 0 and day 7, and the test is strictly greater - so all four read NOT_RECORDED at
  days=14 despite duty recorded on every open day through 2026-10-29. Confirmed as pure
  window arithmetic: the same call at days=90 reported 0. Now the 7-day rule only applies
  when open days actually exist beyond the cutoff; otherwise it falls back to "any cover
  evidence in the window".
- Fix (E) **marked separately in the code** - BriefingService (both loops) and
  WicCardsService called the 3-arg `WicHoursResolver.Resolve`, which ignores
  `LocationCodeLegacy`: a centre whose opening hours live under the legacy bridge code
  read as closed there (skipped / rendered CLOSED) while ForecastService saw it open.
  Both now use the 4-arg overload every other coverage reader uses. Both also now honour
  the per-day `hours.MinRequired` override instead of only `loc.MinAgentsRequired`.
- Before -> after for 2026-10-01 (verified read-only in-process against the real DB; the
  server on port 5000 was never stopped or restarted):
  | surface | before | after |
  |---|---|---|
  | banner days=14 | 0 gaps, **4 NOT_RECORDED** | 0 gaps, **0 NOT_RECORDED** |
  | banner days=90 | 0 gaps, 0 notRecorded | unchanged |
  | Coverage Risk (28d) | 0 at-risk days | unchanged |
  | KPIs | open 31 / covered 31 / at-risk 0 / 100% | unchanged |
  | Gaps & Recommendations | **19 gaps, each 0/1 + Find Substitute** | **0 gaps** |
  | Locations page | CLOSED 12 / COVERED 12 / **UNCOVERED 19** | CLOSED 12 / COVERED 29 / PARTIAL 2 / **UNCOVERED 0** |
  25 of 31 open cards now carry the check-in badge (19 ROSTER_ONLY + 6 that have an
  on-site agent plus one unrecorded one) - the 521-row defect is that widespread.
- **Deviation from the proposal, deliberate:** the Locations page is COVERED 29 /
  PARTIAL 2, not COVERED 31. Augsburg and Hannover are two-interval centres (07:30-12:00
  + 12:30-16:00 and 09:00-12:00 + 13:00-15:00) whose single rostered agent covers only
  the first interval, so the card's minute-based metric correctly reads 56% / 60%
  PARTIAL. Not a disagreement with the headcount surfaces - a finer metric. Root cause is
  separate and NOT fixed: `RosterService.BuildPlanAsync` writes WIC rows from
  `hours.OpenTime`/`CloseTime` and ignores `OpenTime2`/`CloseTime2`. Five active centres
  have a second interval (Augsburg, Essenbach, Hannover, München, Quickborn); at the
  other three a second agent closes the afternoon block. **Needs an RTM decision** - is
  the afternoon block genuinely unstaffed at Augsburg/Hannover, or should the generator
  write both intervals?
- Tests: `Backend.Tests/WicPresenceUnificationTests.cs`, 11 cases - resolver
  ROSTER_ONLY/ON_SITE/NONE incl. dedup-by-employee and the absence-with-stale-task guard;
  ROSTER_ONLY out of the briefing gap list; a genuinely unstaffed open centre STILL in
  it; cards COVERED + badged + agent listed vs. still UNCOVERED for a real gap;
  once-weekly centre not NOT_RECORDED at days=14 AND a no-duty-at-all centre still
  NOT_RECORDED; legacy-code hours resolving identically in Forecast, Cards and Briefing.
  BriefingService is exercised through a real DI graph over EF InMemory (it needs
  SubstitutionService -> ReachabilityService -> IServiceScopeFactory).
  Full suite: **102 passed / the same 5 pre-existing failures**, 107 total (was 96).
- Note for whoever runs the suite next: a temporary SqlServer-backed harness made
  `Preview_RegionalHoliday_OnlySkippedForThatBundesland` pass, because Microsoft.Data
  .SqlClient registers CodePagesEncodingProvider as a side effect. That is NOT a fix -
  the real bug stands: `Mojibake`'s static FIELD initializer runs `Encoding.GetEncoding(1252)`
  before its static CONSTRUCTOR registers the provider, so the registration can never help
  its own fields. Left alone (outside this change's scope).
- No DB writes. The 521-row import defect is untouched by design.
- **Rebuild needed:** backend (new service + 4 rewired services) AND frontend
  (`npm run build` - new i18n keys + the card badge). Not run - never deploy.

## Done 2026-10-01 (two-interval centres: duty spans the break - RTM rule)
- RTM decision: at a centre with OpenTime2/CloseTime2 the gap between intervals is only a
  break. The generator must write BOTH intervals and the break must never count as a
  coverage hole. Affected: **Augsburg, Essenbach, Hannover, München, Quickborn** (the only
  5 active centres with a second interval).
- **Representation chosen: ONE ShiftEntries row spanning first open -> last close.** Not a
  preference - the schema forecloses two rows: ShiftEntries carries a single
  ShiftStart/ShiftEnd pair and has THREE unique indexes that each cap it at one row per
  agent-day (`UQ_ShiftEntries_EmpDate` on (EmployeeId, ShiftDate), `UQ_ShiftEntries_EmpDateType`,
  `UQ_ShiftEntries_EmpDateSheet`); a second row cannot be inserted at all, and 0 duplicate
  agent+date groups exist today. Two rows would mean dropping a unique index plus auditing
  every `shiftByEmpDate` dictionary and ShiftDuplicateResolver in the codebase. The project
  already used the spanning convention for the mirror-image READ:
  `WicShiftService.SplitWorkingShift` collapses a two-block WorkingShift to
  first-start..last-end.
- New `WicHoursResolver.DutyWindow(h)` - the one definition (first interval's open through
  the LAST interval's close; `(null, null)` when closed/time-less so callers keep their own
  fallback). All three WIC row writers now use it:
  1. `RosterService.BuildPlanAsync` - the WIC PlanRow for an open day (was `hours.CloseTime`).
  2. `WicShiftService` WIC_DUTY upsert, POST /api/wic/shifts (was `hours?.CloseTime`).
  3. `WicShiftService.CreateAssignmentAsync` - **centre-hours fallback only**; the
     request-time and prior-WorkingShift priorities above it are untouched.
  AgentTask 20-char truncation and every overwrite guard are unchanged.
- **Step 2 needed no production change, and that is the interesting part.**
  `CoverageCalculator` already measures required time as the sum of the opening intervals
  only (`CalcOpenMinutes`: Augsburg = 270 + 210 = 480, the 30-min break excluded) and
  credits an agent against each interval separately (`CalcAgentCoverage`, `CoveredByUnion`
  for the pooled figure). The break belongs to neither interval, so a spanning agent scores
  480/480 = 100% FULL/COVERED automatically. Pinned by tests rather than asserted.
- Generator before -> after on 2026-10-01 (read-only PreviewAsync against the real DB,
  Overwrite=true, no writes):
  | centre | hours | was written | now written |
  |---|---|---|---|
  | Augsburg | 07:30-12:00 + 12:30-16:00 | 07:30-**12:00** | 07:30-**16:00** |
  | Hannover | 09:00-12:00 + 13:00-15:00 | 09:00-**12:00** | 09:00-**15:00** |
- Surface agreement for all 5 centres on 2026-10-01 (read-only, after the fix): forecast
  COVERED for all five, 0 briefing gaps, 0 banner gaps/notRecorded. Cards still read
  Augsburg PARTIAL 56% and Hannover PARTIAL 60% **because existing rows were not rewritten**
  - the two axes are both right: one head IS rostered (headcount), and that head's window
  still misses the afternoon block (minutes). Essenbach/München/Quickborn read COVERED 100%
  because a second agent already spans the day there.
- **NO DB writes. No existing row rewritten.** Inventory for the RTM decision on
  regeneration (rows that genuinely UNDER-cover the open minutes - rows that merely use a
  non-canonical window but still span both intervals are excluded, e.g. a München row at
  08:00-17:00, so the number is not overstated):
  | centre | under-covering rows | dated 2026-10-01+ | who |
  |---|---|---|---|
  | Augsburg | 109 | **85** | Kamil Filipowicz 76, Mohamad Nasir Amany 9 |
  | Essenbach | 190 | **135** | John Daniel Wendland 91, Adam Szilvagyi 44 |
  | Hannover | 85 | **61** | Olaf Wittenberg 61 |
  | München | 108 | **81** | Eyup Akyurek 81 |
  | Quickborn | 83 | **64** | Amir Nassri 59, Bishal Maharjan 5 |
  | **TOTAL** | **575** | **426** | running to 2027-02-26 |
  To clean them up: re-run the generator per centre with Overwrite=true (same-centre WIC
  rows are not overwrite-protected, so it rewrites its own rows; absences/TRAINING/VWIC and
  WIC duty at OTHER centres stay protected). Not done - never deploy, never write without
  being asked.
- Tests: `Backend.Tests/WicTwoIntervalCentreTests.cs`, 11 cases - DutyWindow for two
  intervals / single interval / closed+blank; generator output spans both intervals for a
  two-interval centre and is unchanged for a single-interval one; required minutes exclude
  the break; a spanning agent is FULL/COVERED 100%; an agent covering only the first
  interval is still PARTIAL 56% (the regression guard); the same two cases end-to-end
  through WicCardsService; and a test pinning that the headcount axis deliberately does NOT
  distinguish them, so nobody "fixes" the surfaces into agreement by flattening one metric.
  Full suite: **113 passed / the same 5 pre-existing failures**, 118 total (was 107).
- Rebuild needed: **backend only** (3 writers + WicHoursResolver). No frontend change -
  nothing in the UI or i18n was touched this time.

## Done 2026-10-01 (WIC Attendance check-in header: one definition, invariant by construction)
- Symptom: header read **39 Expected / 37 Checked in / 15 Not yet in** (37 + 15 = 52), the
  Not Checked In table listed 8+ people at 63-123 min delay, and Augsburg showed
  "Currently Present 3" against "Agents 1 / Min. 1".
- Root cause: **the three numbers were computed inline in WicAttendance.tsx from two
  different populations.** "Checked in" was `|Set(employee_id)|` over the ENTIRE kiosk
  population (ShiftKioskDB covers the whole GSD workforce, not just WIC agents), with no
  intersection against Expected; Expected/Not-yet-in came from the WIC roster
  (/api/wic/cards). Verified split at 09:08: 24 expected-and-in + 15 expected-not-in = 39,
  plus 12 checked in who were never expected, plus **one kiosk row with a blank
  employee_id** counted as a 37th "person" - which is exactly how 37 arose.
- NOT the WicPresenceResolver class of bug: both backend endpoints this page uses already
  go through the resolver and agree. No backend change was needed or made. But it IS
  connected - 24 of the 39 Expected were ROSTER_ONLY agents who only appear in
  cards.assignedAgents because of the 2026-10-01 WicCardsService change earlier the same
  day; before that Expected would have been ~15 and Not-yet-in ~4. That change made the
  existing arithmetic bug much more visible.
- Fix - the definition now lives in **`Frontend/src/pages/attendance/checkinStatus.ts`**
  (pure, React-free, unit-tested):
  ```
  Expected  = rostered for WIC duty today at an OPEN centre (dedup by employeeId;
              absence markers AND "GSD" excluded)
  CheckedIn = Expected ∩ {kiosk ACTIVE|DONE}
  NotYetIn  = Expected \ {kiosk ACTIVE|DONE}
            => Expected = CheckedIn + NotYetIn, always, by construction
  ```
  - A fourth clickable figure for every other kiosk check-in, outside the identity, never
    folded into CheckedIn. (Originally shipped as "In Elsewhere"; renamed the same day to
    **"Other staff checked in"** - see the correction entry below.)
  - Blank `employee_id` kiosk rows excluded from every count, surfaced once as a
    data-quality note (currently 1).
  - **"GSD"** added to the absence-marker set: an agent moved to GSD backlog is at work
    but cannot check in at a WIC, so counting them Expected guaranteed a permanent
    shortfall - and because "GSD" is not HH:MM they were also dropped from the
    Not-Checked-In table, i.e. in Expected and in no other bucket.
  - Not Checked In is built from the same `notYetIn` set as the header (table and figure
    can no longer disagree), and every row lands in exactly one panel instead of being
    dropped when shiftStart is not a time.
  - `delayMinutes` now measured in the **centre's** timezone (`centreTimeZone(country)`,
    DE->Europe/Berlin, NL->Europe/Amsterdam) instead of the viewer's browser.
  - **Removed `kioskActiveAt()`**, which matched a kiosk record to a centre with
    `location.includes(displayName)`. BLUEPRINT_SCREENS.md §6 requires matching by
    `employee_id`, "never by location name" - this was a documented-invariant violation.
    `buildPresencePair()` matches by employee_id first and consults the free-text location
    only to attribute someone NOT rostered at the centre, then on EXACT tokens so the
    multi-value "Augsburg | Landshut" form attributes to both centres and to nothing else.
  - Per centre: **"Present (kiosk) N · Rostered M"** with the delta flagged both ways.
    Augsburg: Present (kiosk) 3 · Rostered 1, "2 present, not rostered here" (Sina
    Sidharthan, Mohamad Nasir Amany - both have plain WORKING/PATTERN_FILL rows and no
    WIC duty anywhere today). **For RTM: either the roster is missing them or they are on
    site informally.**
  - ROSTER_ONLY agents keep their real delay and carry only the factual "check-in not
    recorded" badge. They are never labelled an artefact anywhere in the UI.
- Before -> after (real data, 09:27 snapshot, computed by running the COMPILED module):
  | figure | before | after |
  |---|---|---|
  | Expected | 39 | **38** (Merlin Voss, shiftStart=GSD, removed) |
  | Checked in | 37 | **23** (now Expected ∩ kiosk) |
  | Not yet in | 15 | 15 |
  | Other staff checked in | - | **13** (new) |
  | kiosk rows w/o employee_id | - | **1** (new note, excluded) |
  | identity | 37 + 15 = 52 vs 39 **BROKEN** | 23 + 15 = 38 = 38 **holds** |
  Not Checked In: 15 rows == Not-yet-in 15; main 15 / noRecord 0 / upcoming 0.
- Tests: `Frontend/src/pages/attendance/checkinStatus.test.ts`, **19 cases**, run via
  `npm run test:attendance`. No JS test runner is installed in this project, so the script
  compiles with the already-present `tsc` and runs on plain `node` - no new dependency, no
  npm install, works offline. Pins: the invariant (incl. the exact 39/37/15 shape), blank
  employee_id, "Augsburg | Landshut", exact-token vs substring matching, GSD excluded,
  closed centres excluded, dedup across cards, the other-staff figure, centre-timezone
  delay, midnight handling, and that Zone B drops no row.
  `tsconfig.app.json` now excludes `src/**/*.test.ts` so the node-only test file cannot
  break `npm run build`; `Frontend/.attendance-test-out/` is gitignored.
- Backend untouched this turn. `tsc -b` clean; eslint on the two files: 0 new problems
  (16 pre-existing `no-explicit-any` / `set-state-in-effect` in untouched code).
- No DB writes. Server on port 5000 never stopped or restarted; no Release build, no
  wwwroot copy.
- **Rebuild needed: frontend only (`npm run build`).** Note the 09:16 deploy predates all
  of this turn's edits (09:22-09:27), so the live bundle contains none of it - it is one
  iteration behind, not mixed.

## Done 2026-10-01 (correction: no "elsewhere" concept; kiosk work-mode badge instead)
- RTM correction: **the "checked in elsewhere / In Elsewhere" concept is removed entirely.**
  It does not exist in the kiosk and confused users. The kiosk's free-text `location` must
  never be compared with WIC centres to classify anyone.
- What the agent actually chooses at check-in is the **work mode**, and the field is
  **`status_log.work_location`**. `GET /api/attendance` already returns it - ShiftKiosk
  `server.py` line ~417: `ISNULL(sl.work_location,'') AS work_location` - the dashboard's
  `KioskRecord` type simply never declared it. **No kiosk/server change was needed.**
  Note `vw_daily_attendance` does NOT expose it; `db_get_attendance` builds its own query.
- Distinct `work_location` values in use (read-only census): **`WIC`** (286 all-time, 29 on
  2026-10-01) and **`HOME_OFFICE`** (227 / 9). Blank only on OFF/AUTO_LOGOUT/EMERGENCY
  events. **`HOME_OFFICE` is what the kiosk UI labels "Virtual WIC Support"** /
  "Virtueller WIC-Support" (`server.py` `loc_home_office`, which even carries the comment
  `// stored value: HOME_OFFICE`). The dashboard now uses the kiosk's own wording so RTM
  reads the same words in both tools. There is no third value - "Virtual WIC Support" is a
  label, not a separate stored mode.
- Changes:
  - `workModeOf(record)` + `WORK_MODE_LABEL_KEY` in checkinStatus.ts; returns null for a
    blank or unrecognised value so a future kiosk option shows NO badge rather than a
    wrong one.
  - `<WorkModeBadge>` rendered next to every checked-in person: Checked In drawer,
    Currently Present chips, and the Other-staff list.
  - 4th figure renamed to **"Other staff checked in"** / **"Weitere Mitarbeitende
    eingecheckt"**; drawer lists name, kiosk location, check-in time and the work-mode
    badge. (That change also replaced the drawer's location subtitle with the employee id
    - wrong, corrected the same day: see the entry below.)
  - **`kioskNamesCentre` / `kioskLocationTokens` deleted.** `buildPresencePair` matches by
    `employee_id` ONLY. Consequence, and it is the intended one: Sina Sidharthan and
    Mohamad Nasir Amany no longer appear under Augsburg's Currently Present (they are not
    rostered there); they appear in Other staff checked in with their work-mode badge.
    Augsburg therefore reads Present (kiosk) 1 - Rostered 1. The "present, not rostered
    here" delta badge is gone - it was only computable by comparing locations.
  - Every "elsewhere" string removed from UI, i18n keys, identifiers and comments. Verified:
    `grep -ri elsewhere Frontend/src` returns nothing.
  - Unchanged: Expected = Checked in + Not yet in, the blank-employee_id exclusion note,
    "GSD" excluded from Expected, Zone B driven off the same Expected set, centre-timezone
    delays.
- Tests: **21 passed** (`npm run test:attendance`) - the invariant, blank employee_id,
  work-mode reading incl. case/whitespace tolerance and the null-not-a-guess rule, the
  label-key mapping, the badge travelling with every checked-in person in all three
  places, presence matched by employee_id only (the Augsburg case pinned explicitly), and
  a blank-id row never counted as present.
- Frontend only. No DB writes, server on port 5000 untouched, no build or wwwroot copy run.
  **Rebuild needed: frontend (`npm run build`).**

## Done 2026-10-01 (correction: kiosk location is shown again, id is secondary)
- RTM correction: removing the "elsewhere" classification was right, but I also removed
  the kiosk **location text from the display** and put the employee id in its place. That
  was over-reach - only the *classifying* use had to go. **RTM needs to SEE the location.**
- Now in all three places (Checked in drawer, Currently Present chips, Other staff checked
  in list): name + work-mode badge on the first line; the kiosk `location` text **verbatim**
  underneath (e.g. "Hannover | Emmerthal", "Landshut | Essenbach"), with the employee id
  after it as a small muted secondary item. Never the id instead of the location; a missing
  kiosk row renders a dash, not the id.
- **The location stays display-only.** It is not parsed, split, normalised, compared with a
  WIC centre, matched, counted or used to flag anyone. All matching and counting remains by
  `employee_id` (buildPresencePair / buildCheckinStatus) - that part of the previous change
  was correct and is unchanged.
- To make the display contract testable rather than asserted about markup, the row builders
  moved out of the page into checkinStatus.ts: **`displayRowForAgent()`** and
  **`displayRowForKioskRecord()`**, both returning `CheckinDisplayRow` (name, employeeId,
  location verbatim, workMode, times, status). All three UI sites use them, so they cannot
  drift apart again.
- Tests: **26 passed** (was 21). Five new: a roster agent's row carries the location
  verbatim incl. the multi-value form; an other-staff row does too; the location is never
  blanked in favour of the id (the exact regression); a missing kiosk row leaves location
  null and still shows the id and no badge; and "showing the location does not make it
  count for anything" - Sina Sidharthan is displayed with "Augsburg | Landshut" yet is
  still not counted present at Augsburg nor expected.
- Also fixed while in there: `checkinDuration()` took this page's narrow status union, which
  blocked reuse of the shared row type - widened to `string` (it only ever compares "DONE").
- Frontend only. No DB writes, server on port 5000 untouched, no build or wwwroot copy run.
  **Rebuild needed: frontend (`npm run build`).**

## Done 2026-10-01 (manual-change logic: exception days, ad-hoc presence, protection)
- RTM principle implemented system-wide: **a manual change is a legitimate decision, never
  an error and never a gap.** Full spec in BLUEPRINT_LOGIC.md "Manual changes".
- Phase 0 found two facts that changed the plan:
  1. **No exception-day mechanism existed.** WicOpeningHours is keyed by DayOfWeek and
     EffectiveFrom means "from this date onward", so an exception-open centre read CLOSED
     everywhere. CONTEXT only ever named the Brueckenstrasse exception list as an OPEN ITEM.
  2. **Erik Goecks has a WIC_DUTY row today with AgentTask = NULL** (SourceSheet=ROSTER,
     AutoGenerated=1). He IS on WIC duty; there is simply no centre link, and the kiosk API
     omits status_log.wic_center_id. Hence the "no centre assigned" list rather than a guess.
  3. Zero rows carried SourceSheet/SourceModule='MANUAL', so existing fields could not
     distinguish manual from automatic.
- **Schema added (additive, nullable, idempotent COL_LENGTH guards in Program.cs; no
  backfill, no existing row rewritten):** ShiftEntries.ManualChangeBy/ManualChangeAt/
  ManualChangeNote; WicOpeningHours.ExceptionDate/ManualChangeBy/ManualChangeAt.
- Protection verified and fixed: roster Overwrite (IsProtectedFromOverwrite now returns
  true for any manual row - ProtectedAbsenceTypes did not cover a hand-edited WORKING/BO
  row), the startup IsWicDuty cleanup, and the flipped-ShiftType warning. All three now
  skip manual rows. Automatic rows are still replaced normally (tested both ways).
- Header before -> after for 2026-10-01: Expected 38 -> 38, Checked in 25 -> 25, Not yet in
  13 -> 13, **Other staff 14 -> 10**, plus the new **"In, no centre" = 4** (Erik Goecks
  among them). Invariant 25 + 13 = 38 holds.
- **Brueckenstrasse is STILL CLOSED today** and that is correct: the code now supports
  exception days, but no exception row exists for 2026-10-01 and writing data was not
  permitted. See RTM open items.
- Tests: Backend.Tests/WicManualChangeTests.cs (9 cases: exception opens one date only,
  does not leak to next week, can also close a normally-open day, both resolver overloads,
  exception day open in Forecast, exception day with nobody is STILL a real gap, manual
  duty raises no check-in warning, manual row survives Overwrite, automatic row still
  replaced). Affected backend tests: **50/50 pass**. Frontend: **30/30**
  (npm run test:attendance) incl. ad-hoc WIC check-in population, no-guess-from-location,
  manual exempt from the warning, manual provenance reaches the label.
- Caught during implementation: selecting the cards' roster-only agents by
  CheckInNotRecorded silently dropped MANUAL agents from the coverage maths once manual
  rows became exempt from that flag. Now selected by "rostered here, no on-site row".
- Rebuild needed: **backend AND frontend**. The DDL runs on backend startup.

## Done 2026-10-01 (RTM final: three-tile header, everyone-at-work-is-expected, test account out)
- RTM decisions implemented verbatim. Spec now in BLUEPRINT_SCREENS.md §6.
  CheckedIn = ALL distinct kiosk ACTIVE/DONE ids (any work mode, any location, roster or
  not; blank employee_id still excluded + the one-line note). Expected = today's WIC roster
  at open centres PLUS every checked-in person not in that set. NotYetIn = Expected minus
  CheckedIn. The identity is now UNCONDITIONAL: every checked-in person is added to
  Expected, so CheckedIn can never exceed it - pinned by "the invariant holds for every mix"
  (incl. someone marked AL on the roster who checks in anyway).
- Header reduced to **three tiles**; the "In, no centre" and "Other staff" tiles are gone.
  Under Checked in: "on WIC roster X - not on roster Y". Off-roster WIC check-ins with no
  centre link keep the Assign to centre action and are NOT flagged as a problem.
- `rosterExpected` added as a separate field: the per-centre views and the Not-Checked-In
  table use it, because an off-roster check-in has no rostered shift to be late for.
- Test account **9074466** (DEMO / Stojnic Nebojsa) excluded everywhere via ONE named list
  per half: `TEST_EMPLOYEE_IDS` (checkinStatus.ts) and `TestAccounts.ExcludedEmployeeIds`
  (Backend/Services/TestAccounts.cs). Applied in ForecastService, WicCardsService,
  BriefingService, WicCoverageGapService, WicPresenceResolver and the attendance module.
- Before -> after for 2026-10-01 (compiled module against live data):
  Expected 38 -> **52**, Checked in 24 -> **38**, Not yet in 14 -> **14**; the two extra
  tiles (4 + 11) are gone. Breakdown onRoster 24 / offRoster 14, needsAssign 4, blankId 1.
  Invariant 38 + 14 = 52 holds; test agent absent from every list.
- Tests: frontend **35/35** (5 new: CheckedIn ignores work mode and roster, no-work-mode
  person counted, invariant for every mix, test agent excluded everywhere, Zone B roster
  only). Affected backend tests **50/50**. One fixture used 9074466 as a filler id and had
  to change - worth remembering when writing new fixtures.
- Rebuild needed: backend AND frontend.

## Backups / undo
Tables: ShiftEntries_bak_2027ext, _bak_wicfix, _bak_backupfix, PublicHolidays_bak_mojibake,
  ShiftEntries_bak_hamburgfix (Oender Arslan, Hamburg, 2026-12-15/16/17/22/23),
  ShiftEntries_bak_coverage90 (all MAIN/BACKUP agents, 2026-09-30..2026-12-28, 5412 rows,
  62 employees - also covers Kevin Heynen's Essen - BP1 pre-write row, Id 61398),
  ShiftEntries_bak_hamburg_oct (Bishal Maharjan + Oender Arslan, Hamburg,
  2026-10-14..2026-10-27, 28 rows),
  ShiftEntries_bak_coverage90_v2 + WicShiftEntries_bak_coverage90_v2 (all active
  MAIN/BACKUP agents, current state as of 2026-09-30 ~15:00, 2026-09-30..2026-12-28,
  5412 + 6167 rows - supersedes the staler _bak_coverage90 for anyone touched since)
Roster batches: 40-150 (2027 extension), 151-193 (MAIN fix), 194-223 (BACKUP fix, SYSTEM_BACKUPFIX),
  225 (Hamburg fix Dec, HAMBURGFIX), 228 (Essen - BP1 fix, Kevin Heynen), 239 (Hamburg
  fix Oct 14-27, Oender Arslan, S69307), 247 (Hamburg fix Oct 28-30, Oender Arslan,
  S69307) — 224, 226, 227, and others in the low-230s/240s are harmless identity gaps
  from rolled-back diagnostic/dry-run transactions, no batch rows exist there.
  Berlin - Bruckenstrasse 2026-10-13 deliberately NOT written (standing E.ON
  arrangement, needs a decision first - see Open items).

## Open items
- FOR RTM (Silvija/Yiting) TO PLAN: 10 active employees have no regular weekday
  roster for most of Nov/Dec 2026 (confirmed via GET /api/roster/missing?days=90,
  dateGapsByEmployee) - Mark Bachmann (38 missing dates, MAIN Essen - BP1), Erdal
  Coskun (41, MAIN Essen - BP1), Adam Szilvagyi (34, MAIN Essenbach), Mitchel
  Sullivan (41), Samantha Buys (41), Cortneigh Halim (41), Pascal Dutz (44, on
  SickLeave nearly all year, still active), plus 3 pre-existing cases unrelated to
  WIC (Antonios Gaber 62, Patrick Henschel 64, DEMO Test Agent 64 - the last is a
  test account, probably ignorable). Only fixed the one concrete WIC consequence
  this session (Essen - BP1 2026-12-24, see Done); the other ~44 missing weekdays
  across these 3 WIC MAINs are NOT fixed and can surface as new coverage-gaps
  endpoint hits at Essen - BP1/Essenbach any time before someone rosters them.
  Root cause is data (weekday coverage source - SourceModule=ASSIGN or a manual
  RosterBatch - stops after 2026-10-30 for these employees, not a code bug); this
  needs an RTM decision (re-run whatever normally rosters them, or confirm some are
  actually leaving/on long-term leave), not another code fix. Checked against the
  separate "7 skipped in 2027 extension" list (below) - NOT the same people, only 3
  overlap (Mitchel Sullivan, Cortneigh Halim, Pascal Dutz); no log/file records that
  original list, reconstructed from RosterBatches 40-150 + absence volume.
- FOR RTM, needs a data action (code is ready, I may not write data): to make
  Berlin - Brueckenstrasse open on Thu 2026-10-01 (or any exception day), insert ONE
  WicOpeningHours row with ExceptionDate=<date>, IsClosed=0, OpenTime/CloseTime and
  ManualChangeBy/At. Until then every surface correctly shows it CLOSED.
- FOR RTM / kiosk owner: GET /api/attendance does not return status_log.wic_center_id,
  although the kiosk stores it. That is the only reliable agent->centre link for an ad-hoc
  check-in. Adding it to db_get_attendance's SELECT would let the 4 "In, no centre" agents
  be counted at their actual centre automatically instead of needing a manual assign.
- FOR RTM: the hand-edited rows (e.g. Kamil Filipowicz, Augsburg) are not marked manual -
  the marker is new and nothing was backfilled by design. They will carry the "Manual"
  label only once a human edits them again through a path that sets ManualChangeBy. Decide
  whether any historical rows should be marked.
- [CLOSED 2026-10-01] Augsburg + Hannover two-interval hours: RTM ruled the gap is only a
  break. All three WIC row writers now span both intervals via WicHoursResolver.DutyWindow
  and the break is never a coverage hole. Code side done - see Done 2026-10-01
  (two-interval centres). **Still open as a DATA decision:** 426 existing under-covering
  rows dated 2026-10-01+ across the 5 centres (Augsburg 85, Essenbach 135, Hannover 61,
  München 81, Quickborn 64) were deliberately NOT rewritten; regenerate per centre with
  Overwrite=true when you want them corrected.
- Mojibake static-init order bug: the static field initializer calls
  Encoding.GetEncoding(1252) before the static constructor registers
  CodePagesEncodingProvider, so Preview_RegionalHoliday_OnlySkippedForThatBundesland
  fails unless something else registered the provider first. One-line fix, not done.
- OverviewService.GetWicSummaryAsync / GET /api/overview/wic-status is dead code - no
  frontend caller. Left alone deliberately; delete or wire it, do not migrate it.
- FOR RTM: Augsburg 2026-10-01 had Sina Sidharthan and Mohamad Nasir Amany kiosk-ACTIVE
  on site with NO WIC duty rostered anywhere that day (plain WORKING/PATTERN_FILL rows).
  The attendance page now flags this as "2 present, not rostered here". Decide whether
  the roster is missing them or they were on site informally.
- Auth:EnforceAuthorization -> true after verifying S69307 login in a private window
- 24 employees have no Bundesland (message to Yiting drafted, not confirmed sent)
- 7 employees skipped in the 2027 extension (long absence during both reference weeks)
- Abdulrahman Aldera is MAIN for Stadland AND Stade, hours overlap Mon+Thu, 43 days - needs a decision
- Berlin - Brueckenstrasse: the 2026-10-13 gap is resolved as a FALSE POSITIVE (see Done
  2026-10-01) - a flipped ShiftType, not the standing arrangement. Whether the standing
  E.ON arrangement means Erik Goecks should be rostered there at all is STILL open and
  still needs an RTM decision / exception list; no coverage was written for that date.
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
