# GSD Dashboard

Internal workforce management dashboard for the **GSD DE (Global Service Desk Germany)** and **WIC (Workforce In Contact)** teams. Provides real-time visibility into shift schedules, WIC location coverage, attendance, sick leave, vacations, and annual leave balances — with Excel export for every view.

**Stack:** ASP.NET Core 8 · React 19 · TypeScript · SQL Server 2022 · TanStack Query · Tailwind CSS

---

## Prerequisites

| Requirement | Version | Notes |
|-------------|---------|-------|
| .NET SDK | 8.0+ | [Download](https://dotnet.microsoft.com/download) |
| Node.js | 20+ | [Download](https://nodejs.org) |
| SQL Server Express | 2022 | Must run at `localhost\SQLEXPRESS` |
| Windows Authentication | — | Required for SQL Server connection |

---

## Installation

### 1. Database

Open **SQL Server Management Studio** (or Azure Data Studio), connect to `localhost\SQLEXPRESS`, and run:

```sql
-- execute the full contents of:
Backend/schema.sql
```

This creates the `GSDDashboard` database, all 13 tables, indexes, and seeds the 40 WIC locations. Run it only once on a clean instance.

Verify:
```sql
USE GSDDashboard;
SELECT COUNT(*) FROM WicLocations;  -- expect 40
```

### 2. Backend

```bash
cd Backend
dotnet restore
dotnet run
```

The API starts at **http://localhost:5000**.

- Swagger UI: http://localhost:5000/swagger
- Health check: http://localhost:5000/health

### 3. Frontend

```bash
cd Frontend
npm install
npm run dev
```

The dashboard opens at **http://localhost:5173**.

---

## Configuration

### Backend (`Backend/appsettings.json`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173", "http://localhost:3000"]
  }
}
```

Adjust the connection string if your SQL Server instance name or database name differs.

### Frontend (`Frontend/.env`)

```env
VITE_API_BASE_URL=http://localhost:5000
```

This file is optional for local development — the client defaults to `http://localhost:5000` if not set. Set it when deploying to a remote server.

---

## Available Scripts

### Frontend

```bash
npm run dev       # Start Vite dev server with HMR
npm run build     # TypeScript check + production build
npm run preview   # Preview the production build locally
npm run lint      # ESLint check
```

---

## Production Build

Build the frontend and copy the output into the backend's static files folder:

```bash
cd Frontend
npm run build
# Then copy dist/ contents to Backend/wwwroot/
```

Run the backend — it serves both the API and the SPA from a single port:

```bash
cd Backend
dotnet run --environment Production
```

---

## Project Structure

```
GSDDashboard/
├── Backend/
│   ├── Program.cs              # Entry point + all API route registrations
│   ├── GSDContext.cs           # EF Core DbContext
│   ├── appsettings.json        # Connection string and CORS config
│   ├── schema.sql              # Database initialisation script
│   ├── *Service.cs             # 17 domain service files
│   └── Models/                 # EF entities and DTOs
│
└── Frontend/
    └── src/
        ├── main.tsx            # React entry, providers, i18n
        ├── App.tsx             # Router and shell layout
        ├── api/client.ts       # All API calls in one place
        ├── pages/              # 18 page components (one per route)
        ├── components/         # Shared UI components
        └── i18n/               # EN/DE translation files
```

---

## Features at a Glance

- **Shift overview** — Full shift plan with colour-coded shift types, filterable by team lead, role, and date range
- **WIC coverage** — Live status (COVERED / PARTIAL / UNCOVERED / CLOSED) for all 40 WIC locations
- **WIC attendance** — Daily on-site attendance per location
- **Sick leave & vacations** — Records, stats, and calendar views
- **Annual leave balance** — Eligible / taken / remaining per employee
- **Training schedule** — Sessions, topics, agent assignments, coverage ranking
- **WIC pipeline** — Planned projects and workload at each location
- **Excel exports** — Every view has Today / 7-day / 30-day `.xlsx` download buttons
- **Bilingual** — Full English and German UI, switchable at runtime

---

## Troubleshooting

| Symptom | Check |
|---------|-------|
| API returns 500 | Verify SQL Server is running and Windows Authentication is enabled |
| Frontend shows no data | Confirm backend is running on port 5000; check browser console for CORS errors |
| Excel download fails | Backend must be reachable; check network tab for the download endpoint response |
| Wrong language showing | Clear `localStorage` (key: `i18nextLng`) and reload |
