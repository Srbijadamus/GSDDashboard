// ============================================================
// API Client — typed fetch wrapper for GSD Dashboard
// All endpoints point to /api (proxied to localhost:5000 in dev)
// ============================================================

const BASE = '/api'

async function apiFetch<T>(url: string): Promise<T> {
  const res = await fetch(`${BASE}${url}`)
  if (!res.ok) throw new Error(`API error ${res.status}: ${url}`)
  return res.json() as Promise<T>
}

// ---- Types ----

export interface DashboardSummary {
  date: string
  workingVoice: number
  workingChat: number
  workingSSP: number
  workingDispatcher: number
  onAL: number
  onSL: number
  onTraining: number
  onWicDuty: number
  onPH: number
  onOFF: number
  totalActive: number
  wicUnoccupiedCount: number
}

export interface TeamLeadSummary {
  teamLeadName: string
  working: number
  onAL: number
  onSL: number
  training: number
  wicAssigned: number
  totalAgents: number
}

export interface WicAgent {
  employeeId: string
  name: string
  teamLead: string
  workingShift: string | null
  wicOpeningHours: string | null
}

export interface WicCard {
  locationCode: string
  displayName: string
  city: string
  country: string
  openingSchedule: string | null
  status: 'OCCUPIED' | 'UNOCCUPIED' | 'CLOSED' | 'PH'
  agents: WicAgent[]
  agentCount: number
}

export interface ShiftRow {
  employeeId: string
  fullName: string
  engagement: string | null
  primaryRole: string | null
  secondaryRole: string | null
  teamLeadName: string | null
  shiftDate: string
  shiftType: string
  shiftStart: string | null
  shiftEnd: string | null
  isWicDuty: boolean
  rawValue: string | null
}

export interface SickLeaveRow {
  id: number
  employeeId: string | null
  firstName: string | null
  lastName: string | null
  fullName: string | null
  teamLeadName: string | null
  firstDay: string
  lastDay: string
  durationDays: number | null
  leaveType: string | null
  childName: string | null
  comments: string | null
}

export interface SickLeaveStats {
  totalActive: number
  averageDuration: number
  selfCount: number
  childCount: number
  byTeamLead: { teamLead: string; count: number }[]
}

export interface Employee {
  id: number
  employeeId: string
  firstName: string | null
  lastName: string | null
  fullName: string | null
  engagement: string | null
  primaryRole: string | null
  secondaryRole: string | null
  teamLeadName: string | null
  category: string | null
  isActive: boolean
  isTrainee: boolean
}

export interface ALBalance {
  id: number
  employeeId: string | null
  employeeName: string | null
  eligibleDays: number
  plannedTakenAL: number
  remainingAL: number
  countSL: number
  countUL: number
  countWorkingSundays: number
}

export interface WicLocation {
  id: number
  locationCode: string
  displayName: string
  fullAddress: string | null
  postalCode: string | null
  city: string | null
  country: string | null
  isActive: boolean
}

// ---- API functions ----

export const api = {
  // Dashboard
  summary: (date: string)         => apiFetch<DashboardSummary>(`/dashboard/summary?date=${date}`),
  teamLeads: (date: string)        => apiFetch<TeamLeadSummary[]>(`/dashboard/teamlead-summary?date=${date}`),
  wicCards: (date: string)         => apiFetch<WicCard[]>(`/dashboard/wic-cards?date=${date}`),

  // Shifts
  shifts: (params: string)         => apiFetch<ShiftRow[]>(`/shifts?${params}`),
  workingToday: (date: string)     => apiFetch<ShiftRow[]>(`/shifts/working-today?date=${date}`),
  downloadShifts: (params: string) => fetch(`${BASE}/shifts/download?${params}`),

  // Sick Leave
  sickLeaves: (params: string)     => apiFetch<SickLeaveRow[]>(`/sickleave?${params}`),
  activeSickLeave: (date: string)  => apiFetch<SickLeaveRow[]>(`/sickleave/active?date=${date}`),
  sickLeaveStats: (params: string) => apiFetch<SickLeaveStats>(`/sickleave/stats?${params}`),
  downloadSickLeave: (p: string)   => fetch(`${BASE}/sickleave/download?${p}`),

  // Employees
  employees: (params: string)      => apiFetch<Employee[]>(`/employees?${params}`),

  // AL Balance
  alBalance: ()                    => apiFetch<ALBalance[]>(`/albalance`),

  // WIC
  wicLocations: ()                 => apiFetch<WicLocation[]>(`/wic/locations`),
  wicShifts: (params: string)      => apiFetch<any[]>(`/wic?${params}`),
  downloadWic: (params: string)    => fetch(`${BASE}/wic/download?${params}`),
}

// ---- Download helper ----
export async function downloadExcel(fetchPromise: Promise<Response>, filename: string) {
  const res = await fetchPromise
  if (!res.ok) throw new Error('Download failed')
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}
