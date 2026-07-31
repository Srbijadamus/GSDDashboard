// Relative by default: works whether the app is served from localhost:5000,
// a devtunnel URL, or anywhere else — the fetch just targets the same origin
// the page was loaded from. Only set VITE_API_BASE_URL if the API is
// genuinely on a different, CORS/auth-open origin than the frontend.
const API_BASE = import.meta.env.VITE_API_BASE_URL ?? ""

export async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, options)
  if (!res.ok) {
    let msg = `API error ${res.status}`
    try { const body = await res.json(); if (body?.error) msg = body.error } catch {}
    throw new Error(msg)
  }
  return res.json()
}

export async function downloadExcel(path: string, filename: string) {
  const res = await fetch(`${API_BASE}${path}`)
  if (!res.ok) throw new Error("Download failed")
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement("a")
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}

const today = () => new Date().toISOString().split("T")[0]
const daysAgo = (n: number) => {
  const d = new Date()
  d.setDate(d.getDate() - n)
  return d.toISOString().split("T")[0]
}

export const api = {
  dashboard: {
    summary:        (date?: string) => apiFetch<any>(`/api/dashboard/summary?date=${date ?? today()}`),
    teamleadSummary:(date?: string) => apiFetch<any[]>(`/api/dashboard/teamlead-summary?date=${date ?? today()}`),
    wicCards:       (date?: string) => apiFetch<any[]>(`/api/dashboard/wic-cards?date=${date ?? today()}`),
  },
  shifts: {
    get:          (params: string) => apiFetch<any[]>(`/api/shifts?${params}`),
    workingToday: (date?: string)  => apiFetch<any[]>(`/api/shifts/working-today?date=${date ?? today()}`),
    downloadToday: () => downloadExcel(`/api/shifts/download?from=${today()}&to=${today()}`, `Shifts_${today()}.xlsx`),
    download7:     () => downloadExcel(`/api/shifts/download?from=${daysAgo(7)}&to=${today()}`, `Shifts_7days.xlsx`),
    download30:    () => downloadExcel(`/api/shifts/download?from=${daysAgo(30)}&to=${today()}`, `Shifts_30days.xlsx`),
  },
  wic: {
    locations:    () => apiFetch<any[]>("/api/wic/locations"),
    shifts:       (params: string) => apiFetch<any[]>(`/api/wic?${params}`),
    coverage:     (date?: string)  => apiFetch<any[]>(`/api/wic/coverage?date=${date ?? today()}`),
    cards:        (date?: string, country?: string) => apiFetch<any[]>(`/api/wic/cards?date=${date ?? today()}${country ? "&country=" + country : ""}`),
    downloadToday: () => downloadExcel(`/api/wic/download?from=${today()}&to=${today()}`, `WIC_${today()}.xlsx`),
    download7:     () => downloadExcel(`/api/wic/download?from=${daysAgo(7)}&to=${today()}`, `WIC_7days.xlsx`),
    download30:    () => downloadExcel(`/api/wic/download?from=${daysAgo(30)}&to=${today()}`, `WIC_30days.xlsx`),
  },
  sickLeave: {
    get:          (params: string) => apiFetch<any[]>(`/api/sickleave?${params}`),
    active:       (date?: string)  => apiFetch<any[]>(`/api/sickleave/active?date=${date ?? today()}`),
    stats:        ()               => apiFetch<any>("/api/sickleave/stats"),
    downloadToday: () => downloadExcel(`/api/sickleave/download?from=${today()}&to=${today()}`, `SickLeave_${today()}.xlsx`),
    download7:     () => downloadExcel(`/api/sickleave/download?from=${daysAgo(7)}&to=${today()}`, `SickLeave_7days.xlsx`),
    download30:    () => downloadExcel(`/api/sickleave/download?from=${daysAgo(30)}&to=${today()}`, `SickLeave_30days.xlsx`),
    create:        (body: any)             => apiFetch<any>("/api/sickleave", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }),
    patch:         (id: number, body: any)  => apiFetch<any>(`/api/sickleave/${id}`, { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }),
    remove:        (id: number)             => fetch(`${API_BASE}/api/sickleave/${id}`, { method: "DELETE" }),
    endActive:     (employeeId: string)     => apiFetch<{ closed: number }>(`/api/sickleave/end-active/${employeeId}`, { method: "POST" }),
  },
  vacations: {
    get:          (params: string) => apiFetch<any[]>(`/api/vacations?${params}`),
    current:      (date?: string)  => apiFetch<any[]>(`/api/vacations/active?date=${date ?? today()}`),
    upcoming:     (days?: number)  => apiFetch<any[]>(`/api/vacations/upcoming?days=${days ?? 7}`),
    downloadToday: () => downloadExcel(`/api/vacations/download?from=${today()}&to=${today()}`, `Vacations_${today()}.xlsx`),
    download7:     () => downloadExcel(`/api/vacations/download?from=${daysAgo(7)}&to=${today()}`, `Vacations_7days.xlsx`),
    download30:    () => downloadExcel(`/api/vacations/download?from=${daysAgo(30)}&to=${today()}`, `Vacations_30days.xlsx`),
  },
  alBalance: {
    get:          () => apiFetch<any[]>("/api/albalance"),
    getByEmployee:(id: string) => apiFetch<any>(`/api/albalance/${id}`),
  },
  employees: {
    get:      (params?: string) => apiFetch<any[]>(`/api/employees${params ? "?" + params : ""}`),
    getById:  (id: string)      => apiFetch<any>(`/api/employees/${id}`),
    timeline: (id: string)      => apiFetch<any>(`/api/employees/${id}/timeline`),
  },
  breaks: {
    get:          (date?: string)  => apiFetch<any[]>(`/api/breaks${date ? "?date=" + date : ""}`),
    distribute:   (body: any)      => apiFetch<any>("/api/breaks/auto-distribute", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }),
    start:        (id: number)     => apiFetch<any>(`/api/breaks/${id}/start`,  { method: "POST" }),
    end:          (id: number)     => apiFetch<any>(`/api/breaks/${id}/end`,    { method: "POST" }),
    cancel:       (id: number)     => apiFetch<any>(`/api/breaks/${id}/cancel`, { method: "POST" }),
    manual:       (body: any)      => apiFetch<any>("/api/breaks/manual", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }),
  },
  attendance: {
    get:          (params: string) => apiFetch<any[]>(`/api/attendance?${params}`),
    downloadToday: () => downloadExcel(`/api/attendance/download?from=${today()}&to=${today()}`, `Attendance_${today()}.xlsx`),
    download7:     () => downloadExcel(`/api/attendance/download?from=${daysAgo(7)}&to=${today()}`, `Attendance_7days.xlsx`),
    download30:    () => downloadExcel(`/api/attendance/download?from=${daysAgo(30)}&to=${today()}`, `Attendance_30days.xlsx`),
  },
  wicCoverage: {
    patchAgent: (kid: string, body: object) =>
      fetch(`${API_BASE}/api/wic-coverage/agents/${encodeURIComponent(kid)}`, {
        method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
      }),
    pinBackupB: (code: string, body: object) =>
      fetch(`${API_BASE}/api/wic-coverage/wics/${encodeURIComponent(code)}/backup-b`, {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
      }),
  },
  boList: {
    get:    (date: string) => apiFetch<any[]>(`/api/bo-list?date=${date}`),
    create: (body: any)    => apiFetch<any>("/api/bo-list", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }),
    update: (id: number, body: any) => apiFetch<any>(`/api/bo-list/${id}`, { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }),
    remove: (id: number)   => fetch(`${API_BASE}/api/bo-list/${id}`, { method: "DELETE" }),
  },
}





