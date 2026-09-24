Good work. Before deploy, three things:

1. SWAGGER - you noted it yourself: /swagger is anonymous because it is not under /api.
   Over the devtunnel that publishes the full endpoint map to anyone. Disable it outside
   Development, or put it behind the DEV policy. Your choice, but state which you did.

2. Do NOT deploy to port 5000 yet. Show me first:
   - git diff --stat (what changed)
   - the final smoke test output, all tests passing in ONE run
   - the enforcement flag: confirm default-deny is currently OFF in the deployed config,
     so if anything is wrong my live dashboard keeps working
   Then STOP.

3. Give me the PS1 script that lists the 63 active employees with no KID
   (EmployeeId, FullName, TeamLeadName) so I can collect their KIDs.

Also confirm in one line each:
- my S69307 login works over BOTH http://localhost:5000 and the https devtunnel URL
- WicAttendance / kiosk data still loads
- ports 5016 and 8000 were never touched
