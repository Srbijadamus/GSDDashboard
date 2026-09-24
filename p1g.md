Wiring is confirmed done. Now build and test only - NO deploy in this session.

1. dotnet build -c Release - report errors and warnings
2. frontend build - Roster.tsx was assembled from two chunks, so check TypeScript errors
   carefully
3. run the unit tests including RosterServiceTests, paste the output

That is all. Do not deploy, do not restart anything, do not touch the database, the
enforcement flag, C:\HealthCheck, or ports 5016 and 8000.

If a build fails, report the errors and stop - do not fix them in this session.
