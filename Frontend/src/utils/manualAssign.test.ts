import { manualStatusOf, needsAnyway, isSick, type ManualShiftRow } from "./manualAssign.js"
import assert from "node:assert/strict"

function row(p: Partial<ManualShiftRow> & { employeeId: string; shiftDate: string; shiftType: string }): ManualShiftRow {
  return { ...p }
}

// free = not rostered
{
  assert.equal(manualStatusOf(undefined), "free")
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "" })), "free")
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "EMPTY" })), "free")
}

// rostered without task
{
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "WORKING" })), "rosteredNoTask")
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "WORKING", agentTask: "" })), "rosteredNoTask")
}

// other WIC
{
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "WIC_DUTY" })), "otherWic")
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "WORKING", agentTask: "WIC Recklinghausen" })), "otherWic")
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "WORKING", isWicDuty: true })), "otherWic")
}

// BO
{
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "WORKING", rawValue: "BO" })), "bo")
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "WORKING", agentTask: "BO" })), "bo")
}

// annual leave
{
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "AL" })), "annualLeave")
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "HALF_AL" })), "annualLeave")
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "OFF" })), "annualLeave")
}

// sick
{
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "SL" })), "sick")
}

// training
{
  assert.equal(manualStatusOf(row({ employeeId: "E1", shiftDate: "2026-11-02", shiftType: "TRAINING" })), "training")
}

// needsAnyway
{
  assert.equal(needsAnyway("free"), false)
  assert.equal(needsAnyway("rosteredNoTask"), false)
  assert.equal(needsAnyway("bo"), false)
  assert.equal(needsAnyway("annualLeave"), true)
  assert.equal(needsAnyway("otherWic"), true)
  assert.equal(needsAnyway("training"), true)
  // Sick leave is never assignable — not even "Assign anyway".
  assert.equal(needsAnyway("sick"), false)
}

// isSick
{
  assert.equal(isSick("sick"), true)
  assert.equal(isSick("annualLeave"), false)
  assert.equal(isSick("free"), false)
}

console.log("manualAssign tests OK")
