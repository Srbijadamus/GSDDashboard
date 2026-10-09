import { Fragment, useEffect, useMemo, useRef, useState } from "react"
import type { CSSProperties } from "react"
import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { UserPlus } from "lucide-react"
import { apiFetch } from "../api/client"

// ── Rollout ──────────────────────────────────────────────────────────────────────
// A Rollout day is ONE WIC centre opened on ONE date especially for new workers, with
// its own working hours (often different from the centre's standard hours for that
// weekday). RTM enters it by hand and it is the authority: it replaces the centre's
// standard hours for that date and adds the selected workers to that centre for those
// hours. It is MANUAL and no automatic process ever creates, changes or deletes it.
//
// Every user-visible string goes through i18n (EN/DE) — see
// src/i18n/locales/{en,de}/common.json under "rollout". "Rollout", "Pipeline", "WIC"
// and "RTM" are established product names and stay unchanged in both languages.

// ── Frozen API contract (Backend/RolloutService.cs) ──────────────────────────────
type RolloutWorkerDto = {
  employeeId: string
  employeeName: string | null
  fromPipeline: boolean
  pipelineNote: string | null
  shiftEntryId: number | null
  wicShiftEntryId: number | null
}

type RolloutWarningDto = {
  employeeId: string
  employeeName: string | null
  date: string
  type: string // OVERLAP | UNKNOWN_EMPLOYEE
  otherLocation: string | null
  otherStart: string | null
  otherEnd: string | null
}

type RolloutDayDto = {
  id: number
  locationCode: string
  locationName: string
  date: string
  startTime: string
  endTime: string
  note: string | null
  standardStart: string | null
  standardEnd: string | null
  standardClosed: boolean
  isDifferentFromStandard: boolean
  isManual: boolean
  createdBy: string | null
  createdAt: string | null
  updatedBy: string | null
  updatedAt: string | null
  manualChangeNote: string | null
  workers: RolloutWorkerDto[]
  warnings: RolloutWarningDto[]
}

type RolloutCandidateDto = {
  employeeId: string
  employeeName: string | null
  teamLeadName: string | null
  isTrainee: boolean
  plannedRole: string | null
  fromPipeline: boolean
  pipelineRole: string | null
}

type RolloutCandidatesDto = {
  pipelineAvailable: boolean
  pipelineNames: string[]
  workers: RolloutCandidateDto[]
}

type StandardHoursDto = {
  locationCode: string
  date: string
  startTime: string | null
  endTime: string | null
  isClosed: boolean
}

type SaveRolloutBody = {
  locationCode: string
  date: string
  startTime: string
  endTime: string
  note: string | null
  workers: { employeeId: string; fromPipeline: boolean; pipelineNote: string | null }[]
}

type WicLocation = {
  locationCode: string
  displayName: string
  city?: string | null
  country?: string | null
  isActive?: boolean
}

// ── Local helpers ────────────────────────────────────────────────────────────────
type SelectedWorker = {
  employeeId: string
  employeeName: string | null
  fromPipeline: boolean
  pipelineNote: string
}

/** A fetched answer is only used while it still belongs to the centre+date it was asked for. */
type Keyed<T> = { key: string; value: T }

function isoDate(d: Date): string {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, "0")
  const day = String(d.getDate()).padStart(2, "0")
  return `${y}-${m}-${day}`
}

function addDays(iso: string, days: number): string {
  const d = new Date(`${iso}T00:00:00`)
  d.setDate(d.getDate() + days)
  return isoDate(d)
}

function errMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e)
}

/** Server timestamps arrive as yyyy-MM-ddTHH:mm:ss (see RolloutService.ToDtoAsync). */
function fmtStamp(value: string | null): string {
  if (!value) return "—"
  return value.replace("T", " ").slice(0, 16)
}

// ── Shared styles (same conventions as Pipeline.tsx / WicSchedule.tsx) ────────────
const inputStyle: CSSProperties = { padding: "7px 10px", borderRadius: 6, fontSize: 12, width: "100%", fontFamily: "IBM Plex Sans", outline: "none" }
const inputCls = "bg-sunken border border-line-subtle text-ink"
const labelStyle: CSSProperties = { fontSize: 11, marginBottom: 4, display: "block" }
const labelCls = "text-ink-soft"
const badgeStyle: CSSProperties = { padding: "2px 8px", borderRadius: 4, fontSize: 10, fontWeight: 600, whiteSpace: "nowrap" }
const primaryBtnStyle: CSSProperties = { border: "none", color: "#fff", padding: "8px 16px", borderRadius: 6, fontSize: 12, cursor: "pointer", fontWeight: 600 }
const secondaryBtnStyle: CSSProperties = { padding: "8px 16px", borderRadius: 6, fontSize: 12, cursor: "pointer" }
const smallBtnStyle: CSSProperties = { padding: "4px 8px", borderRadius: 4, fontSize: 10, cursor: "pointer" }
const tdStyle: CSSProperties = { padding: "9px 12px", fontSize: 12, verticalAlign: "top", borderBottom: "1px solid rgb(var(--border-subtle))" }
const thStyle: CSSProperties = { padding: "10px 12px", fontSize: 10, fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em", textAlign: "left" }
const tdCls = "text-ink"
const thCls = "text-ink-soft border-b border-line-subtle bg-sunken"

function Badge({ text, cls, title }: { text: string; cls: string; title?: string }) {
  return <span title={title} className={cls} style={badgeStyle}>{text}</span>
}

/** One non-blocking warning line (OVERLAP / UNKNOWN_EMPLOYEE). */
function WarningText({ warning }: { warning: RolloutWarningDto }) {
  const { t } = useTranslation()
  if (warning.type === "UNKNOWN_EMPLOYEE") {
    return <span>{t("rollout.warnings.unknownEmployee", { id: warning.employeeId })}</span>
  }
  const name = warning.employeeName ?? warning.employeeId
  const time = warning.otherStart && warning.otherEnd ? `${warning.otherStart}–${warning.otherEnd}` : "—"
  const location = warning.otherLocation && warning.otherLocation.trim().length > 0
    ? warning.otherLocation
    : t("rollout.warnings.unknownLocation")
  return <span>{t("rollout.warnings.overlap", { name, date: warning.date, location, time })}</span>
}

function WarningList({ warnings, emptyText }: { warnings: RolloutWarningDto[]; emptyText?: string }) {
  const { t } = useTranslation()
  if (warnings.length === 0) {
    return <span className="text-ink-soft">{emptyText ?? t("rollout.warnings.none")}</span>
  }
  return (
    <ul style={{ margin: 0, paddingLeft: 14, display: "flex", flexDirection: "column", gap: 2 }}>
      {warnings.map((w, i) => (
        <li key={`${w.employeeId}-${w.type}-${i}`} className="text-warn-fg" style={{ fontSize: 11 }}>
          <WarningText warning={w} />
        </li>
      ))}
    </ul>
  )
}

// ── Add / Edit modal ─────────────────────────────────────────────────────────────
type RolloutModalProps = {
  day: RolloutDayDto | null
  locations: WicLocation[]
  onClose: () => void
  onSaved: (warnings: RolloutWarningDto[]) => void
}

function RolloutModal({ day, locations, onClose, onSaved }: RolloutModalProps) {
  const { t } = useTranslation()
  const isEdit = day !== null

  const [locationCode, setLocationCode] = useState(day?.locationCode ?? "")
  const [date, setDate] = useState(day?.date ?? isoDate(new Date()))
  const [startTime, setStartTime] = useState(day?.startTime ?? "")
  const [endTime, setEndTime] = useState(day?.endTime ?? "")
  const [noteText, setNoteText] = useState(day?.note ?? "")

  // Every answer is stored with the centre|date key it belongs to, so switching the centre
  // can never show the previous centre's hours, candidates or confirmation.
  const [standardState, setStandardState] = useState<Keyed<StandardHoursDto> | null>(null)
  const [standardErrorState, setStandardErrorState] = useState<Keyed<string> | null>(null)

  const [candidatesState, setCandidatesState] = useState<Keyed<RolloutCandidatesDto> | null>(null)
  const [candidatesErrorState, setCandidatesErrorState] = useState<Keyed<string> | null>(null)
  const [search, setSearch] = useState("")

  const [selected, setSelected] = useState<SelectedWorker[]>(() =>
    (day?.workers ?? []).map(w => ({
      employeeId: w.employeeId,
      employeeName: w.employeeName,
      fromPipeline: w.fromPipeline,
      pipelineNote: w.pipelineNote ?? "",
    }))
  )
  // An out-of-Pipeline pick is an explicit decision: it waits here until RTM confirms.
  const [confirmState, setConfirmState] = useState<Keyed<RolloutCandidateDto> | null>(null)

  const [previewWarnings, setPreviewWarnings] = useState<RolloutWarningDto[]>([])
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)

  const centreKey = locationCode && date ? `${locationCode}|${date}` : ""

  const standard = standardState && standardState.key === centreKey ? standardState.value : null
  const standardError = standardErrorState && standardErrorState.key === centreKey ? standardErrorState.value : null
  const standardLoading = centreKey !== "" && standardState?.key !== centreKey && standardErrorState?.key !== centreKey

  const candidates = useMemo(
    () => (candidatesState && candidatesState.key === centreKey ? candidatesState.value.workers : []),
    [candidatesState, centreKey]
  )
  const pipelineAvailable = candidatesState && candidatesState.key === centreKey ? candidatesState.value.pipelineAvailable : null
  const pipelineNames = useMemo(
    () => (candidatesState && candidatesState.key === centreKey ? candidatesState.value.pipelineNames : []),
    [candidatesState, centreKey]
  )
  const candidatesError = candidatesErrorState && candidatesErrorState.key === centreKey ? candidatesErrorState.value : null
  const candidatesLoading = centreKey !== "" && candidatesState?.key !== centreKey && candidatesErrorState?.key !== centreKey
  const confirmCandidate = confirmState && confirmState.key === centreKey ? confirmState.value : null

  // Standard hours may pre-fill the times only for a centre+date combination RTM has not
  // answered for yet. While editing, the stored times belong to the loaded day.
  const appliedStandardKey = useRef(isEdit && day ? `${day.locationCode}|${day.date}` : "")

  // Centre + date chosen → the centre's STANDARD hours for that weekday (pattern only).
  useEffect(() => {
    if (!centreKey) return
    let cancelled = false
    apiFetch<StandardHoursDto>(
      `/api/rollout/standard-hours?locationCode=${encodeURIComponent(locationCode)}&date=${date}`
    )
      .then(std => {
        if (cancelled) return
        setStandardState({ key: centreKey, value: std })
        setStandardErrorState(null)
        if (appliedStandardKey.current !== centreKey) {
          appliedStandardKey.current = centreKey
          setStartTime(std.startTime ?? "")
          setEndTime(std.endTime ?? "")
        }
      })
      .catch((e: unknown) => {
        if (cancelled) return
        setStandardErrorState({ key: centreKey, value: errMessage(e) })
      })
    return () => { cancelled = true }
  }, [centreKey, locationCode, date])

  // Centre + date chosen → the agents the Pipeline page recorded for that centre and date.
  useEffect(() => {
    if (!centreKey) return
    let cancelled = false
    apiFetch<RolloutCandidatesDto>(
      `/api/rollout/candidates?locationCode=${encodeURIComponent(locationCode)}&date=${date}`
    )
      .then(res => {
        if (cancelled) return
        setCandidatesState({ key: centreKey, value: res })
        setCandidatesErrorState(null)
      })
      .catch((e: unknown) => {
        if (cancelled) return
        setCandidatesErrorState({ key: centreKey, value: errMessage(e) })
      })
    return () => { cancelled = true }
  }, [centreKey, locationCode, date])

  const filteredCandidates = useMemo(() => {
    const q = search.trim().toLowerCase()
    return candidates
      .filter(c => !q
        || (c.employeeName ?? "").toLowerCase().includes(q)
        || c.employeeId.toLowerCase().includes(q))
      .sort((a, b) =>
        Number(b.fromPipeline) - Number(a.fromPipeline) ||
        (a.employeeName ?? a.employeeId).localeCompare(b.employeeName ?? b.employeeId))
  }, [candidates, search])

  const pipelineCandidates = filteredCandidates.filter(c => c.fromPipeline)
  const otherCandidates = filteredCandidates.filter(c => !c.fromPipeline)
  const selectedIds = new Set(selected.map(s => s.employeeId))

  // Pipeline names are free text: some may match no employee at all — show them.
  const unmatchedPipelineNames = useMemo(() => {
    if (pipelineNames.length === 0) return []
    const matched = new Set(
      candidates.filter(c => c.pipelineRole).map(c => (c.pipelineRole ?? "").trim().toLowerCase())
    )
    return pipelineNames.filter(n => !matched.has(n.trim().toLowerCase()))
  }, [pipelineNames, candidates])

  function addCandidate(c: RolloutCandidateDto) {
    setSelected(prev => prev.some(s => s.employeeId === c.employeeId)
      ? prev
      : [...prev, {
          employeeId: c.employeeId,
          employeeName: c.employeeName,
          fromPipeline: c.fromPipeline, // exactly the candidate's value — never forced
          pipelineNote: "",
        }])
  }

  function removeSelected(employeeId: string) {
    setSelected(prev => prev.filter(s => s.employeeId !== employeeId))
  }

  function updateWorkerNote(employeeId: string, value: string) {
    setSelected(prev => prev.map(s => s.employeeId === employeeId ? { ...s, pipelineNote: value } : s))
  }

  const standardRange = standard && !standard.isClosed && standard.startTime && standard.endTime
    ? `${standard.startTime}–${standard.endTime}`
    : null
  const timesComplete = startTime !== "" && endTime !== ""
  const timesDifferFromStandard = standard !== null && (standard.isClosed
    ? startTime !== "" || endTime !== ""
    : startTime !== (standard.startTime ?? "") || endTime !== (standard.endTime ?? ""))

  async function handleSave() {
    if (!locationCode || !date) { setSaveError(t("rollout.modal.requiredCentre")); return }
    if (!startTime || !endTime) { setSaveError(t("rollout.modal.requiredTimes")); return }
    if (endTime <= startTime) { setSaveError(t("rollout.modal.invalidRange")); return }

    const body: SaveRolloutBody = {
      locationCode,
      date,
      startTime,
      endTime,
      note: noteText.trim() ? noteText.trim() : null,
      workers: selected.map(s => ({
        employeeId: s.employeeId,
        fromPipeline: s.fromPipeline,
        pipelineNote: s.pipelineNote.trim() ? s.pipelineNote.trim() : null,
      })),
    }

    setSaving(true)
    setSaveError(null)

    // Preview first: it writes nothing and a warning NEVER blocks the save.
    let warnings: RolloutWarningDto[] | null = null
    try {
      warnings = await apiFetch<RolloutWarningDto[]>("/api/rollout/preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      })
      setPreviewWarnings(warnings)
    } catch {
      setPreviewWarnings([])
    }

    try {
      const options: RequestInit = {
        method: isEdit && day ? "PUT" : "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      }
      const path = isEdit && day ? `/api/rollout/${day.id}` : "/api/rollout"
      await apiFetch<RolloutDayDto>(path, options)
      onSaved(warnings ?? [])
    } catch (e) {
      setSaveError(t("rollout.modal.error", { message: errMessage(e) }))
    } finally {
      setSaving(false)
    }
  }

  function candidateRow(c: RolloutCandidateDto) {
    const isSelected = selectedIds.has(c.employeeId)
    return (
      <div
        key={c.employeeId}
        className="bg-sunken border border-line-subtle"
        style={{ borderRadius: 6, padding: "6px 8px", display: "flex", alignItems: "center", gap: 8 }}
      >
        <div style={{ flex: 1, minWidth: 0 }}>
          <div className="text-ink" style={{ fontSize: 12, fontWeight: 600, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
            {c.employeeName ?? c.employeeId}
          </div>
          <div className="text-ink-muted font-mono" style={{ fontSize: 10 }}>
            {c.employeeId}
            {c.teamLeadName ? ` · ${t("rollout.modal.teamLead", { name: c.teamLeadName })}` : ""}
            {c.isTrainee ? ` · ${t("rollout.modal.trainee")}` : ""}
            {c.plannedRole ? ` · ${t("rollout.modal.role", { role: c.plannedRole })}` : ""}
          </div>
        </div>
        {c.fromPipeline && (
          <Badge
            text={t("rollout.modal.pipelineBadge")}
            cls="bg-info-bg border border-info-bd text-info-fg"
            title={c.pipelineRole ? t("rollout.modal.pipelineRole", { role: c.pipelineRole }) : undefined}
          />
        )}
        {isSelected
          ? <span className="text-ink-soft" style={{ fontSize: 11 }}>{t("rollout.modal.added")}</span>
          : (
            <button
              type="button"
              onClick={() => c.fromPipeline ? addCandidate(c) : setConfirmState({ key: centreKey, value: c })}
              className="bg-info-bg border border-info-bd text-info-fg"
              style={smallBtnStyle}
            >
              {t("rollout.modal.add")}
            </button>
          )}
      </div>
    )
  }

  return (
    <div
      style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.7)", zIndex: 1000, display: "flex", alignItems: "center", justifyContent: "center" }}
      onClick={e => e.target === e.currentTarget && !saving && onClose()}
    >
      <div className="bg-raised border border-line-subtle" style={{ borderRadius: 10, padding: 24, width: 720, maxWidth: "95vw", maxHeight: "92vh", overflowY: "auto" }}>
        <h2 className="text-ink" style={{ fontSize: 16, fontWeight: 600, marginBottom: 6 }}>
          {isEdit ? t("rollout.modal.editTitle") : t("rollout.modal.createTitle")}
        </h2>
        <p className="text-ink-soft" style={{ fontSize: 11, marginBottom: 18, lineHeight: 1.5 }}>
          {t("rollout.modal.manualNotice")}
        </p>

        <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <div style={{ display: "flex", gap: 8 }}>
            <div style={{ flex: 2 }}>
              <label className={labelCls} style={labelStyle}>{t("rollout.modal.centre")}</label>
              <select value={locationCode} onChange={e => setLocationCode(e.target.value)} className={inputCls} style={inputStyle}>
                <option value="">{t("rollout.modal.selectCentre")}</option>
                {locations.map(l => <option key={l.locationCode} value={l.locationCode}>{l.displayName}</option>)}
              </select>
            </div>
            <div style={{ flex: 1 }}>
              <label className={labelCls} style={labelStyle}>{t("rollout.modal.date")}</label>
              <input type="date" value={date} onChange={e => setDate(e.target.value)} className={inputCls} style={inputStyle} />
            </div>
          </div>

          <div style={{ display: "flex", gap: 8 }}>
            <div style={{ flex: 1 }}>
              <label className={labelCls} style={labelStyle}>{t("rollout.modal.startTime")}</label>
              <input type="time" value={startTime} onChange={e => setStartTime(e.target.value)} className={inputCls} style={inputStyle} />
            </div>
            <div style={{ flex: 1 }}>
              <label className={labelCls} style={labelStyle}>{t("rollout.modal.endTime")}</label>
              <input type="time" value={endTime} onChange={e => setEndTime(e.target.value)} className={inputCls} style={inputStyle} />
            </div>
          </div>

          <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
            {standardLoading && <div className="text-ink-soft" style={{ fontSize: 11 }}>{t("rollout.modal.standardLoading")}</div>}
            {!standardLoading && standardError && <div className="text-crit-fg" style={{ fontSize: 11 }}>{t("rollout.modal.standardError")}</div>}
            {!standardLoading && standard?.isClosed && (
              <div className="text-ink-soft" style={{ fontSize: 11 }}>{t("rollout.modal.standardClosed")}</div>
            )}
            {!standardLoading && standardRange && (
              <div className="text-ink-soft" style={{ fontSize: 11 }}>{t("rollout.modal.standardHint", { range: standardRange })}</div>
            )}
            {timesComplete && timesDifferFromStandard && (
              <div className="bg-warn-bg border border-warn-bd text-warn-fg" style={{ borderRadius: 6, padding: "6px 10px", fontSize: 11, fontWeight: 600 }}>
                {standard?.isClosed
                  ? t("rollout.modal.differentClosed")
                  : t("rollout.modal.different", { range: standardRange ?? "—" })}
              </div>
            )}
            {timesComplete && !timesDifferFromStandard && standardRange && (
              <div className="text-ink-soft" style={{ fontSize: 11 }}>{t("rollout.modal.matchesStandard")}</div>
            )}
          </div>

          <div>
            <label className={labelCls} style={labelStyle}>{t("rollout.modal.note")}</label>
            <textarea
              value={noteText}
              onChange={e => setNoteText(e.target.value)}
              rows={2}
              className={inputCls}
              style={{ ...inputStyle, resize: "vertical" }}
              placeholder={t("rollout.modal.notePlaceholder")}
            />
          </div>

          {/* ── Worker picker ─────────────────────────────────────────────────── */}
          <div className="border-t border-line-subtle" style={{ paddingTop: 12 }}>
            <div className="text-ink" style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>{t("rollout.modal.workers")}</div>
            <div className="text-ink-soft" style={{ fontSize: 11, marginBottom: 8 }}>{t("rollout.modal.workersHint")}</div>

            {!centreKey
              ? <div className="text-ink-soft" style={{ fontSize: 11 }}>{t("rollout.modal.workersNeedCentre")}</div>
              : (
                <>
                  <input
                    value={search}
                    onChange={e => setSearch(e.target.value)}
                    className={inputCls}
                    style={inputStyle}
                    placeholder={t("rollout.modal.searchWorkers")}
                  />

                  {pipelineAvailable === false && (
                    <div className="bg-warn-bg border border-warn-bd text-warn-fg" style={{ borderRadius: 6, padding: "6px 10px", fontSize: 11, marginTop: 8 }}>
                      {t("rollout.modal.pipelineUnavailable")}
                    </div>
                  )}
                  {pipelineAvailable === true && (
                    <div className="text-ink-soft" style={{ fontSize: 11, marginTop: 8 }}>{t("rollout.modal.pipelineAvailable")}</div>
                  )}
                  {unmatchedPipelineNames.length > 0 && (
                    <div className="text-ink-soft" style={{ fontSize: 11, marginTop: 4 }}>
                      {t("rollout.modal.pipelineNamesUnmatched", { names: unmatchedPipelineNames.join(", ") })}
                    </div>
                  )}

                  {confirmCandidate && (
                    <div className="bg-warn-bg border border-warn-bd text-warn-fg" style={{ borderRadius: 6, padding: "8px 10px", fontSize: 11, marginTop: 8 }}>
                      <div style={{ marginBottom: 6 }}>
                        {t("rollout.modal.addAnywayQuestion")} — <strong>{confirmCandidate.employeeName ?? confirmCandidate.employeeId}</strong>
                      </div>
                      <div style={{ display: "flex", gap: 6 }}>
                        <button
                          type="button"
                          onClick={() => { addCandidate(confirmCandidate); setConfirmState(null) }}
                          className="bg-info-solid"
                          style={{ ...smallBtnStyle, color: "#fff", border: "none", fontWeight: 600 }}
                        >
                          {t("rollout.modal.addAnyway")}
                        </button>
                        <button
                          type="button"
                          onClick={() => setConfirmState(null)}
                          className="bg-sunken border border-line-subtle text-ink-muted"
                          style={smallBtnStyle}
                        >
                          {t("rollout.modal.cancelAdd")}
                        </button>
                      </div>
                    </div>
                  )}

                  {candidatesLoading && <div className="text-ink-soft" style={{ fontSize: 11, marginTop: 8 }}>{t("rollout.modal.candidatesLoading")}</div>}
                  {!candidatesLoading && candidatesError && (
                    <div className="text-crit-fg" style={{ fontSize: 11, marginTop: 8 }}>{t("rollout.modal.candidatesError", { message: candidatesError })}</div>
                  )}

                  {!candidatesLoading && !candidatesError && (
                    <div style={{ maxHeight: 220, overflowY: "auto", display: "flex", flexDirection: "column", gap: 6, marginTop: 8 }}>
                      {filteredCandidates.length === 0 && (
                        <div className="text-ink-soft" style={{ fontSize: 11 }}>{t("rollout.modal.candidatesEmpty")}</div>
                      )}
                      {pipelineCandidates.length > 0 && (
                        <div className="text-ink-soft" style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".06em" }}>
                          {t("rollout.modal.sectionPipeline")}
                        </div>
                      )}
                      {pipelineCandidates.map(candidateRow)}
                      {otherCandidates.length > 0 && (
                        <div className="text-ink-soft" style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".06em", marginTop: 4 }}>
                          {t("rollout.modal.sectionOther")}
                        </div>
                      )}
                      {otherCandidates.map(candidateRow)}
                    </div>
                  )}

                  {/* Selected workers */}
                  <div className="border-t border-line-subtle" style={{ marginTop: 12, paddingTop: 10 }}>
                    <div className="text-ink" style={{ fontSize: 12, fontWeight: 600, marginBottom: 6 }}>
                      {t("rollout.modal.selectedTitle", { n: selected.length })}
                    </div>
                    {selected.length === 0
                      ? <div className="text-ink-soft" style={{ fontSize: 11 }}>{t("rollout.modal.selectedNone")}</div>
                      : (
                        <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                          {selected.map(s => (
                            <div
                              key={s.employeeId}
                              className="bg-sunken border border-line-subtle"
                              style={{ borderRadius: 6, padding: "6px 8px", display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}
                            >
                              <div style={{ flex: 1, minWidth: 140 }}>
                                <div className="text-ink" style={{ fontSize: 12, fontWeight: 600 }}>{s.employeeName ?? s.employeeId}</div>
                                <div className="text-ink-muted font-mono" style={{ fontSize: 10 }}>{s.employeeId}</div>
                              </div>
                              {s.fromPipeline
                                ? <Badge text={t("rollout.modal.pipelineBadge")} cls="bg-info-bg border border-info-bd text-info-fg" />
                                : <Badge text={t("rollout.modal.notPipelineBadge")} cls="bg-warn-bg border border-warn-bd text-warn-fg" />}
                              <button
                                type="button"
                                onClick={() => removeSelected(s.employeeId)}
                                className="bg-crit-bg border border-crit-bd text-crit-fg"
                                style={smallBtnStyle}
                              >
                                {t("rollout.modal.remove")}
                              </button>
                              {!s.fromPipeline && (
                                <input
                                  value={s.pipelineNote}
                                  onChange={e => updateWorkerNote(s.employeeId, e.target.value)}
                                  className={inputCls}
                                  style={{ ...inputStyle, width: "100%", fontSize: 11 }}
                                  placeholder={t("rollout.modal.outOfPipelineNotePlaceholder")}
                                />
                              )}
                            </div>
                          ))}
                        </div>
                      )}
                  </div>
                </>
              )}
          </div>

          {/* Preview warnings: informational only, they never block the save. */}
          {previewWarnings.length > 0 && (
            <div className="bg-warn-bg border border-warn-bd" style={{ borderRadius: 6, padding: "8px 10px" }}>
              <div className="text-warn-fg" style={{ fontSize: 11, fontWeight: 600, marginBottom: 4 }}>{t("rollout.modal.previewTitle")}</div>
              <WarningList warnings={previewWarnings} />
            </div>
          )}

          {saveError && (
            <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ borderRadius: 6, padding: "8px 12px", fontSize: 12 }}>
              {saveError}
            </div>
          )}
        </div>

        <div style={{ display: "flex", gap: 8, justifyContent: "flex-end", marginTop: 20 }}>
          <button type="button" onClick={onClose} disabled={saving} className="bg-sunken border border-line-subtle text-ink-muted" style={secondaryBtnStyle}>
            {t("rollout.modal.cancel")}
          </button>
          <button type="button" onClick={handleSave} disabled={saving} className="bg-info-solid" style={primaryBtnStyle}>
            {saving ? t("rollout.modal.saving") : t("rollout.modal.save")}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Page ─────────────────────────────────────────────────────────────────────────
export default function Rollout() {
  const { t } = useTranslation()

  const today = isoDate(new Date())
  const [from, setFrom] = useState(today)
  const [to, setTo] = useState(addDays(today, 60))
  const [locationFilter, setLocationFilter] = useState("")

  const [locations, setLocations] = useState<WicLocation[]>([])
  const [locationsError, setLocationsError] = useState<string | null>(null)

  const [modalOpen, setModalOpen] = useState(false)
  const [editDay, setEditDay] = useState<RolloutDayDto | null>(null)

  const [confirmDeleteId, setConfirmDeleteId] = useState<number | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [lastWarnings, setLastWarnings] = useState<RolloutWarningDto[]>([])
  const [savedNotice, setSavedNotice] = useState(false)

  const activeLocations = useMemo(() => locations.filter(l => l.isActive !== false), [locations])

  // Reloads whenever the filter bar changes.
  const daysQuery = useQuery<RolloutDayDto[]>({
    queryKey: ["rollout-days", from, to, locationFilter],
    queryFn: () => {
      const code = locationFilter ? `&locationCode=${encodeURIComponent(locationFilter)}` : ""
      return apiFetch<RolloutDayDto[]>(`/api/rollout?from=${from}&to=${to}${code}`)
    },
  })
  const days = daysQuery.data ?? []
  const loading = daysQuery.isLoading
  const loadError = daysQuery.error ? errMessage(daysQuery.error) : null
  const reload = () => { void daysQuery.refetch() }

  useEffect(() => {
    let cancelled = false
    apiFetch<WicLocation[]>("/api/wic/locations")
      .then(data => { if (!cancelled) setLocations(data) })
      .catch((e: unknown) => { if (!cancelled) setLocationsError(errMessage(e)) })
    return () => { cancelled = true }
  }, [])

  async function handleDelete(id: number) {
    setActionError(null)
    try {
      await apiFetch<{ deleted: boolean }>(`/api/rollout/${id}`, { method: "DELETE" })
      setConfirmDeleteId(null)
      await daysQuery.refetch()
    } catch (e) {
      setActionError(t("rollout.delete.error", { message: errMessage(e) }))
    }
  }

  function openCreate() {
    setEditDay(null)
    setModalOpen(true)
  }

  function openEdit(day: RolloutDayDto) {
    setEditDay(day)
    setModalOpen(true)
  }

  function closeModal() {
    setModalOpen(false)
    setEditDay(null)
  }

  function handleSaved(warnings: RolloutWarningDto[]) {
    setLastWarnings(warnings)
    setSavedNotice(true)
    closeModal()
    reload()
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", flexWrap: "wrap", gap: 10 }}>
        <div style={{ display: "flex", alignItems: "flex-start", gap: 10 }}>
          <UserPlus size={18} className="text-info-fg" style={{ marginTop: 4 }} />
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
              <h1 className="text-ink" style={{ fontSize: 22, fontWeight: 600, margin: 0 }}>{t("rollout.title")}</h1>
              <Badge text={t("rollout.manualBadge")} cls="bg-warn-bg border border-warn-bd text-warn-fg" title={t("rollout.manualBadgeTooltip")} />
            </div>
            <p className="text-ink-soft" style={{ fontSize: 12, marginTop: 6, maxWidth: 900, lineHeight: 1.55 }}>
              {t("rollout.subtitle")}
            </p>
          </div>
        </div>
        <button type="button" onClick={openCreate} className="bg-info-solid" style={primaryBtnStyle}>
          {t("rollout.action.new")}
        </button>
      </div>

      {/* Filter bar */}
      <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, padding: "12px 14px", display: "flex", gap: 10, alignItems: "flex-end", flexWrap: "wrap" }}>
        <div style={{ minWidth: 220 }}>
          <label className={labelCls} style={labelStyle}>{t("rollout.filter.centre")}</label>
          <select value={locationFilter} onChange={e => setLocationFilter(e.target.value)} className={inputCls} style={inputStyle}>
            <option value="">{t("rollout.filter.allCentres")}</option>
            {activeLocations.map(l => <option key={l.locationCode} value={l.locationCode}>{l.displayName}</option>)}
          </select>
        </div>
        <div>
          <label className={labelCls} style={labelStyle}>{t("rollout.filter.from")}</label>
          <input type="date" value={from} onChange={e => setFrom(e.target.value)} className={inputCls} style={inputStyle} />
        </div>
        <div>
          <label className={labelCls} style={labelStyle}>{t("rollout.filter.to")}</label>
          <input type="date" value={to} onChange={e => setTo(e.target.value)} className={inputCls} style={inputStyle} />
        </div>
        <button type="button" onClick={reload} className="bg-sunken border border-line-subtle text-ink" style={secondaryBtnStyle}>
          {t("rollout.filter.reload")}
        </button>
      </div>

      {locationsError && (
        <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ borderRadius: 6, padding: "10px 14px", fontSize: 13 }}>
          {t("rollout.locationsError", { message: locationsError })}
        </div>
      )}
      {loadError && (
        <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ borderRadius: 6, padding: "10px 14px", fontSize: 13 }}>
          {t("rollout.loadError", { message: loadError })}
        </div>
      )}
      {actionError && (
        <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ borderRadius: 6, padding: "10px 14px", fontSize: 13 }}>
          {actionError}
        </div>
      )}
      {savedNotice && (
        <div className="bg-good-bg border border-good-bd text-good-fg" style={{ borderRadius: 6, padding: "10px 14px", fontSize: 13 }}>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 10, alignItems: "flex-start" }}>
            <div>
              <div style={{ fontWeight: 600 }}>{t("rollout.saved")}</div>
              {lastWarnings.length > 0 && (
                <div style={{ marginTop: 4 }}>
                  <div style={{ fontSize: 11, fontWeight: 600 }}>{t("rollout.lastWarnings")}</div>
                  <WarningList warnings={lastWarnings} />
                </div>
              )}
            </div>
            <button type="button" onClick={() => setSavedNotice(false)} className="bg-sunken border border-line-subtle text-ink-muted" style={smallBtnStyle}>
              {t("rollout.close")}
            </button>
          </div>
        </div>
      )}

      {loading && <div className="text-ink-soft" style={{ padding: 40, textAlign: "center" }}>{t("rollout.loading")}</div>}

      {!loading && (
        <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, overflow: "hidden" }}>
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
              <thead>
                <tr>
                  {[
                    t("rollout.col.date"),
                    t("rollout.col.centre"),
                    t("rollout.col.hours"),
                    t("rollout.col.standard"),
                    t("rollout.col.note"),
                    t("rollout.col.workers"),
                    t("rollout.col.provenance"),
                    t("rollout.col.warnings"),
                    t("rollout.col.actions"),
                  ].map(h => <th key={h} className={thCls} style={thStyle}>{h}</th>)}
                </tr>
              </thead>
              <tbody>
                {days.length === 0 && (
                  <tr>
                    <td colSpan={9} className={`${tdCls} text-ink-soft`} style={{ ...tdStyle, textAlign: "center", padding: 40 }}>
                      {t("rollout.empty")}
                    </td>
                  </tr>
                )}
                {days.map(d => {
                  const standardRange = d.standardStart && d.standardEnd ? `${d.standardStart}–${d.standardEnd}` : null
                  const names = d.workers.map(w => w.employeeName ?? w.employeeId)
                  return (
                    <Fragment key={d.id}>
                      <tr className="hover:bg-hovered">
                        <td className={`${tdCls} font-mono`} style={{ ...tdStyle, whiteSpace: "nowrap" }}>{d.date}</td>
                        <td className={tdCls} style={{ ...tdStyle, fontWeight: 600 }}>
                          {d.locationName}
                          <div className="text-ink-muted font-mono" style={{ fontSize: 10 }}>{d.locationCode}</div>
                        </td>
                        <td className={`${tdCls} font-mono`} style={{ ...tdStyle, whiteSpace: "nowrap" }}>{d.startTime}–{d.endTime}</td>
                        <td className={tdCls} style={tdStyle}>
                          {d.isDifferentFromStandard ? (
                            <div style={{ display: "flex", flexDirection: "column", gap: 4, alignItems: "flex-start" }}>
                              <Badge text={t("rollout.badge.different")} cls="bg-warn-bg border border-warn-bd text-warn-fg" />
                              {d.standardClosed
                                ? <span className="text-ink-soft" style={{ fontSize: 10 }}>{t("rollout.badge.standardClosed")}</span>
                                : standardRange
                                  ? <span className="text-ink-soft font-mono" style={{ fontSize: 10 }}>{t("rollout.badge.standardRange", { range: standardRange })}</span>
                                  : null}
                            </div>
                          ) : (
                            <span className="text-ink-soft" style={{ fontSize: 11 }}>{t("rollout.badge.matchesStandard")}</span>
                          )}
                        </td>
                        <td className="text-ink-muted" style={{ ...tdStyle, maxWidth: 200 }}>{d.note ?? "—"}</td>
                        <td className={tdCls} style={{ ...tdStyle, maxWidth: 240 }}>
                          <div>{t("rollout.workers.count", { count: d.workers.length })}</div>
                          {names.length > 0 && (
                            <div className="text-ink-muted" style={{ fontSize: 10, marginTop: 2 }}>
                              {names.slice(0, 3).join(", ")}
                              {names.length > 3 ? ` ${t("rollout.workers.more", { n: names.length - 3 })}` : ""}
                            </div>
                          )}
                          {d.workers.some(w => !w.fromPipeline) && (
                            <div style={{ marginTop: 3 }}>
                              <Badge text={t("rollout.workers.notPipelineBadge")} cls="bg-warn-bg border border-warn-bd text-warn-fg" />
                            </div>
                          )}
                        </td>
                        <td className="text-ink-muted" style={{ ...tdStyle, fontSize: 11 }}>
                          {d.isManual && (
                            <div style={{ marginBottom: 3 }}>
                              <Badge text={t("rollout.badge.manual")} cls="bg-info-bg border border-info-bd text-info-fg" title={t("rollout.badge.manualTooltip")} />
                            </div>
                          )}
                          <div>{t("rollout.provenance.created", { by: d.createdBy ?? t("rollout.provenance.unknown"), at: fmtStamp(d.createdAt) })}</div>
                          {d.updatedAt && d.updatedAt !== d.createdAt && (
                            <div>{t("rollout.provenance.updated", { by: d.updatedBy ?? t("rollout.provenance.unknown"), at: fmtStamp(d.updatedAt) })}</div>
                          )}
                        </td>
                        <td className={tdCls} style={{ ...tdStyle, maxWidth: 280 }}>
                          <WarningList warnings={d.warnings} />
                        </td>
                        <td className={tdCls} style={{ ...tdStyle, whiteSpace: "nowrap" }}>
                          <div style={{ display: "flex", gap: 4 }}>
                            <button type="button" onClick={() => openEdit(d)} className="bg-info-bg border border-info-bd text-info-fg" style={smallBtnStyle}>
                              {t("rollout.action.edit")}
                            </button>
                            <button type="button" onClick={() => setConfirmDeleteId(d.id)} className="bg-crit-bg border border-crit-bd text-crit-fg" style={smallBtnStyle}>
                              {t("rollout.action.delete")}
                            </button>
                          </div>
                        </td>
                      </tr>
                      {confirmDeleteId === d.id && (
                        <tr>
                          <td colSpan={9} className="bg-crit-bg border-b border-crit-bd text-crit-fg" style={{ padding: "10px 12px", fontSize: 12 }}>
                            <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
                              <span style={{ fontWeight: 600 }}>{t("rollout.delete.confirm")}</span>
                              <button
                                type="button"
                                onClick={() => void handleDelete(d.id)}
                                className="bg-crit-solid"
                                style={{ ...smallBtnStyle, border: "none", color: "#fff", fontWeight: 600 }}
                              >
                                {t("rollout.delete.yes")}
                              </button>
                              <button
                                type="button"
                                onClick={() => setConfirmDeleteId(null)}
                                className="bg-sunken border border-line-subtle text-ink-muted"
                                style={smallBtnStyle}
                              >
                                {t("rollout.delete.no")}
                              </button>
                            </div>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  )
                })}
              </tbody>
            </table>
          </div>
          <div className="border-t border-line-subtle text-ink-soft font-mono" style={{ padding: "8px 12px", fontSize: 11 }}>
            {t("rollout.count", { count: days.length })}
            {days.some(d => d.warnings.length > 0) ? ` · ${t("rollout.warnings.nonBlocking")}` : ""}
          </div>
        </div>
      )}

      {modalOpen && (
        <RolloutModal
          day={editDay}
          locations={activeLocations}
          onClose={closeModal}
          onSaved={handleSaved}
        />
      )}
    </div>
  )
}
