import { useState, useRef, useEffect } from "react"
import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { Download, FileSpreadsheet, FileArchive } from "lucide-react"
import { apiFetch } from "../api/client"

// ── Types ─────────────────────────────────────────────────────────────────────

interface Employee {
  employeeId: string
  fullName: string | null
  isActive: boolean
}

type ExportType = "ShiftPlan" | "SickLeave" | "AnnualLeave" | "Attendance" | "ALBalance" | "Training"
type ExportFormat = "xlsx" | "csv"

const ALL_TYPES: ExportType[] = ["ShiftPlan", "SickLeave", "AnnualLeave", "Attendance", "ALBalance", "Training"]

// ── Helpers ───────────────────────────────────────────────────────────────────

function getMonthStart(): string {
  const d = new Date()
  d.setDate(1)
  return d.toISOString().split("T")[0]
}

function getToday(): string {
  return new Date().toISOString().split("T")[0]
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function Export() {
  const { t } = useTranslation()

  // Filters
  const [from, setFrom] = useState(getMonthStart)
  const [to, setTo]     = useState(getToday)
  const [employeeSearch, setEmployeeSearch] = useState("")
  const [selectedEmployee, setSelectedEmployee] = useState<Employee | null>(null)
  const [dropdownOpen, setDropdownOpen] = useState(false)
  const [teamLead, setTeamLead] = useState("")

  // Types
  const [selectedTypes, setSelectedTypes] = useState<Set<ExportType>>(new Set(ALL_TYPES))

  // Format
  const [exportFormat, setExportFormat] = useState<ExportFormat>("xlsx")

  // Loading state
  const [exporting, setExporting] = useState(false)
  const exportTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  // ── Employees for type-ahead ──────────────────────────────────────────────

  const { data: employees = [] } = useQuery<Employee[]>({
    queryKey: ["employees-all"],
    queryFn:  () => apiFetch<Employee[]>("/api/employees"),
    staleTime: 5 * 60 * 1000,
  })

  const filtered = employeeSearch.length >= 2
    ? employees.filter(e =>
        e.isActive &&
        (e.fullName ?? "").toLowerCase().includes(employeeSearch.toLowerCase())
      ).slice(0, 10)
    : []

  // Close dropdown on outside click
  const dropdownRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node))
        setDropdownOpen(false)
    }
    document.addEventListener("mousedown", handler)
    return () => document.removeEventListener("mousedown", handler)
  }, [])

  // Cleanup timer on unmount
  useEffect(() => () => {
    if (exportTimerRef.current) clearTimeout(exportTimerRef.current)
  }, [])

  // ── Type toggle ───────────────────────────────────────────────────────────

  const toggleType = (type: ExportType) => {
    setSelectedTypes(prev => {
      const next = new Set(prev)
      next.has(type) ? next.delete(type) : next.add(type)
      return next
    })
  }

  const toggleAll = () => {
    setSelectedTypes(prev =>
      prev.size === ALL_TYPES.length ? new Set() : new Set(ALL_TYPES)
    )
  }

  // ── Export ────────────────────────────────────────────────────────────────

  const handleExport = () => {
    if (selectedTypes.size === 0 || exporting) return

    const params = new URLSearchParams({ from, to, types: [...selectedTypes].join(","), format: exportFormat })
    if (selectedEmployee) params.set("employeeId", selectedEmployee.employeeId)
    if (teamLead.trim())  params.set("teamLead", teamLead.trim())

    window.open(`/api/hr-export?${params}`, "_blank")

    setExporting(true)
    exportTimerRef.current = setTimeout(() => setExporting(false), 3000)
  }

  // ── Input / checkbox class helpers ────────────────────────────────────────

  const inputCls =
    "bg-sunken border border-line-subtle text-ink py-[7px] px-[10px] rounded-[6px] text-[13px] " +
    "outline-none focus:border-accent w-full transition-colors duration-fast"

  const typeKey: Record<ExportType, string> = {
    ShiftPlan:   "export.shiftPlan",
    SickLeave:   "export.sickLeave",
    AnnualLeave: "export.annualLeave",
    Attendance:  "export.attendance",
    ALBalance:   "export.alBalance",
    Training:    "export.training",
  }

  return (
    <div className="max-w-2xl mx-auto space-y-6 py-2">

      {/* Page title */}
      <div>
        <h1 className="text-xl font-semibold text-ink">{t("export.title")}</h1>
        <p className="text-sm text-ink-muted mt-0.5">
          {t("export.title")}
        </p>
      </div>

      {/* ── Date range ─────────────────────────────────────────────────── */}
      <section className="bg-raised border border-line-subtle rounded-lg p-4 space-y-3">
        <p className="text-xs font-semibold uppercase tracking-wide text-ink-soft">
          {t("export.from")} / {t("export.to")}
        </p>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="block text-xs text-ink-muted mb-1">{t("export.from")}</label>
            <input
              type="date"
              value={from}
              onChange={e => setFrom(e.target.value)}
              className={inputCls}
            />
          </div>
          <div>
            <label className="block text-xs text-ink-muted mb-1">{t("export.to")}</label>
            <input
              type="date"
              value={to}
              onChange={e => setTo(e.target.value)}
              className={inputCls}
            />
          </div>
        </div>
      </section>

      {/* ── Optional filters ───────────────────────────────────────────── */}
      <section className="bg-raised border border-line-subtle rounded-lg p-4 space-y-3">
        <p className="text-xs font-semibold uppercase tracking-wide text-ink-soft">Filters</p>

        {/* Employee type-ahead */}
        <div ref={dropdownRef} className="relative">
          <label className="block text-xs text-ink-muted mb-1">{t("export.employee")}</label>
          <input
            type="text"
            placeholder={t("export.employee")}
            value={selectedEmployee ? (selectedEmployee.fullName ?? selectedEmployee.employeeId) : employeeSearch}
            onChange={e => {
              setSelectedEmployee(null)
              setEmployeeSearch(e.target.value)
              setDropdownOpen(true)
            }}
            onFocus={() => { if (!selectedEmployee) setDropdownOpen(true) }}
            className={inputCls}
          />
          {selectedEmployee && (
            <button
              type="button"
              onClick={() => { setSelectedEmployee(null); setEmployeeSearch("") }}
              className="absolute right-2 top-[28px] text-ink-muted hover:text-ink transition-colors duration-fast text-xs px-1"
            >
              ✕
            </button>
          )}
          {dropdownOpen && filtered.length > 0 && !selectedEmployee && (
            <ul className="absolute z-50 mt-1 w-full bg-raised border border-line-subtle rounded-md shadow-md overflow-hidden">
              {filtered.map(emp => (
                <li key={emp.employeeId}>
                  <button
                    type="button"
                    className="w-full text-left px-3 py-2 text-[13px] text-ink hover:bg-nav-hover transition-colors duration-fast"
                    onMouseDown={e => e.preventDefault()}
                    onClick={() => {
                      setSelectedEmployee(emp)
                      setEmployeeSearch("")
                      setDropdownOpen(false)
                    }}
                  >
                    <span className="font-medium">{emp.fullName}</span>
                    <span className="ml-2 text-ink-muted text-xs font-mono">{emp.employeeId}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>

        {/* Team lead */}
        <div>
          <label className="block text-xs text-ink-muted mb-1">{t("export.teamLead")}</label>
          <input
            type="text"
            placeholder={t("export.teamLead")}
            value={teamLead}
            onChange={e => setTeamLead(e.target.value)}
            className={inputCls}
          />
        </div>
      </section>

      {/* ── Data types ─────────────────────────────────────────────────── */}
      <section className="bg-raised border border-line-subtle rounded-lg p-4 space-y-3">
        <div className="flex items-center justify-between">
          <p className="text-xs font-semibold uppercase tracking-wide text-ink-soft">
            {t("export.types")}
          </p>
          <button
            type="button"
            onClick={toggleAll}
            className="text-xs text-accent hover:underline transition-colors duration-fast"
          >
            {selectedTypes.size === ALL_TYPES.length ? "Deselect all" : "Select all"}
          </button>
        </div>
        <div className="grid grid-cols-2 gap-2">
          {ALL_TYPES.map(type => (
            <label
              key={type}
              className="flex items-center gap-2 cursor-pointer group"
            >
              <input
                type="checkbox"
                checked={selectedTypes.has(type)}
                onChange={() => toggleType(type)}
                className="accent-accent w-4 h-4 rounded shrink-0"
              />
              <span className="text-[13px] text-ink group-hover:text-ink-muted transition-colors duration-fast">
                {t(typeKey[type])}
              </span>
            </label>
          ))}
        </div>
        {selectedTypes.size === 0 && (
          <p className="text-xs text-crit-fg">Select at least one data type.</p>
        )}
      </section>

      {/* ── Format ─────────────────────────────────────────────────────── */}
      <section className="bg-raised border border-line-subtle rounded-lg p-4 space-y-3">
        <p className="text-xs font-semibold uppercase tracking-wide text-ink-soft">
          {t("export.format")}
        </p>
        <div className="flex gap-4">
          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="radio"
              name="exportFormat"
              value="xlsx"
              checked={exportFormat === "xlsx"}
              onChange={() => setExportFormat("xlsx")}
              className="accent-accent w-4 h-4"
            />
            <FileSpreadsheet size={15} className="text-info-fg" />
            <span className="text-[13px] text-ink">{t("export.excel")}</span>
          </label>
          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="radio"
              name="exportFormat"
              value="csv"
              checked={exportFormat === "csv"}
              onChange={() => setExportFormat("csv")}
              className="accent-accent w-4 h-4"
            />
            <FileArchive size={15} className="text-warn-fg" />
            <span className="text-[13px] text-ink">{t("export.csv")}</span>
          </label>
        </div>
      </section>

      {/* ── Export button ──────────────────────────────────────────────── */}
      <div className="flex justify-end">
        <button
          type="button"
          onClick={handleExport}
          disabled={selectedTypes.size === 0 || exporting}
          className={[
            "flex items-center gap-2 px-5 py-2.5 rounded-lg font-semibold text-sm transition-colors duration-fast",
            selectedTypes.size === 0 || exporting
              ? "bg-raised border border-line-subtle text-ink-muted cursor-not-allowed"
              : "bg-accent text-white hover:bg-accent/90 active:scale-[0.98]",
          ].join(" ")}
        >
          <Download size={16} />
          {exporting ? "Generating…" : t("export.exportBtn")}
        </button>
      </div>

    </div>
  )
}
