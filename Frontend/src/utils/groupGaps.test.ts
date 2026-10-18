import { groupGaps, type CoverageGap } from "./groupGaps.js"
import assert from "node:assert/strict"

function gap(p: Partial<CoverageGap> & { date: string; locationCode: string }): CoverageGap {
  return {
    weekday: "Mon",
    displayName: "Centre A",
    city: "City",
    category: "UNCOVERED_ABSENCE",
    present: 0,
    required: 1,
    openTime: "09:00",
    closeTime: "17:00",
    openTime2: null,
    closeTime2: null,
    assignedAgents: [],
    availableFloaters: [],
    ...p,
  }
}

// 1. merging: same key, consecutive dates -> one group
{
  const g = groupGaps([
    gap({ date: "2026-11-02", locationCode: "A" }),
    gap({ date: "2026-11-03", locationCode: "A" }),
    gap({ date: "2026-11-04", locationCode: "A" }),
  ])
  assert.equal(g.length, 1)
  assert.equal(g[0].dayCount, 3)
  assert.equal(g[0].from, "2026-11-02")
  assert.equal(g[0].to, "2026-11-04")
  assert.equal(g[0].days.length, 3)
}

// 2. break on people change
{
  const g = groupGaps([
    gap({ date: "2026-11-02", locationCode: "A", assignedAgents: [{ employeeId: "E1", fullName: "A", assignmentType: "MAIN", status: "AL", taskLabel: "AL" }] }),
    gap({ date: "2026-11-03", locationCode: "A", assignedAgents: [{ employeeId: "E2", fullName: "B", assignmentType: "MAIN", status: "AL", taskLabel: "AL" }] }),
  ])
  assert.equal(g.length, 2)
}

// 3. break on status change (present 0 -> 1)
{
  const g = groupGaps([
    gap({ date: "2026-11-02", locationCode: "A", present: 0, required: 2 }),
    gap({ date: "2026-11-03", locationCode: "A", present: 1, required: 2 }),
  ])
  assert.equal(g.length, 2)
  assert.equal(g[0].status, "UNCOVERED")
  assert.equal(g[1].status, "PARTIAL")
}

// 4. weekend does NOT break a group (Fri + Mon merge, Sat/Sun absent)
{
  const g = groupGaps([
    gap({ date: "2026-11-06", locationCode: "A" }), // Fri
    gap({ date: "2026-11-09", locationCode: "A" }), // Mon
  ])
  assert.equal(g.length, 1)
  assert.equal(g[0].dayCount, 2)
  assert.equal(g[0].from, "2026-11-06")
  assert.equal(g[0].to, "2026-11-09")
}

// 5. partial floater availability -> suggestion lists only the days it covers
{
  const g = groupGaps([
    gap({ date: "2026-11-02", locationCode: "A", availableFloaters: [{ employeeId: "F1", fullName: "Floater", assignmentType: "REGIONAL", status: "NONE", taskLabel: "" }] }),
    gap({ date: "2026-11-03", locationCode: "A" }), // F1 not available here
  ])
  assert.equal(g.length, 1)
  assert.equal(g[0].suggestions.length, 1)
  assert.equal(g[0].suggestions[0].availableDates.length, 1)
  assert.equal(g[0].suggestions[0].availableDates[0], "2026-11-02")
}

// 6. a floater available on ALL days of a full group
{
  const f = { employeeId: "F1", fullName: "Floater", assignmentType: "REGIONAL", status: "NONE", taskLabel: "" }
  const g = groupGaps([
    gap({ date: "2026-11-02", locationCode: "A", availableFloaters: [f] }),
    gap({ date: "2026-11-03", locationCode: "A", availableFloaters: [f] }),
    gap({ date: "2026-11-04", locationCode: "A", availableFloaters: [f] }),
  ])
  assert.equal(g.length, 1)
  assert.equal(g[0].suggestions.length, 1)
  assert.equal(g[0].suggestions[0].availableDates.length, 3)
}

// 7. different locations are never merged
{
  const g = groupGaps([
    gap({ date: "2026-11-02", locationCode: "A" }),
    gap({ date: "2026-11-02", locationCode: "B" }),
  ])
  assert.equal(g.length, 2)
}

console.log("groupGaps: all tests passed")
