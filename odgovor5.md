GO - deploy with the flag OFF, exactly as you described.

Order:
a) backup bin\Release\net8.0 to C:\Temp\gsd_auth_backup
b) stop task GSDDashboard-Backend, build Release, start task
c) health check + confirm the dashboard behaves exactly as before (flag OFF)
d) run PS1_AuthSmokeTest.ps1 against BOTH http://localhost:5000 and the devtunnel https URL
e) report both results and STOP

Do NOT turn EnforceAuthorization on. I will log in as S69307 myself first, and only after
that confirms working will I tell you to flip the flag.

If anything fails, roll back from the backup immediately and report - do not try to fix
forward on the live instance.

Also tell me in the final report:
- the exact URL I open to see the login page
- the exact SQL INSERT that restores my DEV access if I ever lock myself out
