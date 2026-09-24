READ ONLY first. The real question was about WIC, not the shift plan.

A team lead asked: "The new person for Demmin, Antonios Gaber, such as Samantha and Cortneigh -
they are added in Employee List but not appear in the shift plan - how i can add them into it?"

Demmin is a WIC centre. He is not asking about the shift plan for its own sake - he is asking
how to get a new person working at Demmin. The shift plan was only the symptom he could see.

These four are new and now have plain WORKING rosters from the new Roster Generator:
  9139928 Antonios Gaber    -> intended for Demmin
  3193180 Cortneigh Halim
  9123476 Patrick Henschel
  3193178 Samantha Buys

READ documentation/ first - the WIC blueprints - then the code.

REPORT:
1. Every prerequisite for a person to be assignable to a WIC location: role, qualification,
   region, WicAssignments row, an existing ShiftEntry for that day, anything else.
2. For each of the four: exactly what is present and what is missing. Name the table, the
   column, the value.
3. Demmin specifically: what does that location require, and can Antonios be assigned there
   today? If not, what is the exact gap?
4. Does WIC assignment work with ShiftEntries created by the Roster Generator, or does it
   expect something the old import set that we do not - SourceModule, AgentTask, IsWicDuty,
   LocationId?
5. Which screen does RTM use for this assignment, and would it work for these four right now?

Quote the docs where they answer. Say "the docs do not cover this" rather than guessing.
Change nothing, deploy nothing.
