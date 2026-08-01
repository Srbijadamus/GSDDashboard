import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom"
import { AppShell } from "./layout/AppShell"
import Overview from "./pages/Overview"
import Employees from "./pages/Employees"
import SickLeave from "./pages/SickLeave"
import Vacations from "./pages/Vacations"
import WicLocations from "./pages/WicLocations"
// @ts-ignore
import WicShifts from "./pages/WICShifts/index"
// @ts-ignore
import Pipeline from "./pages/Pipeline"
import Training from "./pages/Training"
import WicAttendance from "./pages/WicAttendance"
// @ts-ignore
import WicSchedule from "./pages/WicSchedule"
import Shifts from "./pages/Shifts"
import ALBalance from "./pages/ALBalance"
import Attendance from "./pages/Attendance"
import ALCalendar from "./pages/ALCalendar"
import VWICPage from "./pages/VWICPage"
import BreakPlanner from "./pages/BreakPlanner"
import WicCoverage from "./pages/WicCoverage"
import BoList from "./pages/BoList"
import WicAnnualLeave from "./pages/WicAnnualLeave"
import BulkRtm from "./pages/BulkRtm"
import WicAssistant from "./pages/WicAssistant"

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<AppShell />}>
          <Route index element={<Overview />} />
          <Route path="/shifts"          element={<Shifts />} />
          <Route path="/wic-shifts"      element={<WicShifts />} />
          <Route path="/vwic"            element={<VWICPage />} />
          <Route path="/breaks"          element={<BreakPlanner />} />
          <Route path="/wic-attendance"  element={<WicAttendance />} />
          <Route path="/wic-schedule"    element={<WicSchedule />} />
          <Route path="/pipeline"        element={<Pipeline />} />
          <Route path="/training"        element={<Training />} />
          <Route path="/wic"             element={<WicLocations />} />
          <Route path="/attendance"      element={<Attendance />} />
          <Route path="/sickleave"       element={<SickLeave />} />
          <Route path="/vacations"       element={<Vacations />} />
          <Route path="/albalance"       element={<ALBalance />} />
          <Route path="/alcalendar"      element={<ALCalendar />} />
          <Route path="/employees"       element={<Employees />} />
          <Route path="/wic-coverage"    element={<WicCoverage />} />
          <Route path="/wic-al"          element={<WicAnnualLeave />} />
          <Route path="/assistant"       element={<WicAssistant />} />
          <Route path="/wic-assistant"   element={<Navigate to="/assistant" replace />} />
          <Route path="/bo-list"         element={<BoList />} />
          <Route path="/bulk-rtm"        element={<BulkRtm />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
