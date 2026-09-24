Your previous session timed out after 900s. Part of the work is already on disk - check
RosterService.cs and RosterBatch.cs first and CONTINUE from there. Do not start over, do not
re-investigate. Your plan was approved and is correct.

Already done: RosterBatch.cs LocationCode, and the RosterService.cs record changes
(RosterRequest.LocationCode, preview/generate/batch/location DTOs, the header comment).

Still to do, in this order - report briefly after each step so nothing is lost if you time
out again:
1. finish RosterService.cs: ValidateAsync and BuildPlanAsync for WIC mode, GenerateAsync
   (WIC rows + WicShiftEntry upsert + WicAgentAssignments MAIN row), DeleteBatchAsync cleanup,
   GET /api/roster/locations
2. Program.cs: idempotent ALTER TABLE RosterBatches ADD LocationCode
3. Frontend Roster.tsx: location dropdown, opening days shown read-only, Type column and
   WIC/BO totals in preview, Location column in the batch list
4. frontend build, then copy wwwroot to bin\Release\net8.0\wwwroot

Skip the unit tests - I would rather have the feature finished. Tell me when a backend rebuild
is needed, I run it myself.

Write large file sections in one write rather than many small edits - incremental edits are
what timed out last time.

Do not deploy, do not touch the enforcement flag, C:\HealthCheck, or ports 5016 and 8000.
