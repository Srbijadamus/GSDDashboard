import { useState } from "react"
import { apiFetch } from "../../api/client"

const SKIP_LABELS = {
  CLOSED_DAY: "Closed day / Tag geschlossen",
  ABSENCE: "Absence (AL/SL/OFF) / Abwesenheit",
  FIXED_OUTSIDE_HOURS: "Fixed time outside opening hours / Feste Zeit außerhalb der Öffnungszeiten",
  UNCHANGED: "Unchanged / Unverändert",
  INVALID_FIXED_TIME: "Invalid fixed time / Ungültige feste Zeit",
}

export default function AdjustTimesModal({ locationCode, locationName, shiftIds, onClose, onSuccess }) {
  const [mode, setMode] = useState("opening") // "opening" | "fixed"
  const [fixedStart, setFixedStart] = useState("")
  const [fixedEnd, setFixedEnd] = useState("")
  const [preview, setPreview] = useState(null) // { results: [...] }
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState("")
  const [applied, setApplied] = useState(null)

  const body = () => ({
    locationCode,
    shiftIds,
    mode: mode === "opening" ? "OPENING_HOURS" : "FIXED",
    start: mode === "fixed" ? fixedStart : null,
    end: mode === "fixed" ? fixedEnd : null,
  })

  const runPreview = async () => {
    setBusy(true)
    setError("")
    setPreview(null)
    try {
      const res = await apiFetch("/api/wic-shifts/bulk-time/preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body()),
      })
      setPreview(res)
    } catch (e) {
      setError(e?.message ?? "Preview failed")
    } finally {
      setBusy(false)
    }
  }

  const runApply = async () => {
    setBusy(true)
    setError("")
    try {
      const res = await apiFetch("/api/wic-shifts/bulk-time/apply", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body()),
      })
      setApplied(res)
      onSuccess?.()
    } catch (e) {
      setError(e?.message ?? "Apply failed")
    } finally {
      setBusy(false)
    }
  }

  const changedCount = preview?.results?.filter(r => !r.skipReason).length ?? 0
  const skippedCount = preview?.results?.filter(r => r.skipReason).length ?? 0

  return (
    <div style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.7)", zIndex: 2000, display: "flex", alignItems: "center", justifyContent: "center" }}
      onClick={e => e.target === e.currentTarget && !busy && onClose()}>
      <div style={{ background: "rgb(var(--surface-sunken))", border: "1px solid rgb(var(--line-subtle))", borderRadius: 12, width: 520, maxWidth: "94vw", maxHeight: "88vh", overflow: "hidden", display: "flex", flexDirection: "column" }}>
        <div style={{ padding: "16px 20px", borderBottom: "1px solid rgb(var(--line-subtle))", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <div>
            <div style={{ fontSize: 14, fontWeight: 700, color: "rgb(var(--text-primary))" }}>Adjust times / Zeiten anpassen</div>
            <div style={{ fontSize: 11, color: "rgb(var(--text-secondary))", marginTop: 2 }}>{locationName} · {shiftIds.length} selected / ausgewählt</div>
          </div>
          <button onClick={onClose} disabled={busy} style={{ background: "none", border: "none", color: "rgb(var(--text-secondary))", fontSize: 18, cursor: "pointer" }}>X</button>
        </div>

        {applied ? (
          <div style={{ padding: 20 }}>
            <div className="bg-good-bg border border-good-bd text-good-fg" style={{ borderRadius: 6, padding: "10px 12px", fontSize: 12, marginBottom: 12 }}>
              ✓ {applied.changed} changed / geändert, {applied.skipped} skipped / übersprungen.
            </div>
            <div style={{ display: "flex", justifyContent: "flex-end" }}>
              <button onClick={onClose} style={{ padding: "8px 18px", borderRadius: 6, background: "rgb(var(--st-info-solid))", border: "none", color: "#fff", fontSize: 12, fontWeight: 600, cursor: "pointer" }}>Close / Schließen</button>
            </div>
          </div>
        ) : (
          <>
            <div style={{ padding: 20, display: "flex", flexDirection: "column", gap: 14, overflowY: "auto" }}>
              {/* Mode */}
              <div>
                <label style={{ fontSize: 11, color: "rgb(var(--text-secondary))", textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }}>Mode / Modus</label>
                <div style={{ display: "flex", gap: 8 }}>
                  <button onClick={() => setMode("opening")}
                    style={{ flex: 1, padding: "8px 0", borderRadius: 6, border: "1px solid rgb(var(--line-subtle))", background: mode === "opening" ? "rgb(var(--st-info-bg))" : "transparent", color: mode === "opening" ? "rgb(var(--st-info-solid))" : "rgb(var(--text-secondary))", cursor: "pointer", fontSize: 12, fontWeight: 600 }}>
                    Use opening hours / Öffnungszeiten
                  </button>
                  <button onClick={() => setMode("fixed")}
                    style={{ flex: 1, padding: "8px 0", borderRadius: 6, border: "1px solid rgb(var(--line-subtle))", background: mode === "fixed" ? "rgb(var(--st-info-bg))" : "transparent", color: mode === "fixed" ? "rgb(var(--st-info-solid))" : "rgb(var(--text-secondary))", cursor: "pointer", fontSize: 12, fontWeight: 600 }}>
                    Set time / Zeit setzen
                  </button>
                </div>
              </div>

              {mode === "fixed" && (
                <div style={{ display: "flex", gap: 10 }}>
                  <div style={{ flex: 1 }}>
                    <label style={{ fontSize: 11, color: "rgb(var(--text-secondary))", display: "block", marginBottom: 4 }}>From / Von</label>
                    <input type="time" value={fixedStart} onChange={e => setFixedStart(e.target.value)}
                      style={{ width: "100%", background: "rgb(var(--surface-sunken))", border: "1px solid rgb(var(--line-subtle))", color: "rgb(var(--text-primary))", padding: "7px 10px", borderRadius: 6, fontSize: 12, outline: "none", boxSizing: "border-box" }} />
                  </div>
                  <div style={{ flex: 1 }}>
                    <label style={{ fontSize: 11, color: "rgb(var(--text-secondary))", display: "block", marginBottom: 4 }}>To / Bis</label>
                    <input type="time" value={fixedEnd} onChange={e => setFixedEnd(e.target.value)}
                      style={{ width: "100%", background: "rgb(var(--surface-sunken))", border: "1px solid rgb(var(--line-subtle))", color: "rgb(var(--text-primary))", padding: "7px 10px", borderRadius: 6, fontSize: 12, outline: "none", boxSizing: "border-box" }} />
                  </div>
                </div>
              )}

              {!preview && (
                <button onClick={runPreview} disabled={busy || (mode === "fixed" && (!fixedStart || !fixedEnd))}
                  style={{ padding: "9px 16px", borderRadius: 6, border: "none", background: "rgb(var(--st-info-solid))", color: "#fff", cursor: "pointer", fontSize: 12, fontWeight: 600, opacity: (busy || (mode === "fixed" && (!fixedStart || !fixedEnd))) ? 0.6 : 1 }}>
                  {busy ? "…" : "Preview / Vorschau"}
                </button>
              )}

              {/* Preview table */}
              {preview && (
                <div>
                  <div style={{ display: "flex", gap: 10, marginBottom: 8 }}>
                    <span className="font-mono" style={{ fontSize: 11, color: "rgb(var(--st-good-solid))" }}>{changedCount} change</span>
                    <span className="font-mono" style={{ fontSize: 11, color: "rgb(var(--st-warn-solid))" }}>{skippedCount} skip</span>
                  </div>
                  <div style={{ maxHeight: 260, overflowY: "auto", border: "1px solid rgb(var(--line-subtle))", borderRadius: 6 }}>
                    <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 11 }}>
                      <thead>
                        <tr className="bg-sunken">
                          {["Date / Datum", "Emp", "Old / Alt", "New / Neu", "Status"].map(h => (
                            <th key={h} className="text-ink-soft border-b border-line-subtle" style={{ padding: "5px 8px", textAlign: "left", fontSize: 9, fontWeight: 700, textTransform: "uppercase", whiteSpace: "nowrap" }}>{h}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {preview.results.map(r => (
                          <tr key={r.id} className="border-b border-line-subtle">
                            <td className="font-mono" style={{ padding: "4px 8px", fontSize: 10 }}>{r.date}</td>
                            <td className="font-mono" style={{ padding: "4px 8px", fontSize: 10 }}>{r.employeeId}</td>
                            <td className="font-mono" style={{ padding: "4px 8px", fontSize: 10, color: "rgb(var(--text-secondary))" }}>{r.oldTime ?? "—"}</td>
                            <td className="font-mono" style={{ padding: "4px 8px", fontSize: 10, color: r.skipReason ? "rgb(var(--text-secondary))" : "rgb(var(--st-good-solid))" }}>
                              {r.newTime ?? "—"}
                            </td>
                            <td style={{ padding: "4px 8px", fontSize: 9, color: r.skipReason ? "rgb(var(--st-warn-solid))" : "rgb(var(--st-good-solid))", fontWeight: 600 }}>
                              {r.skipReason ? (SKIP_LABELS[r.skipReason] ?? r.skipReason) : "OK"}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {error && <div style={{ fontSize: 12, color: "rgb(var(--st-crit-solid))" }}>{error}</div>}
            </div>

            <div style={{ display: "flex", gap: 8, padding: "14px 20px", borderTop: "1px solid rgb(var(--line-subtle))", justifyContent: "flex-end" }}>
              <button onClick={onClose} disabled={busy} style={{ padding: "8px 16px", borderRadius: 6, border: "1px solid rgb(var(--line-subtle))", background: "transparent", color: "rgb(var(--text-secondary))", cursor: "pointer", fontSize: 12 }}>Cancel / Abbrechen</button>
              {preview && (
                <button onClick={runApply} disabled={busy || changedCount === 0}
                  style={{ padding: "8px 16px", borderRadius: 6, border: "none", background: "rgb(var(--st-info-solid))", color: "#fff", cursor: "pointer", fontSize: 12, fontWeight: 600, opacity: (busy || changedCount === 0) ? 0.6 : 1 }}>
                  {busy ? "…" : "Apply / Übernehmen"}
                </button>
              )}
            </div>
          </>
        )}
      </div>
    </div>
  )
}
