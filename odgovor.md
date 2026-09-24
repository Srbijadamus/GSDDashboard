APPROVED - proceed, but in this order, and STOP where told.

1. PLAN APPROVAL
- 6.1 AppUserRoles table: approved. Do NOT put roles on Employees.
- 6.2 login/session/rate limit: approved.
- 6.3 enforcement incl. default-deny, AGENT global write-block, AGENT allow-list,
  and locking down /api/admin/cleanup-wic-orphans + DemoDataAdmin to DEV only: approved.
- 6.5 frontend: approved.
- /api/debug/client-error: keep it anonymous, but rate-limit it the same way as login
  and never echo the submitted content back in a response.

2. RUN THE VERIFICATION YOURSELF NOW
The script in your section 7 is read-only SELECTs. Create it and run it yourself,
paste the full output, and then STOP again. Do not start Phase 2 coding yet.

3. EXTRA RULES FOR THE SEED (answer these with the query output)
- Duplicate KIDs: if query 4 returns any row, a KID is not a unique identity. Report it
  and STOP. Never pick one of the duplicates. Plan for it in code too: if a login KID
  matches more than one active employee, return 401 and log it - never guess.
- Team leads: show me the resolved name + KID list from query 6 and WAIT for my
  confirmation before inserting a single TEAM_LEAD row. Matching by FullName is fragile
  in this DB, so also report every TeamLeadName that could NOT be matched to a KID.
- If S69307, S60028 or Y6525 is missing or inactive, STOP and report. Do not fuzzy-match.

4. LOCKOUT PROTECTION - this is a hard requirement
Before the default-deny policy goes live, the DEV row for S69307 must already exist and
be verified by a real query. Also give me a documented recovery path that works with no
browser session at all, e.g. a single SQL INSERT into AppUserRoles that restores DEV
access, written into documentation\auth_rbac.md. I must never be able to lock myself out
of my own machine.

5. Verify the Secure cookie works both over the devtunnel https URL and on
http://localhost:5000 before you call the deploy successful.
