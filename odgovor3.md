PS1_AuthVerify.ps1 OUTPUT - key facts, all confirmed:
- Employees has PrimaryKid and SecondaryKid, both nullable. No auth tables exist.
- 126 employees, 120 active. 68 rows have NULL PrimaryKid, 63 of them ACTIVE.
- No duplicate KIDs anywhere (queries 4 and 4b both empty).
- Query 5 EMPTY: S60028, Y6525 and S69307 exist in NEITHER PrimaryKid NOR SecondaryKid.
- Query 6: all six team leads have NULL employee id and NULL KID - they exist only as
  text in Employees.TeamLeadName, they are not employee rows.

DECISIONS - implement exactly this:

1. AppUserRoles is the LOGIN table, as in odgovor2.md. Non-employees (me, RTM, team leads)
   live there with EmployeeId NULL. Do NOT create fake Employees rows for anyone, and do
   NOT write any KID into the Employees table - WicCoverageImport upserts it at startup.

2. 63 ACTIVE AGENTS HAVE NO KID. Login resolution becomes, in this order:
   a) AppUserRoles, IsActive=1, Kid non-empty -> that role (EmployeeId may be NULL)
   b) Employees active, PrimaryKid or SecondaryKid exact match -> AGENT
   c) Employees active, EmployeeId exact match -> AGENT
      (precedent: WicCoverageService.GetAgentByKidAsync already accepts either)
   d) otherwise 401 neutral "Unknown KID"
   Any input matching more than one active row -> 401 + log, never guess.
   An AGENT session must always carry a real employeeId; reject at startup any
   AppUserRoles row with Role=AGENT and EmployeeId NULL.
   Keep the login field labelled "KID" in the UI, with helper text saying the employee
   number also works.

3. SEED (idempotent, at startup):
   - S69307 -> DEV, DisplayName "Stojnic Nebojsa", EmployeeId 9074466, IsActive=1
   - S60028 -> RTM, "Silvija Angelova", EmployeeId NULL, IsActive=1
   - Y6525  -> RTM, "Yiting Qiang",     EmployeeId NULL, IsActive=1
   - Six TEAM_LEAD rows (Tobias Rossberg, Delia Panaitescu, Oliver Schleusen,
     Karlo Coric, Ion Ciuceanu, Jaroslaw Brzeszkiewicz), EmployeeId NULL, Kid EMPTY,
     IsActive=0. A row with an empty Kid must never authenticate. I will send their KIDs later.

4. ORDER OF WORK - lockout protection is a hard requirement:
   a) create AppUserRoles, seed the DEV row, verify with a real query
   b) prove I can log in with S69307 over BOTH the https devtunnel URL and
      http://localhost:5000 (Secure cookie must work on both)
   c) only then enable default-deny and the remaining policies
   d) write documentation\auth_rbac.md including the no-browser recovery INSERT

5. Report how many active employees are reachable by rule (b) vs (c) after implementation,
   and give me a PS1 script that lists the 63 active employees with no KID (id, name,
   team lead) so I can collect their KIDs later.

Proceed with Phase 2 now. Stop before deploy and show me what you changed.
