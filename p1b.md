Your previous session timed out. Everything is still on disk - RosterService.cs, RosterBatch.cs,
RosterServiceTests.cs, PS1_RosterBackup.ps1, and changes in Program.cs and GSDContext.cs.
Check git diff first, then CONTINUE. Do not start over, do not re-read the whole project.

WHAT IS CLEARLY MISSING: the frontend. Frontend/src/pages/ has only AgentHome.tsx and Login.tsx,
so there is no UI for RTM to actually use the roster feature.

Finish:
1. dotnet build -c Release - 0 errors, 0 warnings
2. run the unit tests, paste the output
3. confirm the RBAC rule is in place: RTM, TEAM_LEAD, DEV allowed, AGENT 403 server-side
4. build the frontend page: pick employee, date range, times, working days, PREVIEW showing
   row count and first/last date, then Generate. Add it to the nav for RTM/TEAM_LEAD/DEV only.
   Include the delete-a-batch path so a bad run can be undone.
5. back up the database before any write to it
6. deploy with PS1_19 and the deploy sentinel

Do not touch the enforcement flag, C:\HealthCheck beyond the sentinel, ports 5016 and 8000.
If a tool call fails twice, stop. No success claims without the output that proves it.

Report what is done and what is left.
