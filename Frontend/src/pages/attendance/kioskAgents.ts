export interface KioskAgent {
  id: number
  full_name: string
  employee_id: string
  team_leader: string | null
  location: string | null
  active: number
}

export const INVALID_KIOSK_AGENTS = "Invalid kiosk agent list response"

export function isKioskAgentList(value: unknown): value is KioskAgent[] {
  return Array.isArray(value) && value.every((agent: unknown) => {
    if (typeof agent !== "object" || agent === null) return false
    return "id" in agent && typeof agent.id === "number" &&
      Number.isSafeInteger(agent.id) && agent.id > 0 &&
      "full_name" in agent && typeof agent.full_name === "string" &&
      "employee_id" in agent && typeof agent.employee_id === "string" &&
      "team_leader" in agent && (agent.team_leader === null || typeof agent.team_leader === "string") &&
      "location" in agent && (agent.location === null || typeof agent.location === "string") &&
      "active" in agent && typeof agent.active === "number"
  })
}

export function parseKioskAgents(value: unknown): KioskAgent[] {
  if (!isKioskAgentList(value)) throw new Error(INVALID_KIOSK_AGENTS)
  return value
}
