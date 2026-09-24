PROBLEM: two active employees have zero shift records, and the shift plan cannot show them.

  9139928  Antonios Gaber    TL Delia Panaitescu   SourceSheet GSD_DE   0 shifts
  3193180  Cortneigh Halim   TL Oliver Schleusen   SourceSheet GSD_DE   0 shifts

Verified by me in the live DB. ShiftEntries joins on EmployeeId, so this is not a name
matching problem. Existing ShiftEntries run from 2026-01-01 to 2027-01-08, so the roster
data itself is current - these two were simply never imported.

A third person, "Samantha", is not in Employees at all under that name.

PHASE 1 - INVESTIGATE, READ ONLY, then STOP and report:
1. Find the import that populates ShiftEntries. Which file or sheet does it read, where does
   that file live, when did it last run, and how does it decide which people to include?
2. Specifically: does the shift import filter by SourceSheet, team, engagement, or any other
   criterion that would exclude someone whose Employees.SourceSheet is GSD_DE? Most existing
   ShiftEntries rows have an EMPTY SourceSheet, one sample showed MOVE_TO_GSD - explain what
   that column means and whether it is related.
3. Are 9139928 and 3193180 present in the source file but skipped, or absent from it entirely?
   If you cannot read the source file, say so and tell me exactly where it is so I can look.
4. How many OTHER active employees have zero shift records? List them. I want to know if this
   is two people or a whole category.

PHASE 2 - after I approve:
Propose how to get these people into the shift plan, and propose a check that surfaces this
automatically in future - an active employee with no roster should be visible in the dashboard,
not discovered when a team lead emails me about it.

Do not change any data. Do not deploy. Do not touch the enforcement flag.
