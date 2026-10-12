// Tests for berlinStamp — the browser-side "show Europe/Berlin" rule.
//
// Runs on the TypeScript compiler that is installed plus plain node (no framework,
// no new dependency, works offline):
//
//     cd Frontend && npm run test:berlin-time

import assert from "node:assert/strict"

// Run the whole file in a deliberately non-Berlin process zone: if the helper used
// the local zone (toLocaleString / getHours), every assertion below would fail.
process.env.TZ = "America/New_York"

import { BERLIN_TIME_ZONE, berlinStamp } from "./berlinTime.js"

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

test("summer time: a UTC instant is +02:00 in Berlin (CEST)", () => {
  assert.equal(berlinStamp(new Date("2026-07-01T10:00:00Z")), "2026-07-01 12:00:00")
})

test("winter time: the same UTC instant is +01:00 in Berlin (CET)", () => {
  assert.equal(berlinStamp(new Date("2026-01-15T10:00:00Z")), "2026-01-15 11:00:00")
})

test("the Berlin day starts at midnight Berlin, not at midnight UTC", () => {
  // 22:30 UTC in summer is already the next day in Berlin.
  assert.equal(berlinStamp(new Date("2026-07-01T22:30:00Z")), "2026-07-02 00:30:00")
})

test("the DST change is the platform's rule, not a fixed offset", () => {
  // Last Sunday of March 2026: 00:59 UTC is 01:59 Berlin (CET), 01:00 UTC is 03:00 (CEST).
  assert.equal(berlinStamp(new Date("2026-03-29T00:59:00Z")), "2026-03-29 01:59:00")
  assert.equal(berlinStamp(new Date("2026-03-29T01:00:00Z")), "2026-03-29 03:00:00")
})

test("the output ignores the viewer's own time zone", () => {
  const instant = new Date("2026-10-12T08:31:28Z")
  const local = instant.toLocaleString("sv-SE")
  assert.equal(berlinStamp(instant), "2026-10-12 10:31:28")
  assert.notEqual(local, berlinStamp(instant))
  assert.equal(BERLIN_TIME_ZONE, "Europe/Berlin")
})

console.log(`\n${passes} passed, ${failures} failed`)
if (failures > 0) process.exit(1)
