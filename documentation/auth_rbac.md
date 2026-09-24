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

