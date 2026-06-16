# GSD Dashboard — General Overview

## What Is It?

The **GSD Dashboard** is an internal workforce management and visibility platform built for the **GSD DE (Global Service Desk Germany)** and **WIC (Workforce In Contact)** teams at EON. It gives team leads and management a real-time window into scheduling, shift coverage, attendance, sick leave, vacations, and annual leave balances — all in one place.

The application is **read-only by design**: it surfaces data from an existing SQL Server database and lets users export reports to Excel. There is no data entry through the app.

---

## Who Is It For?

| User | What They Use It For |
|------|---------------------|
| **Team Leads** | Monitor their team's daily shifts, leave, and WIC assignments |
| **Management** | High-level coverage overview across all teams and locations |
| **HR / Scheduling** | Track sick leave, vacations, annual leave balances, compliance |
| **WIC Coordinators** | See daily attendance and coverage at the 40 WIC locations |

The workforce covered spans approximately **122 employees** and **40 WIC on-site support locations** — 38 in Germany and 2 in the Netherlands.

---

## Core Features

### Shift Management
View the full shift plan for any date range, filtered by team lead, role, or employee type. Each shift is color-coded by type (working, WIC duty, annual leave, sick leave, public holiday, training, etc.).

### WIC Coverage Monitoring
Track which of the 40 WIC locations are covered each day. Each location is shown as COVERED, PARTIAL, UNCOVERED, or CLOSED based on the number of agents on-site versus the minimum required.

### Daily Attendance
See actual presence at WIC locations: which agents showed up, which were absent, and which locations were closed or on a public holiday.

### Leave Management
- **Sick Leave** — Active and historical sick leave records with date ranges and leave types.
- **Vacations** — Approved, pending, and upcoming vacation requests per employee.
- **Annual Leave (AL) Balance** — Eligible vs. taken vs. remaining days per employee, including half-day and unpaid leave counts.
- **AL Calendar** — Visual calendar view of annual leave across the team.

### Training Scheduling
View training topics and scheduled sessions, including which employees are assigned to each session and coverage/impact rankings.

### WIC Pipeline
Track projects and workload planned at WIC locations — titles, dates, number of agents needed, and status.

### Excel Exports
Every major data view has a download button that exports the data to a formatted `.xlsx` file. Downloads are available for today, the last 7 days, or the last 30 days.

### Bilingual UI
The entire interface supports **English and German**, switchable at any time via the language toggle.

---

## What It Is Not

- Not a scheduling or data-entry tool — data comes from an upstream system
- Not a time-tracking or payroll system
- Not public-facing — internal use only, running on a corporate network

---

## Data Flow at a Glance

```
SQL Server 2022  →  ASP.NET Core 8 API  →  React Dashboard  →  Excel Reports
   (source)           (read-only)          (visualization)       (ClosedXML)
```
