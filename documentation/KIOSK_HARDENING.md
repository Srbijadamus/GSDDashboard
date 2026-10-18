# Kiosk hardening — actual state on this branch

> Written 2026-10-18 from the code on `security/kiosk-hardening`. Unlike
> `KIOSK_HARDENING_DEPLOY_NOTE.md` (which still sequences steps behind a
> `SupervisorMode` that does not exist in code), this document describes only
> what is actually implemented and verified in the repository.

## What is implemented

The browser no longer calls the ShiftKiosk server directly. It calls Pulse,
Pulse calls the kiosk with a **service device token**, and the full kiosk
payload — including `employee_id`, which doubles as a Pulse sign-in identifier —
is returned only to a caller that already holds a Pulse session. The identifier
never reaches an anonymous client again.

### Endpoints (`Backend/Modules/Auth/KioskEndpoints.cs`)

| Route | Method | Guards |
|---|---|---|
| `/api/kiosk/attendance` | GET | role gate |
| `/api/kiosk/agents` | GET | role gate |
| `/api/kiosk/manual-checkin` | POST | role gate + same-origin + positive `agentId` + session actor |
| `/api/kiosk/manual-logout` | POST | role gate + same-origin + positive `agentId` + session actor |

**Role gate** (`CheckAccess`): anonymous → **401**; DEV / RTM / TEAM_LEAD →
allowed; AGENT or unknown role → **403**. This gate is **always enforced**,
independent of the `Auth:EnforceAuthorization` rollout flag.

**Manual actions** additionally require:
- header `X-Pulse-Request: 1`,
- an `Origin` whose authority matches the request `Host` (same-origin),
- a positive `agentId`,
- a non-empty session actor (`ctx.User.Identity.Name`).

Violations return 403/400 before any proxy call, and the manual action is
audit-logged under the `KioskAudit` logger.

### Proxy (`Backend/Services/KioskProxyService.cs`)

`KioskProxy : IKioskProxy` forwards to the kiosk upstream over **HTTP loopback
only** (`Kiosk:BaseUrl`, default `http://localhost:8000`). It fails loudly, never
silently:

| Condition | Result |
|---|---|
| `Kiosk:ServiceToken` missing | **503** — refuses to call the kiosk anonymously |
| upstream non-2xx | passthrough of the upstream status |
| feed is not an array of objects with the expected id | **502** |
| action response has no `success: true` | **502** |
| request cancelled | rethrow |
| timeout | **504** |
| upstream unavailable | **503** |
| malformed JSON | **502** |

### Configuration

`Kiosk:BaseUrl`, `Kiosk:ServiceToken` and `Kiosk:ServiceDeviceId` come from the
**external runtime config** loaded by `PulseRuntimeConfiguration.Load`
(`PULSE_RUNTIME_CONFIG` env var, or `Pulse:RuntimeConfigPath`, default
`C:\ProgramData\PulseBackend\backend.json`). The token and device id are **not in
the repository**; the device is enrolled per-device on the kiosk server with
`admin_cli.py device enroll --hostname pulse-backend` and is revocable per device.

The `Kiosk` section in `Backend/appsettings.json` is intentionally empty
(`BaseUrl` = `http://localhost:8000`, `ServiceToken`/`ServiceDeviceId` = `""`).
The app refuses to start if the external config does not supply a complete kiosk
section (`PulseRuntimeConfiguration.Load` throws).

### Tests

- `Backend.Tests/KioskProxyAccessTests.cs` — role gate: anonymous 401, allowed
  roles 200, other roles 403, with the RBAC flag both on and off.
- `Backend.Tests/KioskProxyResponseTests.cs` — proxy response mapping
  (503/502/504 on misconfiguration / outage / timeout).

## What is NOT implemented (do not assume)

- There is **no `SupervisorMode`** and **no `SupervisorAuthClient`** anywhere in
  `.cs` or `appsettings*.json`. Every mention of them lives only in
  `documentation/*.md`.
- General RBAC is still **not enforced**: `Auth:EnforceAuthorization=false` in
  `Backend/appsettings.json`. The kiosk role gate above is the exception — it is
  enforced regardless of that flag.
- Anonymous-read closure on the kiosk side (dan2/dan3 flags such as
  `anon_identifiers`, `session_auth`, `supervisor_auth`) is a separate ShiftKiosk
  change, tracked in `KIOSK_HARDENING_DEPLOY_NOTE.md` and the kiosk rollout doc.

## General RBAC — current state and enabling plan

**Current state:** `RbacMiddleware` (`Backend/RbacMiddleware.cs`) is a default-deny
gate for `/api/*`, controlled by `Auth:EnforceAuthorization` in
`Backend/appsettings.json`, which is **`false`** (rollout phase). While `false`
the middleware is a no-op: login works but nothing is blocked. The only always-on
authorization today is the kiosk proxy role gate above.

**Rules once `Auth:EnforceAuthorization=true`:**

| Caller | Result |
|---|---|
| non-`/api` paths (`/health`, static files, SPA) | untouched |
| `/api/auth/login`, `/api/debug/client-error` | anonymous allow-list (both rate-limited) |
| unauthenticated `/api/*` | **401** |
| DEV | unrestricted, bypasses everything |
| `/api/admin/*` | DEV only, **403** otherwise |
| TEAM_LEAD / RTM | unrestricted read/write on functional endpoints |
| AGENT | GET allow-list scoped to their own `EmployeeId` (own record, own timeline, own AL balance, own-filtered lists, own WIC coverage); every write → **403** |

**Tests:** `Backend.Tests/RbacMiddlewareTests.cs` asserts anonymous 401, the
allow-list, DEV/`/api/admin` rules, TEAM_LEAD/RTM, AGENT own-data-only / no-writes,
unknown-role 403, and the no-op when the flag is `false` — in an isolated host with
no database.

**Plan to enable (do NOT flip in production config without this):**

1. Prove a DEV login over **both** the https devtunnel URL and
   `http://localhost:5000` (cookie `SecurePolicy=SameAsRequest` must work on both).
2. Verify at least one ACTIVE DEV row with a non-empty `Kid` exists in the
   production `AppUserRoles` table (the startup lockout guard already enforces
   this).
3. Flip `Auth:EnforceAuthorization` to `true`, restart, and smoke-test each role:
   anonymous 401, DEV full access, RTM/TEAM_LEAD functional access, AGENT own-data
   only, `/api/admin` 403 for non-DEV.
4. Watch the `RBAC` logger for unexpected 403s before a full rollout.

**Risks:**

- **Lockout:** with zero active DEV rows the app refuses to start (hard guard).
- **Agent breakage:** any endpoint an AGENT legitimately needs but that is not in
  the `AgentAllowed` allow-list will 403 — review `AgentAllowed` before enabling.
- **CORS/origin:** the SPA is served same-origin on port 5000, so enabling RBAC does
  not depend on CORS, but cross-origin dev (localhost:5173) must keep the dev
  origins in `appsettings.Development.json`.
- **Kiosk proxy** is independent: it stays enforced regardless of this flag.
