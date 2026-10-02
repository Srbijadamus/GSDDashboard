# ShiftKiosk — Security Migration Proposal

> **STATUS 2026-10-02 — a hardening branch now exists.** This document describes the
> system AS IT RUNS TODAY, which stays accurate until the branch is deployed. What
> changes, in what order, with rollback per item, is in
> [KIOSK_HARDENING_DEPLOY_NOTE.md](KIOSK_HARDENING_DEPLOY_NOTE.md).
> Branch `security/kiosk-hardening` in `C:\ShiftKiosk_secure` + `C:\GSDDashboard_secure`.

Status: **proposal only.** No code, configuration, installer or script has been changed.
Date: 2026-10-02.

Built exclusively on the as-is documentation produced alongside it:
[KIOSK_AUTH_IDENTITY_ASIS.md](KIOSK_AUTH_IDENTITY_ASIS.md) ·
[KIOSK_ENDPOINT_ACCESS_MATRIX.md](KIOSK_ENDPOINT_ACCESS_MATRIX.md) ·
[KIOSK_DATA_EXPOSURE_INVENTORY.md](KIOSK_DATA_EXPOSURE_INVENTORY.md) ·
[KIOSK_DEPLOYMENT.md](KIOSK_DEPLOYMENT.md) ·
[KIOSK_AUDIT_LOG_SEMANTICS.md](KIOSK_AUDIT_LOG_SEMANTICS.md) ·
[KIOSK_GAPS_AND_CONTRADICTIONS.md](KIOSK_GAPS_AND_CONTRADICTIONS.md)

**Constraints taken as given:** ~60 kiosks installed and in use, ~70 more to install; all
carry the same shared key; they can only be replaced gradually; the server runs on the
owner's own machine. The shared key therefore cannot be rotated or removed on day one, and
the plan must tolerate a long period with old and new kiosks side by side.

---

## 1. Threat model

### 1.1 The governing fact

> **Any secret stored on a kiosk can be extracted by the person using that kiosk.**

This is not a theoretical caveat here — it is enforced by the installer.
`KIOSK_PACKAGE/install_kiosk.bat:115-118` grants `BUILTIN\Users` Read+Execute recursively
over `C:\ShiftKiosk` and **aborts the install if the grant fails**, because the kiosk runs
as the logged-on agent (`install_kiosk.bat:137-141`, group principal `S-1-5-32-545`). The
shared key lives in `C:\ShiftKiosk\kiosk\config.ini` in plaintext (`main_kiosk.py:301`).

**Therefore: identity must not rest on a secret the kiosk user can read.** Every option in
§2 is judged first against this sentence. A per-device key, a signing key, or a rotated
shared key all fail it — they change *who else* can impersonate, never *whether the agent
can impersonate themselves or a colleague*.

### 1.2 Attackers and what each can do today

| Attacker | Capability today | Evidence |
|---|---|---|
| **Anyone who can reach the server** (no credential at all) | Read the full live attendance of every agent incl. `SICK_LEAVE` and free-text notes; read the whole `agents` table incl. names, usernames, employee ids, KIDs and both e-mail addresses; subscribe to `/ws` and receive every status event live; download `/openapi.json`. **And: call `GET /api/session` to mint an 8-hour token that satisfies every "protected" endpoint** | matrix §1; auth §2.1 |
| **Anyone with the shared key** | Everything above, plus: full `status_log` history and all exports; the complete `manual_actions` audit log; **post a status as any agent**; **manual check-in/logout of any agent attributed to any name**; acknowledge emergencies; **bulk-overwrite the `agents` table** via `/api/import-agents`; create/update/delete WIC centres | matrix §2; auth §4.3 |
| **Anyone with a kiosk** (i.e. all ~60 agents) | Everything in both rows above, because they can read the key from `config.ini`. Additionally: exit the kiosk silently with `Ctrl+Shift+Alt+A`, leaving no `OFF`/`AUTO_LOGOUT` record | deployment §4.1, §5 |
| **Anyone with the OpenAPI dump** | The complete endpoint map and payload shapes, enabling all of the above without guesswork | matrix §1 item 10 |
| **Anyone on the network path** | Harvest the key: kiosks send it with `verify=False` on every request, so a host that wins routing or name resolution for the tunnel hostname receives it without a certificate warning | deployment §8 |
| **Anyone who has ever cloned the repository** | Holds the key permanently — it is in three historical commits | auth §6.2 |

Note the ranking: the *unauthenticated* row is the most important one. The security test
used the shared key, but §2.1 of the auth document shows the key was never required.

### 1.3 What nobody can currently detect

Reads, exports, authentication attempts, session-token issuance, imports and WIC-centre
changes are **not logged at all**; the caller IP is always empty; `client_version` is
discarded (audit §3, auth §4.4). The tester's bulk download left no server-side trace, and
neither would a real exfiltration. Any claim about "who did what" rests on
client-supplied strings (audit §5).

### 1.4 Capability after each phase

| Capability | Today | After Phase 0 | After Phase 1 | After Phase 2 | After Phase 3 |
|---|---|---|---|---|---|
| Anonymous read of everyone's attendance | ✔ | ✘ | ✘ | ✘ | ✘ |
| Anonymous token minting (`/api/session`) | ✔ | ✘ | ✘ | ✘ | ✘ |
| Key-holder reads full history / audit / exports | ✔ | ✘ | ✘ | ✘ | ✘ |
| Key-holder rewrites `agents` (import) | ✔ | ✘ | ✘ | ✘ | ✘ |
| Key-holder performs manual check-in/logout | ✔ | ✘ | ✘ | ✘ | ✘ |
| **Key-holder posts a status as another agent** | ✔ | ✔ | ✔ (legacy path only) | ✔ (legacy path only) | ✘ |
| Agent reads a colleague's shift state | ✔ | ✔ | ✔ (legacy path only) | ✔ (legacy path only) | ✘ |
| Forged actor name in the audit log | ✔ | ✔ | ✔ (legacy path only) | ✘ for admin actions | ✘ |

The one capability Phase 0 cannot remove is impersonation on the kiosk endpoints, because
removing it requires an authenticated subject. That is Phase 1's entire purpose, and it is
why the residual-risk list in §4 exists.

---

## 2. Options for trustworthy identity

Judged against §1.1. "Impact on installed kiosks" assumes the documented reality that
**there is no update mechanism — every kiosk change is an administrator visiting that
laptop and re-running the installer** (deployment §6).

> **Framing consequence:** since no option can be pushed remotely, *every* option costs
> ~60 laptop visits for Phase 1 and ~70 more at install time. Kiosk impact therefore does
> **not** discriminate between the options, and the choice must be made on security merit
> and on whether it works over the existing transport.

| Option | Protects against | Does **not** protect against | Effort | Impact on installed kiosks |
|---|---|---|---|---|
| **A — Windows Integrated Authentication** (Negotiate/Kerberos; server behind IIS or HTTP.sys, identity from the logged-on Windows session) | Everything in §1.1: the credential is a Kerberos ticket held by the OS, not a file the agent can read. Impersonation of a colleague becomes impossible without their Windows credentials. Gives a server-derived actor for the audit log. | A user acting under their *own* account (insider misuse, correctly attributed); a compromised Windows account | **Medium** server-side, **large** transport-side | All must be re-pointed and re-installed. **Blocker:** kiosks currently reach the server at the devtunnel URL (`kiosk/config.ini`), and uvicorn runs plain HTTP with no TLS (`server.py:3642`). Negotiate cannot work through the devtunnel relay — it needs a direct, SPN-resolvable internal host. Adopting A therefore also requires moving kiosks onto an internal URL and standing up a reverse proxy. |
| **B — Entra ID via MSAL (Integrated Windows Auth / Windows broker, silent)** ← **RECOMMENDED** | Same as A: the token is obtained at runtime from the OS broker / tenant, never stored in a readable file. Server validates a JWT and derives identity from the `upn`/`oid` claim. Yields real user identity **plus group/role claims**, which Phase 2 needs anyway. | A user acting under their own account; a stolen refresh token on a fully compromised laptop; tenant misconfiguration | **Medium–large** | All must be re-installed — but **no transport change**: JWT bearer works over the existing devtunnel exactly as the current header does. |
| **C — Per-device credentials / certificates provisioned at install** | Narrows the blast radius: a leaked credential implicates one laptop, and the legacy key can be retired per device. Makes device-level revocation possible. | **Fails §1.1.** A file-resident per-device secret is still readable by that device's user (deployment §4.1), and the *username is still self-asserted* — the agent can post any colleague's name using their own device credential. Does not produce trustworthy identity. | Medium | All must be re-installed |
| **D — Signed requests bound to device and user** (HMAC over the body with a device key + OS-reported username) | Integrity and replay protection in transit; detects body tampering by a network attacker | **Fails §1.1 for the same reason as C**, and more subtly: the signing key is readable, so the agent can sign *any* username. Adds cryptographic ceremony around an unchanged trust assumption. | Medium | All must be re-installed |
| **E — Reuse what the system already has: the port-5000 KID login + RBAC** | For **human** users this is a real, working control: KID login, `gsd_auth` HttpOnly cookie, four roles, `RbacMiddleware`, startup lockout guards, a tested smoke suite (`auth_rbac.md`) | Nothing for **kiosks** — it is an interactive browser login; an unattended tkinter app at logon cannot use it. Also still flag-gated (`Auth:EnforceAuthorization` = `false`, `auth_rbac.md` § Rollout) | **Small** (already built) | None |

### 2.1 Recommendation

**Adopt Option B — Entra ID via MSAL with Integrated Windows Auth — as the single identity
mechanism for kiosk→server traffic, and reuse Option E (the existing port-5000 KID login
and RBAC) for human admin and supervisor functions in Phase 2.**

Rationale, each point sourced:

1. **It satisfies §1.1**, which C and D do not. The credential is minted at runtime from the
   Windows session via the broker; nothing bearer-shaped sits in a file the agent can read.
2. **It needs no transport re-architecture**, which is the one thing that makes A
   impractical today: kiosks talk to a devtunnel URL (`kiosk/config.ini`) and the app
   serves plain HTTP (`server.py:3642`). A JWT in an `Authorization` header traverses that
   path unchanged; Negotiate does not.
3. **A Microsoft 365 / Entra tenant demonstrably exists.** The server already sends mail
   through `smtp.office365.com` (`server.py:42`, `server_config.ini` `[Email]`) and posts
   Adaptive Cards to a Teams webhook (`server.py:35`, `server.py:859`). ASSUMPTION — that
   the same tenant can issue app registrations for this use; not verifiable from source.
4. **It delivers the roles Phase 2 needs** as token claims, rather than requiring a second
   authorization system to be invented for the kiosk server.
5. **Kiosk impact is not a differentiator** (§2 framing), so B's main cost is shared by
   every alternative.

**Non-negotiable design rule for all phases:**

> The server derives identity from authentication. `agent_username`, `agent_hostname`,
> `performed_by` and `acked_by` must stop being inputs and become **server-populated
> fields**. On the new path they are ignored if present in the body.

**Documented contingency (not a co-recommendation):** if a decision is taken to move
kiosks onto an internal, Negotiate-capable path for other reasons, Option A becomes
preferable on merit and should replace B. Decision gate: *can every kiosk reach a
domain-resolvable internal host for the server?* If no — B. This is the only open
architectural question in the plan.

---

## 3. Phased plan

### Phase 0 — Server-only hardening that cannot break a kiosk

The entire phase is server-side. Correctness rests on one verified fact: **the kiosk uses
only six endpoints and sends the key only as a header** — `/health`, `/api/status`,
`/api/agent-lookup`, `/api/shift-status`, `/api/agent-location-options`,
`/api/today-holiday` (`main_kiosk.py:389, 400, 417, 451, 706, 821`). Anything else can be
restricted without touching a laptop.

| # | Change | Breaks old kiosks? | Breaks anything else? |
|---|---|---|---|
| 0.1 | **Require authentication for `GET /api/session`.** Today it mints an 8 h token to anyone (`server.py:1011`) and that token is accepted everywhere the master key is (`server.py:960`) — the single highest-value fix. | **No** — kiosks never call it (`main_kiosk.py` has no `/api/session` call site) | **Yes — `/dashboard` breaks** until 0.2 is in place. The dashboard fetches it on load (`server.py:2791, 3595, 3621`). Do 0.1 and 0.2 together. |
| 0.2 | **Put `/dashboard` behind a human login.** Reuse the port-5000 KID session (Option E) or place both behind a reverse-proxy auth. Required because `/dashboard` is open (`server.py:3635`) and its JS performs *every* admin write with a session token — import, manual actions, WIC-centre CRUD (`server.py:2957, 2984, 3318`). | **No** | Changes how supervisors reach the dashboard — the main user-visible change in Phase 0 |
| 0.3 | **Split the legacy key's scope to the six kiosk endpoints only.** Introduce a second dependency (e.g. `verify_admin`) for everything else, accepting only an authenticated session from 0.2. Removes key access to `/api/history`, `/api/audit-log`, all exports, `/api/import-agents`, `/api/wic-centers` writes, and the manual actions. | **No** — none of those endpoints appears in `main_kiosk.py` | No, once 0.2 is done |
| 0.4 | **Protect the open data endpoints** `/api/agents` (`server.py:1052`), `/api/teams` (`:1056`), `/api/attendance` (`:1060`), `/api/agents-template` (`:1639`), and require auth on `/ws` (`:1649`). | **No** | **Yes — two consumers.** (a) the embedded dashboard, fixed by 0.2; (b) **the WorkForce Pulse browser frontend calls kiosk `/api/attendance` directly** (`BLUEPRINT_SCREENS.md:163`, `PROJECT_BLUEPRINT.md:279` `VITE_KIOSK_API_URL`). That frontend must send a credential first, or 0.4 breaks the WIC Attendance page. **Sequence 0.4 after that frontend is updated.** |
| 0.5 | **Disable or protect `/openapi.json`, `/docs`, `/redoc`** — pass `docs_url=None, redoc_url=None, openapi_url=None` to `FastAPI(...)` (`server.py:951`), or gate them like port-5000 gates `/swagger` to DEV only (`auth_rbac.md` § Notes). | **No** — kiosks never fetch them | No |
| 0.6 | **Stop accepting the *master key* in the URL** (`?token=`), while still accepting *session tokens* there. `verify_api_key` currently treats both identically (`server.py:956-960`). | **No** — kiosks are header-only (`main_kiosk.py:389…`) | **Yes if done naively.** The dashboard uses `?token=` for **9 download buttons** via `window.open` (`server.py:2999, 3009, 3016, 3066, 3136, 3144, 3392, 3400, 3560`). Keep query auth for session tokens, reject the master key there. |
| 0.7 | **Reject unknown usernames on `/api/status`.** Today `agent_id` is left `NULL` and the row is written anyway (`server.py:93-107`, `921`). | **No** — a correctly installed kiosk always sends a real `os.getlogin()`. **Caveat:** any agent missing from `agents` would start failing instead of silently producing orphan rows — arguably the desired behaviour, but it changes failure mode. Check for existing `NULL agent_id` rows first. | No |
| 0.8 | **Rate-limit** `/api/session`, `/api/status`, `/api/agent-lookup` (none exists today — `server.py:953` is the only middleware). | **No**, if limits are set well above one kiosk's rate: ~6 calls at startup plus one per status change (deployment §8) | No |
| 0.9 | **Log what is currently invisible**: authentication failures, session-token issuance, reads of `/api/history` and `/api/audit-log`, exports, imports, WIC-centre changes. Record the caller IP — stop passing `""` into `db_insert_status` (`server.py:1021`) and persist `client_version` (accepted at `server.py:966`, discarded at `server.py:98-107`). | **No** | No. **Prerequisite for Phase 3** — without `client_version` there is no way to see which kiosks are still legacy. |
| 0.10 | **Enable certificate verification** on the kiosk side (`verify=False` → `True`, `main_kiosk.py:389, 400, 417, 451, 706, 821`). | **Not applicable — this is a kiosk change, so it is Phase 1 work**, listed here only because it is the one client-side fix that is independent of the auth redesign. | — |
| 0.11 | *Evaluated and rejected for Phase 0:* **stop returning other users' data to a kiosk.** Cannot be done: scoping a response to the caller requires an authenticated subject, and the credential carries none (inventory §4). Deferred to Phase 1. | — | — |

**Risk:** moderate and concentrated in 0.2/0.4 — both change how existing *dashboards*
work, not kiosks. 0.1 without 0.2 takes the supervisor dashboard down.
**Rollback:** every item is a server-side edit to `server.py` plus a service restart
(`ShiftKioskServer` scheduled task). Revert the file, restart. No kiosk, database or
installer state changes, so rollback is clean and immediate.

### Phase 1 — New authentication alongside the old (dual mode)

| Aspect | Detail |
|---|---|
| **Server changes** | Add a second authentication dependency that validates an Entra ID JWT (signature, `aud`, `iss`, `exp`) and extracts the caller's UPN. Map UPN → `agents` row (`windows_username`, or better `infosys_email`/`eon_email`, which already exist — `server.py:504-508`). On the new path, **ignore** `agent_username`/`agent_hostname` in the body and populate them from the validated token. Accept either credential on the six kiosk endpoints; record which path was used on every row. |
| **Kiosk changes** | Acquire a token via MSAL silent/IWA at startup and before each post; send `Authorization: Bearer`. Stop reading `api_key` from `config.ini`. Set `verify=True` (0.10). New installer package; **~60 laptop visits**, no remote push (deployment §6). |
| **Scoping** | On the new path, scope `/api/shift-status` and `/api/agent-location-options` to the authenticated caller, closing item 0.11 for new kiosks. |
| **Risk** | Token acquisition failing at logon would stop check-in. Mitigate: on acquisition failure fall back to the legacy key for a defined window, log it loudly, and keep the offline queue (`main_kiosk.py:349-384`) as the existing safety net. |
| **Rollback** | Per laptop: re-run the previous installer package. Server-side: the legacy dependency is still live for every endpoint, so reverting a kiosk needs no server change. |

### Phase 2 — Admin and supervisor functions on real user authentication, separated from kiosks

| Aspect | Detail |
|---|---|
| **Server changes** | Replace the Phase 0 interim gate on `/api/import-agents`, the manual actions, `/api/emergency/ack`, `/api/wic-centers` writes, `/api/history`, `/api/audit-log` and all exports with role checks on an authenticated human identity. Mirror the model already specified in `auth_rbac.md` (`AGENT` / `TEAM_LEAD` / `RTM` / `DEV`) rather than inventing a second scheme. **Derive `performed_by` and `acked_by` from the session and delete them from `ManualActionPayload` / `AckPayload`** (`server.py:969-994`) — this is what finally makes the audit log evidential (audit §2.2). |
| **Kiosk changes** | None. Kiosks never call these endpoints. |
| **Risk** | Low technically; the real risk is lockout, so follow the ordering `auth_rbac.md` § Rollout already proves out: seed roles, smoke-test on a spare port, deploy, only then enforce. Note that port 5000's own enforcement flag is still `false` (`auth_rbac.md` line 3) — flipping it is a prerequisite if Option E is the chosen admin gate. |
| **Rollback** | A single enforcement flag, exactly as port 5000 does it (`auth_rbac.md` § Recovery). |

### Phase 3 — Retire the legacy path

| Aspect | Detail |
|---|---|
| **Criteria to switch off** | (1) Zero requests on the legacy path for a full sustained period (suggest 30 days) across *all six* kiosk endpoints; (2) the count of distinct hostnames on the new path equals the expected installed count; (3) a named owner confirms each remaining hostname is decommissioned. |
| **How to see who is still legacy** | **This does not work today and must be built in Phase 0.9.** Today the server records neither `client_version` (accepted then discarded, `server.py:966` vs `98-107`) nor the caller IP (always `''`, `server.py:1021`), and no auth events are logged at all (audit §3). The only existing signal is `status_log.agent_hostname` (`server.py:100`) — which is client-supplied and therefore unreliable. After 0.9 the query is "distinct `agent_hostname` + `client_version` + auth path, grouped by week". There is also **no install inventory to compare against** — the per-laptop record table in `DEPLOY_AGENT_LAPTOPS.md:482-495` is empty (deployment §7); populating it is a prerequisite for criterion (2). |
| **Server changes** | Delete the legacy dependency and `API_KEY` handling (`server.py:31, 955-962`); remove the session-token-as-API-key equivalence. |
| **Kiosk changes** | None — by definition no legacy kiosk remains. |
| **Risk** | A forgotten kiosk fails silently at check-in. Mitigate by alerting on legacy-path use for the 30 days *before* switch-off, so the set is known to be empty rather than assumed. |
| **Rollback** | Re-add the legacy dependency and restart. Keep the old key available until the window closes; only then rotate it out of existence. |

---

## 4. Residual risk during the legacy period

After Phase 0 is complete and until Phase 3 removes the legacy path, a holder of the
legacy key **can still**:

1. **Post a status event as any agent**, on any date/time they supply, with any
   `work_location` and `wic_center_id`, and any free-text `note` — `/api/status`
   (`server.py:1015-1021`). The event is attributed to the named agent with no marker.
2. **Trigger notifications in another agent's name** — a forged `SICK_LEAVE` or
   `EMERGENCY` sends a Teams card and an e-mail whose subject carries that agent's real
   name and team leader (`server.py:1025-1038`).
3. **Read any agent's current shift state** — `/api/shift-status?username=…`
   (`server.py:1295-1301`).
4. **Enumerate valid usernames** — `/api/agent-lookup` returns 404 for unknown and 200 for
   known (`server.py:1285-1293`).
5. **Read any agent's WIC centres and coverage role** —
   `/api/agent-location-options?agent_id=…` (`server.py:1303-1317`).
6. **Corrupt attendance data at will**, since every write above is indistinguishable from a
   genuine kiosk. Existing rows cannot be re-validated retrospectively.
7. **Suppress their own tracking** — `Ctrl+Shift+Alt+A` exits the kiosk with no status
   event and no password (`main_kiosk.py:1102-1112`), so a shift simply stops being
   recorded.

What Phase 0 *does* remove from the key: all history, audit-log and export reads; the
agent-table import; WIC-centre writes; manual check-in/logout; emergency acknowledgement;
and — via 0.1 — the anonymous token path that made the key optional in the first place.

**Accept explicitly:** attendance data written during the legacy period is **not
trustworthy evidence** about any individual, and should not be used in a performance or
disciplinary context. That limitation ends only at Phase 3.

---

## 5. Data protection checklist

Not legal advice. Items to action, each tied to a documented finding.

### 5.1 Rotate

| Item | Why |
|---|---|
| The shared `api_key` | In git history in three commits (`README.md:233`), on ~60 agent-readable laptops (deployment §4.1), and used in the tester's curl commands. **Rotation is deferred by constraint, not safe** — schedule it at Phase 3 and treat the current value as compromised until then. |
| The HMAC signing key for session tokens | It **is** `API_KEY` (`server.py:53`). Rotating the key invalidates all tokens — expected, 8 h TTL. Consider a separate secret so the two concerns can be rotated independently. |
| `server_config.ini` `[Email] sender_password` | A mailbox password sits in plaintext next to the API key in a file read at startup (`server.py:43`). Not implicated in the test, but in the same blast radius. |
| The Teams webhook URL | A bearer-equivalent capability URL in the same file (`server.py:35`). |

### 5.2 Clean

| Item | Detail |
|---|---|
| **The tester's entries** | The tester signed in as a supervisor and started a shift as him. Expect (a) `status_log` rows with that supervisor's `agent_username` and the **tester's own `agent_hostname`** — the mismatch is the only way to find them; (b) possibly `manual_actions` rows with a `performed_by` the tester typed. Identify by hostname/timestamp, then **annotate rather than silently delete** — there is no deletion audit trail (audit §3), so a quiet delete is itself untraceable. Record the decision and the row ids. |
| Forged notifications | If the test produced `SICK_LEAVE`/`EMERGENCY` events, Teams cards and e-mails naming a real person were sent (`server.py:1025-1038`). Correct the record with the recipients. |
| `server.log` | Append-only, unrotated, contains the client-supplied names from the test (`server.py:1022`, audit §4). Preserve a copy as evidence of the test before any cleanup. |
| Retention | No purge job or retention setting exists for `status_log` or `manual_actions` (inventory §3). Define a retention period and implement it — unbounded presence and sick-leave history is the underlying exposure that made the test findings severe. |

### 5.3 Flag / inform

| Stakeholder | What they need to be told |
|---|---|
| **Data protection officer / works council (Betriebsrat)** | That attendance records including **`SICK_LEAVE` status per named employee** were retrievable — and that `/api/attendance` and `/ws` exposed the live version of this **with no credential at all** (inventory §2.1). Sick-leave data is a special category under GDPR Art. 9; in Germany attendance monitoring is also co-determination-relevant. This is the item with a potential reporting clock. |
| **The impersonated supervisor** | That a shift was started in his name during an authorised test, and that the audit log cannot distinguish forged from genuine manual actions (audit §2.2). |
| **All ~130 agents** (via the appropriate channel) | Scope of data that was reachable: name, Windows username, employee id, KIDs, both e-mail addresses, hostname, presence times, work location, free-text notes, and sick-leave status. |
| **Team leads / RTMs** | That attendance and worked-hours data from the legacy period is not reliable evidence about an individual (§4). |
| **Whoever owns the repository** | It must remain private while the key is in history, and the key must be rotated at Phase 3 (`README.md:233`). |
| **Security / IT** | Whether the devtunnel is reachable from outside the corporate network — this decides whether the unauthenticated endpoints were internet-exposed. NOT FOUND, and out of scope here (gaps § N7). **Answer this first:** it sets the severity of everything above. |

---

## 6. Prioritized to-do list

Priority order is by risk reduced per unit of effort. "Breaks old kiosks" is determined
from `main_kiosk.py`'s six call sites, as established in §3.

| # | Action | Files / functions to touch | Breaks old kiosks | Effort |
|---|---|---|---|---|
| 1 | Establish whether the devtunnel is externally reachable | no code — network/tunnel owner | **no** | small |
| 2 | Require auth on `GET /api/session` **and** gate `/dashboard` | `server.py:1011-1013` (`get_session`), `server.py:3635-3636` (`dashboard`); new `verify_admin` dependency near `server.py:955` | **no** | small |
| 3 | Disable `/openapi.json`, `/docs`, `/redoc` | `server.py:951` — add `docs_url=None, redoc_url=None, openapi_url=None` to `FastAPI(...)` | **no** | small |
| 4 | Protect `/api/agents`, `/api/teams`, `/api/agents-template`, `/ws` | `server.py:1052, 1056, 1639, 1649` | **no** | small |
| 5 | Reject the master key in the URL; keep session tokens in the query | `server.py:955-962` (`verify_api_key`) — split the two credential kinds | **no** (dashboard download buttons must keep query-token support: `server.py:2999, 3009, 3016, 3066, 3136, 3144, 3392, 3400, 3560`) | small |
| 6 | Scope the legacy key to the six kiosk endpoints; move all others to `verify_admin` | decorators at `server.py:1042, 1068, 1088, 1106, 1125, 1128, 1132, 1135, 1144, 1156, 1167, 1211, 1237, 1241, 1254, 1267, 1275, 1281, 1337, 1361, 1426, 1435, 1466, 1476, 1513, 1554, 1563, 1597` | **no** | medium |
| 7 | Add audit logging for reads/exports/imports/auth; persist caller IP and `client_version` | `server.py:90-107` (`db_insert_status`), `server.py:1021` (the `""` argument), `server.py:955-962`, `db_init()` at `server.py:533-551` for the new column | **no** | medium |
| 8 | Protect `/api/attendance` — **after** the Pulse frontend sends a credential | `server.py:1060`; consumer `Frontend/src/pages/WicAttendance.tsx` via `VITE_KIOSK_API_URL` (`BLUEPRINT_SCREENS.md:163`) | **no** (breaks the Pulse WIC Attendance page if sequenced wrongly) | medium |
| 9 | Rate-limit `/api/session`, `/api/status`, `/api/agent-lookup` | `server.py:951-953` (middleware), route decorators | **no** | small |
| 10 | Reject unknown usernames on `/api/status`; audit existing `NULL agent_id` rows first | `server.py:1016-1021`, `server.py:93-97`, `server.py:914-921` (`get_agent_info`) | **no** | small |
| 11 | Populate the kiosk inventory (hostname → agent → build) | `DEPLOY_AGENT_LAPTOPS.md:482-495` (B9 table) — documentation + an operational pass | **no** | medium |
| 12 | Correct the stale documentation | `documentation/PROJECT_BLUEPRINT.md` § 8.1 (QR/card, "not yet deployed"); `ShiftKiosk/CODE_OVERVIEW.md` (9 h vs 11 h, route table, "API key / token"); `ShiftKiosk/README.md:229` (add the `/api/session` caveat) | **no** | small |
| 13 | **Entra ID dual-mode authentication** (Phase 1) | `server.py:955` (new JWT dependency), `server.py:963-967` (`StatusPayload` — stop trusting identity fields), `server.py:1015-1021`, `1285-1317`; kiosk `main_kiosk.py:300-303, 386-389, 414-417, 703-706, 818-821`; new installer package in `KIOSK_PACKAGE/Files/` | **yes — requires re-install on every kiosk** | **large** |
| 14 | Enable TLS verification in the kiosk | `main_kiosk.py:389, 400, 417, 451, 706, 821` (`verify=False` → `True`) | **yes — kiosk change, ship with #13** | small |
| 15 | Remove `performed_by` / `acked_by` from payloads; derive the actor from the session (Phase 2) | `server.py:969-994` (both validators), `server.py:1069-1082`, `1089-1099`, `1107-1116` | **no** | medium |
| 16 | Role-based authorization for admin endpoints, mirroring `auth_rbac.md` | new role check in `server.py`; model per `documentation/auth_rbac.md` § Enforcement | **no** | medium |
| 17 | Password/secret-free exit: make `Ctrl+Shift+Alt+A` emit a status event or require a supervisor credential | `main_kiosk.py:1102-1112`, `1392-1395` (`_supervisor_exit`) | **yes — kiosk change, ship with #13** | small |
| 18 | Define and implement retention for `status_log` / `manual_actions` | new job; `db_init()` at `server.py:501` for any schema support | **no** | medium |
| 19 | Rotate the shared key and remove the legacy dependency (Phase 3) | `server.py:31`, `955-962`; `server_config.ini`; `KIOSK_PACKAGE/Files/config.ini` | **yes — by design, only when none remain** | small |

Items 1–12 are server-side or documentation-only and **break no installed kiosk**. They
remove every capability in §1.2 except kiosk-endpoint impersonation, and they can start
immediately. Item 13 is the only large one, and it is the only one that fixes identity.
