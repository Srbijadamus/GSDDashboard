Excellent work - this explains it properly.

I do NOT have a current Shift Plan export yet. I will ask for one. Until it arrives:
DO NOT re-import the June file. Re-importing a stale roster over everyone would be worse
than the current gap.

Do only these two things now, then STOP:

1. THE HENSCHEL DUPLICATE - investigate first, change nothing.
   P37233 Patrick Henschel (created 07.07 via SyncEmployees.ps1) and 9123476 Patrick Henschel
   (created today). Report: are these the same person? Which ID appears in the Shift Plan
   Excel? Does either have shifts, absences, WIC assignments, or an AppUserRoles row? Which
   one should survive, and what exactly would have to be merged or removed? Two identities
   for one person is worse than a missing roster - it means someone logs in and sees half
   their data. Do not merge or delete anything yet.

2. THE "MISSING ROSTER" CHECK - build it now, it does not depend on the Excel.
   Active employees with zero ShiftEntries in the next 14 days, surfaced in the dashboard
   itself - a banner or an exception card, visible to TEAM_LEAD, RTM and DEV, not to AGENT.
   Also flag the inverse case if you can do it cheaply: people who appear in ShiftEntries but
   have no active Employees row.
   Build it, but do not deploy - show me first.

Not now, but noted for when the current export arrives: Samantha Buys (3193178) needs an
Employees row before any import, otherwise her roster lands nowhere again.

Do not touch the enforcement flag. Do not deploy.
