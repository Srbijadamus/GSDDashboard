import { BrowserRouter, Routes, Route, NavLink } from "react-router-dom"
import { useTranslation } from "react-i18next"
// import { useTheme } from "next-themes"
import {
  LayoutDashboard, Users, Calendar, Heart, MapPin,
  ClipboardList, Building2, Scale, Globe
} from "lucide-react"
import Overview from "./pages/Overview"
import Employees from "./pages/Employees"
import SickLeave from "./pages/SickLeave"
import Vacations from "./pages/Vacations"
import WicLocations from "./pages/WicLocations"
// @ts-ignore
import WicShifts from "./pages/WICShifts"
// @ts-ignore
import Pipeline from "./pages/Pipeline"
import Training from "./pages/Training"
// @ts-ignore
import WicAttendance from "./pages/WicAttendance"
// @ts-ignore
import WicSchedule from "./pages/WicSchedule"
import Shifts from "./pages/Shifts"
import ALBalance from "./pages/ALBalance"
import Attendance from "./pages/Attendance"
import ALCalendar from "./pages/ALCalendar"

function LangToggle() {
  const { i18n } = useTranslation()
  return (
    <button
      onClick={() => i18n.changeLanguage(i18n.language === "en" ? "de" : "en")}
      style={{
        background: "var(--card2)", border: "1px solid var(--border)",
        color: "var(--text2)", padding: "4px 10px", borderRadius: 6,
        fontSize: 11, cursor: "pointer", fontFamily: "IBM Plex Mono",
        display: "flex", alignItems: "center", gap: 4
      }}
    >
      <Globe size={12} />
      {i18n.language === "en" ? "DE" : "EN"}
    </button>
  )
}

export default function App() {
  const { t } = useTranslation()
  const today = new Date().toLocaleDateString("de-DE")

  const navItems = [
    { to: "/",           icon: LayoutDashboard, label: t("nav.overview") },
    { to: "/shifts",     icon: ClipboardList,   label: t("nav.shifts") },
    { to: "/wic-shifts",     icon: Building2,   label: t("nav.wicShifts") },
    { to: "/wic-attendance", icon: Calendar,  label: "WIC Attendance" },
    { to: "/wic-schedule",    icon: MapPin,    label: "WIC Schedule" },
    { to: "/pipeline",   icon: ClipboardList,   label: "Pipeline" },
    { to: "/training",   icon: Users,           label: "Training" },
    { to: "/wic",        icon: MapPin,          label: t("nav.wicLocations") },
    { to: "/attendance", icon: Calendar,        label: t("nav.attendance") },
    { to: "/sickleave",  icon: Heart,           label: t("nav.sickLeave") },
    { to: "/vacations",  icon: Calendar,        label: t("nav.vacations") },
    { to: "/albalance",  icon: Scale,           label: t("nav.alBalance") },
    { to: "/alcalendar", icon: Calendar,        label: "AL Calendar" },
    { to: "/employees",  icon: Users,           label: t("nav.employees") },
  ]

  return (
    <BrowserRouter>
      <div style={{ display: "flex", height: "100vh", overflow: "hidden", background: "var(--bg)" }}>

        {/* SIDEBAR */}
        <aside style={{
          width: 200, background: "var(--sidebar)",
          borderRight: "1px solid var(--border)",
          display: "flex", flexDirection: "column", flexShrink: 0
        }}>
          <div style={{
            padding: "14px 16px", borderBottom: "1px solid var(--border)",
            display: "flex", alignItems: "center", gap: 8
          }}>
            <span style={{
              background: "var(--accent)", color: "#fff",
              fontSize: 10, fontWeight: 600, padding: "3px 7px",
              borderRadius: 4, fontFamily: "IBM Plex Mono"
            }}>GSD</span>
            <span style={{ fontSize: 12, fontWeight: 600, color: "var(--text)" }}>
              EON GSD Dashboard
            </span>
          </div>

          <nav style={{ flex: 1, padding: "8px 0", overflowY: "auto" }}>
            {navItems.map(({ to, icon: Icon, label }) => (
              <NavLink key={to} to={to} end={to === "/"}
                style={({ isActive }) => ({
                  display: "flex", alignItems: "center", gap: 8,
                  padding: "9px 16px", fontSize: 12, textDecoration: "none",
                  color: isActive ? "var(--accent)" : "var(--text2)",
                  background: isActive ? "rgba(59,126,255,.08)" : "transparent",
                  borderLeft: isActive ? "2px solid var(--accent)" : "2px solid transparent",
                  transition: "all .15s"
                })}>
                <Icon size={14} />
                <span>{label}</span>
              </NavLink>
            ))}
          </nav>
        </aside>

        {/* MAIN */}
        <div style={{ flex: 1, display: "flex", flexDirection: "column", overflow: "hidden" }}>

          {/* TOPBAR */}
          <div style={{
            padding: "10px 20px", borderBottom: "1px solid var(--border)",
            background: "var(--sidebar)", display: "flex",
            alignItems: "center", justifyContent: "space-between"
          }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: "var(--text)" }}>
              EON GSD Dashboard
            </span>
            <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <span style={{ fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
                {today}
              </span>
              <LangToggle />
            </div>
          </div>

          {/* PAGE */}
          <main style={{ flex: 1, overflowY: "auto", padding: 20 }}>
            <Routes>
              <Route path="/"           element={<Overview />} />
              <Route path="/shifts"     element={<Shifts />} />
              <Route path="/wic-shifts"     element={<WicShifts />} />
              <Route path="/wic-attendance" element={<WicAttendance />} />
              <Route path="/wic-schedule"    element={<WicSchedule />} />
              <Route path="/pipeline"   element={<Pipeline />} />
              <Route path="/training"   element={<Training />} />
              <Route path="/wic"        element={<WicLocations />} />
              <Route path="/attendance" element={<Attendance />} />
              <Route path="/sickleave"  element={<SickLeave />} />
              <Route path="/vacations"  element={<Vacations />} />
              <Route path="/albalance"  element={<ALBalance />} />
              <Route path="/alcalendar" element={<ALCalendar />} />
              <Route path="/employees"  element={<Employees />} />
            </Routes>
          </main>
        </div>
      </div>
    </BrowserRouter>
  )
}








