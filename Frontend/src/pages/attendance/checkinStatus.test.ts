// Tests for the WIC check-in status definition.
//
// This project has no JS test runner installed, so these run on the TypeScript
// compiler that IS installed plus plain node:
//
//     cd Frontend && npm run test:attendance
//
// No framework, no new dependency, works offline.

import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import {
  buildCheckinStatus,
  buildPresencePair,
  buildZoneBRows,
  centreTimeZone,
  displayRowForAgent,
  displayRowForKioskRecord,
  groupZoneBRows,
  breakMinutes,
  isOnBreak,
  isTestEmployee,
  nowMinutesInZone,
  ON_BREAK_DURATION_KEY,
  ON_BREAK_LABEL_KEY,
  ON_BREAK_STATUS,
  parseHhMm,
  TEST_EMPLOYEE_IDS,
  WORK_MODE_LABEL_KEY,
  workModeOf,
  type CheckinKioskRecord,
  type CheckinLocation,
} from "./checkinStatus.js"

let failures = 0
let passes = 0
function test(name: string, fn: () => void): void {
  try {
    fn()
    passes++
    console.log(`  ok   ${name}`)
  } catch (err) {
    failures++
    console.log(`  FAIL ${name}`)
    console.log(`       ${(err as Error).message.split("\n").join("\n       ")}`)
  }
}

// ── Fixtures ─────────────────────────────────────────────────────────────────

function centre(
  displayName: string,
  agents: Array<{ id: string; name: string; start: string | null; notRecorded?: boolean; manual?: boolean }>,
  opts: { closed?: boolean; city?: string; country?: string } = {},
): CheckinLocation {
  return {
    locationCode: `CODE~${displayName}`,
    displayName,
    city: opts.city ?? displayName,
    country: opts.country ?? "DE",
    todaySchedule: { isClosed: opts.closed ?? false },
    assignedAgents: agents.map(a => ({
      employeeId: a.id, name: a.name, shiftStart: a.start, shiftEnd: "16:00",
      checkInNotRecorded: (a.notRecorded ?? false) && !(a.manual ?? false),
      isManualChange: a.manual ?? false,
      manualChangeBy: a.manual ? "S60028" : null,
      manualChangeAt: a.manual ? "2026-10-01 07:05" : null,
    })),
  }
}

function kioskRow(
  id: string, name: string, status: string,
  workLocation: string | null = "WIC", location: string | null = null,
  lastEventTime: string | null = null,
  // Every fixture in this file predates agent_group and depicts the WIC
  // population the page is actually about, so "WIC" is the correct default
  // rather than a new parameter every call site would have to pass.
  agentGroup: string | null = "WIC",
): CheckinKioskRecord {
  return {
    employee_id: id, full_name: name, attendance_status: status,
    work_location: workLocation, location, last_event_time: lastEventTime,
    agent_group: agentGroup,
  }
}

// ── THE INVARIANT ────────────────────────────────────────────────────────────

test("Expected = CheckedIn + NotYetIn (the invariant the header used to break)", () => {
  const cards = [
    centre("Augsburg", [{ id: "1", name: "A", start: "07:30" }]),
    centre("Hamburg", [{ id: "2", name: "B", start: "09:00" }]),
    centre("Trier", [{ id: "3", name: "C", start: "08:00" }]),
  ]
  const kiosk = [
    kioskRow("1", "A", "ACTIVE"),
    kioskRow("2", "B", "DONE"),
    kioskRow("3", "C", "NOT_CHECKED_IN"),
    // Not expected anywhere - must never land in CheckedIn.
    kioskRow("99", "Outsider", "ACTIVE", "HOME_OFFICE"),
  ]
  const s = buildCheckinStatus(cards, kiosk)

  // The outsider is Expected too (anyone at work today is expected) and Checked in.
  assert.equal(s.rosterExpected.length, 3)
  assert.equal(s.expected.length, 4)
  assert.equal(s.checkedIn.length, 3)
  assert.equal(s.notYetIn.length, 1)
  assert.equal(s.checkedIn.length + s.notYetIn.length, s.expected.length, "invariant")
  assert.equal(s.checkedInOnRoster, 2)
  assert.equal(s.checkedInOffRoster, 1)
})

test("the reported 2026-10-01 shape: 39/37/15 becomes a clean partition", () => {
  // 24 expected and checked in, 15 expected and not, 12 other staff checked in,
  // 1 kiosk row with a blank employee_id.
  const agents = Array.from({ length: 39 }, (_, i) => ({
    id: `E${i}`, name: `Agent ${i}`, start: "08:00",
  }))
  const cards = [centre("Big", agents)]
  const kiosk: CheckinKioskRecord[] = [
    ...agents.slice(0, 24).map(a => kioskRow(a.id, a.name, "ACTIVE")),
    ...Array.from({ length: 12 }, (_, i) => kioskRow(`X${i}`, `Outsider ${i}`, "ACTIVE", "HOME_OFFICE")),
    kioskRow("", "No Id Person", "DONE", null),
  ]
  const s = buildCheckinStatus(cards, kiosk)

  // 39 roster + 12 off-roster check-ins = 51 Expected; 24 + 12 = 36 Checked in; 15 not in.
  assert.equal(s.rosterExpected.length, 39)
  assert.equal(s.expected.length, 51)
  assert.equal(s.checkedIn.length, 36)
  assert.equal(s.notYetIn.length, 15)
  assert.equal(s.checkedIn.length + s.notYetIn.length, s.expected.length, "invariant")
  assert.equal(s.checkedInOffRoster, 12)
  assert.equal(s.blankKioskIdCount, 1)
})

// ── Blank employee_id ────────────────────────────────────────────────────────

test("blank employee_id is excluded from every count and surfaced once", () => {
  const cards = [centre("Augsburg", [{ id: "1", name: "A", start: "07:30" }])]
  const kiosk = [
    kioskRow("1", "A", "ACTIVE"),
    kioskRow("", "No Id", "DONE", null),
    kioskRow("   ", "Whitespace Id", "ACTIVE", null),
  ]
  const s = buildCheckinStatus(cards, kiosk)

  assert.equal(s.checkedIn.length, 1, "only the real id counts")
  assert.equal(s.expected.length, 1, "a blank id is never a person")
  assert.equal(s.blankKioskIdCount, 2)
  assert.equal(s.checkedIn.length + s.notYetIn.length, s.expected.length, "invariant")
})

// ── "GSD" excluded from Expected ─────────────────────────────────────────────

test("shiftStart 'GSD' is excluded from Expected (cannot check in at a WIC)", () => {
  const cards = [
    centre("Helmstedt", [
      { id: "1", name: "Merlin Voss", start: "GSD" },
      { id: "2", name: "Real Agent", start: "09:00" },
    ]),
  ]
  const s = buildCheckinStatus(cards, [])

  assert.deepEqual(s.rosterExpected.map(a => a.employeeId), ["2"])
  assert.equal(s.notYetIn.length, 1)
  assert.equal(s.checkedIn.length + s.notYetIn.length, s.expected.length, "invariant")
})

test("absence markers and closed centres are excluded from Expected", () => {
  const cards = [
    centre("Open", [
      { id: "1", name: "Working", start: "08:00" },
      { id: "2", name: "On AL", start: "AL" },
      { id: "3", name: "Sick", start: "SICK" },
      { id: "4", name: "No time", start: null },
    ]),
    centre("Closed", [{ id: "5", name: "At a closed centre", start: "08:00" }], { closed: true }),
  ]
  const s = buildCheckinStatus(cards, [])

  assert.deepEqual(s.rosterExpected.map(a => a.employeeId), ["1"])
})

test("an agent on two cards is counted once", () => {
  const cards = [
    centre("First", [{ id: "1", name: "Dual", start: "08:00" }]),
    centre("Second", [{ id: "1", name: "Dual", start: "08:00" }]),
  ]
  const s = buildCheckinStatus(cards, [])
  assert.equal(s.rosterExpected.length, 1)
})

test("ROSTER_ONLY agents stay in Expected and in Not Checked In", () => {
  // They are genuinely rostered; they must not be filtered out or relabelled.
  const cards = [centre("Wesel", [{ id: "1", name: "Angelika Weber", start: "08:00", notRecorded: true }])]
  const s = buildCheckinStatus(cards, [])
  assert.equal(s.rosterExpected.length, 1)
  assert.equal(s.notYetIn.length, 1)

  const rows = buildZoneBRows(s, [], new Date("2026-10-01T08:30:00+02:00"))
  assert.equal(rows.length, 1)
  assert.equal(rows[0].checkInNotRecorded, true, "badge flag is carried through")
  assert.equal(rows[0].delayMinutes, 30, "real delay, not suppressed")
})

// ── Work mode (the badge) ────────────────────────────────────────────────────
// The agent chooses at check-in where they work FROM; the kiosk stores it in
// status_log.work_location and GET /api/attendance returns it as `work_location`.
// Values in use: "WIC" and "HOME_OFFICE" (shown in the kiosk UI as
// "Virtual WIC Support"). The free-text `location` field is never consulted.

test("workModeOf reads the kiosk's work_location choice", () => {
  assert.equal(workModeOf(kioskRow("1", "On site", "ACTIVE", "WIC")), "WIC")
  assert.equal(workModeOf(kioskRow("2", "Virtual", "ACTIVE", "HOME_OFFICE")), "HOME_OFFICE")
})

test("workModeOf is case- and whitespace-tolerant", () => {
  assert.equal(workModeOf(kioskRow("1", "x", "ACTIVE", " wic ")), "WIC")
  assert.equal(workModeOf(kioskRow("2", "y", "ACTIVE", "home_office")), "HOME_OFFICE")
})

test("workModeOf returns null rather than guessing", () => {
  // No badge is better than a wrong badge if the kiosk adds a new option.
  assert.equal(workModeOf(kioskRow("1", "x", "ACTIVE", "")), null)
  assert.equal(workModeOf(kioskRow("2", "y", "ACTIVE", null)), null)
  assert.equal(workModeOf(kioskRow("3", "z", "ACTIVE", "SOMETHING_NEW")), null)
  assert.equal(workModeOf(undefined), null)
  assert.equal(workModeOf({ employee_id: "4", full_name: "no field", attendance_status: "ACTIVE" }), null)
})

test("every work mode has an i18n label key", () => {
  assert.equal(WORK_MODE_LABEL_KEY.WIC, "attendance.workMode.WIC")
  assert.equal(WORK_MODE_LABEL_KEY.HOME_OFFICE, "attendance.workMode.HOME_OFFICE")
})

test("the work mode travels with every checked-in person", () => {
  // What the badge renders from, in all three places it appears.
  const cards = [centre("Hamburg", [
    { id: "1", name: "On site", start: "08:00" },
    { id: "2", name: "Virtual", start: "08:00" },
  ])]
  const kiosk = [
    kioskRow("1", "On site", "ACTIVE", "WIC"),
    kioskRow("2", "Virtual", "ACTIVE", "HOME_OFFICE"),
    kioskRow("77", "Other person", "ACTIVE", "HOME_OFFICE"),
  ]
  const s = buildCheckinStatus(cards, kiosk)
  const byId = new Map(kiosk.map(r => [r.employee_id, r]))

  assert.deepEqual(s.checkedIn.map(a => workModeOf(byId.get(a.employeeId))),
    ["WIC", "HOME_OFFICE", "HOME_OFFICE"], "off-roster person is checked in too")
  assert.equal(s.checkedInOffRoster, 1)

  // Presence chips must be able to show it too, so a virtual check-in is not read as
  // on-site presence.
  const p = buildPresencePair(cards[0], kiosk)
  assert.deepEqual(p.presentKiosk.map(workModeOf).sort(), ["HOME_OFFICE", "WIC"])
})

// ── Display rows: location shown, id secondary ───────────────────────────────
// RTM needs to SEE the kiosk location text. It is passed through verbatim and is
// display-only: nothing matches, counts or classifies on it (pinned separately below).

test("a roster agent's row carries the kiosk location verbatim", () => {
  const cards = [centre("Hannover", [{ id: "9121281", name: "Olaf Wittenberg", start: "09:00" }])]
  const rec = kioskRow("9121281", "Olaf Wittenberg", "ACTIVE", "WIC", "Hannover | Emmerthal")
  const s = buildCheckinStatus(cards, [rec])
  const row = displayRowForAgent(s.expected[0], rec, "ACTIVE")

  assert.equal(row.location, "Hannover | Emmerthal", "multi-value location passed through unchanged")
  assert.equal(row.employeeId, "9121281", "id is present as the secondary item")
  assert.equal(row.name, "Olaf Wittenberg")
  assert.equal(row.workMode, "WIC", "badge data travels with the row")
})

test("an other-staff row carries the kiosk location verbatim", () => {
  const rec = kioskRow("9126882", "Khaled Alali", "ACTIVE", "HOME_OFFICE", "Landshut | Essenbach")
  const row = displayRowForKioskRecord(rec)

  assert.equal(row.location, "Landshut | Essenbach")
  assert.equal(row.employeeId, "9126882")
  assert.equal(row.workMode, "HOME_OFFICE")
  assert.equal(row.checkinTime, rec.checkin_time ?? null)
})

test("location is never blanked out in favour of the id", () => {
  // The regression this pins: the id had replaced the location in all three places.
  const cards = [centre("Trier", [{ id: "1", name: "A", start: "08:00" }])]
  const rec = kioskRow("1", "A", "ACTIVE", "WIC", "Saarbrücken | Trier")
  const s = buildCheckinStatus(cards, [rec])
  for (const row of [displayRowForAgent(s.expected[0], rec, "ACTIVE"), displayRowForKioskRecord(rec)]) {
    assert.equal(row.location, "Saarbrücken | Trier")
    assert.notEqual(row.location, row.employeeId)
  }
})

test("a missing kiosk row leaves the location null, not the id", () => {
  const cards = [centre("Trier", [{ id: "1", name: "Not in yet", start: "08:00" }])]
  const s = buildCheckinStatus(cards, [])
  const row = displayRowForAgent(s.expected[0], undefined, "NOT_CHECKED_IN")

  assert.equal(row.location, null)       // the UI renders a dash
  assert.equal(row.employeeId, "1")
  assert.equal(row.workMode, null)       // no badge when the kiosk recorded nothing
  assert.equal(row.status, "NOT_CHECKED_IN")
})

test("showing the location does not make it count for anything", () => {
  // Displayed and ignored at the same time: Sina is shown with "Augsburg | Landshut"
  // but is not rostered at Augsburg, so she is not present there and not expected.
  const augsburg = centre("Augsburg", [{ id: "9112561", name: "Kamil Filipowicz", start: "07:30" }])
  const sina = kioskRow("9124691", "Sina Sidharthan", "ACTIVE", "WIC", "Augsburg | Landshut")
  const kiosk = [kioskRow("9112561", "Kamil Filipowicz", "ACTIVE", "WIC", "Augsburg"), sina]

  assert.equal(displayRowForKioskRecord(sina).location, "Augsburg | Landshut", "shown")

  const p = buildPresencePair(augsburg, kiosk)
  assert.equal(p.presentKiosk.length, 1, "not counted at Augsburg")
  const s = buildCheckinStatus([augsburg], kiosk)
  assert.deepEqual(s.needsCentreAssignment.map(r => r.employee_id), ["9124691"],
    "work mode WIC => Assign action, still not attributed to Augsburg")
  assert.equal(s.rosterExpected.length, 1)
})

// ── Present (kiosk) vs Rostered ──────────────────────────────────────────────

test("presence is matched by employee_id only — never by location", () => {
  // Augsburg 2026-10-01: Kamil Filipowicz is rostered and checked in. Sina Sidharthan
  // and Mohamad Nasir Amany were also kiosk-ACTIVE with Augsburg in their free-text
  // location, and the old substring match credited them to this centre. They are not
  // rostered here, so they now belong to "Other staff checked in" instead — the kiosk
  // location field says nothing about where anyone is today.
  const augsburg = centre("Augsburg", [{ id: "9112561", name: "Kamil Filipowicz", start: "07:30" }])
  const kiosk = [
    kioskRow("9112561", "Kamil Filipowicz", "ACTIVE", "WIC", "Augsburg"),
    kioskRow("9124691", "Sina Sidharthan", "ACTIVE", "WIC", "Augsburg | Landshut"),
    kioskRow("9132077", "Mohamad Nasir Amany", "ACTIVE", "WIC", "Augsburg"),
  ]
  const p = buildPresencePair(augsburg, kiosk)

  assert.equal(p.presentKiosk.length, 1)
  assert.equal(p.presentKiosk[0].employee_id, "9112561")
  assert.equal(p.rostered.length, 1)
  assert.equal(p.rosteredNotPresent.length, 0)

  const s = buildCheckinStatus([augsburg], kiosk)
  // Both checked in with work mode WIC, so they are ad-hoc WIC presence with no centre
  // we may attribute them to — not "other staff", and not guessed into Augsburg.
  assert.deepEqual(s.needsCentreAssignment.map(r => r.full_name).sort(),
    ["Mohamad Nasir Amany", "Sina Sidharthan"])
  assert.equal(s.checkedInOffRoster, 2)
})

test("a blank-id kiosk row is never counted as present at a centre", () => {
  const c = centre("Hamburg", [{ id: "1", name: "A", start: "08:00" }])
  const p = buildPresencePair(c, [kioskRow("", "No Id", "ACTIVE", "WIC")])
  assert.equal(p.presentKiosk.length, 0)
})

test("a rostered agent who has not badged in shows under rosteredNotPresent", () => {
  const c = centre("Trier", [{ id: "1", name: "Suhrab Sadieqy", start: "08:00" }])
  const p = buildPresencePair(c, [kioskRow("1", "Suhrab Sadieqy", "NOT_CHECKED_IN")])
  assert.equal(p.presentKiosk.length, 0)
  assert.equal(p.rosteredNotPresent.length, 1)
})

// ── Delay uses the centre timezone, not the browser ──────────────────────────

test("delay is measured in the centre's timezone", () => {
  // 08:30 Europe/Berlin == 06:30 UTC. A browser in UTC used to compute 06:30 - 08:00
  // and report no delay; the centre is 30 minutes in.
  const cards = [centre("Hamburg", [{ id: "1", name: "A", start: "08:00" }], { country: "DE" })]
  const s = buildCheckinStatus(cards, [])
  const rows = buildZoneBRows(s, [], new Date("2026-10-01T06:30:00Z"))

  assert.equal(rows.length, 1)
  assert.equal(rows[0].shiftStarted, true)
  assert.equal(rows[0].delayMinutes, 30)
})

test("a shift that has not started yet reports no delay", () => {
  const cards = [centre("Hamburg", [{ id: "1", name: "A", start: "14:00" }])]
  const s = buildCheckinStatus(cards, [])
  const rows = buildZoneBRows(s, [], new Date("2026-10-01T06:30:00Z")) // 08:30 Berlin
  assert.equal(rows[0].shiftStarted, false)
  assert.equal(rows[0].delayMinutes, 0)
  assert.equal(groupZoneBRows(rows).upcoming.length, 1)
})

test("nowMinutesInZone handles midnight without rolling to 24:00", () => {
  assert.equal(nowMinutesInZone("Europe/Berlin", new Date("2026-10-01T22:00:00Z")), 0)
})

test("parseHhMm rejects non-times", () => {
  assert.equal(parseHhMm("08:00"), 480)
  assert.equal(parseHhMm("GSD"), null)
  assert.equal(parseHhMm(null), null)
  assert.equal(parseHhMm("25:00"), null)
})

// ── Zone B groups the same Expected set, dropping nothing ────────────────────

test("Zone B is built from the header's Not-yet-in set and drops no row", () => {
  const cards = [
    centre("Started", [{ id: "1", name: "Late", start: "07:00" }]),
    centre("Upcoming", [{ id: "2", name: "Later", start: "14:00" }]),
    centre("NoKiosk", [{ id: "3", name: "No record", start: "07:00" }]),
  ]
  const kiosk = [
    kioskRow("1", "Late", "NOT_CHECKED_IN"),
    kioskRow("2", "Later", "NOT_CHECKED_IN"),
  ]
  const s = buildCheckinStatus(cards, kiosk)
  const rows = buildZoneBRows(s, kiosk, new Date("2026-10-01T06:30:00Z")) // 08:30 Berlin
  const g = groupZoneBRows(rows)

  assert.equal(rows.length, s.notYetIn.length, "one row per not-yet-in agent")
  assert.equal(g.main.length, 1)
  assert.equal(g.upcoming.length, 1)
  assert.equal(g.noRecord.length, 1)
  assert.equal(g.main.length + g.upcoming.length + g.noRecord.length, rows.length, "nothing dropped")
})

test("main is sorted by descending delay", () => {
  const cards = [
    centre("A", [{ id: "1", name: "30m", start: "08:00" }]),
    centre("B", [{ id: "2", name: "90m", start: "07:00" }]),
  ]
  const kiosk = [
    kioskRow("1", "30m", "NOT_CHECKED_IN"),
    kioskRow("2", "90m", "NOT_CHECKED_IN"),
  ]
  const s = buildCheckinStatus(cards, kiosk)
  const g = groupZoneBRows(buildZoneBRows(s, kiosk, new Date("2026-10-01T06:30:00Z")))
  assert.deepEqual(g.main.map(r => r.delayMinutes), [90, 30])
})

// ── Manual changes and ad-hoc deployments ────────────────────────────────────
// RTM principle: a manual change is a legitimate decision, never an error and never a
// gap. An ad-hoc WIC check-in with no roster entry is presence, not "other staff".

test("an ad-hoc WIC check-in is Expected and Checked in, and offered an Assign", () => {
  // Erik Goecks 2026-10-01: kiosk work mode WIC, no centre on his roster row.
  const cards = [centre("Hamburg", [{ id: "1", name: "Rostered", start: "09:00" }])]
  const kiosk = [
    kioskRow("1", "Rostered", "ACTIVE", "WIC"),
    kioskRow("9074431", "Erik Goecks", "ACTIVE", "WIC", "Berlin"),
    kioskRow("9099999", "Someone Virtual", "ACTIVE", "HOME_OFFICE"),
  ]
  const s = buildCheckinStatus(cards, kiosk)

  // Both are Expected and Checked in; only the WIC one needs a centre assigning.
  assert.deepEqual(s.needsCentreAssignment.map(r => r.full_name), ["Erik Goecks"])
  assert.equal(s.checkedInOffRoster, 2)
  assert.equal(s.checkedIn.length, 3)
  assert.equal(s.expected.length, 3)
  assert.equal(s.checkedIn.length + s.notYetIn.length, s.expected.length, "invariant")
})

test("an ad-hoc WIC check-in is never guessed into a centre from its location text", () => {
  const cards = [centre("Berlin - Brückenstrasse", [{ id: "x", name: "Nobody", start: "09:00" }])]
  const erik = kioskRow("9074431", "Erik Goecks", "ACTIVE", "WIC", "Berlin - Brückenstrasse")
  const s = buildCheckinStatus(cards, [erik])

  assert.equal(s.needsCentreAssignment.length, 1, "offered an Assign to centre action")
  const p = buildPresencePair(cards[0], [erik])
  assert.equal(p.presentKiosk.length, 0, "not counted at the centre his location text names")
  // ...but the text is still shown to RTM.
  assert.equal(displayRowForKioskRecord(erik).location, "Berlin - Brückenstrasse")
})

test("a manual duty never raises 'check-in not recorded'", () => {
  const cards = [centre("Augsburg", [
    { id: "1", name: "Kamil Filipowicz", start: "07:00", manual: true },
    { id: "2", name: "Auto rostered", start: "07:00", notRecorded: true },
  ])]
  const s = buildCheckinStatus(cards, [])
  const rows = buildZoneBRows(s, [], new Date("2026-10-01T06:30:00Z"))
  const kamil = rows.find(r => r.employeeId === "1")!
  const auto = rows.find(r => r.employeeId === "2")!

  assert.equal(kamil.checkInNotRecorded, false, "manual rows carry no data-quality warning")
  assert.equal(auto.checkInNotRecorded, true, "the warning still works for automatic rows")
  assert.equal(kamil.delayMinutes, 90, "still shown with a real delay, not hidden")
})

test("a manual row's provenance reaches the display row for the neutral label", () => {
  const cards = [centre("Augsburg", [
    { id: "1", name: "Kamil Filipowicz", start: "07:00", manual: true },
  ])]
  const s = buildCheckinStatus(cards, [])
  const row = displayRowForAgent(s.expected[0], undefined, "NOT_CHECKED_IN")

  assert.equal(row.isManualChange, true)
  assert.equal(row.manualChangeBy, "S60028")
  assert.equal(row.manualChangeAt, "2026-10-01 07:05")
})

// ── RTM decisions 2026-10-01: populations and the test account ──────────────

// ── Group separation 2026-10-06 ──────────────────────────────────────────────

test("Voice/VWIC/BO/unclassified kiosk check-ins never reach /wic-attendance", () => {
  const cards = [centre("Hamburg", [{ id: "1", name: "Rostered", start: "09:00" }])]
  const kiosk = [
    kioskRow("1", "Rostered WIC", "ACTIVE", "WIC", null, null, "WIC"),
    kioskRow("2", "Voice agent", "ACTIVE", "HOME_OFFICE", null, null, "VOICE"),
    kioskRow("3", "VWIC agent", "ACTIVE", "HOME_OFFICE", null, null, "VWIC"),
    kioskRow("4", "BO agent", "ACTIVE", "HOME_OFFICE", null, null, "BO"),
    kioskRow("5", "Unclassified", "ACTIVE", "HOME_OFFICE", null, null, null),
  ]
  const s = buildCheckinStatus(cards, kiosk)

  assert.equal(s.checkedIn.length, 1, "only the WIC-group row is counted")
  assert.deepEqual(s.checkedIn.map(a => a.employeeId), ["1"])
  assert.equal(s.expected.length, 1)
  assert.equal(s.checkedIn.length + s.notYetIn.length, s.expected.length, "invariant")
})

test("Checked in = ALL kiosk ACTIVE/DONE, whatever the work mode or roster", () => {
  const cards = [centre("Hamburg", [{ id: "1", name: "Rostered", start: "09:00" }])]
  const kiosk = [
    kioskRow("1", "Rostered", "ACTIVE", "WIC"),
    kioskRow("2", "Ad hoc WIC", "ACTIVE", "WIC"),
    kioskRow("3", "Virtual", "DONE", "HOME_OFFICE"),
    kioskRow("4", "No work mode", "ACTIVE", null),
  ]
  const s = buildCheckinStatus(cards, kiosk)

  assert.equal(s.checkedIn.length, 4, "no filtering by work mode or roster")
  assert.equal(s.expected.length, 4)
  assert.equal(s.notYetIn.length, 0)
  assert.equal(s.checkedIn.length + s.notYetIn.length, s.expected.length, "invariant")
  assert.equal(s.checkedInOnRoster, 1)
  assert.equal(s.checkedInOffRoster, 3)
  // Only the WIC-mode off-roster person needs a centre.
  assert.deepEqual(s.needsCentreAssignment.map(r => r.full_name), ["Ad hoc WIC"])
})

test("a check-in with no work mode recorded is still counted", () => {
  const s = buildCheckinStatus([], [kioskRow("7", "No mode", "ACTIVE", null)])
  assert.equal(s.checkedIn.length, 1)
  assert.equal(s.expected.length, 1)
  assert.equal(s.needsCentreAssignment.length, 0, "no mode => no centre to assign")
})

test("the invariant holds for every mix", () => {
  const cards = [centre("A", [
    { id: "r1", name: "In", start: "08:00" },
    { id: "r2", name: "Out", start: "08:00" },
    { id: "r3", name: "On AL", start: "AL" },
    { id: "r4", name: "On GSD", start: "GSD" },
  ])]
  const kiosk = [
    kioskRow("r1", "In", "ACTIVE", "WIC"),
    kioskRow("r3", "On AL", "ACTIVE", "WIC"),   // absent on the roster yet checked in
    kioskRow("x1", "Off roster", "DONE", "HOME_OFFICE"),
    kioskRow("", "Blank", "ACTIVE", "WIC"),
  ]
  const s = buildCheckinStatus(cards, kiosk)

  assert.equal(s.checkedIn.length + s.notYetIn.length, s.expected.length, "invariant")
  assert.equal(s.blankKioskIdCount, 1)
  // r3 is filtered out of the roster set but checked in, so it still lands in Expected —
  // that is what makes the identity unconditional.
  assert.ok(s.expected.some(a => a.employeeId === "r3"))
  assert.ok(!s.rosterExpected.some(a => a.employeeId === "r3"))
})

test("the test-account list is empty: 9074466 is a normal agent now", () => {
  // Group separation 2026-10-06: 9074466 (Stojnic Nebojsa) is a normal BO-group
  // agent. It is the agent_group filter, never an id list, that keeps him off
  // /wic-attendance; Remote Attendance shows him with the other BO agents.
  assert.equal(TEST_EMPLOYEE_IDS.size, 0, "the exclusion list is intentionally empty")
  assert.equal(isTestEmployee("9074466"), false, "no longer an excluded test account")
  assert.equal(isTestEmployee(null), false)
  assert.equal(isTestEmployee(undefined), false)
  assert.equal(isTestEmployee("1"), false)

  const cards = [centre("Hamburg", [{ id: "1", name: "Real agent", start: "09:00" }])]
  const kiosk = [
    kioskRow("1", "Real agent", "ACTIVE", "WIC"),
    kioskRow("9074466", "Stojnic Nebojsa", "DONE", "HOME_OFFICE", null, null, "BO"),
  ]
  const s = buildCheckinStatus(cards, kiosk)

  // Only the WIC-group row reaches this page; the BO check-in belongs to Remote Attendance.
  assert.equal(s.checkedIn.length, 1)
  assert.deepEqual(s.checkedIn.map(a => a.employeeId), ["1"])
  assert.equal(s.checkedIn.length + s.notYetIn.length, s.expected.length, "invariant")
})

test("an explicit manual check-in/logout is visible without becoming roster coverage", () => {
  // The isManualKioskEvent() escape hatch in buildCheckinStatus stays in place for a
  // future excluded test account. With the list empty it is inert and an explicit
  // manual event is simply a normal WIC-group kiosk row on this page.
  const cards = [centre("Hamburg", [{ id: "1", name: "Rostered", start: "09:00" }])]
  for (const [status, note] of [
    ["ACTIVE", "Manual check-in by Operator / Manuell durch Operator"],
    ["DONE", "Manual logout by Operator / Manuell durch Operator"],
  ]) {
    const record: CheckinKioskRecord = {
      ...kioskRow("9074466", "Stojnic Nebojsa", status, null, null, null, "WIC"),
      status_note: note,
      checkin_time: "2026-10-05 09:47:54",
      checkout_time: status === "DONE" ? "2026-10-05 14:10:00" : null,
    }
    const s = buildCheckinStatus(cards, [record, record])
    assert.equal(s.checkedIn.length, 1, "one real manual check-in, not duplicate feed rows")
    assert.equal(s.checkedIn[0].employeeId, "9074466")
    assert.equal(s.checkedInOffRoster, 1, "off the roster => no centre coverage")
    assert.equal(s.rosterExpected.length, 1, "the real roster row is untouched")
    assert.equal(s.expected.length, s.checkedIn.length + s.notYetIn.length)
    assert.equal(buildPresencePair(cards[0], [record]).presentKiosk.length, 0)
    const display = displayRowForAgent(s.checkedIn[0], record, "ACTIVE")
    assert.equal(display.checkinTime, record.checkin_time)
    assert.equal(display.checkoutTime, record.checkout_time)
    assert.equal(display.status, status)
  }
})

test("Zone B only lists roster agents, never off-roster check-ins", () => {
  const cards = [centre("Hamburg", [{ id: "1", name: "Late", start: "07:00" }])]
  const kiosk = [kioskRow("x", "Off roster", "ACTIVE", "HOME_OFFICE")]
  const s = buildCheckinStatus(cards, kiosk)
  const rows = buildZoneBRows(s, kiosk, new Date("2026-10-01T06:30:00Z"))
  assert.deepEqual(rows.map(r => r.employeeId), ["1"])
})


// ── Break / PAUSE (2026-10-02 regression) ───────────────────────────────

test("a PAUSE kiosk row counts as checked in", () => {
  const cards = [centre("Hamburg", [
    { id: "1", name: "On break", start: "07:00" },
    { id: "2", name: "At desk", start: "07:00" },
  ])]
  const kiosk = [
    kioskRow("1", "On break", ON_BREAK_STATUS, "WIC"),
    kioskRow("2", "At desk", "ACTIVE", "WIC"),
  ]
  const s = buildCheckinStatus(cards, kiosk)
  assert.deepEqual(s.checkedIn.map(a => a.employeeId).sort(), ["1", "2"])
  assert.equal(s.checkedInOnRoster, 2)
})

test("a paused agent never appears in Not yet in, nor in the Zone B table", () => {
  const cards = [centre("Hamburg", [
    { id: "1", name: "On break", start: "07:00" },
    { id: "2", name: "Truly absent", start: "07:00" },
  ])]
  // Marcus Rusch's shape on 2026-10-02: checked in 07:18, last event PAUSE at 12:27.
  const kiosk = [kioskRow("1", "On break", ON_BREAK_STATUS, "WIC")]
  const s = buildCheckinStatus(cards, kiosk)

  assert.deepEqual(s.notYetIn.map(a => a.employeeId), ["2"], "only the agent with no row")
  const rows = buildZoneBRows(s, kiosk, new Date("2026-10-02T10:48:00Z"))
  assert.deepEqual(rows.map(r => r.employeeId), ["2"], "no PAUSE row in the late table")
})

test("per-centre presence keeps a paused agent, drops a checked-out one", () => {
  const c = centre("Hamburg", [
    { id: "1", name: "On break", start: "07:00" },
    { id: "2", name: "Went home", start: "07:00" },
    { id: "3", name: "At desk", start: "07:00" },
  ])
  const kiosk = [
    kioskRow("1", "On break", ON_BREAK_STATUS, "WIC"),
    kioskRow("2", "Went home", "DONE", "WIC"),
    kioskRow("3", "At desk", "ACTIVE", "WIC"),
  ]
  const p = buildPresencePair(c, kiosk)
  assert.deepEqual(p.presentKiosk.map(r => r.employee_id).sort(), ["1", "3"],
    "PAUSE is present (on site, on a break); DONE is not (checked out)")
  assert.deepEqual(p.rosteredNotPresent.map(a => a.employeeId), ["2"])
})

test("the invariant still holds with breaks in every mix", () => {
  const mixes: CheckinKioskRecord[][] = [
    [],
    [kioskRow("1", "A", ON_BREAK_STATUS, "WIC")],
    [kioskRow("1", "A", ON_BREAK_STATUS, null), kioskRow("2", "B", "DONE", "HOME_OFFICE")],
    [kioskRow("1", "A", "ACTIVE", "WIC"), kioskRow("2", "B", ON_BREAK_STATUS, "WIC"),
     kioskRow("99", "Off roster, on break", ON_BREAK_STATUS, "WIC")],
    [kioskRow("", "Blank id", ON_BREAK_STATUS, "WIC"),
     kioskRow("9074466", "Test account", ON_BREAK_STATUS, "WIC"),
     kioskRow("1", "A", ON_BREAK_STATUS, "WIC")],
  ]
  const cards = [centre("Hamburg", [
    { id: "1", name: "A", start: "07:00" },
    { id: "2", name: "B", start: "09:00" },
  ])]
  for (const kiosk of mixes) {
    const s = buildCheckinStatus(cards, kiosk)
    assert.equal(s.checkedIn.length + s.notYetIn.length, s.expected.length,
      `Expected = CheckedIn + NotYetIn for mix ${JSON.stringify(kiosk.map(r => r.employee_id))}`)
  }
})

test("isOnBreak and the neutral label key", () => {
  assert.equal(ON_BREAK_STATUS, "PAUSE", "the kiosk's raw status, passed through by server.py")
  assert.ok(isOnBreak("PAUSE"))
  for (const s of ["ACTIVE", "DONE", "NOT_CHECKED_IN", "SICK", "", null, undefined]) {
    assert.ok(!isOnBreak(s), `${String(s)} is not a break`)
  }
  assert.equal(ON_BREAK_LABEL_KEY, "attendance.onBreak")
  // The label reaches the display rows the badge is rendered from.
  const rec = kioskRow("1", "On break", ON_BREAK_STATUS, "WIC")
  assert.ok(isOnBreak(displayRowForKioskRecord(rec).status))
  const cards = [centre("Hamburg", [{ id: "1", name: "On break", start: "07:00" }])]
  const expected = buildCheckinStatus(cards, [rec]).expected[0]
  assert.ok(isOnBreak(displayRowForAgent(expected, rec, "NOT_CHECKED_IN").status))
})

// ── Break badge: neutral label + duration ──────────────────────────────

// 13:08 Berlin time, the moment the symptom was observed on 2026-10-02.
const AT_1308 = new Date("2026-10-02T11:08:00Z")

test("break duration is measured from the PAUSE event, in the centre's timezone", () => {
  // Marcus Rusch: ON_SHIFT 07:18:24, PAUSE 12:27:48, no resume.
  const rec = kioskRow("9130649", "Marcus Rusch", ON_BREAK_STATUS, "WIC", null, "2026-10-02 12:27:48")
  assert.equal(breakMinutes(rec, centreTimeZone("DE"), AT_1308), 41, "Pause 41m")
  assert.equal(breakMinutes(rec, centreTimeZone("NL"), AT_1308), 41, "NL never diverges from DE")
})

test("no duration is shown rather than a guessed or negative one", () => {
  const at = AT_1308
  const tz = centreTimeZone("DE")
  assert.equal(breakMinutes(kioskRow("1", "A", "ACTIVE", "WIC", null, "2026-10-02 12:27:48"), tz, at), null,
    "not on a break")
  assert.equal(breakMinutes(kioskRow("1", "A", "DONE", "WIC", null, "2026-10-02 12:27:48"), tz, at), null,
    "checked out, not on a break")
  assert.equal(breakMinutes(kioskRow("1", "A", ON_BREAK_STATUS, "WIC", null, null), tz, at), null,
    "kiosk sent no last_event_time")
  assert.equal(breakMinutes(kioskRow("1", "A", ON_BREAK_STATUS, "WIC", null, "rubbish"), tz, at), null,
    "unparseable timestamp")
  assert.equal(breakMinutes(kioskRow("1", "A", ON_BREAK_STATUS, "WIC", null, "2026-10-02 13:30:00"), tz, at), null,
    "clock skew would make it negative")
  assert.equal(breakMinutes(null, tz, at), null)
  assert.equal(breakMinutes(kioskRow("1", "A", ON_BREAK_STATUS, "WIC", null, "2026-10-02 13:08:00"), tz, at), 0,
    "a break that just started is 0, not null")
})

test("the break start reaches the display rows the badge renders from", () => {
  const rec = kioskRow("1", "On break", ON_BREAK_STATUS, "WIC", null, "2026-10-02 12:27:48")
  const cards = [centre("Hamburg", [{ id: "1", name: "On break", start: "07:00" }])]
  const expected = buildCheckinStatus(cards, [rec]).expected[0]

  for (const row of [displayRowForKioskRecord(rec), displayRowForAgent(expected, rec, "NOT_CHECKED_IN")]) {
    assert.ok(isOnBreak(row.status), "the badge condition")
    assert.equal(row.lastEventTime, "2026-10-02 12:27:48", "the duration source")
  }
  // A row with no kiosk record carries no break start and so renders no chip.
  assert.equal(displayRowForAgent(expected, null, "NOT_CHECKED_IN").lastEventTime, null)
})

test("the break labels are neutral keys, not the 'Not in' status key", () => {
  assert.equal(ON_BREAK_LABEL_KEY, "attendance.onBreak")
  assert.equal(ON_BREAK_DURATION_KEY, "attendance.onBreakFor")
  for (const key of [ON_BREAK_LABEL_KEY, ON_BREAK_DURATION_KEY]) {
    assert.ok(!key.includes("status."), `${key} must not reuse a kioskDrawer status key`)
  }
})

test("both locales define the break labels (run from Frontend/)", () => {
  for (const [lang, label, duration] of [
    ["en", "On break", "Pause {{n}}m"],
    ["de", "In Pause", "Pause {{n}} Min."],
  ] as const) {
    const json = JSON.parse(readFileSync(`src/i18n/locales/${lang}/common.json`, "utf8"))
    const a = json.attendance
    assert.equal(a.onBreak, label, `${lang} ${ON_BREAK_LABEL_KEY}`)
    assert.equal(a.onBreakFor, duration, `${lang} ${ON_BREAK_DURATION_KEY}`)
    assert.ok(a.onBreakFor.includes("{{n}}"), `${lang} duration must interpolate the minutes`)
  }
})

// ── Result ───────────────────────────────────────────────────────────────────

console.log("")
console.log(`${passes} passed, ${failures} failed`)
if (failures > 0) process.exit(1)
