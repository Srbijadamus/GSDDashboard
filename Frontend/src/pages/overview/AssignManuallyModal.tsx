import { useMemo, useState } from "react"
import { useTranslation } from "react-i18next"
import { useQuery } from "@tanstack/react-query"
import { apiFetch } from "../../api/client"
import { manualStatusOf, needsAnyway, type ManualStatus, type ManualShiftRow } from "../../utils/manualAssign"
import type { GapGroupV21 } from "../../utils/groupGaps"

interface Employee {
  employeeId: string
  fullName: string | null
}

interface Props {
  group: GapGroupV21
  onClose: () => void
  onAssigned: () => void
}

const STATUS_TOKEN: Record<ManualStatus, { bg: string; fg: string }> = {
  free:          { bg: "rgb(var(--st-good-bg))",  fg: "rgb(var(--st-good-fg))" },
  rosteredNoTask:{ bg: "rgb(var(--ov2-notask-bg))", fg: "rgb(var(--ov2-notask-fg))" },
  otherWic:      { bg: "rgb(var(--ov2-otherwic-bg))", fg: "rgb(var(--ov2-otherwic-fg))" },
  bo:            { bg: "rgb(var(--ov2-bo-bg))",   fg: "rgb(var(--ov2-bo-fg))" },
  annualLeave:   { bg: "rgb(var(--ov2-annualleave-bg))", fg: "rgb(var(--ov2-annualleave-fg))" },
  sick:          { bg: "rgb(var(--st-crit-bg))",  fg: "rgb(var(--st-crit-fg))" },
  training:      { bg: "rgb(var(--st-learn-bg))", fg: "rgb(var(--st-learn-fg))" },
}

export function AssignManuallyModal({ group, onClose, onAssigned }: Props) {
  const { t } = useTranslation()
  const p = (k: string, opts?: Record<string, unknown>) => String(t(`overviewV21.manual.${k}`, opts as never))

  const allDates = useMemo(() => group.days.map(d => d.date), [group])
  const [query, setQuery] = useState("")
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [pickDays, setPickDays] = useState(false)
  const [picked, setPicked] = useState<Set<string>>(new Set(allDates))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<number | null>(null)

  const { data: employees = [] } = useQuery<Employee[]>({
    queryKey: ["employees-active", allDates[0]],
    queryFn: () => apiFetch<Employee[]>("/api/employees?active=true"),
    staleTime: 5 * 60 * 1000,
  })

  const { data: shifts = [] } = useQuery<ManualShiftRow[]>({
    queryKey: ["shifts-manual-assign", group.from, group.to],
    queryFn: () => apiFetch<ManualShiftRow[]>(`/api/shifts?from=${group.from}&to=${group.to}`),
    staleTime: 60 * 1000,
  })

  const shiftByEmpDate = useMemo(() => {
    const m = new Map<string, Map<string, ManualShiftRow>>()
    for (const s of shifts) {
      const d = (s.shiftDate ?? "").slice(0, 10)
      if (!m.has(s.employeeId)) m.set(s.employeeId, new Map())
      m.get(s.employeeId)!.set(d, s)
    }
    return m
  }, [shifts])

  const statusFor = (employeeId: string, date: string): ManualStatus =>
    manualStatusOf(shiftByEmpDate.get(employeeId)?.get(date))

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase()
    const list = employees.filter(e => {
      if (!q) return true
      const name = (e.fullName ?? "").toLowerCase()
      return name.includes(q) || e.employeeId.toLowerCase().includes(q)
    })
    return list.slice(0, 100)
  }, [employees, query])

  const selectedDates = pickDays ? allDates.filter(d => picked.has(d)) : allDates
  const selected = employees.find(e => e.employeeId === selectedId) ?? null

  const anyBlocked = selected != null &&
    selectedDates.some(d => needsAnyway(statusFor(selected.employeeId, d)))

  function toggleDay(d: string) {
    setPicked(prev => {
      const next = new Set(prev)
      if (next.has(d)) next.delete(d); else next.add(d)
      return next
    })
  }

  async function assign(assignAnyway: boolean) {
    if (!selected) return
    setBusy(true)
    setError(null)
    let ok = 0
    let firstError: string | null = null
    for (const d of selectedDates) {
      try {
        const res = await fetch("/api/wic/assignments", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            employeeId: selected.employeeId,
            locationCode: group.locationCode,
            date: d,
            shiftStart: null,
            shiftEnd: null,
            assignAnyway,
          }),
        })
        if (!res.ok) {
          const body = await res.text().catch(() => "")
          firstError = firstError ?? `HTTP ${res.status}${body ? ": " + body : ""}`
          continue
        }
        const data = await res.json()
        if (data.saved === true) ok++
        else firstError = firstError ?? (data.reason ?? `not saved on ${d}`)
      } catch (e) {
        firstError = firstError ?? String(e)
      }
    }
    setDone(ok)
    setError(firstError)
    setBusy(false)
    onAssigned()
  }

  const inputStyle: React.CSSProperties = {
    width: "100%", padding: "7px 10px", borderRadius: 6, fontSize: 12,
    border: "1px solid rgb(var(--line-subtle))", background: "rgb(var(--surface-sunken))",
    color: "rgb(var(--text-primary))", outline: "none", boxSizing: "border-box",
  }

  return (
    <div style={{ position: "fixed", inset: 0, zIndex: 1200, background: "rgba(0,0,0,0.55)", display: "flex", alignItems: "center", justifyContent: "center" }} onClick={onClose}>
      <div style={{ background: "rgb(var(--surface-overlay))", border: "1px solid rgb(var(--border-default))", borderRadius: 12, padding: 18, width: 560, maxWidth: "94vw", maxHeight: "90vh", display: "flex", flexDirection: "column" }} onClick={e => e.stopPropagation()}>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 12 }}>
          <strong style={{ fontSize: 14 }}>{p("title")}</strong>
          <span style={{ marginLeft: "auto", fontSize: 12, color: "rgb(var(--text-tertiary))" }}>{group.displayName}</span>
        </div>

        {done !== null && (
          <div style={{ background: "rgb(var(--st-info-bg))", color: "rgb(var(--st-info-fg))", borderRadius: 8, padding: "8px 12px", fontSize: 12, marginBottom: 10 }}>
            {error ? `${t("overviewV2.assignPartial", { ok: done, failed: 1 })} ${error}` : t("overviewV2.assignOk", { count: done })}
          </div>
        )}

        <input
          value={query}
          onChange={e => setQuery(e.target.value)}
          placeholder={p("searchPlaceholder")}
          style={{ ...inputStyle, marginBottom: 10 }}
        />

        <div style={{ display: "flex", gap: 6, marginBottom: 10, flexWrap: "wrap" }}>
          <button onClick={() => setPickDays(false)} style={{ padding: "4px 10px", borderRadius: 6, fontSize: 11, border: pickDays ? "1px solid rgb(var(--border-default))" : "none", background: pickDays ? "transparent" : "rgb(var(--action-solid))", color: pickDays ? "rgb(var(--text-secondary))" : "rgb(var(--action-fg))", cursor: "pointer" }}>{p("allDays", { count: allDates.length })}</button>
          <button onClick={() => setPickDays(true)} style={{ padding: "4px 10px", borderRadius: 6, fontSize: 11, border: pickDays ? "none" : "1px solid rgb(var(--border-default))", background: pickDays ? "rgb(var(--action-solid))" : "transparent", color: pickDays ? "rgb(var(--action-fg))" : "rgb(var(--text-secondary))", cursor: "pointer" }}>{p("pickDays")}</button>
          {pickDays && (
            <span style={{ display: "flex", gap: 4, flexWrap: "wrap", alignItems: "center" }}>
              {allDates.map(d => (
                <label key={d} style={{ fontSize: 11, display: "flex", gap: 4, alignItems: "center", cursor: "pointer", color: "rgb(var(--text-secondary))" }}>
                  <input type="checkbox" checked={picked.has(d)} onChange={() => toggleDay(d)} className="accent-info-solid" />
                  {d.slice(5)}
                </label>
              ))}
            </span>
          )}
        </div>

        <div style={{ overflowY: "auto", flex: 1, display: "flex", flexDirection: "column", gap: 6 }}>
          {matches.length === 0 && (
            <div style={{ padding: 20, textAlign: "center", color: "rgb(var(--text-tertiary))", fontSize: 12 }}>{p("empty")}</div>
          )}
          {matches.map(e => {
            const isSel = selectedId === e.employeeId
            const statuses = allDates.map(d => statusFor(e.employeeId, d))
            return (
              <button key={e.employeeId} onClick={() => setSelectedId(e.employeeId)} style={{ textAlign: "left", display: "flex", alignItems: "center", gap: 10, padding: "7px 10px", borderRadius: 8, border: isSel ? "1px solid rgb(var(--focus-ring))" : "1px solid rgb(var(--border-subtle))", background: isSel ? "rgb(var(--surface-sunken))" : "rgb(var(--surface-raised))", cursor: "pointer" }}>
                <span style={{ fontSize: 12, color: "rgb(var(--text-primary))", fontWeight: 600, minWidth: 140 }}>{e.fullName ?? e.employeeId}</span>
                <span style={{ fontFamily: "monospace", fontSize: 11, color: "rgb(var(--text-tertiary))", minWidth: 70 }}>{e.employeeId}</span>
                <span style={{ display: "flex", gap: 4, flexWrap: "wrap", flex: 1 }}>
                  {statuses.map((s, i) => {
                    const tok = STATUS_TOKEN[s]
                    return (
                      <span key={i} title={`${allDates[i]}: ${t(`overviewV21.manual.status.${s}`)}`} style={{ display: "inline-flex", alignItems: "center", gap: 4, padding: "1px 6px", borderRadius: 999, background: tok.bg, color: tok.fg, fontSize: 10 }}>
                        {allDates[i].slice(5)}
                      </span>
                    )
                  })}
                </span>
              </button>
            )
          })}
        </div>

        {selected && (
          <div style={{ display: "flex", gap: 8, justifyContent: "flex-end", marginTop: 12, paddingTop: 10, borderTop: "1px solid rgb(var(--border-subtle))" }}>
            <button onClick={onClose} style={{ padding: "7px 14px", borderRadius: 6, fontSize: 12, border: "1px solid rgb(var(--border-default))", background: "transparent", color: "rgb(var(--text-secondary))", cursor: "pointer" }}>{p("cancel")}</button>
            {anyBlocked ? (
              <button disabled={busy || selectedDates.length === 0} onClick={() => assign(true)} style={{ padding: "7px 14px", borderRadius: 6, fontSize: 12, fontWeight: 600, border: "none", cursor: "pointer", background: "rgb(var(--st-warn-solid))", color: "#fff" }}>{p("assignAnyway")}</button>
            ) : (
              <button disabled={busy || selectedDates.length === 0} onClick={() => assign(false)} style={{ padding: "7px 14px", borderRadius: 6, fontSize: 12, fontWeight: 600, border: "none", cursor: "pointer", background: "rgb(var(--ov2-assign-bg))", color: "rgb(var(--ov2-assign-fg))" }}>{p("assign")}</button>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
