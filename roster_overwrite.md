CHANGE: the Roster Generator must be able to overwrite existing entries, not only skip them.

Today it skips every date that already has a ShiftEntry - see the preview "0 rows to create,
8 skipped (existing entries)". That was the original rule and it is now too strict.

Add an "Overwrite existing entries" checkbox to the form, OFF by default, so the current
behaviour stays the default and overwriting is a deliberate choice.

When it is ON:
- existing WORKING, BO, WIC_DUTY and empty rows ARE replaced by the generated row
- absences are NEVER overwritten, even with the checkbox on: AL, HALF_AL, SL, UL, OL, PH,
  LPH, CD, RESIGNED, OFF, OFF_WEEKEND - keep the same blocking list the WIC assignment
  endpoint already uses, do not invent a new one
- the preview must show three numbers: rows to create, rows to REPLACE, rows skipped, and
  list which dates would be replaced and which are protected absences
- deleting the batch must restore what was there before, or if that is not feasible, say so
  clearly in the UI so nobody expects an undo that does not exist

Tell me in your report which approach you took for undo, since replacing rows makes the
existing delete-batch path less simple than it was.

Extend the existing RosterService.cs and Roster.tsx. Build the frontend and copy wwwroot to
bin\Release\net8.0\wwwroot. I will rebuild the backend myself.

Read Roster.tsx with PowerShell Get-Content, not read_files - that tool returned stale content
last time and half your edits failed.

Do not deploy, do not touch the enforcement flag, C:\HealthCheck, or ports 5016 and 8000.
