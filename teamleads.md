TEAM LEAD KIDs - seed these into AppUserRoles as TEAM_LEAD, IsActive=1, EmployeeId NULL.

  J51904   Jaroslaw Brzeszkiewicz
  I15078   Ion Ciuceanu
  T31561   Tobias Rossberg
  O7164    Oliver Schleusen
  K33261   Karlo Coric
  M73784   Delia Maria Panaitescu

Update the six placeholder rows that already exist with empty Kid and IsActive=0 - do not
create duplicates.

TWO THINGS TO VERIFY AND REPORT BEFORE WRITING:
1. Delia: the DB has "Delia Panaitescu", I gave "Delia Maria Panaitescu", and her KID starts
   with M, not D or P - unlike every other lead. Confirm which placeholder row you are
   updating and that the name matches the TeamLeadName values used in Employees. Do not
   silently attach this KID to a row you had to guess at.
2. O7164 has 4 digits while the others have 5. Flag it, do not "correct" it - just tell me so
   I can double-check, then use it exactly as given.

Then:
- verify no KID collides with an existing Employees.PrimaryKid or SecondaryKid, and no two
  AppUserRoles rows share a KID
- show me the resulting AppUserRoles table in full: Kid, Role, DisplayName, EmployeeId, IsActive
- confirm the DEV row for S69307 and both RTM rows are still intact and active

Do NOT turn on the enforcement flag. Do not deploy. Stop after showing me the table.
