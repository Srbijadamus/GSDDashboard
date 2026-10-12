// Tests for the accept-substitute error text.
//
// This project has no JS test runner installed, so these run on the TypeScript
// compiler that IS installed plus plain node:
//
//     cd Frontend && npm run test:substitute-accept
//
// No framework, no new dependency, works offline.

import assert from "node:assert/strict"
import {
  SUBSTITUTE_ACCEPT_ERROR_KEYS,
  SubstituteAcceptError,
  parseSubstituteAcceptError,
  substituteAcceptErrorText,
  type TranslateFn,
} from "./substituteAccept.js"

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

// Records which i18n key and interpolation values the UI would render.
const t: TranslateFn = (key, options) => `${key} ${JSON.stringify(options ?? {})}`

// The exact live shape of the 2026-10-12 rejection.
const manualBody = JSON.stringify({
  code: "MANUAL_ENTRY_PROTECTED",
  error: "Angelika Weber already has a duty today: GSD. It is a protected manual entry - edit the cell directly instead.",
  details: { name: "Angelika Weber", duty: "GSD", date: "2026-10-12" },
})

test("parses a JSON rejection body without losing code or details", () => {
  const body = parseSubstituteAcceptError(409, manualBody)
  assert.equal(body.code, "MANUAL_ENTRY_PROTECTED")
  assert.equal(body.details?.name, "Angelika Weber")
  assert.equal(body.details?.duty, "GSD")
})

test("keeps a non-JSON body as the error instead of a generic sentence", () => {
  const body = parseSubstituteAcceptError(500, "boom from the proxy")
  assert.equal(body.error, "boom from the proxy")
})

test("an empty body still names the status", () => {
  const body = parseSubstituteAcceptError(409, "")
  assert.equal(body.error, "API error 409")
})

test("a protected manual entry is shown through the localized key", () => {
  const err = new SubstituteAcceptError(409, parseSubstituteAcceptError(409, manualBody))
  const text = substituteAcceptErrorText(err, t)
  assert.ok(text.includes(SUBSTITUTE_ACCEPT_ERROR_KEYS.MANUAL_ENTRY_PROTECTED), text)
  assert.ok(text.includes("Angelika Weber"), text)
  assert.ok(text.includes("GSD"), text)
})

test("an already-existing WIC duty is shown through its own key", () => {
  const err = new SubstituteAcceptError(409, {
    code: "ALREADY_HAS_WIC_DUTY",
    error: "Tim Boger already has a WIC duty today: Emmerthal.",
    details: { name: "Tim Boger", duty: "Emmerthal", date: "2026-10-12" },
  })
  const text = substituteAcceptErrorText(err, t)
  assert.ok(text.includes(SUBSTITUTE_ACCEPT_ERROR_KEYS.ALREADY_HAS_WIC_DUTY), text)
  assert.ok(text.includes("Emmerthal"), text)
})

test("a technical failure keeps the real message but is framed in the UI language", () => {
  const err = new SubstituteAcceptError(409, {
    code: "ACCEPT_FAILED",
    error: "Accept substitute failed: Cannot insert duplicate key row.",
    detail: "Cannot insert duplicate key row.",
  })
  const text = substituteAcceptErrorText(err, t)
  assert.ok(text.includes(SUBSTITUTE_ACCEPT_ERROR_KEYS.ACCEPT_FAILED), text)
  assert.ok(text.includes("Cannot insert duplicate key row."), text)
})

test("an unknown code falls back to the server's own text, never to a generic one", () => {
  const err = new SubstituteAcceptError(409, { code: "SOMETHING_NEW", error: "Slot already filled by Holger Petzholdt." })
  assert.equal(substituteAcceptErrorText(err, t), "Slot already filled by Holger Petzholdt.")
})

test("the thrown error message is the server text", () => {
  const err = new SubstituteAcceptError(409, { error: "Manual entry protected" })
  assert.equal(err.message, "Manual entry protected")
  assert.equal(err.status, 409)
})

console.log(`\n${passes} passed, ${failures} failed`)
if (failures > 0) process.exit(1)
