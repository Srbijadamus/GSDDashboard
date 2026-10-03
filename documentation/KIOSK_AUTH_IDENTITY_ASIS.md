# ShiftKiosk — Authentication and Identity (AS-IS)

> **HISTORICAL SNAPSHOT (before dan1), not current deployment instructions.**
> Original findings below retain their original evidence/line numbers.
> Canonical current state: [KIOSK_HARDENING_DEPLOY_NOTE.md](KIOSK_HARDENING_DEPLOY_NOTE.md).
> Client evidence/install: `C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md`.

Scope: the **ShiftKiosk FastAPI server** (`C:\ShiftKiosk\server\server.py`, port 8000)
and the **kiosk client** (`C:\ShiftKiosk\kiosk\main_kiosk.py`). This is a separate
application from the ASP.NET Core WorkForce Pulse backend on port 5000; the backend's own
login/RBAC is documented in [auth_rbac.md](auth_rbac.md) and does **not** apply here.

Every statement is labelled CONFIRMED (with file + line), ASSUMPTION, or NOT FOUND.
Line numbers refer to the files as read on 2026-10-02.

> Nothing was executed to produce this document. All findings are from reading source.

---

## 1. Summary of the authentication model

CONFIRMED — There is exactly **one shared secret** for the whole system, and **no user
accounts, no sessions tied to a person, and no role or permission concept** on the kiosk
server.

| Property | Value | Source |
|---|---|---|
| Mechanism | single shared API key | `server.py:31`, `server.py:955-962` |
| Accepted in | request header `X-API-Key` **or** query parameter `token` | `server.py:956-959` |
| Secret storage (server) | `C:\ShiftKiosk\server\server_config.ini`, `[Server] api_key`, plaintext | `server.py:30-31`, `server_config.ini` |
| Secret storage (kiosk) | `C:\ShiftKiosk\kiosk\config.ini`, `[Server] api_key`, plaintext | `main_kiosk.py:300-301`, `kiosk/config.ini` |
| Per-user authentication | none | NOT FOUND IN CODE OR DOCS |
| Per-device authentication | none | NOT FOUND IN CODE OR DOCS |
| Role / permission model | none | NOT FOUND IN CODE OR DOCS |
| Rate limiting | none | NOT FOUND IN CODE OR DOCS — `server.py:953` is the only `add_middleware` call |

---

## 2. How a request is authenticated

CONFIRMED — `server.py:955-962` is the only authentication code path:

```python
async def verify_api_key(
    x_api_key: Optional[str] = Header(None),
    token: Optional[str] = Query(None)
):
    key = x_api_key or token
    if key and (key == API_KEY or _verify_session_token(key)):
        return
    raise HTTPException(status_code=401, detail="Invalid API key")
```

Facts that follow directly:

1. **Header or URL.** The header name is `X-API-Key` (FastAPI maps the parameter
   `x_api_key` to that header). The query parameter `token` is an equally valid
   alternative, so the secret can travel in the URL — and therefore into any proxy,
   devtunnel or server access log. CONFIRMED `server.py:956-959`.
2. **Header wins.** `x_api_key or token` means a present-but-wrong header is used and the
   query parameter is *not* tried as a fallback. CONFIRMED `server.py:959`.
3. **The comparison is not constant-time.** `key == API_KEY` is plain string equality.
   The session-token path immediately below *does* use `hmac.compare_digest`
   (`server.py:65`), so the inconsistency sits inside one function. CONFIRMED
   `server.py:960`. Practical remote exploitability is low; recorded for completeness.
4. **It is a gate, not an identity.** `verify_api_key` returns `None`. It tells the
   endpoint nothing about *who* called, and no endpoint receives a caller identity from
   authentication. CONFIRMED `server.py:955-962` (no return value). This is the root cause
   of every identity finding in section 4.

### 2.1 The session-token path is an unauthenticated bypass of the API key

CONFIRMED — and this is the most important finding in this document.

| Step | Code | Source |
|---|---|---|
| Token format | `{unix_ts}.{hmac_sha256(API_KEY, unix_ts)}` | `server.py:52-55` |
| TTL | 28800 s = 8 h | `server.py:50` |
| Verification | TTL check + `hmac.compare_digest` | `server.py:57-68` |
| Token is minted by | `GET /api/session` | `server.py:1011-1013` |
| Auth required to mint a token | **none** — the route has no `dependencies=[...]` | `server.py:1011` |

Because `verify_api_key` accepts *either* the master key *or* any valid session token
(`server.py:960`), and because `GET /api/session` hands a valid session token to **any
anonymous caller** (`server.py:1011-1013`):

> **Anyone who can reach the server can call `GET /api/session`, receive a token valid for
> 8 hours, and use it to satisfy every endpoint that is "protected" by the API key.
> Knowledge of the shared key is not required at all.**

CONFIRMED by composition of `server.py:1011-1013` and `server.py:955-962`.

This materially widens the threat model used in the security test: the tester used the
shared key, but the key was never necessary. `ShiftKiosk/README.md:229` documents the
intent — "The dashboard obtains a short-lived session token on page load … the master API
key is never sent to the browser" — which is accurate, but the document does not state
that the token-issuing endpoint is itself unauthenticated, nor that the token is accepted
everywhere the master key is.

ASSUMPTION — the open `/api/session` is deliberate, because `/dashboard` is served without
authentication (`server.py:3635-3636`) and its JavaScript needs a token on load. No code
comment states this; the intent recorded in `README.md:229` is consistent with it.

---

## 3. Transport

| Fact | Status | Source |
|---|---|---|
| uvicorn starts with no TLS parameters — plain HTTP | CONFIRMED | `server.py:3642` — `uvicorn.run("server:app", host=host, port=port, reload=False)`, no `ssl_keyfile`/`ssl_certfile` |
| HTTPS toward kiosks is terminated by the devtunnel, not the app | CONFIRMED | `kiosk/config.ini` → `url = https://ssr7tm2l-8000.euw.devtunnels.ms`; `technical.md` § Kiosk Server |
| The kiosk client disables TLS certificate verification on **every** call | CONFIRMED | `main_kiosk.py:389, 400, 417, 451, 706, 821` — all pass `verify=False` |
| CORS allows every origin, method and header | CONFIRMED | `server.py:953` — `allow_origins=["*"], allow_methods=["*"], allow_headers=["*"]` |

`verify=False` means a kiosk will hand the shared key to any host that can win name
resolution or routing for the tunnel hostname, with no certificate warning.

---

## 4. How the server decides which agent a request belongs to

CONFIRMED — **identity is always taken from the request body or query string. It is never
derived from authentication.** No code path lets the authenticated caller constrain the
subject of the request.

| Endpoint | Identity field | Trusted from | Source |
|---|---|---|---|
| `POST /api/status` | `agent_username`, `agent_hostname` | request body | `server.py:963-967`, `server.py:1016-1021` |
| `POST /api/manual-checkin` | `agent_id` (subject), `performed_by` (actor) | request body | `server.py:969-983`, `server.py:1069-1082` |
| `POST /api/manual-logout` | `agent_id` (subject), `performed_by` (actor) | request body | `server.py:969-983`, `server.py:1089-1099` |
| `POST /api/emergency/ack/{id}` | `acked_by` (actor) | request body | `server.py:985-994`, `server.py:1107-1110` |
| `GET /api/agent-lookup` | `username` | query string | `server.py:1285-1293` |
| `GET /api/shift-status` | `username` | query string | `server.py:1295-1301` |
| `GET /api/agent-location-options` | `agent_id` | query string | `server.py:1303-1317` |

### 4.1 Resolution of `agent_username`

CONFIRMED — two independent lookups, both by `agents.windows_username`:

- `get_agent_info(agent_username)` — `server.py:914-921`:
  `SELECT full_name, team_leader FROM agents WHERE windows_username=? AND active=1`
- `db_insert_status(payload, ip)` — `server.py:93-97`:
  `SELECT id FROM agents WHERE windows_username=? AND active=1`

### 4.2 What happens with an unknown username

CONFIRMED — **nothing is rejected. The event is still written.**

| Behaviour | Source |
|---|---|
| `get_agent_info` falls back to `{"full_name": <submitted username>, "team_leader": ""}` | `server.py:921` |
| `db_insert_status` leaves `agent_id` as `None` and inserts the row anyway | `server.py:93-107` |
| `POST /api/status` performs no existence check before inserting | `server.py:1016-1021` |

An arbitrary string posted as `agent_username` therefore creates a `status_log` row with
`agent_id = NULL` and `full_name` echoing the attacker-chosen string. The row is broadcast
to every WebSocket client (`server.py:1023-1024`) and — for `SICK_LEAVE`, `EMERGENCY`,
`AUTO_LOGOUT`, `OFF` — can trigger a Teams card and an e-mail whose subject contains that
string (`server.py:1025-1038`, gated by `should_notify`, `server.py:910-912`).

**Exception — `/api/agent-lookup` is the one endpoint that rejects an unknown username**
(HTTP 404, `server.py:1291-1292`). It is advisory only: the kiosk calls it at startup to
resolve `agent_id` (`main_kiosk.py:414-429`), but `POST /api/status` does not require that
the lookup succeeded. CONFIRMED — `main_kiosk.py:425` logs `username=… not in agents table`
and the kiosk continues.

### 4.3 Impersonation is a direct consequence, not a validation flaw

CONFIRMED — the security test's third finding is reproduced by design. `POST /api/status`
writes whatever `agent_username` / `agent_hostname` the body contains
(`server.py:1016-1021` → `server.py:98-107`), and the only credential needed is the shared
key *or* an anonymous session token (§2.1). Changing the two constants at
`main_kiosk.py:302-303` is sufficient, and no server-side check can detect it, because the
server has no other source of identity.

### 4.4 Fields accepted but not recorded

| Field | Accepted at | Recorded? | Source |
|---|---|---|---|
| `client_version` (default `"2.0.0"`) | `StatusPayload`, `server.py:966` | **No** — absent from the `status_log` INSERT column list | `server.py:98-107` |
| caller IP | `db_insert_status(payload, ip)` signature, `server.py:90` | **No** — `/api/status` passes the empty string, so `ip_address` is always `''` | `server.py:1021` |

Both matter for migration: neither the kiosk build nor the source address of a request is
retained, so the server cannot today distinguish a legacy kiosk from a new one, or from
curl. See [SECURITY_MIGRATION_PROPOSAL.md](SECURITY_MIGRATION_PROPOSAL.md) § Phase 3.

---

## 5. Is there any role or permission concept?

NOT FOUND IN CODE OR DOCS — the kiosk server makes no authorization decision of any kind.
There is no role column, no claims, and no per-endpoint permission check beyond the binary
`verify_api_key` gate.

One near-miss that must not be mistaken for one: `wic_agent_roles.role` with values
`MAIN` / `BACKUP` / `REGIONAL` (`server.py:1306-1312`). That is a **WIC coverage** role —
which agent covers which centre — and is never consulted for access control.

The four access roles `AGENT` / `TEAM_LEAD` / `RTM` / `DEV` in [auth_rbac.md](auth_rbac.md)
belong to the **port-5000 ASP.NET backend only** and have no effect on port 8000.

---

## 6. How the kiosk obtains and stores the key

CONFIRMED — the key is baked into the install package and copied to every machine.

| Step | Detail | Source |
|---|---|---|
| Source of truth | `deploy\Files\config.ini` / `KIOSK_PACKAGE\Files\config.ini`, containing the shared `api_key` | `KIOSK_PACKAGE/install_kiosk.bat:101`, `deploy/Deploy-Application.ps1:11` |
| Written to | `C:\ShiftKiosk\kiosk\config.ini` | `KIOSK_PACKAGE/install_kiosk.bat:107`, `deploy/Deploy-Application.ps1:117` |
| Format | plaintext INI, `[Server] api_key = …` | `kiosk/config.ini` |
| Read at startup | `cfg.get("Server", "api_key", fallback="")` | `main_kiosk.py:301` |
| Sent as | `headers={"X-API-Key": API_KEY}` on every request | `main_kiosk.py:389, 417, 451, 706, 821` |
| Per-device uniqueness | none — the same file is copied to every laptop | CONFIRMED — the installer copies one static file; no key-generation step exists in `install_kiosk.bat` or `Deploy-Application.ps1` |

### 6.1 Every kiosk user can read the key — by mandatory design

CONFIRMED — `KIOSK_PACKAGE/install_kiosk.bat:115-118`:

```bat
echo   Granting BUILTIN\Users (S-1-5-32-545) RX on C:\ShiftKiosk...
icacls "C:\ShiftKiosk" /grant "*S-1-5-32-545:(OI)(CI)RX" /T
if errorlevel 1 (
    call :fail "icacls grant failed. The agent will get PermissionError at logon."
)
```

`BUILTIN\Users` is granted **Read+Execute, inherited, recursively** over all of
`C:\ShiftKiosk` — which contains `kiosk\config.ini`. The installer **aborts** if the grant
fails, because the kiosk runs as the logged-on agent and could not otherwise read its own
configuration.

This cannot be tightened away: the process that needs the key runs as the user who must
not have it. Any design that keeps a bearer secret in a file the kiosk process can read
gives that secret to the agent. This single fact decides the migration design — see
[SECURITY_MIGRATION_PROPOSAL.md](SECURITY_MIGRATION_PROPOSAL.md) § 1.

### 6.2 The key is in git history

CONFIRMED — `ShiftKiosk/README.md:233`:

> "The `api_key` value was committed to the repository in three early commits
> (`0717d87`, `560ba47`, `77304d3`) and remains in git history; the repository must stay
> **private**."

`server_config.ini` and `kiosk/config.ini` are in `.gitignore` *now*
(`CODE_OVERVIEW.md` § `server/server_config.ini`), which does not remove the historical
blobs. Anyone who has ever cloned the repository holds the key.

---

## 7. What the kiosk sends, and when

CONFIRMED — `main_kiosk.py`. All six calls carry the `X-API-Key` header except `/health`,
and all pass `verify=False`.

| When | Call | Source |
|---|---|---|
| Startup — resolve identity | `GET /api/agent-lookup?username=<os.getlogin()>` | `main_kiosk.py:414-417` |
| Startup — resume an open shift | `GET /api/shift-status?username=…` | `main_kiosk.py:818-821` |
| Startup — holiday banner | `GET /api/today-holiday` | `main_kiosk.py:450-451` |
| Reachability probe | `GET /health` (no key sent) | `main_kiosk.py:398-400` |
| Location picker | `GET /api/agent-location-options?agent_id=…` | `main_kiosk.py:703-706` |
| Every status change | `POST /api/status` | `main_kiosk.py:386-389`, payload built at `main_kiosk.py:460` |

Identity values are computed **locally** and sent in the body:

```python
USERNAME = os.getlogin()          # main_kiosk.py:302
HOSTNAME = socket.gethostname()   # main_kiosk.py:303
```

Status values accepted by the server: `ON_SHIFT`, `SICK_LEAVE`, `OFF`, `EMERGENCY`,
`AUTO_LOGOUT`, `PAUSE` (`server.py:1002`, enforced at `server.py:1017-1018`).

**The kiosk never uses the `token` query parameter.** CONFIRMED — all call sites pass the
key in the header only (`main_kiosk.py:389, 417, 451, 706, 821`). This is the evidence
that removing query-parameter authentication does not break installed kiosks.

---

## 8. Known-weak points, consolidated

All CONFIRMED, sources as given above.

1. `GET /api/session` mints an 8-hour bearer token to anonymous callers, and that token is
   accepted everywhere the master key is → the shared key is not a control boundary (§2.1).
2. Identity is entirely client-asserted; the server has no other source (§4).
3. Unknown usernames are accepted and written, not rejected (§4.2).
4. Every kiosk user can read the shared key, by a mandatory ACL grant (§6.1).
5. The key is permanently in git history (§6.2).
6. `/api/agents`, `/api/teams`, `/api/attendance`, `/api/agents-template`,
   `/api/today-holiday`, `/dashboard`, `/ws`, `/docs`, `/redoc`, `/openapi.json` require
   nothing at all — see [KIOSK_ENDPOINT_ACCESS_MATRIX.md](KIOSK_ENDPOINT_ACCESS_MATRIX.md).
7. No rate limiting on any endpoint, including `/api/session`.
8. Kiosk clients do not verify TLS certificates (§3).
9. Neither `client_version` nor the caller IP is recorded (§4.4).
