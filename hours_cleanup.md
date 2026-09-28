FIX IN CODE: changing a WIC centre's opening days leaves stale WIC duties behind on days the
centre is now closed.

What happened (verified by me in the live DB, already cleaned up manually):
Dortmund (DE~44139~Dortmund~Florianstraße 15–21) was changed on 2026-09-16 to four opening
days - WicOpeningHours now has DayOfWeek=5 IsClosed=1 with EffectiveFrom 2026-09-16.
But Christian Martino still had ShiftEntries WIC_DUTY rows with AgentTask='Dortmund' on
2026-09-25 and 2026-10-02, plus 4 matching WicShiftEntries rows. WIC Schedule showed them
correctly - the data was wrong, not the screen. I deleted those rows by hand today.

WHAT TO BUILD:
When opening hours are saved and a day becomes closed (or the centre is closed entirely),
future duties for that location on the now-closed weekday must not be left behind.

Two things, and tell me which you consider correct before building:
 a) the save itself cleans up future duties for the now-closed days, or
 b) the save reports them and asks the user to confirm removal
I lean towards (b) - silently deleting people's planned duties is worse than showing them -
but decide with the code in front of you and tell me why.

Rules either way:
- only FUTURE dates, never touch the past
- respect EffectiveFrom: only dates on or after the change
- remove both the ShiftEntries WIC_DUTY row and its WicShiftEntries rows
- never remove absences
- the agent ends up with nothing on that day; say so in the UI so RTM knows to add BO

READ documentation/ first, then find where opening hours are saved (the Opening Hours panel
on WIC Attendance). Report the file, the endpoint, and your recommendation BEFORE changing
anything.

Also add a read-only check I can run: list any future WIC duty that falls on a day its
location is closed. Dortmund is clean now, so it should return nothing.

Read files with PowerShell Get-Content, not read_files - that tool returns stale content.
Build the frontend if involved and copy wwwroot to bin\Release\net8.0\wwwroot.
Tell me if a backend rebuild is needed - I run it myself.
Do not deploy, do not touch the enforcement flag, C:\HealthCheck, or ports 5016 and 8000.
