Build this. The rule is simple:

  Days the chosen WIC location is OPEN  -> WIC duty at that location
  All other working days                -> BO (back office)
  Weekends and public holidays          -> skipped

RTM picks the agent, the location and the date range. Nothing else to decide - RTM knows which
location. People rotate between WICs anyway, and any later change is made through the existing
WIC screens. This only creates the starting schedule.

Extend the existing Roster Generator (Backend/RosterService.cs + Frontend/src/pages/Roster.tsx).
Keep everything it already has: preview before writing, batch record, delete the batch.

Requirements:
- location dropdown listing active WicLocations
- opening days read from the system, never typed by the user
- preview shows which dates become WIC and which become BO, and the totals
- the generated rows must look exactly like WIC days already look in this system - match the
  existing correct rows, do not invent a new shape
- BO days use whatever value BO already uses in ShiftEntries today
- if the agent also needs a WicAgentAssignments row to appear in the coverage view, create it,
  since the dashboard cannot do that today

Build it, build the frontend, copy wwwroot to bin\Release\net8.0\wwwroot (npm run build fails
on the cp step, it is a Linux command). Tell me if a backend rebuild is needed - I run that
myself.

Do not deploy, do not touch the enforcement flag, C:\HealthCheck, or ports 5016 and 8000.
If a tool call fails twice, stop.
