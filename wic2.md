CORRECTION: I deleted those roster batches myself through the UI. That is why ShiftEntries
and RosterBatches are empty for those four. Nothing is broken - and it confirms the delete
path works against the real database.

So treat it as: the four have NO shifts at all right now. That is the true starting state.

Continue the WIC investigation with that in mind, and answer the question that matters:

Antonios Gaber (9139928, PrimaryRole=WIC, Mecklenburg-Vorpommern) is meant to work at Demmin.
Demmin is a WIC centre and has two locations in WicLocations.

Tell me, concretely, the full sequence a person has to go through to actually work at Demmin,
from "row exists in Employees" to "assigned and visible in WIC coverage":
- what must exist in WicAgentAssignments, and who creates it
- what must exist in ShiftEntries, and whether a plain WORKING row is enough or it needs
  IsWicDuty and LocationId
- what the assignment endpoint itself requires
- which screen RTM uses for each step

Then say which of those steps the dashboard can do today and which it cannot.

Change nothing, deploy nothing. Report only.
