BUG in the AGENT "My View" page: the "My upcoming shifts" card shows
"No upcoming shifts found" although the data exists.

Proof from the live DB (I ran this myself just now):
  SELECT COUNT(*) Total,
         SUM(CASE WHEN ShiftDate >= CAST(GETDATE() AS date) THEN 1 ELSE 0 END) Future,
         MAX(ShiftDate) LastDate
  FROM ShiftEntries WHERE EmployeeId='9074341'
  -> Total 321, Future 73, LastDate 2026-12-31
Sample rows: 2026-12-31, 2026-12-30, 2026-12-29 ... all ShiftType=WORKING,
ShiftStart 06:00, ShiftEnd 15:00, IsWicDuty=False.
Tested as agent Anas Daba, EmployeeId 9074341, logged in through /login on the devtunnel.

Note the real column name is ShiftDate (not Date) - I got that wrong first myself.

FIND THE CAUSE, do not guess. Likely candidates to check explicitly:
- the endpoint filters on the wrong identifier: Employees.Id vs Employees.EmployeeId.
  This project has been bitten by that mismatch before.
- a date comparison between date and datetime that silently matches nothing
- a narrow window (e.g. next 7 days) that misses a roster written further ahead

Report which one it is, with the exact file and line, BEFORE changing anything.
Then fix it and prove the fix by showing the API response for employee 9074341.

Also: while the enforcement flag is OFF the backend does not filter by session, so make
sure your fix works for the enforced case too - the card must show the LOGGED-IN agent's
shifts, never shifts chosen by a URL parameter.

Do not deploy. Do not touch the enforcement flag. Stop after the fix is proven locally.
