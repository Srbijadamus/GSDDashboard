BUG: an agent on sick leave still counts in the WIC coverage calculation, which drags a
location down to PARTIAL or UNCOVERED even when another agent covers the whole day.

Evidence from the live DB, 2026-09-24:
  Brokdorf    -> UNCOVERED, yet Jannik Borner has WIC_DUTY 07:15-16:15 (full opening hours).
                 Sam Alisha Metzner is SL that day and is still being counted.
  Essen - BP1 -> PARTIAL, yet Kuhlmann AND Bachmann both have WIC_DUTY 09:00-16:30.
                 Erdal Coskun is SL and is still being counted.
  Potsdam     -> PARTIAL, yet Hamyaz Pathan has WIC_DUTY 07:30-16:30.
                 Dennis Markus is SL and is still being counted.
In each case the sick agent has a WicShiftEntries row, and the matching ShiftEntries row has
ShiftType = SL.

REQUIRED BEHAVIOUR: an agent whose ShiftEntry for that date is an absence must be excluded
from the coverage calculation entirely - not counted as covering, and not dragging the status
down. Use the same absence list the WIC assignment endpoint already treats as blocking
(AL, HALF_AL, SL, UL, PH, LPH, OL, OFF, OFF_WEEKEND, RESIGNED) - do not invent a new list.

The agent should still be VISIBLE on the card, marked as absent, so RTM can see that a person
is out and arrange a substitute. Only the coverage maths changes.

READ documentation/ first, then find where coverage status is computed. Report which file and
which method decides Covered / Partial / Uncovered, and how it currently handles absences,
BEFORE changing anything.

Then fix it, build the frontend if the frontend is involved, and copy wwwroot to
bin\Release\net8.0\wwwroot. Tell me if a backend rebuild is needed - I run that myself.

Verify with those three locations on 2026-09-24: all three must become Covered.
Also check Wesel on the same date - both agents there work only 08:00-12:00, so if that
location opens longer, PARTIAL is correct there and must stay.

Read files with PowerShell Get-Content, not read_files - that tool has returned stale content.
Do not deploy, do not touch the enforcement flag, C:\HealthCheck, or ports 5016 and 8000.
