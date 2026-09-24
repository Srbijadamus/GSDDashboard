Update the documentation to match what the system actually does now. Everything below is
already built, deployed and verified - this task is documentation only, no code changes.

READ the existing files in C:\GSDDashboard\documentation\ first and follow their style and
structure. Update the existing documents where the change belongs; create a new file only
where there is genuinely no home for it.

WHAT CHANGED SINCE THE DOCS WERE LAST WRITTEN:

1. Authentication and roles (documentation/auth_rbac.md exists - verify it is still accurate)
   - KID-only login, no password. AppUserRoles is the login table.
   - Roles: AGENT (own data, read-only), TEAM_LEAD, RTM (both full), DEV.
   - Login resolution order: AppUserRoles -> Employees PrimaryKid/SecondaryKid -> EmployeeId.
   - Auth:EnforceAuthorization is currently FALSE. Document what happens when it is turned on
     and how to turn it on and off safely.
   - Seeded: S69307 DEV, S60028 + Y6525 RTM, six TEAM_LEAD rows.

2. Roster Generator - a new feature, new page under Planning
   - Plain mode: employee, date range, times, working days -> WORKING rows.
   - WIC mode: employee, WIC location, date range -> WIC duty on the centre's open days,
     BO on the other working days, weekends and public holidays skipped.
   - Preview before writing, batch record, delete batch.
   - Overwrite mode: off by default; when on it replaces existing rows but NEVER absences,
     and originals are snapshotted so deleting the batch restores them.
   - Document that this is now the way a new employee or a new WIC agent is set up, and that
     the June Excel import is dead and must never be re-run.

3. Missing-roster warning on the Overview page
   - Active employees with no shifts in the next 14 days, and shifts belonging to people with
     no active employee row. Visible to RTM, TEAM_LEAD, DEV.

4. Deploy sentinel
   - C:\HealthCheck\DEPLOY_IN_PROGRESS pauses the watchdog during a deploy, with a 30-minute
     stale valve. PS1_19 creates and removes it. Document the manual sequence too, since I
     often build by hand: create sentinel, stop the API process, build, remove sentinel.

5. Coverage fix
   - An agent whose ShiftEntry is an absence (AvailabilityResolver.BlockingAbsenceTypes) is
     excluded from the coverage calculation but stays visible on the card, marked absent.
     Previously only the SickLeaves and Vacations tables were checked, so an SL that existed
     only as a ShiftEntry counted as covering.

6. Known issues to record honestly, not hide:
   - 4 RosterServiceTests fail because the EF InMemory provider does not support the
     transactions GenerateAsync uses. Test-environment problem, not an application bug.
   - PROJECT_BLUEPRINT.md defines RTM as "Return to Main" while auth_rbac.md uses RTM as a
     role. Same three letters, two meanings - flag this clearly.
   - check_resigned.ps1 and PS1_70_HardDeleteResigned.ps1 list employees 3193178 and 3193180
     as resigned although they are active. Running the hard-delete script would remove them.
   - Essen - BP1 has MinRequired=3 but two agents, so headcount-based views show PARTIAL.

Do not change any code. Do not deploy. Report which files you changed and what you added.
