// Tests for the Bulk RTM Entry "Paste & Parse" rules.
//
// This project has no JS test runner installed, so these run on the TypeScript
// compiler that IS installed plus plain node:
//
//     cd Frontend && npm run test:bulk-rtm
//
// No framework, no new dependency, works offline.

import assert from "node:assert/strict"
import {
  BULK_RTM_CATEGORIES,
  BULK_RTM_SHIFT_TYPE,
  BULK_RTM_WRITE_KIND,
  OPEN_ENDED_END_DATE,
  buildBulkRtmWrites,
  isWritableStatus,
  localToday,
  parseAgentLine,
  resolveAgentLine,
  resolveAgentList,
  summarizeRows,
} from "./bulkRtmParse.js"
import { resolveEmployee } from "./resolveEmployee.js"

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

const EMPLOYEES = [
  { employeeId: "1001", fullName: "Christian Pastors" },
  { employeeId: "1002", fullName: "Dominik Bajic" },
  { employeeId: "1003", fullName: "Tri Toan Nguyen" },
  { employeeId: "1004", fullName: "Negin Bazmi" },
  { employeeId: "1005", fullName: "Aleksandrina Dencheva" },
  { employeeId: "1006", fullName: "Elena Schlosser" },
  { employeeId: "1007", fullName: null },
]

const DAY = "2026-10-09"

// ── Parsing: one name per line, no date, no time ─────────────────────────────

test("a plain name is the whole line — nothing is ignored", () => {
  const p = parseAgentLine("Christian Pastors")
  assert.equal(p.rawName, "Christian Pastors")
  assert.equal(p.ignoredTimeOrDate, false)
})

test("leading/trailing and repeated spaces are collapsed", () => {
  const p = parseAgentLine("   Elena    Schlosser  ")
  assert.equal(p.rawName, "Elena Schlosser")
  assert.equal(p.ignoredTimeOrDate, false)
})

test("a pasted shift time is ignored, the name survives", () => {
  const p = parseAgentLine("Christian Pastors 08:00 - 17:00")
  assert.equal(p.rawName, "Christian Pastors")
  assert.equal(p.ignoredTimeOrDate, true)
})

test("a pasted time range without spaces, and a night range, are ignored", () => {
  assert.equal(parseAgentLine("Dominik Bajic 08:00-17:00").rawName, "Dominik Bajic")
  assert.equal(parseAgentLine("Tri Toan Nguyen 22:00 - 07:00").rawName, "Tri Toan Nguyen")
  assert.equal(parseAgentLine("Tri Toan Nguyen 22:00 - 07:00").ignoredTimeOrDate, true)
})

test("a pasted date is ignored, the name survives", () => {
  const iso = parseAgentLine("Christian Pastors 2026-10-09")
  assert.equal(iso.rawName, "Christian Pastors")
  assert.equal(iso.ignoredTimeOrDate, true)

  const de = parseAgentLine("Dominik Bajic 09.10.2026")
  assert.equal(de.rawName, "Dominik Bajic")
  assert.equal(de.ignoredTimeOrDate, true)
})

test("a pasted date and time together are both ignored", () => {
  const p = parseAgentLine("Elena Schlosser 2026-10-09 08:00 - 17:00")
  assert.equal(p.rawName, "Elena Schlosser")
  assert.equal(p.ignoredTimeOrDate, true)
})

test("a line that is only a time keeps an empty name and is never resolved", () => {
  const p = parseAgentLine("08:00 - 17:00")
  assert.equal(p.rawName, "")
  assert.equal(p.ignoredTimeOrDate, true)
})

// ── Recognition: case and whitespace insensitive ─────────────────────────────

test("lower case and extra spaces still recognise the employee", () => {
  const r = resolveAgentLine("  christian   PASTORS ", EMPLOYEES)
  assert.equal(r.status, "resolved")
  assert.equal(r.resolved?.employeeId, "1001")
})

test("swapped first/last name is recognised (order-insensitive)", () => {
  const r = resolveAgentLine("Pastors Christian", EMPLOYEES)
  assert.equal(r.status, "resolved-corrected")
  assert.equal(r.resolved?.employeeId, "1001")
})

test("umlauts/diacritics differences do not block recognition", () => {
  const r = resolveAgentLine("elena schlosser", EMPLOYEES)
  assert.equal(r.resolved?.employeeId, "1006")
})

// ── A typo is never corrected automatically ──────────────────────────────────

test("the three reported typos give a suggestion, never an automatic pick", () => {
  const cases: Array<[string, string]> = [
    ["Negin BazmiA", "1004"],
    ["leksandrina Dencheva", "1005"],
    ["Elena  Schlosse", "1006"],
  ]
  for (const [typed, expectedId] of cases) {
    const r = resolveAgentLine(typed, EMPLOYEES)
    assert.equal(r.resolved, null, `${typed} must not be auto-picked`)
    assert.equal(r.status, "suggest", `${typed} must be offered as a suggestion`)
    assert.ok(
      (r.suggestions ?? []).some(s => s.employeeId === expectedId),
      `${typed} must suggest ${expectedId}`
    )
  }
})

test("a name with no close candidate is unresolved and unassigned", () => {
  const r = resolveAgentLine("Zzz Qqqq", EMPLOYEES)
  assert.equal(r.resolved, null)
  assert.equal(r.status, "unresolved")
})

test("the same typos DO auto-correct for callers that allow it (Vacations import)", () => {
  const r = resolveEmployee("Negin BazmiA", EMPLOYEES)
  assert.equal(r.status, "resolved-corrected")
  assert.equal(r.resolved?.employeeId, "1004")
})

// ── The list: one row per name, bad rows do not stop good rows ───────────────

test("a pasted list yields one row per line and reports the bad one", () => {
  const rows = resolveAgentList(
    ["Christian Pastors", "Dominik Bajic 08:00 - 17:00", "Elena  Schlosse", "", "Tri Toan Nguyen"].join("\n"),
    EMPLOYEES
  )
  assert.equal(rows.length, 4)
  assert.equal(rows[0].status, "resolved")
  assert.equal(rows[1].status, "resolved")
  assert.equal(rows[1].ignoredTimeOrDate, true)
  assert.equal(rows[2].status, "suggest")
  assert.equal(rows[3].status, "resolved")

  const s = summarizeRows(rows)
  assert.equal(s.recognized, 3)
  assert.equal(s.notRecognized, 1)
})

test("only recognised rows produce a write — a typo is never saved", () => {
  const rows = resolveAgentList(
    ["Christian Pastors", "Elena  Schlosse", "Dominik Bajic"].join("\n"),
    EMPLOYEES
  )
  const writable = rows.filter(r => isWritableStatus(r.status))
  assert.equal(writable.length, 2)
  assert.deepEqual(writable.map(r => r.resolved?.employeeId), ["1001", "1002"])
  // The unrecognised row still carries its reason and its nearest candidates.
  const typo = rows[1]
  assert.equal(isWritableStatus(typo.status), false)
  assert.equal(typo.rawName, "Elena Schlosse")
  assert.ok((typo.suggestions ?? []).length > 0)
})

// ── What a row writes, per category ──────────────────────────────────────────

test("every category is covered by a write kind", () => {
  for (const c of BULK_RTM_CATEGORIES) {
    assert.ok(BULK_RTM_WRITE_KIND[c], `${c} has a write kind`)
  }
})

test("AL / OFF / CD / NIGHT write the manual shift route for today, whole day, no time", () => {
  for (const category of ["AL", "OFF", "CD", "NIGHT"] as const) {
    const writes = buildBulkRtmWrites(category, { employeeId: "1001", displayName: "Christian Pastors", date: DAY })
    assert.equal(writes.length, 1)
    const w = writes[0]
    assert.equal(w.url, "/api/shifts/assign/manual")
    assert.equal(w.body.employeeId, "1001")
    assert.equal(w.body.shiftDate, DAY)
    assert.equal(w.body.shiftType, BULK_RTM_SHIFT_TYPE[category])
    assert.equal(w.body.shiftStart, null)
    assert.equal(w.body.shiftEnd, null)
  }
  assert.equal(BULK_RTM_SHIFT_TYPE.NIGHT, "WORKING")
})

test("SL writes the SickLeave record exactly like the SickLeave page", () => {
  const writes = buildBulkRtmWrites("SL", { employeeId: "1002", displayName: "Dominik Bajic", date: DAY })
  assert.equal(writes.length, 1)
  assert.equal(writes[0].url, "/api/sickleave")
  assert.equal(writes[0].body.employeeId, "1002")
  assert.equal(writes[0].body.startDate, DAY)
  assert.equal(writes[0].body.endDate, OPEN_ENDED_END_DATE)
  assert.equal(writes[0].body.type, "Self")
})

test("BO writes the BO entry for today without a time", () => {
  const writes = buildBulkRtmWrites("BO", { employeeId: "1003", displayName: "Tri Toan Nguyen", date: DAY })
  assert.equal(writes.length, 1)
  assert.equal(writes[0].url, "/api/bo-list")
  assert.equal(writes[0].body.date, DAY)
  assert.equal(writes[0].body.employeeName, "Tri Toan Nguyen")
  assert.equal(writes[0].body.shiftStart, "")
  assert.equal(writes[0].body.shiftEnd, "")
})

test("WIC writes the assignment for today with no time and no invented location", () => {
  const withLoc = buildBulkRtmWrites("WIC", {
    employeeId: "1004", displayName: "Negin Bazmi", date: DAY, locationCode: "ESSEN-TK",
  })
  assert.equal(withLoc.length, 1)
  assert.equal(withLoc[0].url, "/api/wic/assignments")
  assert.equal(withLoc[0].body.locationCode, "ESSEN-TK")
  assert.equal(withLoc[0].body.date, DAY)
  assert.equal(withLoc[0].body.shiftStart, null)
  assert.equal(withLoc[0].body.shiftEnd, null)

  const withoutLoc = buildBulkRtmWrites("WIC", { employeeId: "1004", displayName: "Negin Bazmi", date: DAY })
  assert.equal(withoutLoc.length, 0)
})

// ── The day ──────────────────────────────────────────────────────────────────

test("localToday uses local calendar fields, not the UTC date", () => {
  // 00:30 Berlin on 2026-10-09 is still 2026-10-08 in UTC (22:30). The entry
  // must land on the local day.
  const local = new Date(2026, 9, 9, 0, 30, 0)
  assert.equal(localToday(local), "2026-10-09")
  const evening = new Date(2026, 9, 9, 23, 45, 0)
  assert.equal(localToday(evening), "2026-10-09")
  assert.equal(localToday(new Date(2026, 0, 1, 0, 0, 1)), "2026-01-01")
})

console.log(`\n${passes} passed, ${failures} failed`)
if (failures > 0) process.exit(1)
