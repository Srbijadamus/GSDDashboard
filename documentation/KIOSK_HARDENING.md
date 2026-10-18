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
