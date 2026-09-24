Roster.tsx is finished - 401 lines, RosterInner defined at line 128. Now build, test, deploy.

1. dotnet build -c Release - 0 errors, 0 warnings
2. frontend build - Roster.tsx was assembled from two chunks, so check TypeScript errors
   carefully
3. run the unit tests including RosterServiceTests, paste the output
4. add the Roster page to the router and the nav, visible to RTM, TEAM_LEAD and DEV only.
   The page exists but nothing links to it yet.
5. verify server-side enforcement: an AGENT session calling /api/roster/preview and
   /api/roster/generate must get 403. Show the actual responses.
6. back up the database before anything writes to it
7. deploy with PS1_19 and the deploy sentinel

Do not touch the enforcement flag, C:\HealthCheck beyond the sentinel, or ports 5016 and 8000.
If a tool call fails twice, stop. No success claims without the output that proves it.
