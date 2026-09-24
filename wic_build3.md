CONTINUE. Backend is presumably done - do not redo it. The problem is Roster.tsx only.

Verified state: npx tsc -b gives exactly 3 errors, all "declared but never used":
  line 62   interface Location
  line 160  locationCode / setLocationCode
So your preparation edits landed, but the edits that USE them did not - the locations query,
the dropdown in the form, and locationCode in the request body are all missing.

IMPORTANT: your read_files tool returned STALE content for this file, which is why half your
edits failed to match. You found this yourself. So:
- read the file with PowerShell Get-Content, never read_files
- then write the WHOLE file in one write, not incremental edits

What is still missing in Roster.tsx:
- useQuery for GET /api/roster/locations
- location dropdown in the form, with the opening days shown read-only next to it
- locationCode included in the preview and generate request body
- when a location is chosen, hide the working-days checkboxes - the centre's opening days decide
- Type column (WIC / BO) in the preview table and WIC/BO totals
- Location column in the batch list

When done, run npx tsc -b and paste the output. It must be clean.
Then npx vite build, then copy wwwroot to bin\Release\net8.0\wwwroot with Copy-Item.

Do not deploy, do not touch the enforcement flag, C:\HealthCheck, or ports 5016 and 8000.
