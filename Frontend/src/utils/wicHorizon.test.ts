// Tests for the 90-day Overview horizon (the ONLY horizon that was removed in ad4a854
// and is restored here). Everything in this file is about that one horizon:
//
//     cd Frontend && npm run test:wic-horizon
//
// No framework, no new dependency, works offline. The last group reads the two source
// files whose output is the acceptance criterion the task names — "banner number =
// number of items in the list" — and proves both surfaces still derive from ONE
// /api/wic/coverage-gaps response for ONE horizon, so a 90-day banner cannot show a
// different number than the 90-day list. A text assertion is weaker than a render, but
// it is the part a refactor could silently break (a second query or a second horizon).

import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import { fileURLToPath } from "node:url"
import { dirname, resolve } from "node:path"
import {
  parseHorizon, isWideHorizon, nextHorizon,
  HORIZON_OPTIONS, DEFAULT_HORIZON, MAX_HORIZON,
} from "./wicHorizon.js"

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

// ── the topbar option exists and the page honours it ─────────────────────────

test("90 is offered by the topbar next to 7/14/28", () => {
  assert.deepEqual([...HORIZON_OPTIONS], [7, 14, 28, 90])
  assert.ok(HORIZON_OPTIONS.includes(90))
})

test("?horizon=90 stays 90 (the old Math.min(30, …) clamp is gone)", () => {
  assert.equal(parseHorizon("90"), 90)
  assert.equal(parseHorizon("90"), MAX_HORIZON)
})

test("the three original horizons are unchanged", () => {
  assert.equal(parseHorizon("7"), 7)
  assert.equal(parseHorizon("14"), 14)
  assert.equal(parseHorizon("28"), 28)
})

test("a missing horizon is still 28, never 0 and never 90", () => {
  assert.equal(parseHorizon(null), DEFAULT_HORIZON)
  assert.equal(parseHorizon(undefined), DEFAULT_HORIZON)
  assert.equal(parseHorizon(""), DEFAULT_HORIZON)
  assert.equal(parseHorizon("abc"), DEFAULT_HORIZON)
})

test("a horizon above 90 is clamped to 90 rather than rejected", () => {
  assert.equal(parseHorizon("365"), 90)
  assert.equal(parseHorizon("9999"), 90)
})

test("a horizon below 1 is clamped to 1", () => {
  assert.equal(parseHorizon("0"), 1)
  assert.equal(parseHorizon("-5"), 1)
})

// ── the banner button is the same lever as the topbar ────────────────────────

test("'View 90 days' moves the horizon to 90", () => {
  assert.equal(nextHorizon(14), 90)
  assert.equal(nextHorizon(28), 90)
})

test("once at 90 the button offers the way back (14), not another 90", () => {
  assert.equal(nextHorizon(90), 14)
  assert.equal(nextHorizon(MAX_HORIZON), 14)
})

// ── the day strip must stay legible at 90 ────────────────────────────────────

test("a 90-day window uses the condensed strip, 31 days and below the normal one", () => {
  assert.equal(isWideHorizon(90), true)
  assert.equal(isWideHorizon(31), false)
  assert.equal(isWideHorizon(28), false)
  for (const n of HORIZON_OPTIONS) {
    assert.equal(isWideHorizon(n), n > 31)
  }
})

// ── one source, one number: banner count === list rows ───────────────────────

// The compiled test runs from Frontend/.wic-horizon-test-out, so the repo root of
// the source files is one level up.
const here = resolve(dirname(fileURLToPath(import.meta.url)), "..")
const read = (rel: string) => readFileSync(resolve(here, rel), "utf8")

const overviewSrc = read("src/pages/Overview.tsx")
const dayStripSrc = read("src/pages/overview/DayStrip.tsx")
const exceptionListSrc = read("src/pages/overview/ExceptionList.tsx")
const topbarSrc = read("src/layout/Topbar.tsx")

test("the coverage-gaps query is asked for the page horizon, not a number of its own", () => {
  assert.match(overviewSrc, /\/api\/wic\/coverage-gaps\?days=\$\{horizon\}/)
  const queries = overviewSrc.match(/coverage-gaps\?days=/g) ?? []
  assert.equal(queries.length, 1, "exactly one coverage-gaps query on the page")
})

test("the banner and the list are fed the SAME response object", () => {
  // coverageGaps is the single derived value; both components receive it.
  assert.match(overviewSrc, /const coverageGaps = coverage\?\.gaps \?\? \[\]/)
  assert.match(overviewSrc, /<WicCoverageGapsBanner[\s\S]{0,200}?data=\{coverage\}/)
  assert.match(overviewSrc, /<DayStrip\s+gaps=\{coverageGaps\}/)
  assert.match(overviewSrc, /<ExceptionList\s+gaps=\{coverageGaps\}/)
})

test("the banner number is the length of that response's gap list", () => {
  // count: gaps.length with gaps = data?.gaps ?? [] — the same array the rows render.
  assert.match(overviewSrc, /const gaps = data\?\.gaps \?\? \[\]/)
  assert.match(overviewSrc, /count: gaps\.length/)
  // A 90-day window can return many rows: the banner keeps its own count from the FULL
  // array while rendering only a capped slice (the list below shows the rest).
  assert.match(overviewSrc, /const GAP_ROW_CAP = 8/)
  assert.match(overviewSrc, /gapsExpanded \? gaps : gaps\.slice\(0, GAP_ROW_CAP\)\)\.map\(g => \(/)
})

test("the exception list builds its rows from the gap array it is given", () => {
  assert.match(exceptionListSrc, /for \(const g of gaps\) \{/)
  assert.match(exceptionListSrc, /allExceptions\.push\(/)
})

test("the 'View 90 days' button sets the SAME ?horizon= the topbar writes", () => {
  assert.match(overviewSrc, /params\.set\("horizon", String\(next\)\)/)
  assert.match(overviewSrc, /onHorizonChange\(nextHorizon\(horizon\)\)/)
  assert.match(topbarSrc, /next\.set\('horizon', h\)/)
  assert.match(topbarSrc, /HORIZON_OPTIONS\.map/)
})

test("the day strip scrolls horizontally in the wide window instead of squeezing cells", () => {
  assert.match(dayStripSrc, /isWideHorizon\(dates\.length\)/)
  assert.match(dayStripSrc, /overflow-x-auto/)
  assert.match(dayStripSrc, /wide \? 'w-10 shrink-0' : 'flex-1 min-w-0'/)
})

test("banner and day strip both use i18n keys (no hardcoded horizon text)", () => {
  for (const key of [
    "overview.coverageBanner.view90",
    "overview.coverageBanner.view14",
    "overview.dayStrip.window",
    "overview.dayStrip.gapDayCount",
    "overview.dayStrip.noGapDays",
  ]) {
    assert.ok(overviewSrc.includes(key) || dayStripSrc.includes(key), `${key} used`)
  }
})

console.log(`\n${passes} passed, ${failures} failed`)
if (failures > 0) process.exit(1)
