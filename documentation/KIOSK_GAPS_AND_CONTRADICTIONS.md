# ShiftKiosk — Gaps and Contradictions (documentation vs. code)

> **HISTORICAL AUDIT, not current deployment state or unresolved findings list.**
> Some cited documentation has since been corrected; retain this as evidence.
> Canonical current state: [KIOSK_HARDENING_DEPLOY_NOTE.md](KIOSK_HARDENING_DEPLOY_NOTE.md).
> Client evidence/install: `C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md`.

Companion to [KIOSK_AUTH_IDENTITY_ASIS.md](KIOSK_AUTH_IDENTITY_ASIS.md),
[KIOSK_ENDPOINT_ACCESS_MATRIX.md](KIOSK_ENDPOINT_ACCESS_MATRIX.md),
[KIOSK_DATA_EXPOSURE_INVENTORY.md](KIOSK_DATA_EXPOSURE_INVENTORY.md),
[KIOSK_DEPLOYMENT.md](KIOSK_DEPLOYMENT.md) and
[KIOSK_AUDIT_LOG_SEMANTICS.md](KIOSK_AUDIT_LOG_SEMANTICS.md).

Where documentation and code disagree, **the code describes actual behaviour**. No existing
document was overwritten; both versions are recorded below.

---

## Part 1 — Contradictions

### C1 — Kiosks are described as unbuilt QR/card terminals; they are deployed laptop apps

| | |
|---|---|
| **Docs say** | "Kiosk terminals at WIC locations (Zone B) check in agents via QR code or card scan" (`documentation/PROJECT_BLUEPRINT.md:292`); "Zone B kiosk hardware is not yet deployed. The attendance page UI is complete but actual live data is absent until the terminals are installed" (`:298`); "the physical kiosk terminals for Zone B WIC locations have not been deployed … `GET /api/attendance` from the kiosk will return empty or test data only" (`:450`) |
| **Code says** | A tkinter desktop app on the agent's own laptop, identity from `os.getlogin()` (`main_kiosk.py:302`), autostarted at logon by scheduled task `ShiftKiosk` (`install_kiosk.bat:137-141`). **No QR or card-scan code path exists** anywhere in `main_kiosk.py`. A full attended installer, per-laptop install instructions and a rollback procedure exist (`DEPLOY_AGENT_LAPTOPS.md`). |
| **Reality** | ~60 installed and in use (system owner). |
| **Verdict** | **Code is correct; `PROJECT_BLUEPRINT.md` § 8.1 is stale.** This is the most consequential contradiction in the set: a reader planning security work from `PROJECT_BLUEPRINT.md` alone would conclude there is nothing deployed to secure. |

### C2 — Auto-logout is documented as 9 hours; the code uses 11

| | |
|---|---|
| **Docs say** | "9-hour auto-logout countdown timer" / "Starts a 9-hour daemon `threading.Timer`" / "9-hour auto-logout with fullscreen countdown" — `ShiftKiosk/CODE_OVERVIEW.md` § `kiosk/main_kiosk.py` (`_start_auto_timer`, `Phase1Kiosk`, `Phase2Widget`) |
| **Code says** | `AUTO_LOGOUT_SECONDS = 11 * 3600` (`main_kiosk.py:784`), and the in-code comment at `main_kiosk.py:796` itself says "the 11h AUTO_LOGOUT timer" |
| **Verdict** | **Code is correct (11 h); `CODE_OVERVIEW.md` is stale.** Affects any `AUTO_LOGOUT`-based reporting, e.g. `/api/logout-audit`. |

### C3 — The "API key / token" distinction in the route table does not exist

| | |
|---|---|
| **Docs say** | `ShiftKiosk/CODE_OVERVIEW.md` § "API routes registered" marks some routes "API key" and others "API key / token", implying two protection levels |
| **Code says** | One function, `verify_api_key` (`server.py:955-962`), accepts the header **and** the `token` query parameter **and** a session token, for all 32 protected routes identically |
| **Verdict** | **Code is correct; the distinction is fictional.** Dangerous because it suggests the "API key"-only routes are harder to reach than they are. |

### C4 — "The kiosk integration is unaffected … no machine-to-machine traffic hits this backend"

| | |
|---|---|
| **Docs say** | `documentation/auth_rbac.md` § "Notes / known limitations": "The kiosk integration is unaffected: `WicAttendance.tsx` calls the ShiftKiosk FastAPI server (port 8000) directly from the browser — no machine-to-machine traffic hits this backend, so no service-token exemption was needed." |
| **Code says** | Accurate **for port 5000's own RBAC**, and confirmed by `BLUEPRINT_SCREENS.md:163`. But traffic *does* flow the other way: `GET /api/today-holiday` on the kiosk server performs a server-side fetch of `http://localhost:5000/api/publicholidays?year=…` (`server.py:1325-1329`). |
| **Verdict** | The sentence is **true as scoped but incomplete**, and it has been read as "the kiosk is out of scope for authentication work". It is not: the kiosk server is a second, unauthenticated front door to the same workforce data. `auth_rbac.md` documents the port-5000 RBAC correctly and nothing in it is wrong; it simply does not cover port 8000. |

### C5 — `[Security]` config section implies controls that are not wired up

| | |
|---|---|
| **Docs say** | `ShiftKiosk/CODE_OVERVIEW.md` § `kiosk/config.ini`: "Sections: `[Company]`, `[Server]`, `[Security]`. Read once at startup by `configparser`." |
| **Code says** | `main_kiosk.py` reads only `[Company] name`, `[Server] url`, `[Server] api_key` (`main_kiosk.py:299-301`) and `[settings] language` (`main_kiosk.py:242`). **Neither `[Security]` key is ever read.** `supervisor_exit_shortcut` is hardcoded at `main_kiosk.py:1102-1103` and `1392-1393`; `require_confirmation` has no read site. |
| **Verdict** | **Code is correct; `[Security]` is dead configuration.** Changing it provides a false sense of control. |

### C6 — The documented purpose of the `performed_by` validator is not met

| | |
|---|---|
| **Docs say** | In-code comment, `server.py:971-973`: "GRC fix (2026-09-15): manual actions must not be recorded under the dashboard's default placeholder 'Supervisor' — a real name is required so manual actions are attributable in the audit log." |
| **Code says** | The validator rejects only empty and the literal `"supervisor"` (`server.py:974-983`). Any other string is accepted and stored verbatim. The value is never checked against any list of people. |
| **Verdict** | The fix removes a placeholder; it **does not produce attributability**, which the comment claims. See [KIOSK_AUDIT_LOG_SEMANTICS.md](KIOSK_AUDIT_LOG_SEMANTICS.md) §2.2. |

### C7 — `/api/session` is documented as a hardening measure without its caveat

| | |
|---|---|
| **Docs say** | `ShiftKiosk/README.md:229`: "The dashboard obtains a short-lived session token on page load from `GET /api/session`. All subsequent API calls use this token — the master API key is never sent to the browser. Tokens are HMAC-SHA256-signed with the master key and expire after 8 hours." |
| **Code says** | All true. What the document omits: `GET /api/session` itself requires **no authentication** (`server.py:1011`), and the token it returns is accepted by `verify_api_key` wherever the master key is (`server.py:960`). |
| **Verdict** | Not a false statement, but the omission inverts the security conclusion a reader draws: the measure that keeps the key out of the browser also makes the key unnecessary. **This is the most important single finding in the documentation set.** |

### C8 — `SECURITY_FINDINGS.md` never covered the kiosk, and two of its findings are stale

| | |
|---|---|
| **Docs say** | `documentation/SECURITY_FINDINGS.md` (dated 2026-07-06) is the project's only prior security document. Its scope line reads "C:\GSDDashboard (ASP.NET Core 8 + React 19, internal network only)". Finding 1 reports unauthenticated write endpoints; Finding 2 reports Swagger exposed with no auth gate. |
| **Code says** | Both have since been addressed on port 5000: `RbacMiddleware` and the four-role model exist (`auth_rbac.md` § Enforcement), and `/swagger` is gated to the DEV role and always enforced (`auth_rbac.md` § Notes). Findings 3–5 were not re-verified in this review. |
| **Gap** | `SECURITY_FINDINGS.md` contains **nothing about the ShiftKiosk server on port 8000** — not the shared key, not the open endpoints, not client-supplied identity. A reader treating it as the security baseline would conclude the kiosk had been assessed. It had not. |
| **Verdict** | Not a contradiction with code, but a **scope gap in the only prior security document**. Findings 1 and 2 should be marked resolved with a pointer to `auth_rbac.md`; the kiosk is covered by the documents listed at the top of this file. |

---

## Part 2 — Gaps in existing documentation (now closed here)

| Gap | Where it should have been | Closed in |
|---|---|---|
| 18 of 41 routes absent from the route table | `ShiftKiosk/CODE_OVERVIEW.md` | [KIOSK_ENDPOINT_ACCESS_MATRIX.md](KIOSK_ENDPOINT_ACCESS_MATRIX.md) §3 |
| `/docs`, `/redoc`, `/openapi.json` are open | nowhere | matrix §1 item 10 |
| `/api/attendance`, `/api/agents`, `/api/teams`, `/ws` return everyone's data with no credential | nowhere | matrix §1; inventory §2.1 |
| Identity is client-supplied on every endpoint | nowhere | [KIOSK_AUTH_IDENTITY_ASIS.md](KIOSK_AUTH_IDENTITY_ASIS.md) §4 |
| Unknown usernames are accepted and written | nowhere | auth §4.2 |
| Every agent can read the shared key via the mandatory ACL grant | nowhere | [KIOSK_DEPLOYMENT.md](KIOSK_DEPLOYMENT.md) §4.1 |
| Kiosk disables TLS verification | nowhere | deployment §8 |
| `client_version` and caller IP are accepted but discarded | nowhere | auth §4.4 |
| No read/export/auth auditing | nowhere | [KIOSK_AUDIT_LOG_SEMANTICS.md](KIOSK_AUDIT_LOG_SEMANTICS.md) §3 |
| No rate limiting | nowhere | auth §1 |
| Supervisor exit sends no status event | nowhere | deployment §5 |
| No retention or deletion for `status_log` / `manual_actions` | nowhere | inventory §3 |
| No update mechanism; updates are manual installer re-runs | partly in `DEPLOY_AGENT_LAPTOPS.md` | deployment §6 |
| No inventory of installed kiosks (B9 table empty) | `DEPLOY_AGENT_LAPTOPS.md:482-495` | deployment §7 |

---

## Part 3 — Still NOT FOUND after searching

Short list, with the reason each cannot be resolved by reading the repository.

| # | Item | Why it cannot be determined from source |
|---|---|---|
| N1 | Authoritative `CREATE TABLE` for `agents`, `status_log`, `manual_actions` | Not in the repository. `db_init()` only *adds* columns to tables it assumes exist (`server.py:501-562`); `C:\ShiftKiosk\db\` holds one view (`vw_shift_summary.sql`). The tables were created out-of-band. The column lists in [KIOSK_DATA_EXPOSURE_INVENTORY.md](KIOSK_DATA_EXPOSURE_INVENTORY.md) §1 are reconstructed from INSERT/ALTER statements and are a **lower bound** — `SELECT *` endpoints would also return any column not referenced in code. Resolvable only by querying `INFORMATION_SCHEMA` on the live database. |
| N2 | Definitions of `vw_current_status` and `vw_shift_summary` | `vw_shift_summary.sql` exists in `db/views/`; `vw_current_status` is read at `server.py:118`, `131` but has no definition in the repository. Exact columns returned by `/api/stats` and `/api/summary` therefore cannot be fully enumerated statically. |
| N3 | Whether a standard agent can **write** to `C:\ShiftKiosk` | The installer grants `Users` **RX** only (`install_kiosk.bat:116`) and sets no write ACE, but inherited permissions on `C:\` are not visible in source. Requires inspecting the live ACL on an installed laptop. Note this does **not** affect the impersonation finding, which needs no write access ([KIOSK_DEPLOYMENT.md](KIOSK_DEPLOYMENT.md) §5). |
| N4 | How many kiosks are installed, and which hostnames/builds | No inventory exists (deployment §7); `client_version` is discarded (auth §4.4). Determinable only by querying `DISTINCT agent_hostname` from `status_log`, which is observational and attacker-controlled. |
| N5 | Whether the dashboard prompts a human for `performed_by` | The dashboard's JavaScript is embedded in the `DASHBOARD_HTML` string (`server.py`, `DASHBOARD_HTML`, ~2000 lines ending at `server.py:3633`). Out of scope for this review, which covered authentication, identity, authorization and kiosk deployment. Does not affect any finding: the server trusts the value regardless of how the client obtained it. |
| N6 | Who currently holds the shared key | The key is in git history (`README.md:233`), in every install package, and on ~60 laptops readable by their users. The holder set is unbounded by construction and cannot be enumerated. |
| N7 | Whether the devtunnel is reachable from outside the corporate network | Tunnel configuration is explicitly out of scope for this task and was not inspected. This determines whether the open endpoints are internet-exposed or LAN-exposed, which changes the severity of every NONE-auth finding but none of the facts. |

Items N1, N2 and N4 could be closed by three read-only SQL queries against the live
database. Nothing was executed for this document, per the constraints of the task.

---

## Part 4 — NOT FOUND items CLOSED on 2026-10-02

Resolved read-only against **ShiftKioskTest** and **GSDDashboard** (the latter
with `SELECT` only). The schema items were a lower bound before; these are the
real column lists.

| Was | Now |
|---|---|
| **N1** DDL for `agents` / `status_log` / `manual_actions` | **CLOSED.** `agents` has **17** columns: `id, first_name, last_name, full_name, team_leader, employee_id, windows_username, location, engagement, active, created_at, updated_at, primary_kid, secondary_kid, infosys_email, eon_email, wic_location`. `status_log` has **17**, including a `session_id uniqueidentifier` the code never writes — that is the "session IDs" the tester read. `manual_actions` has **6**. |
| **N3** do the KID columns hold data? | **CLOSED — yes, and it matters.** Of 123 active agents: 122 have `employee_id`, **115 have `primary_kid`**, 115 `secondary_kid`, 61 `infosys_email`, 61 `eon_email`. `SELECT * FROM agents` on the unauthenticated `/api/agents` published all of them. |
| **N4** is a privileged KID exposed through the kiosk? | **CLOSED — no.** A cross-database join of `ShiftKioskTest.dbo.agents` against `GSDDashboard.dbo.AppUserRoles` on `primary_kid` / `secondary_kid` / `employee_id` returns **zero rows**. The anonymous leak granted **AGENT-level** Pulse access, not DEV or RTM. The exposure is real and bounded. |
| **N5** indexes on `status_log` | **CLOSED.** `PK(id)` clustered, `idx_ts(agent_timestamp)`, `idx_username(agent_username)`. The new observation writer relies on its own unique index, and the concurrent-session check is an index seek on `idx_username`. |
| **N7** rows with `agent_id IS NULL` | Still open, but no longer blocking: the `unknown_username` anomaly now records them as they happen. |

### New contradiction found — C9

| | |
|---|---|
| **Docs say** | `auth_rbac.md` § "Where roles live" lists the six `TEAM_LEAD` rows as placeholders with `Kid = ''` and `IsActive = 0`, which "can never authenticate". |
| **Database says** | `AppUserRoles` now holds **9 active rows with real KIDs**: `S69307` DEV, `S60028` + `Y6525` RTM, and all six team leads (`T31561`, `M73784`, `O7164`, `K33261`, `I15078`, `J51904`) active. |
| **Verdict** | **The database is correct; `auth_rbac.md` is stale.** It matters operationally: the deploy note creates one supervisor account per real KID, so the count is 9, not 3. |

### New finding — the schema script targets production regardless of `-d`

`Backend/schema.sql` line 16 is `USE GSDDashboard;`. Any `sqlcmd -d <other-db> -i schema.sql`
silently runs against the **live** database. The script is non-destructive
(CREATE and one guarded INSERT only, no DROP/DELETE/UPDATE/ALTER), so the blast
radius is limited to "statements fail because the objects already exist", but the
footgun is real. A retargeted copy for test use is committed as
`tests/pulse_test_schema.sql`.
