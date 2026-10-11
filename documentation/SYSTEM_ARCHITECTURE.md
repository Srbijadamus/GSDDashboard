# SYSTEM_ARCHITECTURE — how the whole thing fits together

**Verification basis:** both repositories and both live databases, read **read-only** on **2026-10-11**.
Every structural claim cites a file, a route count, or a live-database fact.

**Related documents:** [PULSE_FEATURES.md](./PULSE_FEATURES.md) · [SHIFTKIOSK.md](./SHIFTKIOSK.md) ·
[INTEGRATION_AND_DATA_FLOWS.md](./INTEGRATION_AND_DATA_FLOWS.md) ·
[DATA_MODEL.md](./DATA_MODEL.md) · [SECURITY_MODEL.md](./SECURITY_MODEL.md) ·
[OPERATIONS.md](./OPERATIONS.md)

---

## 1. The system in one view

There are **two products and two repositories**, both running on the same Windows machine, side by
side, sharing one SQL Server Express instance but **not** sharing a database.

| | **WorkForce Pulse** | **ShiftKiosk** |
|---|---|---|
| What it is | The workforce-management and visibility application | The attendance-capture system |
| Repository | `C:\GSDDashboard_secure` | `C:\ShiftKiosk_secure` |
| Live deployment folder | `C:\GSDDashboard` (a separate, older checkout) | `C:\ShiftKiosk` |
| Backend | ASP.NET Core Minimal API, port **5000** | Python FastAPI + uvicorn, port **8000** |
| Frontend | React 19 + TypeScript, served from the backend at `/` | Server-rendered HTML embedded in the server file, at `/dashboard` |
| Database | `GSDDashboard` | `ShiftKioskDB` |
| Users | Team leads, RTM, planning, administrators | Agents (on their own laptops), supervisors |
| Namespace retained | `GSDDashboard.API` — the mismatch with "WorkForce Pulse" is deliberate | — |

Both ports are published externally through Microsoft Dev Tunnels, which expire and are restarted by
scheduled tasks.

### Diagram 1 — the whole architecture

```mermaid
flowchart TB
    subgraph AgentPC["Agent Windows laptop"]
        KioskUI["Kiosk client UI<br/>(tkinter, 3.0.0-ng)<br/>Phase 1 fullscreen / Phase 2 widget"]
        Broker["Broker process<br/>runs as SYSTEM<br/>holds device token"]
        UIQueue["UI queue<br/>%LOCALAPPDATA% pending_queue.json"]
        BrokerQueue["Broker queue<br/>broker_queue.json · max 5000"]
        KioskUI -- "named pipe<br/>\\\\.\\pipe\\ShiftKioskBroker<br/>JSON per line" --> Broker
        KioskUI -. "only if broker did not take ownership" .-> UIQueue
        Broker --> BrokerQueue
    end

    subgraph Host["Host machine (Windows 11, one SQL Express instance)"]
        subgraph Pulse["WorkForce Pulse"]
            SPA["React SPA<br/>27 routes, EN/DE<br/>served from Backend/wwwroot"]
            API["ASP.NET Core Minimal API<br/>port 5000 · 161 routes<br/>40 endpoint mappers"]
            Proxy["KioskProxyService<br/>GET /api/kiosk/attendance"]
            SPA -- "same-origin REST + gsd_auth cookie" --> API
            API --- Proxy
        end
        subgraph KioskSrv["ShiftKiosk server"]
            KAPI["FastAPI, port 8000<br/>49 routes · security middleware"]
            DASH["Embedded supervisor dashboard<br/>8 views"]
            KAPI --- DASH
        end
        DB1[("GSDDashboard<br/>35 application tables")]
        DB2[("ShiftKioskDB<br/>14 tables")]
        API --> DB1
        KAPI --> DB2
    end

    subgraph Users["People"]
        Agent["Agent"]
        Sup["Supervisor / RTM / Team lead"]
        Admin["Administrator"]
    end

    Agent --> KioskUI
    Sup --> SPA
    Sup --> DASH
    Admin --> SPA
    Broker -- "HTTPS X-Device-Token (or legacy key)" --> KAPI
    Proxy -- "HTTP loopback + service device token" --> KAPI
    KAPI -- "server-side GET /api/publicholidays (3s timeout)" --> API
    KAPI -- "WebSocket /ws + REST" --> DASH
```

**Reading the diagram.** Only three arrows cross between the two products, and they matter:
the broker calling the kiosk server (attendance writes), Pulse's proxy calling the kiosk server
(attendance reads), and the kiosk server making one outbound call to Pulse for the public-holiday
calendar. Nothing else is shared — not the database, not the credentials, not the code.

---

## 2. WorkForce Pulse — structure

### 2.1 Layers

| Layer | Technology | Where | Notes |
|---|---|---|---|
| Presentation | React 19, TypeScript, Vite, TanStack Query, Tailwind | `Frontend/src` | One page file per route; 27 routes registered |
| API | ASP.NET Core 8 Minimal API | `Backend` | **161 routes**, all registered by 40 mapper extension methods called in sequence from `Program.cs:683-722` |
| Domain services | C# service classes | `Backend/Services`, `Backend/Modules`, `Backend/Assistant`, top-level `*Service.cs` | 60+ classes; the only provably dead class is `CoverageCalculatorTests` |
| Data access | EF Core 8 against SQL Server Express | `Backend/GSDContext.cs` | No EF migrations — idempotent DDL blocks in `Program.cs` are the migration strategy |
| Database | SQL Server Express 2022 | `GSDDashboard` | 52 physical tables: 35 functional + 17 archive copies |
| Static hosting | The API serves the built SPA from `Backend/wwwroot` | — | Content-hashed assets cached forever; `index.html` never cached |

### 2.2 Request path

`Program.cs` composes the pipeline in this order, and the order is load-bearing:

1. Exception handler → a generic JSON 500.
2. **Swagger gate** — any path starting `/swagger` requires an authenticated `DEV` session,
   otherwise **404** (not 401), so the endpoint map is not disclosed. This gate is always on.
3. CORS → rate limiter → authentication → authorization → **RBAC middleware** → default files →
   static files → endpoint mappings → `/health` → single-page-app fallback.
4. After startup: a vacation reconciliation pass runs once before the server begins serving.

Two gates behave differently from the rest and must not be confused with the global switch:

- **`Auth:EnforceAuthorization`** controls only the general RBAC middleware. It is `false` today,
  which makes that middleware a no-op.
- **The always-on gates** are independent of it: `/api/roster/*` (RTM/TEAM_LEAD/DEV),
  `/api/kiosk/*` (authenticated DEV/RTM/TEAM_LEAD), `/swagger` (DEV), and the in-handler
  manual-edit gate on the shift-write routes.

### 2.3 Endpoint map by area

| Area | Prefix | Example capability |
|---|---|---|
| Auth | `/api/auth` | KID login, logout, who-am-I |
| Kiosk proxy | `/api/kiosk` | Attendance feed, agent list, supervisor manual check-in/log-out |
| Shifts | `/api/shifts` | Grid read, coverage, validation, assign (bulk / manual), swap, export |
| WIC | `/api/wic`, `/api/wic-coverage`, `/api/wicschedule` | Shift board, cards, forecast, substitutes, briefing, conflicts, assignment directory, opening-hours versions |
| VWIC | `/api/vwic` | Daily virtual coverage, rotation plans |
| Roster | `/api/roster` | Preview, generate, batches, delete-with-restore, missing-roster check, coverage walk |
| Absence | `/api/sickleave`, `/api/vacations`, `/api/albalance`, `/api/alcalendar` | CRUD plus shift synchronisation |
| People | `/api/employees` | CRUD, future shifts, timeline, AL balance edits |
| Lists and tools | `/api/bo-list`, `/api/rtm`, `/api/rollout`, `/api/pipeline`, `/api/training`, `/api/breaks` | Back-office list, bulk RTM, rollout days, pipeline events, training, break planning |
| Reporting | `/api/hr-export`, `/api/*/download` | The central HR export plus per-page exports |
| Assistant | `/api/assistant`, `/api/wic-assistant` | Internal question answering — no external AI service |
| Admin | `/api/admin` | DEV-only maintenance: demo-data delete, WIC orphan cleanup, WIC migration dry-run |
| Health | `/health` | Anonymous, for the watchdog |

### 2.4 Deployment reality (stated, not idealised)

| Fact | Detail |
|---|---|
| The running binary was built with .NET SDK **10.0.204** | The repository targets .NET 8 and cannot reproduce the live binary 1:1 with SDK 8 |
| The build source is the **working tree**, not `git HEAD` | `HEAD` can lag what is deployed; `C:\GSDDashboard` is a *separate* repo with older sources |
| The watchdog is the only thing allowed to start the executable | A deploy pauses it with a sentinel file, copies, verifies, then removes the sentinel |
| `/health` returning 200 is not evidence | It answers before the database, the proxy and the authenticated routes are proven |
| Frontend changes only count when served from port 5000 | Verifying on the Vite dev server proves nothing about what the user sees |

---

## 3. ShiftKiosk — structure

| Component | Detail |
|---|---|
| Server | One large FastAPI module plus a `server/security` package (`core`, `store`, `accounts`, `crypto`, `flags`) |
| Routes | **49** registered: 41 on the server object, 8 mounted by the security layer (sign-in, flags, login page) |
| Runtime | 32-bit Python, because the ODBC driver is 32-bit |
| Database access | `pyodbc` with Windows authentication; each helper opens and closes its own connection |
| Supervisor UI | HTML/CSS/JavaScript embedded as a string in the server file — no build step, no external assets |
| Security model | A middleware that classifies the caller's credential, evaluates nine named flags, and writes an observation row for every request |
| Clients | Legacy `2.1.1` (shared key) and NG `3.0.0-ng` (per-computer device token via a SYSTEM broker) |
| Auto-logout | 11 hours from shift start, enforced by the client, not the server |

### Diagram 2 — the identity and device model

```mermaid
flowchart LR
    subgraph Creds["Credential types the kiosk server recognises"]
        LK["legacy_key<br/>shared master API key"]
        ST["session_token<br/>ts + HMAC-SHA256(ts, master key)<br/>TTL 8 h · stateless"]
        DT["device_token<br/>random 32 bytes<br/>only SHA-256 stored"]
        SUP["supervisor cookie<br/>password + TOTP account"]
        NONE["none / unknown"]
    end

    subgraph Rules["Nine flags, each off / shadow / enforce"]
        R1["docs_disabled"]
        R2["legacy_scope"]
        R3["key_in_query"]
        R4["anon_identifiers"]
        R5["session_auth"]
        R6["supervisor_auth"]
        R7["device_binding"]
        R8["rate_limit"]
        R9["anomaly_detection"]
    end

    LK --> R2
    ST --> R5
    DT --> R7
    SUP --> R6
    NONE --> R4
    Rules --> DEC{"decision"}
    DEC -->|allow| OK["request proceeds"]
    DEC -->|would_block| OBS["proceeds + observation row<br/>(shadow mode)"]
    DEC -->|block| DENY["JSON refusal"]
    DEC --> LOG[("sec_observation_log<br/>credential · host · path · decision · rule")]
```

**What the diagram is saying.** Every request is labelled with one of five credential kinds, checked
against nine rules, and given one of three verdicts. In shadow mode a rule records "would have
blocked" and lets the request through — that is how the operator decides when a rule is safe to
enforce. Four rules block by default; five observe only.

---

## 4. Diagram 3 — the roster → Pulse → kiosk data flow

```mermaid
flowchart TB
    subgraph Sources["Where roster data comes from today"]
        RTM["RTM / Team lead<br/>Roster Generator, Shift Plan, Bulk RTM, Rollout"]
        IMP["External imports<br/>(not in this repository)"]
        SYNC["Automatic sync<br/>sickness, vacation, startup reconcile"]
    end

    subgraph PulseWrite["Pulse writes"]
        SE[("ShiftEntries<br/>the shift plan")]
        WSE[("WicShiftEntries<br/>per-agent per-centre day")]
        RB[("RosterBatches<br/>run history + overwrite snapshot")]
        GUARD{{"GuardManualShifts<br/>+ manual marker<br/>+ concurrency token"}}
    end

    subgraph Derived["Derived views"]
        CARDS["Coverage cards"]
        FORE["Forecast / briefing"]
        CONF["Conflict detector"]
        MISS["Missing-roster check"]
    end

    subgraph KioskSide["ShiftKiosk"]
        EVENTS[("status_log<br/>attendance events")]
        SEC[("sec_* observation<br/>+ devices + registry")]
        DASH2["Supervisor dashboard<br/>8 views"]
    end

    subgraph PulseRead["Pulse reads the kiosk"]
        PROXY2["GET /api/kiosk/attendance<br/>always-on role gate"]
        WICATT["WIC Attendance<br/>Expected = CheckedIn + NotYetIn"]
    end

    RTM --> SE
    RTM --> WSE
    RTM --> RB
    IMP --> SE
    SYNC --> SE
    SYNC --> WSE
    GUARD -. protects .-> SE
    GUARD -. protects .-> WSE
    SE --> CARDS
    WSE --> CARDS
    SE --> FORE
    WSE --> CONF
    SE --> MISS
    CARDS --> RTM
    FORE --> RTM
    CONF --> RTM
    MISS --> RTM

    BrokerIn["Agent kiosk client"] --> EVENTS
    EVENTS --> SEC
    EVENTS --> DASH2
    EVENTS --> PROXY2
    PROXY2 --> WICATT
    WICATT -. "compared on employee id,<br/>never on location name" .-> WSE
```

**Key point for the board.** The two halves meet at exactly one place: the employee identifier.
Pulse decides who *should* be somewhere from its own roster tables; the kiosk records who actually
checked in; Pulse's WIC Attendance page joins the two on the employee id and shows the difference in
both directions. The kiosk's own free-text location field is displayed but deliberately never used in
that comparison.

---

## 5. Diagram 4 — one shift, end to end

```mermaid
sequenceDiagram
    autonumber
    participant A as Agent
    participant K as Kiosk client
    participant B as Broker (SYSTEM)
    participant S as ShiftKiosk server
    participant D as ShiftKioskDB
    participant P as Pulse proxy

    A->>K: log on · Phase 1 fullscreen
    K->>S: GET /api/shift-status (resume if a shift is open)
    S->>D: read today's latest event + first start
    D-->>S: open / closed
    alt shift already open
        K->>K: skip Phase 1, resume Phase 2
    else no shift
        A->>K: Start Shift + choose work location
        K->>B: pipe: status ON_SHIFT + payload
        B->>S: POST /api/status (X-Device-Token)
        S->>D: INSERT status_log + observation row
        S-->>B: 200 {success, id}
        B-->>K: ok
        K->>K: Phase 2 widget with elapsed time
    end
    A->>K: Pause / resume
    K->>B: pipe: status PAUSE / ON_SHIFT
    B->>S: POST /api/status
    S->>D: INSERT status_log
    Note over K,S: server never invents a status and never auto-logs out
    A->>K: Emergency (with note)
    K->>B: pipe: status EMERGENCY
    B->>S: POST /api/status
    S->>D: INSERT + optional Teams notification
    K->>K: lock workstation
    A->>K: Off
    K->>B: pipe: status OFF
    B->>S: POST /api/status
    S->>D: INSERT status_log
    K->>K: log off Windows
    Note over K: 11 h after the recorded shift start,<br/>the client sends AUTO_LOGOUT and locks
    P->>S: GET /api/attendance (service device token)
    S->>D: read attendance join
    S-->>P: rows with employee id, status, work mode
    P->>P: Expected = CheckedIn + NotYetIn
```

**Where the queue fits.** Every arrow from the client to the server can fail. The legacy client writes
the event to a JSON queue and retries every 30 seconds; the NG client hands the event to the SYSTEM
broker, which owns a durable ordered queue with exponential back-off and a 5,000-event cap. The
server stores an event without an idempotency key, so an ambiguous failure can duplicate a row — the
Newest-row-wins rule means this affects history, not the current status.

---

## 6. Cross-cutting concerns

| Concern | Position |
|---|---|
| **Language** | EN/DE only, in both products. The permanent rule lives in `CLAUDE.md` (Pulse) and in the ShiftKiosk documentation; internal status codes and product names (WIC, VWIC, BO, Voice, GSD, RTM, KID) are never translated. Compliance is **incomplete** in Pulse — see [PULSE_FEATURES.md](./PULSE_FEATURES.md) §5. |
| **Real-time** | Pulse has **no WebSocket at all** and polls (60 s for kiosk data, 30 s for WIC Shifts and breaks, 10 min for the forecast). The kiosk dashboard does use a WebSocket with a 60-second polling fallback. |
| **Manual edits** | A manual marker plus a save-time guard plus concurrency tokens. If any automatic writer misses its check, the guard throws instead of silently overwriting. Full rules in [ROSTER_AND_SCHEDULING.md](./ROSTER_AND_SCHEDULING.md). |
| **Reversibility** | Roster batches snapshot what they overwrite; the deploy keeps code/config/import rollback snapshots; flag, code, schema and application rollbacks are four separate operations. |
| **Observability** | A watchdog per service, an independent 5-minute health check per port, an append-only deploy sentinel, and — on the kiosk side — a per-request observation log of 282,745 rows. |
| **Backup** | SQL backups to the instance's default folder, verified; a database backup is not the same as a code snapshot and neither is a rollback. |

---

## 7. Known architectural risks

1. **Version drift between repository and deployment.** The live binary comes from a different SDK
   and from the working tree; `HEAD` does not prove what is running. Any parity claim must be verified
   against the running artefact, not the source.
2. **Two repositories, one documentation set with holes.** Several kiosk-source comments point at
   `documentation/` files that exist only in the Pulse repository. Until that is fixed, a reader
   inside the kiosk repository cannot follow the references.
3. **Anonymous read path on the kiosk server.** The session token that the kiosk dashboard needs is
   issued without authentication, and it can still read history, audit and exports. Closing it is the
   remaining `dan3` step, not a completed control.
4. **A shared legacy key with roughly sixty dependants.** The retirement criterion is "no legacy
   inventory for 30 days", and the legacy clients are still sending events.
5. **Archive tables inside the live database.** 17 of the 52 tables are point-in-time copies with no
   documented retention. Any statement of "how many tables" must say which count it means.
6. **Session-optional manual stamping.** Three WIC write routes and the rollout routes fall back to a
   fixed actor tag when the session carries no KID, so while the global RBAC flag is off they have no
   401/403 and every caller is stamped as a human.
7. **The `PostalCode`/`City`/`Bundesland` chain and the location-name joins are string-based.** There
   is no foreign key between the WIC assignment table and the employee table; a rename silently
   orphans an assignment.
