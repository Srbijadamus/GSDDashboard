# VIDEO_BRIEF — whiteboard explainer (Erklärvideo)

**Prepared 2026-10-11.** This is the brief for a **whiteboard explainer** — a hand drawing on a board
with a voice-over. It is **not** a screen recording: no screenshots, no application capture, no film of
a person's desktop. Everything is drawn with simple shapes and icons, and the numbers are the live
figures from 2026-10-11 with the system and table they came from.

**Rules this video follows:**

1. **No screenshots and no screen recording.** The board is the only picture.
2. **No personal data of any kind.** No people's names, no user names, no computer names, no network
   addresses, no device identifiers, no keys or tokens. Where a machine appears, it is a generic laptop
   icon.
3. **Say the hard things.** The security part includes what is closed, what is still open, and the
   supervisor-sign-in defect that must not be acted on.
4. **Two systems, never mixed up.** The workforce application and the kiosk have separate agent lists;
   every number on the board is labelled with the system it belongs to.
5. **Simple English.** Short sentences, everyday words, one idea per scene.

Figures named in this brief (all read live, read-only, on 2026-10-11):

| System | Figure | Source |
|---|---|---|
| WorkForce Pulse | **122 active employees** (128 rows) | `GSDDashboard.Employees` |
| WorkForce Pulse | **43 WIC centres** (41 Germany, 2 Netherlands) | `GSDDashboard.WicLocations` |
| WorkForce Pulse | **41,931 shift-plan rows**, 122 of 122 people covered in the next 14 days | `GSDDashboard.ShiftEntries` |
| ShiftKiosk | **123 active kiosk agents** (139 rows): WIC 62, VOICE 32, VWIC 4, BO 25, unclassified 0 | `ShiftKioskDB.agents` (`active=1`) |
| ShiftKiosk | **2,552 attendance events**; **519 supervisor manual actions** | `ShiftKioskDB.status_log`, `manual_actions` |
| ShiftKiosk | **63 computers seen**: **44** on the legacy shared key, **19** on the new device credential | `ShiftKioskDB.sec_kiosk_registry.credential_type` |
| ShiftKiosk | **283,206 observation rows** | `ShiftKioskDB.sec_observation_log` |
| ShiftKiosk | Auto-logout after **11 hours** | kiosk client code |

> **The two lists are not the same list.** Pulse's 122 active employees and the kiosk's 123 active
> agents come from two different databases. Say both, and always say which system you mean.

---

## Scene list

### Scene 1 — The four questions
- **Narration:** "Before this system, four simple questions were hard to answer. Who works where today?
  Is every customer centre covered? Who is off sick? And who has actually turned up?"
- **On the board:** A large question mark in the centre. Four speech bubbles around it, one per question,
  each with a tiny icon: a map pin, a shop, a plaster, a clock.
- **Key message:** The problem was not effort — it was that nobody had one shared picture.

### Scene 2 — Two systems, one goal
- **Narration:** "Today there are two systems. One plans the work. The other records what really
  happened. They meet on one thing only: the employee."
- **On the board:** Two boxes side by side. Left box labelled "PLAN". Right box labelled "REALITY". A
  single arrow between them labelled "employee". Under the arrow, a small key icon.
- **Key message:** Planning and attendance are separate products joined by one identifier.

### Scene 3 — The plan is generated, not typed
- **Narration:** "A planner creates a whole roster in one action. The system follows each centre's
  opening hours, skips weekends and holidays, and shows a preview before it saves."
- **On the board:** A calendar grid drawn by hand. A wand icon touches it and fills several boxes at
  once. A small "preview" card floats above, then a save tick.
- **Key message:** The plan is produced by a rule, so it is consistent and fast.

### Scene 4 — The plan is complete
- **Narration:** "On the eleventh of October, all one hundred and twenty-two active employees in the
  workforce application had a shift in the next fourteen days. The plan was complete."
- **On the board:** One big number "122 / 122". Under it, fourteen small calendar squares, all ticked. A
  separate small box labelled "Pulse".
- **Key message:** Completeness is measured, not assumed.

### Scene 5 — Coverage before the customer feels it
- **Narration:** "Every centre is checked continuously: covered, partly covered, uncovered, or closed. A
  gap is shown before it becomes a problem, together with ranked people who could fill it."
- **On the board:** A map outline with 43 dots (label "43 WIC centres"). Three dots are coloured
  differently — green, amber, red. An arrow from the red dot to a short ranked list with a number one,
  two, three beside small person icons.
- **Key message:** A gap becomes visible early and comes with options.

### Scene 6 — How attendance is captured
- **Narration:** "On the agent's laptop, a small program shows four choices: start, break, emergency,
  off. If the network is down, the events wait in a queue and are sent later."
- **On the board:** A laptop icon. On its screen, four buttons. A dashed line from the laptop to a cloud,
  with a small stack of cards beside the laptop labelled "queue". A clock icon shows "11 h" for the
  automatic end of shift.
- **Key message:** Attendance is captured where the agent is, and nothing is lost offline.

### Scene 7 — Where the two systems meet
- **Narration:** "The attendance page compares who was expected with who checked in. It shows the
  difference in both directions, and it always matches people by employee, never by place name."
- **On the board:** Two columns of small person icons. Left column headed "EXPECTED", right column
  headed "CHECKED IN". Two arrows cross between them, one each way. A small note: "match = employee".
- **Key message:** Expected and actual can disagree safely, because the comparison is honest.

### Scene 8 — A human decision is protected
- **Narration:** "When a person changes a shift, the change carries their mark and the time. Automatic
  jobs are not allowed to overwrite it, and they fail loudly if they try."
- **On the board:** A single calendar cell with a small pen icon and a stamp. Three arrows come towards
  it from boxes labelled "roster", "import" and "cleanup", and each arrow hits a shield and bounces off.
- **Key message:** Automation cannot silently undo a person's decision.

### Scene 9 — The two agent lists (do not mix them up)
- **Narration:** "The workforce application has one hundred and twenty-two active employees. The kiosk
  has its own list: one hundred and twenty-three active agents. Same people, two different systems."
- **On the board:** Two labelled columns. Left: "PULSE 122 active (128 rows)". Right: "KIOSK 123 active
  (139 rows)" with four small group boxes — WIC 62, VOICE 32, VWIC 4, BO 25 — and a fifth box "other 0".
  A thick vertical line with a "not the same list" note between them.
- **Key message:** Two counts, two sources — always say which one you mean.

### Scene 10 — The security story: what was found and fixed
- **Narration:** "A review found that the kiosk server gave out an agent's employee number without
  asking for a credential, and the workforce application accepted it as a sign-in. That path allowed
  someone to sign in as an agent. Both halves are now closed."
- **On the board:** A broken chain of three links. A big red cross over the middle. Then a new chain with
  a padlock, drawn beside it. A small "closed" tick.
- **Key message:** A real exposure was found, bounded, and closed on both sides.

### Scene 11 — The security story: what is still open
- **Narration:** "Some things are still open. A session credential can still be obtained without signing
  in and used to read history and exports. The shared legacy key is still used by forty-four computers.
  And the observation log grows without a retention rule."
- **On the board:** Three open padlocks in a row. Under the first: "read history / exports". Under the
  second: "44 legacy computers". Under the third: "283,206 observation rows and counting". No ticks.
- **Key message:** The honest position is stated, not softened.

### Scene 12 — Finding number one: the supervisor switch that does not exist
- **Narration:** "Several documents describe a supervisor sign-in switch. That switch is not implemented
  in the code as of the eleventh of October. Nothing reads it, so it cannot be switched on. Do not
  follow those instructions."
- **On the board:** A large light switch drawn on the board with a red circle-and-slash over it. Below
  it, the words "NOT IMPLEMENTED IN CODE" and the date. A small document icon with a warning triangle.
- **Key message:** The most serious documentation defect: an operator would think supervision was on
  when nothing had changed.

### Scene 13 — Device binding: live state versus the profile files
- **Narration:** "On the live kiosk server, device binding is enforcing — it blocks. The profile files
  still record it as observing only. The live server is the authority."
- **On the board:** A shield labelled "device binding" with a solid "ENFORCE" tag. Beside it, three small
  file icons labelled "dan1", "dan2", "dan3", each with a faint "shadow" tag and a small clock, drawn
  behind the shield.
- **Key message:** A control is blocking in production while the documents say it has not been promoted.

### Scene 14 — The migration is in progress
- **Narration:** "The kiosk client is being replaced. Sixty-three computers have been seen: forty-four
  still use the old shared key and nineteen use the new device credential. The migration is in
  progress."
- **On the board:** A long horizontal bar split into two parts: a large part labelled "44 legacy", a
  smaller part labelled "19 new". An arrow moving from left to right, labelled "migration in progress".
  No end date is drawn.
- **Key message:** Real progress, not finished — and no rounded numbers.

### Scene 15 — What happens next
- **Narration:** "Next: make the supervisor documents match reality, record the live device-binding
  state, provision supervisor accounts, finish the client migration, and agree a retention rule."
- **On the board:** Five numbered boxes in a row, drawn left to right, each with a small icon: a
  document with a warning, a shield with a tick, a key, a laptop, an hourglass. A horizon line behind
  them.
- **Key message:** The remaining work is finishing security and making the documents true.

---

## Production notes

- **Length:** 15 scenes; at roughly 45–60 seconds each the video lands at about 11–15 minutes. Scene 12
  is the one to rehearse most.
- **Drawing style:** one continuous hand, simple black lines, at most two accent colours. Icons are
  basic shapes: circles for people, rectangles for systems, arrows for flows, a padlock for security, a
  clock for time.
- **Numbers on screen:** draw them large and leave them visible long enough to read. Always write the
  system name next to a count.
- **What never appears on the board:** any screenshot, any real name, any user name, any computer name,
  any network address, any key or token, any individual sickness or incident story.
- **If the recording happens after 2026-10-11:** the observation log and the attendance counts grow
  daily. Re-read the figures and update the board before filming.
