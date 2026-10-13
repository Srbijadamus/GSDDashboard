// Tests for the WIC coverage banner/list state rule (rule 3).
//
//     cd Frontend && npm run test:wic-coverage-state
//
// No framework, no new dependency, works offline.

import assert from "node:assert/strict"
import { coverageBannerState } from "./wicCoverageState.js"

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

const base = { enabled: true, isLoading: false, isError: false, hasData: true, gapCount: 0 }

test("a successful empty response is the only way to say 'no gaps'", () => {
  assert.equal(coverageBannerState(base), "clear")
})

test("gaps are shown when the successful response lists them", () => {
  assert.equal(coverageBannerState({ ...base, gapCount: 5 }), "gaps")
})

test("a 401/403/network error is never shown as 'no gaps'", () => {
  const state = coverageBannerState({ ...base, isError: true, hasData: false, gapCount: 0 })
  assert.equal(state, "error")
  assert.notEqual(state, "clear")
})

test("an error with stale cached data is still an error, not an all-clear", () => {
  const state = coverageBannerState({ ...base, isError: true, hasData: true, gapCount: 0 })
  assert.equal(state, "error")
})

test("a missing response after loading is an error, not an all-clear", () => {
  assert.equal(coverageBannerState({ ...base, hasData: false }), "error")
})

test("loading is its own neutral state, never an all-clear", () => {
  assert.equal(coverageBannerState({ ...base, isLoading: true, hasData: false }), "loading")
})

test("a caller without access renders nothing (disabled), not a green banner", () => {
  assert.equal(coverageBannerState({ ...base, enabled: false, isError: true }), "disabled")
})

test("an error can never produce gaps either", () => {
  assert.notEqual(coverageBannerState({ ...base, isError: true, gapCount: 9 }), "gaps")
})

console.log(`\n${passes} passed, ${failures} failed`)
if (failures > 0) process.exit(1)
