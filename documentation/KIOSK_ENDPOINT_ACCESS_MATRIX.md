# ShiftKiosk — Endpoint Access Matrix (AS-IS)

> **HISTORICAL SNAPSHOT (before dan1), not the current access matrix.**
> Original findings below retain their original evidence/line numbers.
> Canonical current state: [KIOSK_HARDENING_DEPLOY_NOTE.md](KIOSK_HARDENING_DEPLOY_NOTE.md).
> Client evidence/install: `C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md`.

Every route registered in `C:\ShiftKiosk\server\server.py`, verified by reading the
decorator and the handler body — **not** read from the OpenAPI schema. The route line
number is given for each; "Auth" is determined solely by whether the decorator carries
`dependencies=[Depends(verify_api_key)]`.

Authentication mechanics, and why "API key" is weaker than it looks, are in
[KIOSK_AUTH_IDENTITY_ASIS.md](KIOSK_AUTH_IDENTITY_ASIS.md). Personal-data detail per
endpoint is in [KIOSK_DATA_EXPOSURE_INVENTORY.md](KIOSK_DATA_EXPOSURE_INVENTORY.md).

**Read this first:** every cell that says `KEY` is satisfiable by an **anonymous** caller,
because `GET /api/session` issues an 8-hour token to anyone and `verify_api_key` accepts it
(`server.py:1011-1013` + `server.py:960`). `KEY` therefore means "requires one anonymous
round-trip", not "requires the shared secret".

Legend — **NONE** = no auth of any kind · **KEY** = `X-API-Key` header or `?token=`
(master key *or* session token) · *Caller* = who legitimately calls it, from the client
source where that is established.

---

## 1. Unauthenticated routes

CONFIRMED — these decorators carry no `dependencies` argument.

| # | Method | Path | Auth | Identity trusted | Returns / changes | Caller | Route |
|---|---|---|---|---|---|---|---|
| 1 | GET | `/health` | **NONE** | — | `{status, server_time}` | kiosk probe (`main_kiosk.py:398-400`), watchdog (`PROJECT_BLUEPRINT.md:173`) | `server.py:1007` |
| 2 | GET | `/api/session` | **NONE** | — | **mints an 8 h bearer token** accepted by every `KEY` route | `/dashboard` JS (`README.md:229`) | `server.py:1011` |
| 3 | GET | `/api/agents` | **NONE** | — | `SELECT * FROM agents` — full agent master data, all rows (`server.py:481-488`) | dashboard | `server.py:1052` |
| 4 | GET | `/api/teams` | **NONE** | — | distinct `agents.team_leader` (`server.py:476-479`) | dashboard | `server.py:1056` |
| 5 | GET | `/api/attendance` | **NONE** | — | live attendance for **all active agents**, incl. status and notes (`server.py:384-451`) | dashboard; **and the WorkForce Pulse browser frontend** (`BLUEPRINT_SCREENS.md:163`) | `server.py:1060` |
| 6 | GET | `/api/today-holiday` | **NONE** | — | `{holiday, name, date}`; server-side fetches `localhost:5000/api/publicholidays` | kiosk (`main_kiosk.py:450-451`) | `server.py:1320` |
| 7 | GET | `/api/agents-template` | **NONE** | — | blank `.xlsx` import template (column names only, sample row `John Doe`) | dashboard admin | `server.py:1639` |
| 8 | WS | `/ws` | **NONE** | — | on connect: `initial_state` with **full attendance + stats + teams**; then every status event is pushed (`server.py:1649-1655`, broadcasts at `server.py:1023-1024`, `1085`, `1102`, `1119`) | dashboard | `server.py:1649` |
| 9 | GET | `/dashboard` | **NONE** | — | the entire supervisor dashboard HTML/JS | browser | `server.py:3635` |
| 10 | GET | `/docs`, `/redoc`, `/openapi.json` | **NONE** | — | interactive docs + full machine-readable API schema | — | CONFIRMED by omission at `server.py:951` — `FastAPI(title="Shift Kiosk Server", version="2.0.0")` passes no `docs_url`/`redoc_url`/`openapi_url`, so FastAPI's defaults apply |

**This answers the question posed from the OpenAPI dump:** every endpoint that showed no
key/token parameter is genuinely **open**. There is no middleware, dependency or
router-level guard compensating for it — `server.py:953` (`CORSMiddleware`) is the only
`add_middleware` call in the file, and no `APIRouter` is used anywhere (all routes are
registered directly on `app`).

Items 3, 5, 8 and 9 are the material ones: **the complete live attendance picture for every
agent, including `SICK_LEAVE`, is retrievable and streamable with no credential whatsoever.**

---

## 2. Routes behind the shared key

CONFIRMED — each decorator carries `dependencies=[Depends(verify_api_key)]` at the line
shown. None of them performs any further authorization check, and none derives identity
from the credential.

### 2.1 Kiosk endpoints (called by installed kiosks)

| Method | Path | Identity trusted | Returns / changes | Route |
|---|---|---|---|---|
| POST | `/api/status` | `agent_username` + `agent_hostname` **from body** | inserts a `status_log` row; broadcasts to all WS clients; may send Teams + e-mail | `server.py:1015` |
| GET | `/api/agent-lookup` | `username` **from query** | `{agent_id}`; 404 if unknown — the only endpoint that rejects an unknown username | `server.py:1285` |
| GET | `/api/shift-status` | `username` **from query** | `{open, status, shift_start}` for that username | `server.py:1295` |
| GET | `/api/agent-location-options` | `agent_id` **from query** | WIC centres + coverage role for that agent; always `home_office_allowed: true` | `server.py:1303` |

These four plus `/health` and `/api/today-holiday` are the **entire** set a kiosk uses
(`main_kiosk.py:389, 400, 417, 451, 706, 821`). Everything in §2.2–§2.5 is reachable with
the kiosk's key but is never called by a kiosk — that is what makes a scope split possible
in Phase 0.

### 2.2 Write / administrative endpoints

| Method | Path | Identity trusted | Returns / changes | Caller | Route |
|---|---|---|---|---|---|
| POST | `/api/import-agents` | none | **uploads `.xlsx` and upserts the `agents` table** (`import_agents_from_excel`, `server.py:755`) | dashboard admin | `server.py:1042` |
| POST | `/api/manual-checkin` | `agent_id` + `performed_by` **from body** | inserts `status_log` (`ON_SHIFT`, hostname literal `MANUAL`) **and** a `manual_actions` audit row | dashboard | `server.py:1068` |
| POST | `/api/manual-logout` | `agent_id` + `performed_by` **from body** | inserts `status_log` (`OFF`, hostname `MANUAL`) **and** a `manual_actions` audit row | dashboard | `server.py:1088` |
| POST | `/api/emergency/ack/{id}` | `acked_by` **from body** | sets `emergency_acked`/`_by`/`_at` on that `status_log` row, then calls `manual_logout` internally; sends Teams | dashboard | `server.py:1106` |
| POST | `/api/wic-centers` | none | creates a WIC centre | dashboard admin | `server.py:1241` |
| PUT | `/api/wic-centers/{id}` | none | updates a WIC centre | dashboard admin | `server.py:1254` |
| DELETE | `/api/wic-centers/{id}` | none | soft-deletes a WIC centre (`active=0`) | dashboard admin | `server.py:1267` |

### 2.3 Personal-data read endpoints

| Method | Path | Returns | Route |
|---|---|---|---|
| GET | `/api/audit-log` | `manual_actions.*` joined to `agents.full_name`, **all rows, no filter** (`server.py:490-496`) | `server.py:1125` |
| GET | `/api/history` | `SELECT * FROM status_log`, optional date/status filter, **all agents** (`server.py:122-128`) | `server.py:1128` |
| GET | `/api/stats` | `{status: count}` from `vw_current_status` (aggregate only) | `server.py:1132` |
| GET | `/api/summary` | `vw_shift_summary` rows for a date range + optional TL (`server.py:141`) | `server.py:1135` |
| GET | `/api/worked-report` | per-agent worked hours for a date range (`server.py:173`) | `server.py:1426` |
| GET | `/api/logout-audit` | logout events for a date range, default last 30 days (`server.py:209`) | `server.py:1466` |
| GET | `/api/wic-attendance` | per-centre attendance for a date range (`server.py:298`) | `server.py:1554` |
| GET | `/api/wic-centers` | active WIC centres (reference data, no personal data) | `server.py:1237` |
| GET | `/api/wic-coverage` | all agents with check-in/out for a date + centre, per-agent `covered` flag (`server.py:666`) | `server.py:1275` |
| GET | `/api/wic-agents` | WIC agent roster (`server.py:570`) | `server.py:1281` |

### 2.4 Bulk exports

All stream the same data as their JSON siblings, in file form.

| Method | Path | Underlying query | Route |
|---|---|---|---|
| GET | `/api/export/csv` | `db_get_history` — **entire `status_log`** | `server.py:1144` |
| GET | `/api/export/attendance/csv` | `db_get_attendance` | `server.py:1156` |
| GET | `/api/export/summary/csv` | `db_get_shift_summary` | `server.py:1167` |
| GET | `/api/agents/export/csv` | `db_get_agents` | `server.py:1211` |
| GET | `/api/wic-coverage/export/xlsx` | `db_get_wic_coverage` | `server.py:1337` |
| GET | `/api/worked-report/xlsx` | `db_get_worked_report` | `server.py:1361` |
| GET | `/api/worked-report/csv` | `db_get_worked_report` | `server.py:1435` |
| GET | `/api/logout-audit/csv` | `db_get_logout_audit` | `server.py:1476` |
| GET | `/api/logout-audit/xlsx` | `db_get_logout_audit` | `server.py:1513` |
| GET | `/api/wic-attendance/csv` | `db_get_wic_attendance` | `server.py:1563` |
| GET | `/api/wic-attendance/xlsx` | `db_get_wic_attendance` | `server.py:1597` |

---

## 3. Correction to the existing documentation

`ShiftKiosk/CODE_OVERVIEW.md` § "API routes registered" is the closest existing
equivalent of this table. It is **not wrong about any auth value**, but it is **incomplete
and inconsistent**, and must not be relied on:

| Issue | Detail |
|---|---|
| Missing routes | `/api/agent-lookup`, `/api/shift-status`, `/api/agent-location-options`, `/api/today-holiday`, `/api/wic-agents`, `/api/worked-report{,/csv,/xlsx}`, `/api/logout-audit{,/csv,/xlsx}`, `/api/wic-attendance{,/csv,/xlsx}`, `/docs`, `/redoc`, `/openapi.json` — **18 routes absent** from a table of 27 |
| Misleading Auth column | It distinguishes "API key" from "API key / token", implying two different strengths. In code there is **one** function and both credential forms are accepted by all of them (`server.py:955-962`). The distinction does not exist. |
| Does not flag the open routes | `/api/agents`, `/api/attendance`, `/api/teams` are listed as "None" without comment, although they return personal data for all agents |

Where this matrix and `CODE_OVERVIEW.md` differ, **this matrix reflects actual behaviour**,
because every row here was taken from the decorator in `server.py`.

---

## 4. Aggregate counts

CONFIRMED by enumerating the decorators in `server.py`:

| Category | Count |
|---|---|
| Routes with **no** authentication | 9 declared (`server.py:1007, 1011, 1052, 1056, 1060, 1320, 1639, 1649, 3635`) + 3 FastAPI defaults (`/docs`, `/redoc`, `/openapi.json`) = **12** |
| Routes behind the shared key | **32** |
| Routes performing any **authorization** check beyond the key | **0** |
| Routes deriving identity from the credential | **0** |
| Routes a kiosk actually calls | **6** |
