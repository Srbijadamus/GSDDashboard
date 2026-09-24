Approved - execute the plan exactly as presented.

Confirmations:
- Delia: yes, update Id=5 and KEEP the DisplayName as "Delia Panaitescu". Do not rename it
  to the long form - it must match Employees.TeamLeadName.
- O7164: use it verbatim as given. Noted that it is shorter; I will re-check it at the source.

Previous session died with "Error: Unknown session" right after presenting the plan. Nothing
was written. Re-verify the table state before the updates.

Execute:
- the six guarded UPDATEs in one transaction, with the @@ROWCOUNT = 1 assertion per statement
- post-write verification: full AppUserRoles table, duplicate check, Employees collision
  re-check, and confirmation that S69307 (DEV) and S60028 / Y6525 (RTM) are intact and active
- yes, save it as PS1_Seed_TeamLeadKids.ps1 for the record - I want the seeding reproducible
  and auditable, not a one-off inline command

Do not change the enforcement flag, do not rebuild, do not restart, do not deploy.
Show me the final table and stop.
