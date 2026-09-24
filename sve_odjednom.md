Build the whole thing in one pass. Do not stop to ask between phases. Only stop if something
fails or if you are about to do something I did not authorise.

WHAT EXISTS (verified, do not re-investigate):
- Roster came from two one-off events: June Excel import, August PATTERN_FILL. Both dead.
  PATTERN_FILL is not in the repo and cannot be reused. Never propose an Excel import.
- ShiftPattern is empty for 101 of ~120 active employees. Nothing consumes it.
- PATCH /api/shifts/{id} edits existing rows only. POST /api/wic/shifts makes one WIC day.
  bulk-rtm is current-date only. There is no way to create a roster.
- 3 active employees with zero shifts: 9139928 Gaber (TL Delia), 3193180 Halim (TL Oliver),
  9123476 Henschel (TL Tobias). 3193178 Samantha Buys has no Employees row at all.
- P37233 Henschel: inactive, no shifts, no KID. Leave it.
- Auth is live with EnforceAuthorization OFF. Do not change that flag.

BUILD ALL OF THIS:

1. Roster creation for RTM
   - pick employee, date range, times / pattern, working days
   - generates WORKING rows, skips weekends and public holidays like the rest of the system
   - traceable SourceModule so a batch can be found and removed
   - PREVIEW before writing: show what would be created, how many rows, first and last date
   - collision rule: never silently overwrite an existing row - state and implement your rule
   - delete-a-batch path so a bad run is reversible
   - RBAC: RTM, TEAM_LEAD, DEV. AGENT never reaches it, enforced server-side.

2. Missing-roster visibility
   - surface active employees with no shifts in the next 14 days, inside the dashboard
   - visible to RTM, TEAM_LEAD, DEV. Not to AGENT.
   - also flag the inverse: shift rows for employees with no active row

3. Create the Employees row for Samantha Buys, 3193178, TL Oliver Schleusen, active.
   Match the column conventions of neighbouring rows. If anything is ambiguous, leave it
   NULL rather than inventing it, and tell me which fields I need to fill in.

4. Give Gaber, Halim and Henschel a roster using the feature you just built - not by hand
   SQL. If the feature cannot do it, the feature is not finished.

5. Docs: document the new feature in documentation/, and fix the RTM ambiguity -
   PROJECT_BLUEPRINT.md calls RTM "Return to Main", auth_rbac.md calls it a role.

TWO MANDATORY STOPS INSIDE THE RUN - these are not optional:
  a) Back up the database and bin\Release\net8.0 BEFORE the first write of any kind.
     Report where the backups are.
  b) Run the PREVIEW for the three rosters and show me the numbers BEFORE inserting.
     If the preview looks wrong to you, stop instead of writing.

RULES:
- deploy only after tests pass, and use PS1_19 with the deploy sentinel - the watchdog race
  is real and you hit it yourself last time
- do not touch the enforcement flag, do not touch C:\HealthCheck beyond the sentinel
- do not touch ports 5016 or 8000
- if the same tool call fails twice, stop and report - do not loop
- no claim of success without the command output that proves it

Final report: what you built, what you wrote to the database, backup locations, test results,
and anything you chose that I might disagree with.
