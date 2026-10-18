import { groupGaps, groupGapsV21, type CoverageGap } from "./groupGaps.js"
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

// ── v2.1 grouping tests ──────────────────────────────────────────────────────

const candidate = (id: string) => ({ employeeId: id, fullName: "Candidate " + id, assignmentType: "REGIONAL", status: "NONE", taskLabel: "" })

// (a) 5 days, same candidate, different hours -> 1 group, time varies
{
  const c = candidate("C1")
  const g = groupGapsV21([
    gap({ date: "2026-11-02", locationCode: "HAN", openTime: "08:00", closeTime: "16:00", availableFloaters: [c] }),
    gap({ date: "2026-11-03", locationCode: "HAN", openTime: "09:00", closeTime: "17:00", availableFloaters: [c] }),
    gap({ date: "2026-11-04", locationCode: "HAN", openTime: "10:00", closeTime: "18:00", availableFloaters: [c] }),
    gap({ date: "2026-11-05", locationCode: "HAN", openTime: "11:00", closeTime: "19:00", availableFloaters: [c] }),
    gap({ date: "2026-11-06", locationCode: "HAN", openTime: "12:00", closeTime: "20:00", availableFloaters: [c] }),
  ])
  assert.equal(g.length, 1)
  assert.equal(g[0].dayCount, 5)
  assert.equal(g[0].timeVaries, true)
  assert.equal(g[0].time, "")
}

// (b) same candidate, backup's reason changes (BO <-> other WIC) -> 1 group, "varies"
{
  const c = candidate("C1")
  const backup = (status: string, taskLabel: string) => ({ employeeId: "B1", fullName: "Backup", assignmentType: "BACKUP", status, taskLabel })
  const g = groupGapsV21([
    gap({ date: "2026-11-02", locationCode: "HAN", assignedAgents: [backup("BO", "BO")], availableFloaters: [c] }),
    gap({ date: "2026-11-03", locationCode: "HAN", assignedAgents: [backup("WIC_DUTY", "WIC Centre")], availableFloaters: [c] }),
  ])
  assert.equal(g.length, 1)
  const b = g[0].agents.find(a => a.employeeId === "B1")
  assert.ok(b)
  assert.equal(b!.varies, true)
  assert.equal(b!.reason, "varies")
}

// (c) status change breaks the group
{
  const c = candidate("C1")
  const g = groupGapsV21([
    gap({ date: "2026-11-02", locationCode: "HAN", present: 0, required: 2, availableFloaters: [c] }),
    gap({ date: "2026-11-03", locationCode: "HAN", present: 1, required: 2, availableFloaters: [c] }),
  ])
  assert.equal(g.length, 2)
  assert.equal(g[0].status, "UNCOVERED")
  assert.equal(g[1].status, "PARTIAL")
}

// (d) candidate change breaks the group
{
  const c1 = candidate("C1")
  const c2 = candidate("C2")
  const g = groupGapsV21([
    gap({ date: "2026-11-02", locationCode: "HAN", availableFloaters: [c1] }),
    gap({ date: "2026-11-03", locationCode: "HAN", availableFloaters: [c2] }),
  ])
  assert.equal(g.length, 2)
}

// (e) sum of days in groups == gap count
{
  const c = candidate("C1")
  const inputs = [
    gap({ date: "2026-11-02", locationCode: "A", availableFloaters: [c] }),
    gap({ date: "2026-11-03", locationCode: "A", availableFloaters: [c] }),
    gap({ date: "2026-11-04", locationCode: "A", availableFloaters: [c] }),
    gap({ date: "2026-11-02", locationCode: "B", availableFloaters: [c] }),
    gap({ date: "2026-11-09", locationCode: "A", availableFloaters: [c] }), // after a status? no — same key, but 11-09 is not consecutive with 11-04 in a different way
  ]
  const g = groupGapsV21(inputs)
  const sum = g.reduce((s, x) => s + x.days.length, 0)
  assert.equal(sum, inputs.length)
}

// (f) candidate with no assignmentType -> empty sourceType
{
  const c = { employeeId: "C1", fullName: "Candidate C1", assignmentType: "", status: "NONE", taskLabel: "" }
  const g = groupGapsV21([gap({ date: "2026-11-02", locationCode: "HAN", availableFloaters: [c] })])
  assert.equal(g[0].candidates.length, 1)
  assert.equal(g[0].candidates[0].sourceType, "")
}

// (g) 0 / 1 / 3 candidates
{
  const c1 = candidate("C1"), c2 = candidate("C2"), c3 = candidate("C3")
  const zero = groupGapsV21([gap({ date: "2026-11-02", locationCode: "A", availableFloaters: [] })])
  assert.equal(zero[0].candidates.length, 0)
  const one = groupGapsV21([gap({ date: "2026-11-02", locationCode: "A", availableFloaters: [c1] })])
  assert.equal(one[0].candidates.length, 1)
  const three = groupGapsV21([gap({ date: "2026-11-02", locationCode: "A", availableFloaters: [c1, c2, c3] })])
  assert.equal(three[0].candidates.length, 3)
  assert.deepEqual(three[0].candidates.map(c => c.employeeId), ["C1", "C2", "C3"])
}

// (h) a candidate available only some days -> partial availableDates (second candidate)
{
  const c1 = candidate("C1"), c2 = candidate("C2")
  const g = groupGapsV21([
    gap({ date: "2026-11-02", locationCode: "A", availableFloaters: [c1, c2] }),
    gap({ date: "2026-11-03", locationCode: "A", availableFloaters: [c1] }),
    gap({ date: "2026-11-04", locationCode: "A", availableFloaters: [c1] }),
  ])
  assert.equal(g[0].candidates.length, 2)
  assert.equal(g[0].candidates[0].availableDates.length, 3)  // C1 (first) all days
  assert.equal(g[0].candidates[1].availableDates.length, 1)  // C2 only day 1
}

// (i) candidate source ("odakle") from taskLabel / sourceLocation
{
  const a = { employeeId: "C1", fullName: "C1", assignmentType: "REGIONAL", status: "WIC_DUTY", taskLabel: "WIC Recklinghausen" }
  const b = { employeeId: "C2", fullName: "C2", assignmentType: "REGIONAL", status: "WORKING", taskLabel: "", sourceLocation: "Recklinghausen" }
  const c = { employeeId: "C3", fullName: "C3", assignmentType: "REGIONAL", status: "WORKING", taskLabel: "" }
  const g = groupGapsV21([gap({ date: "2026-11-02", locationCode: "A", availableFloaters: [a, b, c] })])
  assert.equal(g[0].candidates[0].source, "WIC Recklinghausen")
  assert.equal(g[0].candidates[1].source, "WIC Recklinghausen")
  assert.equal(g[0].candidates[2].source, "rostered, no task")
}

// (j) a group spanning two calendar weeks -> two week sub-groups (KW 45 + KW 46)
{
  const c = candidate("C1")
  const g = groupGapsV21([
    gap({ date: "2026-11-06", locationCode: "A", availableFloaters: [c] }), // Fri KW 45
    gap({ date: "2026-11-09", locationCode: "A", availableFloaters: [c] }), // Mon KW 46
  ])
  assert.equal(g.length, 1)          // same candidate -> one group
  assert.equal(g[0].weeks.length, 2) // cut into two weeks
  assert.equal(g[0].weeks[0].days.length, 1)
  assert.equal(g[0].weeks[1].days.length, 1)
}

console.log("groupGapsV21: all tests passed")
