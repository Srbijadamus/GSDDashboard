// Tests for the Assign Agent dialog's per-DATE WIC hours resolution.
//
// This project has no JS test runner installed, so these run on the TypeScript compiler
// that IS installed plus plain node:
//
//     cd Frontend && npm run test:wic-open-hours
//
// No framework, no new dependency, works offline.

import assert from "node:assert/strict"
import { resolveDayHours, type WicOpenDay } from "./wicOpenHours.js"

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

// The canonical case: a centre whose weekday pattern says closed (the payload already
// resolved the pattern away, so all we see is the DATE's answer).
function day(date: string, locations: WicOpenDay["locations"]): WicOpenDay {
  return { date, locations }
}

const OPEN_EXCEPTION: WicOpenDay[] = [
  day("2026-10-12", [
    { locationCode: "LOC_A", isOpen: true, openIntervals: [{ openTime: "10:00", closeTime: "16:00" }] },
  ]),
]

const CLOSED_EXCEPTION: WicOpenDay[] = [
  day("2026-10-12", [{ locationCode: "LOC_A", isOpen: false, openIntervals: [] }]),
]

// ── Tests ────────────────────────────────────────────────────────────────────

// 1. THE bug: an open exception on a normally-closed weekday must NOT show the red banner;
//    it shows the exception's hours.
test("open exception day: not closed, hours come from the exception", () => {
  const r = resolveDayHours(OPEN_EXCEPTION, "2026-10-12", "LOC_A")
  assert.equal(r.closed, false)
  assert.equal(r.start, "10:00")
  assert.equal(r.end, "16:00")
})

// 2. A closed exception on a normally-open weekday must show the banner even though the
//    weekly pattern would have had hours.
test("closed exception day: banner is shown", () => {
  const r = resolveDayHours(CLOSED_EXCEPTION, "2026-10-12", "LOC_A")
  assert.equal(r.closed, true)
  assert.equal(r.start, "")
  assert.equal(r.end, "")
})

// 3. Two-interval exception: the window spans first open through last close (the break is
//    not the end of the duty).
test("two-interval exception spans first open to last close", () => {
  const days = [
    day("2026-10-12", [
      {
        locationCode: "LOC_A",
        isOpen: true,
        openIntervals: [
          { openTime: "07:30", closeTime: "12:00" },
          { openTime: "12:30", closeTime: "16:00" },
        ],
      },
    ]),
  ]
  const r = resolveDayHours(days, "2026-10-12", "LOC_A")
  assert.equal(r.closed, false)
  assert.equal(r.start, "07:30")
  assert.equal(r.end, "16:00")
})

// 4. The requested date wins over another day in the payload (horizon > 1).
test("picks the day matching the requested date", () => {
  const days: WicOpenDay[] = [
    day("2026-10-12", [{ locationCode: "LOC_A", isOpen: false, openIntervals: [] }]),
    day("2026-10-13", [
      { locationCode: "LOC_A", isOpen: true, openIntervals: [{ openTime: "09:00", closeTime: "17:00" }] },
    ]),
  ]
  const r = resolveDayHours(days, "2026-10-13", "LOC_A")
  assert.equal(r.closed, false)
  assert.equal(r.start, "09:00")
  assert.equal(r.end, "17:00")
})

// 5. An unknown location is closed — the dialog clears the times and shows the banner
//    rather than silently writing centre hours.
test("unknown location is treated as closed", () => {
  const r = resolveDayHours(OPEN_EXCEPTION, "2026-10-12", "NOPE")
  assert.equal(r.closed, true)
})

// 6. An open centre without usable times is open but leaves the fields empty.
test("open without usable times: not closed, empty times", () => {
  const days = [day("2026-10-12", [{ locationCode: "LOC_A", isOpen: true, openIntervals: [] }])]
  const r = resolveDayHours(days, "2026-10-12", "LOC_A")
  assert.equal(r.closed, false)
  assert.equal(r.start, "")
  assert.equal(r.end, "")
})

// 7. Missing payload / missing selection must not crash and must read as closed.
test("missing payload, date or location is closed", () => {
  assert.equal(resolveDayHours(null, "2026-10-12", "LOC_A").closed, true)
  assert.equal(resolveDayHours([], "2026-10-12", "LOC_A").closed, true)
  assert.equal(resolveDayHours(OPEN_EXCEPTION, "", "LOC_A").closed, true)
  assert.equal(resolveDayHours(OPEN_EXCEPTION, "2026-10-12", "").closed, true)
})

console.log(`\n${passes} passed, ${failures} failed`)
if (failures > 0) process.exit(1)
