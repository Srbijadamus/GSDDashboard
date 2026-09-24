Act mode. Backend is DONE - do not modify any backend file.

The endpoints already exist in Backend/RosterService.cs from line 325:
  POST   /api/roster/preview          body: RosterRequest
  POST   /api/roster/generate         body: RosterRequest
  GET    /api/roster/batches
  DELETE /api/roster/batches/{id}

Read lines 320-400 of RosterService.cs to get the exact RosterRequest fields and the response
shapes. Do not guess them.

ONE TASK: finish Frontend/src/pages/Roster.tsx. It ends at line 112 with // __PART2__ and
RosterInner does not exist, so the frontend does not build.

Write RosterInner:
- employee picker, date range, times, working-day checkboxes
- Preview -> rowCount, skippedExisting, skippedHolidays, first and last date
- Generate, enabled only after a preview
- batch list with delete
- errors surfaced to the user, including 403
Use the types, helpers and style tokens already at the top of the file. Do not redefine them.

Write the whole component in ONE file write, not incremental edits - the last two sessions
timed out mid-edit and left the file unfinished.

Do not build, test or deploy. Stop when __PART2__ is gone and RosterInner is defined.
