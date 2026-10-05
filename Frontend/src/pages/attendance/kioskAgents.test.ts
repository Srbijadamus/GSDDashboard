import assert from "node:assert/strict"
import { INVALID_KIOSK_AGENTS, isKioskAgentList, parseKioskAgents } from "./kioskAgents.js"

const valid = [{
  id: 1, full_name: "Test agent", employee_id: "test",
  team_leader: null, location: null, active: 1,
}]
assert.strictEqual(parseKioskAgents(valid), valid)
assert.deepEqual(parseKioskAgents([]), [])
const realFeedShape = [{ ...valid[0], active: true, employee_id: null }]
assert.strictEqual(parseKioskAgents(realFeedShape), realFeedShape)
assert.strictEqual(parseKioskAgents([{ ...valid[0], active: false }]).length, 1)
for (const invalid of [
  null, undefined, { error: "Forbidden" }, "error", 1,
  [null], [{}], [{ ...valid[0], id: "1" }], [{ ...valid[0], full_name: {} }],
  [{ ...valid[0], active: "true" }], [{ ...valid[0], active: 2 }],
  [{ ...valid[0], employee_id: 123 }],
]) {
  assert.equal(isKioskAgentList(invalid), false)
  assert.throws(() => parseKioskAgents(invalid), { message: INVALID_KIOSK_AGENTS })
}
console.log("Kiosk agent list regression passed: valid/empty lists preserved; invalid responses rejected.")
