# Auth & RBAC — KID-only login and role-based access control

> ## ⚠ NOT IMPLEMENTED IN CODE as of 2026-10-11
>
> **Every `SupervisorMode` setting described on this page — `off`, `shadow`, `enforce` — has no reader
> in the application code.** A repository-wide search finds `SupervisorMode` **only in `documentation\`
> files**; no `.cs` file contains it, no `appsettings*.json` contains the key, and
> `Backend/Services/SupervisorAuthClient.cs` does not exist. The sign-in model and the settings table
> below are **historical preparation**, not available functionality. **Do not use this page as a
> runbook.** Current state: [SECURITY_MODEL.md](./SECURITY_MODEL.md) §4 and
> [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) §0 / item **S14** (finding number 1).

**Current proxy-only candidate (2026-10-04):** production sign-in is the live
KID-only implementation, unchanged. The earlier supervisor password/TOTP
implementation described later on this page was removed from this candidate;
those sections are historical preparation, not available functionality.
Do not enable SupervisorMode for this candidate. Kiosk proxy routes still
require a session with DEV/RTM/TEAM_LEAD independently of general RBAC.

Current live deployment state is maintained in
[LIVE_DEPLOY_RUNBOOK.md](LIVE_DEPLOY_RUNBOOK.md).
General enforcement remains OFF; the proxy and manual-master fixes went live
on 2026-10-05 under the separately authorized SYSTEM watchdog.
The deployed candidate adds independent session/role checks for
both actual legacy cell writes (PATCH `/api/shifts/{id}`, POST `/api/shifts/assign`)
and their dedicated `/manual` aliases. Anonymous401; AGENT/missing or other role403;
DEV/RTM/TEAM_LEAD permitted with a nonempty session KID, used as ManualChangeBy.
Client-supplied actor fields are not accepted; all successful cell writes stamp
ManualChangeBy/At/Note. Generic assign retains its existing duplicate conflict.
The candidate Bulk RTM screen now uses `/api/shifts/assign/bulk`, with the same
session role check but no new manual marker, and 409 on protected manual rows.
Legacy cell and bulk requests were indistinguishable on `/assign`; clients still
posting there receive cell semantics (including manual marking), not a guessed
bulk exemption. The general RBAC flag is unchanged.
The legacy-login/schema description below is historical (2026-09-21);
supervisor mode and active-role corrections are in the later sections.

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

Seeded at startup (idempotent) — verified 2026-09-21. **The concrete KIDs, employee ids and display
names are deliberately not reproduced here** (personal data); the authoritative copy is the seeding
script `PS1_Seed_AppUserRoles.ps1` and the live `AppUserRoles` table:

| Kid    | Role      | EmployeeId | DisplayName         | IsActive |
|--------|-----------|-----------|---------------------|----------|
| `<DEV KID>` | DEV       | `<employee id>`   | `<developer>`     | 1 |
| `<RTM KID 1>` | RTM       | NULL      | `<RTM>`           | 1 |
| `<RTM KID 2>` | RTM       | NULL      | `<RTM>`           | 1 |
| (empty)| TEAM_LEAD | NULL      | `<team lead>`       | 0 |
| (empty)| TEAM_LEAD | NULL      | `<team lead>`       | 0 |
| (empty)| TEAM_LEAD | NULL      | `<team lead>`       | 0 |
| (empty)| TEAM_LEAD | NULL      | `<team lead>`       | 0 |
| (empty)| TEAM_LEAD | NULL      | `<team lead>`       | 0 |
| (empty)| TEAM_LEAD | NULL      | `<team lead>`       | 0 |

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

- `false` (current) → this middleware is a no-op; independent proxy, roster and
  Swagger gates still apply. The secure-copy proxy fix is not yet deployed.
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

## General RBAC rollout (historical checks; separate from kiosk profiles)

The current remaining kiosk/Pulse order is in
[LIVE_DEPLOY_RUNBOOK.md](LIVE_DEPLOY_RUNBOOK.md): Pulse proxy build/service token,
supervisor accounts/recovery, then dan2/dan3. The checks below describe the older
general-RBAC rollout, not authorization to run live operations now.

1. **(done)** `AppUserRoles` created + seeded, verified by real query
   (`PS1_Seed_AppUserRoles.ps1`).
2. **(done)** Backend smoke test on a spare port with `Auth__EnforceAuthorization=true`:
   24/24 checks pass (`PS1_AuthSmokeTest.ps1 -BaseUrl http://localhost:5050`).
3. **(next, deploy)** Deploy normally; log in with the developer account over BOTH
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
-- Revives the existing DEV account WITHOUT reproducing its KID or display name here.
-- If no DEV row exists, insert one with the operator's own KID, employee id and name.
IF EXISTS (SELECT 1 FROM AppUserRoles WHERE Role = 'DEV')
    UPDATE AppUserRoles SET IsActive = 1, UpdatedAt = SYSUTCDATETIME()
    WHERE Role = 'DEV';
ELSE
    INSERT INTO AppUserRoles (Kid, Role, EmployeeId, DisplayName, IsActive)
    VALUES ('<DEV KID>', 'DEV', '<employee id>', '<developer>', 1);
```

If enforcement itself is the problem, set `"Auth": { "EnforceAuthorization": false }`
in `bin\Release\net8.0\appsettings.json` and restart in an authorized window.
This disables general RBAC only, not independent proxy/roster/Swagger gates.
The historical KID recovery SQL above is not sufficient to bypass
`SupervisorMode=enforce`; use the supervisor/break-glass recovery procedure.

## Scripts

| Script | Purpose |
|--------|---------|
| `PS1_Seed_AppUserRoles.ps1` | Idempotent create + seed + verification queries |
| `PS1_AuthSmokeTest.ps1 -BaseUrl <url>` | 24 end-to-end auth/RBAC checks against a running backend |
| `PS1_ListAgentsWithoutKid.ps1 [-CsvOut file]` | The 63 active agents with no KID (id, name, team lead) |

## Notes / known limitations

- The old deployed frontend calls ShiftKiosk attendance directly. The prepared,
  not-yet-deployed frontend instead calls Pulse's authenticated attendance proxy,
  which uses a service device token. Deploy that build/config before dan2.
  Kiosk access is a separate surface: dan1 closes session-token status writes,
  not anonymous session-token history/audit/export reads (open until dan3).
  The old as-is inventory is historical, not today's deployment state. See
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
| `<DEV KID>` | DEV | `<developer>` |
| `<RTM KID 1>` | RTM | `<RTM>` |
| `<RTM KID 2>` | RTM | `<RTM>` |
| `<TEAM_LEAD KID 1>` | TEAM_LEAD | `<team lead>` |
| `<TEAM_LEAD KID 2>` | TEAM_LEAD | `<team lead>` |
| `<TEAM_LEAD KID 3>` | TEAM_LEAD | `<team lead>` |
| `<TEAM_LEAD KID 4>` | TEAM_LEAD | `<team lead>` |
| `<TEAM_LEAD KID 5>` | TEAM_LEAD | `<team lead>` |
| `<TEAM_LEAD KID 6>` | TEAM_LEAD | `<team lead>` |

**The real KIDs and names are not reproduced in this document set.** They are personal data. The list
above preserves the shape (one developer, two RTM, six team leads); the concrete values live in the
live `AppUserRoles` table and in the seeding script.

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

Still `false` in both `appsettings.json` files, so the general `RbacMiddleware`
is a no-op. `GET /api/kiosk/attendance` has an independent, always-on gate in
`Backend/Modules/Auth/KioskEndpoints.cs`: anonymous callers get JSON **401**,
and authenticated callers without DEV, RTM or TEAM_LEAD get JSON **403**.
Only these three roles may receive the full kiosk feed, even with the flag off.
This gate does not change `SupervisorMode` or the general RBAC rollout.
**Close the identifier
leak (Pulse preparation and dan2 in the current runbook) before switching it on**, or it gives false assurance:
an anonymous reader of the kiosk server could obtain a valid AGENT credential.

Decision (2026-10-03): finish the pending `Program.cs` authentication change and
extract the production endpoint mapper so it can be tested without running
startup migrations. Preserve the existing deployment-note/runbook edits.
No live deployment, restart, SQL, or kiosk profile change is part of this fix.

One focused HTTP regression test uses real `gsd_auth` cookies, the production
endpoint mapper and `RbacMiddleware` on a **loopback port**, with an in-memory
kiosk stub and ephemeral cookie keys. It verifies anonymous 401 (no proxy call),
all three supervisor roles 200 with unchanged JSON and query forwarding, and
AGENT/missing/unknown roles 403, with the RBAC flag both false and true.
It does not execute `Program.cs`, connect to any database or contact a kiosk.
If port 5056 is occupied, the test fails rather than replacing an existing server.

```powershell
dotnet test C:\GSDDashboard_secure\Backend.Tests\GSDDashboard.Tests.csproj --no-restore --filter FullyQualifiedName~KioskProxyAccessTests --verbosity minimal
```

Verified on 2026-10-03: **1 passed, 0 failed, 0 skipped**. Backend and test
assemblies built successfully in the secure copy; no full-suite run or deployment.

## What is NOT implemented

**Pulse-wide Team-Lead data scoping is not established by the proxy role gate.**
Kiosk supervisor Team-Lead scoping is implemented; do not conflate that with
Pulse-wide filtering or claim the role gate filters attendance by team.

## Recorded live state - 2026-10-03

Operator-supplied facts, not rechecked by this documentation update: live kiosk
is dan1 (dan0/dan1 and migrations 001/002 applied); legacy kiosks still work.
Anonymous session tokens still read history/audit-log/exports until dan3.
The Pulse proxy gate described above is in the secure copy and **not deployed**.
Required remaining order: Pulse build/proxy/service token, supervisor accounts
and recovery, then dan2/dan3. Database backups:
`C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\`; code:
`C:\ShiftKiosk_backup_20261002_221207` for code. Client evidence, installation
defects and untested BAT additions are recorded in
`C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md`.

See [LIVE_DEPLOY_RUNBOOK.md](LIVE_DEPLOY_RUNBOOK.md) and
[KIOSK_HARDENING_DEPLOY_NOTE.md](KIOSK_HARDENING_DEPLOY_NOTE.md).

Prepared proxy response correction (2026-10-03): valid feeds including virtual
support are passed through; missing identifier fields/invalid JSON return 502,
network outage 503 and timeout 504 instead of 200 []. Request cancellation is
propagated. `KioskProxyResponseTests` is one new focused passing test using an
HTTP handler stub (no database or actual connection); the previously proven
authentication test was not rerun. This does not establish the cause of the
observed live Pulse CHECKED IN 0 versus kiosk Im Dienst 1.
