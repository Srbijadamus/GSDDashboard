# VIDEO_NARRATION — full voice-over script

**Prepared 2026-10-11.** This is the complete narration for the whiteboard explainer described in
[VIDEO_BRIEF.md](./VIDEO_BRIEF.md). It is written to be read aloud, slowly and plainly. Read the words
as they are; the drawing happens beside the voice, it is not described in the text.

**How to read it**

- One scene at a time. Pause after each scene while the drawing finishes.
- Short sentences. Breathe at the full stops.
- Never say a person's name, a user name, a computer name or a network address. If a number appears
  below, it was read from the live systems on 2026-10-11 and it names the system it belongs to.
- The two agent lists are different. Say "the workforce application" or "the kiosk" every time.

---

## Scene 1 — The four questions

Before this system, four simple questions were hard to answer.

Who works where today?

Is every customer centre covered?

Who is off sick?

And who has actually turned up?

Four questions. Four different people. Four different answers.

The problem was never effort. The problem was that nobody had one shared picture.

---

## Scene 2 — Two systems, one goal

Today there are two systems.

The first one plans the work. It is called WorkForce Pulse.

The second one records what really happened. It is called ShiftKiosk.

They are separate products, with separate databases.

They meet in exactly one place: the employee.

---

## Scene 3 — The plan is generated, not typed

A planner creates a whole roster in one action.

The system follows each centre's own opening hours.

It skips weekends and public holidays.

And it shows a preview before it saves anything.

So the plan is made by a rule. That is why it is consistent, and that is why it is fast.

---

## Scene 4 — The plan is complete

On the eleventh of October, the workforce application had one hundred and twenty-two active employees.

All one hundred and twenty-two of them had a shift in the next fourteen days.

One hundred and twenty-two out of one hundred and twenty-two.

That is not luck. It is the result of the roster generator, plus a warning on the front page that
shows when somebody is missing from the plan.

---

## Scene 5 — Coverage before the customer feels it

Every WIC centre is checked continuously.

There are forty-three of them: forty-one in Germany and two in the Netherlands.

Each centre is classified as covered, partly covered, uncovered, or closed.

When a centre is at risk, it is shown before it becomes a customer problem.

And it comes with a ranked list of people who could fill the gap.

---

## Scene 6 — How attendance is captured

On the agent's own laptop, a small program shows four choices.

Start the shift. Take a break. Report an emergency. Finish.

If the network is down, the events wait in a queue on the laptop.

Nothing is lost. When the connection returns, the queue is sent.

And a shift closes automatically after eleven hours.

---

## Scene 7 — Where the two systems meet

The attendance page puts expectation next to reality.

On one side: who was expected to work, from the plan.

On the other side: who actually checked in, from the kiosk.

The page shows the difference in both directions.

And it always matches people by employee, never by the name of a place.

That one rule is what makes the comparison trustworthy.

---

## Scene 8 — A human decision is protected

When a person changes a shift, the change carries their mark and the time it was made.

Automatic jobs are not allowed to overwrite it.

A generator, an import, a cleanup — each one checks first, and each one fails loudly if it tries.

So a human decision cannot be silently undone.

---

## Scene 9 — The two agent lists

Here is something to be careful about.

The workforce application has one hundred and twenty-two active employees. That list lives in the Pulse
database, in the employees table, with one hundred and twenty-eight rows in total.

The kiosk has its own list. It has one hundred and twenty-three active agents, in one hundred and
thirty-nine rows. Those agents are grouped as WIC, sixty-two; Voice, thirty-two; Virtual WIC, four;
Back Office, twenty-five; and none left unclassified.

Same people, two different systems.

So when you say a number, always say which system you mean.

---

## Scene 10 — The security story: what was found and fixed

In early October, a review found a real problem.

The kiosk server published every agent's employee number on an address that asked for no credential at
all.

And the workforce application accepted a bare employee number as a sign-in.

Together, those two facts let anyone who could reach the kiosk server sign in as an agent.

It was bounded to agent level. It was never an administrator and never a team lead.

Both halves are now closed.

The workforce application reaches kiosk data only through its own authenticated proxy. And the kiosk
server's shared key can no longer be used on the session addresses, and a session credential can no
longer write attendance.

The agents had to do nothing.

---

## Scene 11 — The security story: what is still open

We also have to say what is still open.

A session credential can still be obtained from the kiosk server without signing in, and it can still be
used to read attendance history, the audit log and the exports.

The shared legacy key is still used every day by forty-four computers.

And there is no retention rule for the attendance data and the observation data, which includes
sickness information. The observation log holds two hundred and eighty-three thousand two hundred and
six rows, and it grows with every request.

We do not claim there is no risk here. We say exactly where the risk is.

---

## Scene 12 — Finding number one: the supervisor switch that does not exist

This is the most important thing in this video.

Several documents describe a supervisor sign-in switch. They tell an operator to set it to off, or to
shadow, or to enforce.

That switch is not implemented in the code as of the eleventh of October.

There is no such setting in any source file and no such setting in the configuration. The part that
would connect the workforce application to the kiosk's supervisor check does not exist. And nothing in
the running application reads it, so it cannot be switched on.

An operator who follows those instructions would believe supervision had been switched on, when nothing
had changed.

So please do not follow those instructions. The documents are wrong, and the correct behaviour today is
to leave the switch alone.

---

## Scene 13 — Device binding: live state versus the profile files

One more honest point.

On the live kiosk server, device binding is enforcing. It blocks.

Device binding ties a credential to one computer and one user, and it rejects an unknown computer.

But the tracked profile files still record it as observing only.

The live server is the authority. The documents are behind reality, and that difference has to be
settled by the owner.

---

## Scene 14 — The migration is in progress

The kiosk client is being replaced.

The server has seen sixty-three computers.

Forty-four of them still use the old shared key.

Nineteen of them already use the new device credential.

So the migration is real, but it is not finished. That is why we say "migration in progress".

No rounding. No guessing. Just the count from the live registry.

---

## Scene 15 — What happens next

So what happens next?

One. Make the supervisor sign-in documents match reality, so nobody acts on a switch that does nothing.

Two. Record the live device-binding state, so the profile files and the server agree.

Three. Provision the supervisor accounts and prove the recovery path.

Four. Finish the client migration.

Five. Agree a retention rule for attendance and observation data.

The system is in daily use, and the plan is complete. The work that remains is finishing security, and
making the documents true. That is the right order for a system people now depend on.

---

**End of narration.**
