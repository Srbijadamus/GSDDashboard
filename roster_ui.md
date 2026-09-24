CORRECTION - forget the Excel import. There is no current Shift Plan file and there will not
be one. The two environments are separate, nothing gets transferred between them. Stop
treating the Excel as the source of truth; it is dead as a path.

That means the dashboard itself must be able to create and maintain rosters. Build that.

CONTEXT (verified by me):
- 3 active employees have zero ShiftEntries:
    9139928 Antonios Gaber    TL Delia Panaitescu
    3193180 Cortneigh Halim   TL Oliver Schleusen
    9123476 Patrick Henschel  TL Tobias Rossberg
- Samantha Buys (3193178) has no Employees row at all and needs one.
- The Henschel duplicate P37233 is harmless: inactive, no team lead, no shifts, no KID.
  Leave it alone for now, do not spend steps on it.
- Precedent exists: PATTERN_FILL already generated 7,714 ShiftEntries rows for Aug-Dec,
  so generating a roster from a pattern is something this system has done before.
- Employees has a ShiftPattern column. Check whether it is populated for these people.

PHASE 1 - READ ONLY, report then STOP:
1. How did PATTERN_FILL generate its rows? Find the script or the logic. What pattern did it
   apply, and is it reusable? If the script is not in the repo, say so.
2. What does Employees.ShiftPattern contain, for these 4 and in general? Is it usable as the
   input for generating a roster?
3. Is there ANY existing way to create or edit a single shift from the dashboard UI today?
   List every write endpoint that touches ShiftEntries and what calls it.
4. Propose two things:
   a) how to give these 3 people a roster right now
   b) how a TEAM_LEAD or RTM adds or edits an agent's shifts from the dashboard, so this
      never depends on a file or on me again
   For (b), say what you would build and roughly how much work it is. Do not build it yet.

Do not change data. Do not deploy. Do not touch the enforcement flag.
