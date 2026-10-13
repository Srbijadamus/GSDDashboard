// Tests for the roster error text (EN/DE mapping of the server's machine code).
//
//     cd Frontend && npm run test:roster-errors
//
// No framework, no new dependency, works offline — the same pattern as
// substituteAccept.test.ts / bulkRtmParse.test.ts.

import assert from "node:assert/strict"
import {
  ROSTER_ERROR_KEYS,
  RosterApiError,
  parseRosterError,
  rosterErrorText,
  type TranslateFn,
} from "./rosterErrors.js"

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

// Records which i18n key the UI would render (EN or DE, depending on the locale).
const t: TranslateFn = (key) => `t:${key}`

// The exact response body the backend now sends for a WIC batch without a time.
const wicTimeBody = JSON.stringify({
  error: "WIC mode requires the agent's own shift: send shiftStart and shiftEnd (HH:mm). " +
    "The centre's opening hours are never written as the agent's shift.",
  code: "WIC_SHIFT_TIME_REQUIRED",
})

test("parses the code out of the refusal body", () => {
  const body = parseRosterError(400, wicTimeBody)
  assert.equal(body.code, "WIC_SHIFT_TIME_REQUIRED")
  assert.ok((body.error ?? "").includes("shiftStart"))
})

test("maps the WIC time code to its i18n key (EN/DE via the locale files)", () => {
  assert.equal(ROSTER_ERROR_KEYS["WIC_SHIFT_TIME_REQUIRED"], "roster.errors.wicShiftTimeRequired")
  const err = new RosterApiError(400, parseRosterError(400, wicTimeBody))
  assert.equal(rosterErrorText(err, t), "t:roster.errors.wicShiftTimeRequired")
})

test("401 and 403 get their own localised text", () => {
  assert.equal(rosterErrorText(new RosterApiError(401, { error: "Not authenticated" }), t),
    "t:roster.errors.unauthenticated")
  assert.equal(rosterErrorText(new RosterApiError(403, { error: "Forbidden" }), t),
    "t:roster.errors.forbidden")
})

test("an unknown code keeps the server's own sentence", () => {
  const err = new RosterApiError(400, { error: "Unknown or inactive WIC location: X", code: "SOMETHING_NEW" })
  assert.equal(rosterErrorText(err, t), "Unknown or inactive WIC location: X")
})

test("a non-JSON body is not swallowed", () => {
  const body = parseRosterError(500, "<html>boom</html>")
  assert.equal(body.error, "<html>boom</html>")
  assert.equal(rosterErrorText(new RosterApiError(500, body), t), "<html>boom</html>")
})

test("an empty body still yields a readable message", () => {
  assert.equal(parseRosterError(503, "").error, "API 503")
})

test("a plain Error keeps its message (no code path)", () => {
  assert.equal(rosterErrorText(new Error("Something else"), t), "Something else")
})

console.log(`\n${passes} passed, ${failures} failed`)
if (failures > 0) process.exit(1)
