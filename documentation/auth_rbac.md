# Auth & RBAC — KID-only login and role-based access control

Status: Phase 2 implemented, **enforcement flag still OFF** (see Rollout below).
Date: 2026-09-21.

## Roles (exactly four)

| Role      | Read | Write | Notes |
|-----------|------|-------|-------|
| AGENT     | own data only (server-side allow-list, see below) | **none — every write returns 403** | session always carries a real EmployeeId |
| TEAM_LEAD | everything | everything | same rights as RTM |
| RTM       | everything | everything | same rights as TEAM_LEAD |
| DEV       | everything | everything + `/api/admin/*` | bypasses all policies, never restricted |

## Where roles live: `AppUserRoles` (the login table)

Roles are **not** columns on `Employees`. Non-employees (developer, RTMs, team
leads) exist only here with `EmployeeId = NULL`. No fake `Employees` rows, and no
KID is ever written into `Employees` by hand — `WicCoverageImport` upserts KIDs at
startup.

```sql
CREATE TABLE AppUserRoles (
    Id          INT            IDENTITY(1,1) PRIMARY KEY,
    Kid         NVARCHAR(20)   NOT NULL DEFAULT '',  -- '' = placeholder, can never log in
    Role        NVARCHAR(20)   NOT NULL,             -- DEV | RTM | TEAM_LEAD | AGENT
    EmployeeId  NVARCHAR(20)   NULL,                 -- NULL for non-employees; mandatory for AGENT
    DisplayName NVARCHAR(200)  NOT NULL,
    IsActive    BIT            NOT NULL DEFAULT 1,
    CreatedAt   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt   DATETIME2      NULL
);
CREATE UNIQUE INDEX UX_AppUserRoles_Kid ON AppUserRoles (Kid) WHERE Kid <> '';
```

Seeded at startup (idempotent) — verified 2026-09-21:

| Kid    | Role      | EmployeeId | DisplayName         | IsActive |
|--------|-----------|-----------|---------------------|----------|
| S69307 | DEV       | 9074466   | Stojnic Nebojsa     | 1 |
| S60028 | RTM       | NULL      | Silvija Angelova    | 1 |
| Y6525  | RTM       | NULL      | Yiting Qiang        | 1 |
| (empty)| TEAM_LEAD | NULL      | Tobias Rossberg     | 0 |
| (empty)| TEAM_LEAD | NULL      | Delia Panaitescu    | 0 |
| (empty)| TEAM_LEAD | NULL      | Oliver Schleusen    | 0 |
| (empty)| TEAM_LEAD | NULL      | Karlo Coric         | 0 |
| (empty)| TEAM_LEAD | NULL      | Ion Ciuceanu        | 0 |
| (empty)| TEAM_LEAD | NULL      | Jaroslaw Brzeszkiewicz | 0 |

The six TEAM_LEAD rows are placeholders: `Kid = ''` + `IsActive = 0` means they
**can never authenticate**. When a team lead's real KID arrives:

```sql
UPDATE AppUserRoles
SET Kid = '<KID>', IsActive = 1, UpdatedAt = SYSUTCDATETIME()
WHERE Role = 'TEAM_LEAD' AND DisplayName = '<Name>';
```

## Login resolution (AuthService.ResolveLoginAsync) — exact order

Input is trimmed; empty or longer than 20 chars → rejected.

- **(a)** `AppUserRoles` where `IsActive=1 AND Kid<>'' AND Kid = input` → that role
  (EmployeeId may be NULL for non-employees). AGENT row without EmployeeId → rejected
  (also enforced at startup, see below).
- **(b)** `Employees` where `IsActive=1 AND (PrimaryKid = input OR SecondaryKid = input)` → AGENT.
- **(c)** `Employees` where `IsActive=1 AND EmployeeId = input` → AGENT.
  (Precedent: `WicCoverageService.GetAgentByKidAsync` already accepts either.)
- **(d)** otherwise → 401 with the neutral body `{ "error": "Unknown KID" }`.

Any input matching **more than one active row** at any step → 401 + warning log.
Never guess.

Reachability at implementation time (120 active employees): **57** reachable by
rule (b) KID match, **63** reachable only by rule (c) employee-number match
(`PS1_ListAgentsWithoutKid.ps1` lists them for KID collection).

## Endpoints

| Method | Route | Access |
|--------|-------|--------|
| POST | `/api/auth/login` `{ "kid": "..." }` | anonymous, **rate-limited 10/min per IP** |
| POST | `/api/auth/logout` | any authenticated role |
| GET  | `/api/auth/me` | any authenticated role → `{ kid, name, role, employeeId }` |

Session: cookie `gsd_auth`, HttpOnly, SameSite=Lax, 12 h sliding expiration.
`CookieSecurePolicy.SameAsRequest` — Secure over the https devtunnel URL, plain on
`http://localhost:5000`, so login works over **both** (verified).

`/api/debug/client-error` stays anonymous but is rate-limited with the same policy
and never echoes submitted content.

## Enforcement (RbacMiddleware) — gated by `Auth:EnforceAuthorization`

- `false` (current) → middleware is a no-op: login works, nothing is blocked.
- `true` → for every `/api/*` request:
  - anonymous allow-list: `/api/auth/login`, `/api/debug/client-error` (both rate-limited);
  - unauthenticated → 401 `{ "error": "Authentication required" }`;
  - DEV → bypass everything;
  - `/api/admin/*` (`cleanup-wic-orphans`, `demo-data`) → DEV only;
  - TEAM_LEAD / RTM → unrestricted;
  - AGENT → GET allow-list below, everything else 403; **all writes 403**.

Non-`/api` paths (static files, `/health`, SPA fallback) are untouched —
`/health` must stay anonymous for the watchdog. `/swagger` is the one exception: it is
not under `/api`, so it has its own gate in `Program.cs` — DEV role only, **always
enforced** (independent of `Auth:EnforceAuthorization`), everyone else gets 404.

### AGENT read allow-list (all scoped to the session's own EmployeeId/KID)

- `/api/auth/me`, `/api/auth/logout`
- `/api/employees/{ownId}`, `.../timeline`, `.../future-shifts`
- `/api/albalance/{ownId}`
- `/api/vacations?employeeId={ownId}` (unfiltered or foreign id → 403)
- `/api/wic?employeeId={ownId}` (same)
- `/api/wic-coverage/agents/{ownKid|ownId}`
- reference data: `/api/wic/locations`, `/api/publicholidays*`

The frontend AGENT view (`/my`) uses exactly these endpoints. An AGENT cannot read
another agent's row by changing an id in the URL or query string — verified by
`PS1_AuthSmokeTest.ps1`.

## Rollout (lockout protection — this order is a hard requirement)

1. **(done)** `AppUserRoles` created + seeded, verified by real query
   (`PS1_Seed_AppUserRoles.ps1`).
2. **(done)** Backend smoke test on a spare port with `Auth__EnforceAuthorization=true`:
   24/24 checks pass (`PS1_AuthSmokeTest.ps1 -BaseUrl http://localhost:5050`).
3. **(next, deploy)** Deploy normally; log in with S69307 over BOTH
   `https://<devtunnel>` and `http://localhost:5000` — re-run
   `PS1_AuthSmokeTest.ps1 -BaseUrl <url>` against both.
4. **(only then)** Set `"Auth": { "EnforceAuthorization": true }` in
   `bin\Release\net8.0\appsettings.json` and restart the task. No rebuild needed.
   To roll back enforcement instantly: set it back to `false`, restart.

Two startup guards refuse to start the app in a bad state:
- any `AppUserRoles` row with `Role='AGENT'` and empty/NULL `EmployeeId`;
- zero active DEV rows with a non-empty Kid.

## Recovery — no browser needed

If DEV access is ever lost (row deleted/deactivated, cookie lost, whatever), run this
against the database (or `PS1_Seed_AppUserRoles.ps1`, which contains it) and restart
the app — you can never be locked out of your own machine:

```sql
-- sqlcmd -S localhost\SQLEXPRESS -d GSDDashboard -i recovery.sql
IF NOT EXISTS (SELECT 1 FROM AppUserRoles WHERE Kid = 'S69307')
    INSERT INTO AppUserRoles (Kid, Role, EmployeeId, DisplayName, IsActive)
    VALUES ('S69307', 'DEV', '9074466', 'Stojnic Nebojsa', 1);
ELSE
    UPDATE AppUserRoles SET Role = 'DEV', IsActive = 1, UpdatedAt = SYSUTCDATETIME()
    WHERE Kid = 'S69307';
```

If enforcement itself is the problem, set `"Auth": { "EnforceAuthorization": false }`
in `bin\Release\net8.0\appsettings.json` and restart — the app behaves exactly as
before auth existed.

## Scripts

| Script | Purpose |
|--------|---------|
| `PS1_Seed_AppUserRoles.ps1` | Idempotent create + seed + verification queries |
| `PS1_AuthSmokeTest.ps1 -BaseUrl <url>` | 24 end-to-end auth/RBAC checks against a running backend |
| `PS1_ListAgentsWithoutKid.ps1 [-CsvOut file]` | The 63 active agents with no KID (id, name, team lead) |

## Notes / known limitations

- The kiosk integration is unaffected: `WicAttendance.tsx` calls the ShiftKiosk
  FastAPI server (port 8000) directly from the browser — no machine-to-machine
  traffic hits this backend, so no service-token exemption was needed.
  **Scope warning (added 2026-10-02):** that sentence is correct about *this* backend, and
  has been misread as "the kiosk is out of scope for authentication work". It is not. The
  ShiftKiosk server on port 8000 has its own, far weaker authentication — one shared key,
  client-supplied identity, and several endpoints with no authentication at all. None of
  the roles on this page apply there. See
  [KIOSK_AUTH_IDENTITY_ASIS.md](KIOSK_AUTH_IDENTITY_ASIS.md) and
  [KIOSK_ENDPOINT_ACCESS_MATRIX.md](KIOSK_ENDPOINT_ACCESS_MATRIX.md). Traffic does also
  flow the other way: kiosk `/api/today-holiday` server-side fetches
  `localhost:5000/api/publicholidays` (`ShiftKiosk/server/server.py:1325-1329`).
- TEAM_LEAD/RTM are unrestricted on functional endpoints by design; only
  `/api/admin/*` is DEV-only.
- `/swagger` is served outside `/api`, so the RBAC middleware cannot see it. It is
  gated separately in `Program.cs`: DEV role only, always enforced (even while
  `Auth:EnforceAuthorization` is still `false`); everyone else gets 404 so the
  endpoint map is not leaked over the devtunnel.
- `/api/roster/*` (Roster Generator + missing-roster check) has its own server-side
  gate, `RosterAccess` in `Backend/RosterService.cs`: RTM, TEAM_LEAD and DEV only,
  **always enforced** — deliberately NOT behind `Auth:EnforceAuthorization`, so an
  AGENT session gets 403 even while the global rollout flag is off. The page-level
  role check in `Frontend/src/pages/Roster.tsx` is cosmetic; the server gate is the
  real one.
- **Name collision — "RTM" means two things in this codebase.** Here it is an access
  *role* (full read/write, same rights as TEAM_LEAD). `PROJECT_BLUEPRINT.md`
  § Acronym Glossary defines the same three letters as **"Return to Main"**, a
  shift-workflow term used by the `/bulk-rtm` page and the `RtmEntries` table. Both
  usages are established in code (`AppRoles.Rtm`, `RtmEntries`) and are not being
  renamed — context decides which meaning applies.


---

# UPDATE 2026-10-02 — this page was stale, and the model has changed

## Correction: the TEAM_LEAD rows are no longer placeholders

The table above describes the six `TEAM_LEAD` rows as placeholders with
`Kid = ''` and `IsActive = 0` that "can never authenticate". **That is no longer
true.** Read from the live `AppUserRoles` on 2026-10-02 (read-only): there are
**9 active rows, all with real KIDs**.

| Kid | Role | DisplayName |
|---|---|---|
| S69307 | DEV | Stojnic Nebojsa |
| S60028 | RTM | Silvija Angelova |
| Y6525 | RTM | Yiting Qiang |
| T31561 | TEAM_LEAD | Tobias Rossberg |
| M73784 | TEAM_LEAD | Delia Panaitescu |
| O7164 | TEAM_LEAD | Oliver Schleusen |
| K33261 | TEAM_LEAD | Karlo Coric |
| I15078 | TEAM_LEAD | Ion Ciuceanu |
| J51904 | TEAM_LEAD | Jaroslaw Brzeszkiewicz |

This is why `tools/create_supervisors.ps1` creates **nine** accounts, not three.

## The KID-only sign-in was a credential chain, and it is being closed

`ResolveLoginAsync` rule (b) accepts a KID and rule (c) accepts a bare
`EmployeeId` (`Backend/Services/AuthService.cs:63-86`), with **no password and no
second factor** — the code's own comment at `AuthModule.cs:30-31` says so.

The ShiftKiosk server published every agent's `employee_id` on `/api/attendance`,
which required **no credential at all**. Read from the test copy: of 123 active
agents, **122 carry an employee_id and 115 a primary_kid** — all reachable
anonymously. So anyone who could reach the kiosk server could sign in to Pulse as
any agent.

Bounded, and stated precisely: a cross-database join of the kiosk `agents` table
against `AppUserRoles` returns **zero rows**, so the exposure granted **AGENT
level only**, never DEV, RTM or TEAM_LEAD.

Two independent fixes, both required:

1. The kiosk server strips `employee_id`, KIDs and e-mail addresses for
   unauthenticated callers (flag `anon_identifiers`). The Pulse frontend now
   reaches that data through an authenticated server-side proxy,
   `Backend/Services/KioskProxyService.cs` → `GET /api/kiosk/attendance`.
2. `Auth:SupervisorMode` governs the KID path itself.

`AuthService.cs` is deliberately **unchanged**, so `SupervisorMode=off` restores
today's behaviour exactly.

## The new sign-in model

| Setting | Meaning |
|---|---|
| `Auth:SupervisorMode = off` | KID-only sign-in, exactly as before |
| `= shadow` **(default)** | KID-only still works; every use is logged as a would-deny so you can see who would be locked out |
| `= enforce` | username + password + TOTP only; KID-only refused |

Accounts live in the **ShiftKiosk** database (`sec_accounts`) and are created only
by `tools/admin_cli.py`, never over HTTP. Pulse does not re-implement credential
checking: it calls `POST /api/auth/verify` on the kiosk server through
`ISupervisorAuthProvider` and then issues its existing `gsd_auth` cookie. One
implementation of PBKDF2 + TOTP + lockout, not two.

`ISupervisorAuthProvider` is the single seam for a later move to Entra ID,
Kerberos or client certificates — swap the implementation, change no
authorization code.

Only **TEAM_LEAD, RTM and DEV** may hold a Pulse session at `enforce`. Any other
role is refused (`AuthModule.cs`).

**Consequence to accept deliberately:** at `enforce`, agents lose the Pulse `/my`
self-service page, because they have no account and never authenticate.

## Relationship to `Auth:EnforceAuthorization`

Still `false` in both `appsettings.json` files, so `RbacMiddleware` is a no-op and
Pulse currently blocks nothing regardless of who signed in. **Close the identifier
leak (step 8 of the runbook) before switching it on**, or it gives false assurance:
an anonymous reader of the kiosk server could obtain a valid AGENT credential.

## What is NOT implemented

**Team-Lead data scoping.** `sec_accounts.team_leader` is populated and maps to
`agents.team_leader`, but no read endpoint filters on it yet. A TEAM_LEAD who
signs in sees the same data an RTM sees. Implementing it means adding
`WHERE team_leader = @sessionTeamLead` to roughly fifteen query functions. Stated
plainly rather than implied.

See [LIVE_DEPLOY_RUNBOOK.md](LIVE_DEPLOY_RUNBOOK.md) and
[KIOSK_HARDENING_DEPLOY_NOTE.md](KIOSK_HARDENING_DEPLOY_NOTE.md).
