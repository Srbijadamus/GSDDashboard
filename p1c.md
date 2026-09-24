ONE TASK ONLY: finish Frontend/src/pages/Roster.tsx.

Verified state: the file is 4290 bytes, ends at line 112 with the marker // __PART2__.
Line 110 returns <RosterInner /> but that component does not exist, so the frontend does
not build.

Write RosterInner and remove the __PART2__ marker. It needs:
- employee picker, date range, times, working-day checkboxes
- Preview button -> shows rowCount, skippedExisting, skippedHolidays, first and last date
- Generate button, enabled only after a preview
- list of existing batches with a delete action
- errors surfaced to the user, including 403

Use the types, helpers and style tokens already defined at the top of the file - do not
redefine them.

Do not touch any other file. Do not build, do not test, do not deploy in this session.
Finish the file, confirm __PART2__ is gone and RosterInner is defined, then stop.
