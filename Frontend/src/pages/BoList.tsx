import { useState, useEffect } from "react"
import { api } from "../api/client"
import { ListChecks, Plus, Pencil, Trash2, X } from "lucide-react"

interface BoEntry {
  id: number
  employeeName: string
  shiftStart: string
  shiftEnd: string
  note: string | null
  sortOrder: number
}

const todayStr = () => new Date().toISOString().split("T")[0]

const inputStyle: React.CSSProperties = {
  background: "var(--card2)", border: "1px solid var(--border)", borderRadius: 6,
  padding: "7px 10px", color: "var(--text)", fontSize: 13, width: "100%", boxSizing: "border-box",
}

const monoInputStyle: React.CSSProperties = { ...inputStyle, fontFamily: "IBM Plex Mono" }

export default function BoList() {
  const [date, setDate] = useState(todayStr())
  const [entries, setEntries] = useState<BoEntry[]>([])
  const [loading, setLoading] = useState(false)

  const [showAdd, setShowAdd]   = useState(false)
  const [addName, setAddName]   = useState("")
  const [addStart, setAddStart] = useState("08:00")
  const [addEnd, setAddEnd]     = useState("17:00")
  const [addNote, setAddNote]   = useState("")

  const [editEntry, setEditEntry] = useState<BoEntry | null>(null)
  const [editStart, setEditStart] = useState("")
  const [editEnd, setEditEnd]     = useState("")
  const [editNote, setEditNote]   = useState("")

  const load = async () => {
    setLoading(true)
    try {
      setEntries(await api.boList.get(date))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [date])

  const handleAdd = async () => {
    if (!addName.trim()) return
    await api.boList.create({ date, employeeName: addName.trim(), shiftStart: addStart, shiftEnd: addEnd, note: addNote.trim() || null })
    setAddName(""); setAddStart("08:00"); setAddEnd("17:00"); setAddNote("")
    setShowAdd(false)
    load()
  }

  const handleEdit = async () => {
    if (!editEntry) return
    await api.boList.update(editEntry.id, { shiftStart: editStart, shiftEnd: editEnd, note: editNote.trim() || null })
    setEditEntry(null)
    load()
  }

  const handleDelete = async (id: number) => {
    await api.boList.remove(id)
    load()
  }

  const openEdit = (e: BoEntry) => {
    setEditEntry(e)
    setEditStart(e.shiftStart)
    setEditEnd(e.shiftEnd)
    setEditNote(e.note ?? "")
  }

  const closeAdd = () => {
    setShowAdd(false)
    setAddName(""); setAddStart("08:00"); setAddEnd("17:00"); setAddNote("")
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>

      {/* Header row */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", flexWrap: "wrap", gap: 10 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <ListChecks size={18} color="var(--accent)" />
          <h1 style={{ fontSize: 20, fontWeight: 700, color: "var(--text)", margin: 0 }}>BO Liste</h1>
          <span style={{
            background: "var(--accent)", color: "#fff", borderRadius: 12,
            padding: "2px 10px", fontSize: 12, fontWeight: 600, fontFamily: "IBM Plex Mono"
          }}>
            {entries.length}
          </span>
        </div>
        <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
          <input
            type="date" value={date}
            onChange={e => setDate(e.target.value)}
            style={{
              background: "var(--card2)", border: "1px solid var(--border)",
              color: "var(--text)", padding: "6px 10px", borderRadius: 6,
              fontSize: 12, fontFamily: "IBM Plex Mono", cursor: "pointer"
            }}
          />
          <button
            onClick={() => setShowAdd(true)}
            style={{
              background: "var(--accent)", color: "#fff", border: "none",
              borderRadius: 6, padding: "7px 14px", fontSize: 12,
              cursor: "pointer", display: "flex", alignItems: "center", gap: 6
            }}
          >
            <Plus size={13} /> Agent hinzufügen
          </button>
        </div>
      </div>

      {/* Table card */}
      <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
        {loading ? (
          <div style={{ padding: 40, textAlign: "center", color: "var(--text3)", fontSize: 13 }}>Lade...</div>
        ) : entries.length === 0 ? (
          <div style={{ padding: 48, textAlign: "center", color: "var(--text3)", fontSize: 13 }}>
            Keine Einträge für diesen Tag
          </div>
        ) : (
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 13 }}>
            <thead>
              <tr style={{ background: "var(--card2)", borderBottom: "1px solid var(--border)" }}>
                <th style={{ padding: "9px 16px", textAlign: "left", color: "var(--text3)", fontWeight: 500, width: 40 }}>#</th>
                <th style={{ padding: "9px 16px", textAlign: "left", color: "var(--text3)", fontWeight: 500 }}>Name</th>
                <th style={{ padding: "9px 16px", textAlign: "left", color: "var(--text3)", fontWeight: 500, width: 160 }}>Schicht</th>
                <th style={{ padding: "9px 16px", textAlign: "left", color: "var(--text3)", fontWeight: 500 }}>Notiz</th>
                <th style={{ padding: "9px 16px", width: 80 }}></th>
              </tr>
            </thead>
            <tbody>
              {entries.map((e, i) => (
                <tr key={e.id} style={{ borderBottom: "1px solid var(--border)" }}>
                  <td style={{ padding: "10px 16px", color: "var(--text3)", fontFamily: "IBM Plex Mono", fontSize: 11 }}>
                    {i + 1}
                  </td>
                  <td style={{ padding: "10px 16px", color: "var(--text)", fontWeight: 500 }}>
                    {e.employeeName}
                  </td>
                  <td style={{ padding: "10px 16px", color: "var(--text2)", fontFamily: "IBM Plex Mono", fontSize: 12 }}>
                    {e.shiftStart} – {e.shiftEnd}
                  </td>
                  <td style={{ padding: "10px 16px" }}>
                    {e.note && (
                      <span style={{
                        background: "var(--card2)", border: "1px solid var(--border)",
                        borderRadius: 4, padding: "2px 8px", fontSize: 11, color: "var(--text2)"
                      }}>
                        {e.note}
                      </span>
                    )}
                  </td>
                  <td style={{ padding: "10px 16px" }}>
                    <div style={{ display: "flex", gap: 6, justifyContent: "flex-end" }}>
                      <button
                        onClick={() => openEdit(e)}
                        title="Bearbeiten"
                        style={{
                          background: "none", border: "1px solid var(--border)", borderRadius: 5,
                          padding: "4px 8px", cursor: "pointer", color: "var(--text2)"
                        }}
                      >
                        <Pencil size={12} />
                      </button>
                      <button
                        onClick={() => handleDelete(e.id)}
                        title="Löschen"
                        style={{
                          background: "none", border: "1px solid var(--border)", borderRadius: 5,
                          padding: "4px 8px", cursor: "pointer", color: "var(--danger)"
                        }}
                      >
                        <Trash2 size={12} />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Add modal */}
      {showAdd && (
        <div style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.5)", display: "flex", alignItems: "center", justifyContent: "center", zIndex: 1000 }}>
          <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 10, padding: 24, width: 380, display: "flex", flexDirection: "column", gap: 14 }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <span style={{ fontWeight: 600, fontSize: 15, color: "var(--text)" }}>Agent hinzufügen</span>
              <button onClick={closeAdd} style={{ background: "none", border: "none", cursor: "pointer", color: "var(--text3)" }}>
                <X size={16} />
              </button>
            </div>

            <label style={{ display: "flex", flexDirection: "column", gap: 4, fontSize: 12, color: "var(--text2)" }}>
              Name
              <input
                value={addName} onChange={e => setAddName(e.target.value)}
                placeholder="Vollständiger Name" style={inputStyle}
                onKeyDown={e => e.key === "Enter" && handleAdd()}
                autoFocus
              />
            </label>

            <div style={{ display: "flex", gap: 10 }}>
              <label style={{ flex: 1, display: "flex", flexDirection: "column", gap: 4, fontSize: 12, color: "var(--text2)" }}>
                Beginn
                <input value={addStart} onChange={e => setAddStart(e.target.value)} style={monoInputStyle} />
              </label>
              <label style={{ flex: 1, display: "flex", flexDirection: "column", gap: 4, fontSize: 12, color: "var(--text2)" }}>
                Ende
                <input value={addEnd} onChange={e => setAddEnd(e.target.value)} style={monoInputStyle} />
              </label>
            </div>

            <label style={{ display: "flex", flexDirection: "column", gap: 4, fontSize: 12, color: "var(--text2)" }}>
              Notiz (optional)
              <input value={addNote} onChange={e => setAddNote(e.target.value)} placeholder="z.B. Newjoiner, Enviam …" style={inputStyle} />
            </label>

            <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
              <button onClick={closeAdd} style={{ background: "var(--card2)", border: "1px solid var(--border)", borderRadius: 6, padding: "7px 14px", fontSize: 12, cursor: "pointer", color: "var(--text2)" }}>
                Abbrechen
              </button>
              <button onClick={handleAdd} disabled={!addName.trim()} style={{ background: "var(--accent)", border: "none", borderRadius: 6, padding: "7px 14px", fontSize: 12, cursor: "pointer", color: "#fff", opacity: addName.trim() ? 1 : 0.5 }}>
                Hinzufügen
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Edit modal */}
      {editEntry && (
        <div style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.5)", display: "flex", alignItems: "center", justifyContent: "center", zIndex: 1000 }}>
          <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 10, padding: 24, width: 380, display: "flex", flexDirection: "column", gap: 14 }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <span style={{ fontWeight: 600, fontSize: 15, color: "var(--text)" }}>{editEntry.employeeName}</span>
              <button onClick={() => setEditEntry(null)} style={{ background: "none", border: "none", cursor: "pointer", color: "var(--text3)" }}>
                <X size={16} />
              </button>
            </div>

            <div style={{ display: "flex", gap: 10 }}>
              <label style={{ flex: 1, display: "flex", flexDirection: "column", gap: 4, fontSize: 12, color: "var(--text2)" }}>
                Beginn
                <input value={editStart} onChange={e => setEditStart(e.target.value)} style={monoInputStyle} />
              </label>
              <label style={{ flex: 1, display: "flex", flexDirection: "column", gap: 4, fontSize: 12, color: "var(--text2)" }}>
                Ende
                <input value={editEnd} onChange={e => setEditEnd(e.target.value)} style={monoInputStyle} />
              </label>
            </div>

            <label style={{ display: "flex", flexDirection: "column", gap: 4, fontSize: 12, color: "var(--text2)" }}>
              Notiz
              <input value={editNote} onChange={e => setEditNote(e.target.value)} style={inputStyle} />
            </label>

            <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
              <button onClick={() => setEditEntry(null)} style={{ background: "var(--card2)", border: "1px solid var(--border)", borderRadius: 6, padding: "7px 14px", fontSize: 12, cursor: "pointer", color: "var(--text2)" }}>
                Abbrechen
              </button>
              <button onClick={handleEdit} style={{ background: "var(--accent)", border: "none", borderRadius: 6, padding: "7px 14px", fontSize: 12, cursor: "pointer", color: "#fff" }}>
                Speichern
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
