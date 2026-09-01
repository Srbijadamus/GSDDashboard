import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { AlertCircle, AlertTriangle } from "lucide-react"
import { api, apiFetch } from "../api/client"
import { ALHistoryDrawer } from "./Vacations"

export default function ALBalance() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [search, setSearch] = useState("")
  const [editId, setEditId] = useState<string | null>(null)
  const [editVal, setEditVal] = useState("")
  const [error, setError] = useState("")
  const [historyAgent, setHistoryAgent] = useState<{ id: string; name: string } | null>(null)

  const { data, isLoading } = useQuery({
    queryKey: ["albalance"],
    queryFn: api.alBalance.get
  })

  const filtered = data?.filter((a: any) =>
    !search ||
    a.employeeName?.toLowerCase().includes(search.toLowerCase()) ||
    a.employeeId?.toString().includes(search)
  ) ?? []

  const negative = filtered.filter((a: any) => a.remainingAL < 0).length
  const critical = filtered.filter((a: any) => a.remainingAL >= 0 && a.remainingAL <= 5).length
  const warning  = filtered.filter((a: any) => a.remainingAL > 5 && a.remainingAL <= 10).length

  const handleEditSave = async (employeeId: string) => {
    const val = parseInt(editVal)
    if (isNaN(val) || val < 0 || val > 28) {
      setError("Value must be between 0 and 28")
      return
    }
    try {
      await apiFetch(`/api/employees/${employeeId}/albalance`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ alUsed: val })
      } as any)
      qc.invalidateQueries({ queryKey: ["albalance"] })
      setEditId(null)
      setError("")
    } catch {
      setError("Failed to update AL balance")
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-xl font-semibold text-ink">{t("nav.alBalance")}</h1>

      <div className="grid grid-cols-4 gap-3">
        <div className="bg-raised border border-line-subtle rounded-md px-5 py-4">
          <div className="text-2xs uppercase tracking-wide text-ink-soft mb-1.5">Total Employees</div>
          <div className="text-3xl font-semibold font-mono text-ink">{filtered.length}</div>
        </div>
        <div className="bg-raised border border-warn-bd rounded-md px-5 py-4">
          <div className="text-2xs uppercase tracking-wide text-ink-soft mb-1.5">Low Balance (≤10)</div>
          <div className="text-3xl font-semibold font-mono text-warn-fg">{warning}</div>
        </div>
        <div className="bg-raised border border-crit-bd rounded-md px-5 py-4">
          <div className="text-2xs uppercase tracking-wide text-ink-soft mb-1.5">Critical (≤5)</div>
          <div className="text-3xl font-semibold font-mono text-crit-fg">{critical}</div>
        </div>
        <div className="bg-raised border border-crit-bd rounded-md px-5 py-4">
          <div className="text-2xs uppercase tracking-wide text-ink-soft mb-1.5">Negative Balance</div>
          <div className="text-3xl font-semibold font-mono text-crit-fg">{negative}</div>
        </div>
      </div>

      {error && (
        <div className="bg-crit-bg border border-crit-bd rounded-sm px-3.5 py-2 text-xs text-crit-fg">
          {error}
        </div>
      )}

      <input
        placeholder="Search employee..."
        value={search}
        onChange={e => setSearch(e.target.value)}
        className="bg-raised border border-line-default text-ink px-3 py-1.5 rounded-sm text-xs outline-none max-w-xs"
      />

      <div className="bg-raised border border-line-subtle rounded-md overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-xs">
            <thead>
              <tr className="bg-sunken">
                {["ID", "Name", "Eligible", "Taken (click to edit)", "Remaining", "SL Days", "Progress"].map(h => (
                  <th key={h} className="px-3 py-2.5 text-left text-2xs font-medium uppercase tracking-wide text-ink-soft border-b border-line-subtle">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {isLoading && Array.from({ length: 5 }).map((_, i) => (
                <tr key={`sk-${i}`} className="border-b border-line-subtle">
                  {Array.from({ length: 7 }).map((_, j) => (
                    <td key={j} className="px-3 py-2.5"><div className="skeleton" style={{ height: 11 }} /></td>
                  ))}
                </tr>
              ))}
              {filtered.length === 0 && !isLoading && (
                <tr>
                  <td colSpan={7} className="p-6 text-center text-ink-soft">No AL balance data yet</td>
                </tr>
              )}
              {filtered.map((a: any) => {
                const pct        = a.eligibleDays > 0 ? Math.round((a.plannedTakenAL / a.eligibleDays) * 100) : 0
                const isNegative = a.remainingAL < 0
                const isCritical = a.remainingAL >= 0 && a.remainingAL <= 5
                const isWarning  = a.remainingAL > 5 && a.remainingAL <= 10
                const barClass   = (isCritical || isNegative) ? "bg-crit-solid" : isWarning ? "bg-warn-solid" : "bg-info-solid"
                const remClass   = (isCritical || isNegative) ? "text-crit-fg" : isWarning ? "text-warn-fg" : "text-good-fg"
                const isEditing  = editId === a.employeeId

                return (
                  <tr
                    key={a.id}
                    className={`border-b border-line-subtle transition-colors ${
                      isNegative ? "bg-crit-bg hover:bg-crit-bg/70"
                      : isCritical ? "bg-crit-bg/50 hover:bg-crit-bg/70"
                      : "hover:bg-hovered"
                    }`}
                  >
                    <td className="px-3 py-2 font-mono text-2xs text-ink-soft">{a.employeeId}</td>
                    <td className="px-3 py-2 font-medium">
                      <span
                        className="cursor-pointer underline decoration-dotted decoration-ink-soft underline-offset-1"
                        onClick={() => setHistoryAgent({ id: a.employeeId, name: a.employeeName ?? a.employeeId })}
                      >{a.employeeName}</span>
                      {isNegative && <AlertTriangle size={11} className="inline ml-1.5 text-crit-fg align-text-bottom" />}
                      {isCritical && !isNegative && <AlertCircle size={11} className="inline ml-1.5 text-crit-fg align-text-bottom" />}
                    </td>
                    <td className="px-3 py-2 font-mono text-center">{a.eligibleDays}</td>

                    <td className="px-3 py-2 text-center">
                      {isEditing ? (
                        <div className="flex items-center gap-1 justify-center">
                          <input
                            value={editVal}
                            onChange={e => setEditVal(e.target.value)}
                            onKeyDown={e => { if (e.key === "Enter") handleEditSave(a.employeeId); if (e.key === "Escape") setEditId(null) }}
                            autoFocus
                            className="w-11 bg-sunken border border-info-bd text-ink px-1.5 py-0.5 rounded-xs text-xs font-mono outline-none text-center"
                          />
                          <button
                            onClick={() => handleEditSave(a.employeeId)}
                            className="bg-info-solid text-white px-1.5 py-0.5 rounded-xs text-2xs cursor-pointer border-0"
                          >✓</button>
                          <button
                            onClick={() => { setEditId(null); setError("") }}
                            className="bg-sunken border border-line-subtle text-ink-soft px-1.5 py-0.5 rounded-xs text-2xs cursor-pointer"
                          >✕</button>
                        </div>
                      ) : (
                        <span
                          onClick={() => { setEditId(a.employeeId); setEditVal(a.plannedTakenAL.toString()); setError("") }}
                          title="Click to edit"
                          className="font-mono text-info-fg cursor-pointer px-2 py-0.5 rounded-xs border border-transparent hover:border-info-bd transition-colors"
                        >{a.plannedTakenAL}</span>
                      )}
                    </td>

                    <td className={`px-3 py-2 font-mono font-semibold text-center ${remClass}`}>{a.remainingAL}</td>
                    <td className="px-3 py-2 font-mono text-warn-fg text-center">{a.countSL}</td>
                    <td className="px-3 py-2">
                      <div className="flex items-center gap-1.5">
                        <div className="w-20 h-1.5 bg-line-subtle rounded-full overflow-hidden">
                          <div
                            className={`h-full rounded-full transition-[width] ${barClass}`}
                            style={{ width: `${Math.min(pct, 100)}%` }}
                          />
                        </div>
                        <span className="text-2xs text-ink-soft font-mono">{pct}%</span>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
        <div className="px-3 py-2 border-t border-line-subtle text-2xs text-ink-soft font-mono">
          {filtered.length} records · click Taken value to edit
        </div>
      </div>

      {historyAgent && (
        <ALHistoryDrawer
          employeeId={historyAgent.id}
          name={historyAgent.name}
          onClose={() => setHistoryAgent(null)}
        />
      )}
    </div>
  )
}
