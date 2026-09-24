ONE TASK: the missing-roster warning. Nothing else.

Right now nothing in the system notices that an active employee has no roster - we only found
Gaber, Halim and Henschel because a team lead emailed about them. Fix that.

Build a check that surfaces, inside the dashboard:
- active employees with zero ShiftEntries in the next 14 days
- the inverse if it is cheap: ShiftEntries rows for employees with no active Employees row

Visible to RTM, TEAM_LEAD and DEV. Never to AGENT.
Put it where it will actually be seen - the Overview page, or a banner on Shift Plan.
Each listed person should link to the Roster Generator so it can be fixed in one click.

Build it, build the frontend, and copy wwwroot to bin\Release\net8.0\wwwroot (npm run build
fails on the cp step - it is a Linux command).

Do not deploy the backend, do not touch the enforcement flag, C:\HealthCheck, or ports
5016 and 8000. Tell me if a backend rebuild is needed and I will run it myself.
