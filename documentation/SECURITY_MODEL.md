# SECURITY_MODEL — what protects what, and what does not

**Verification basis:** both repositories and both live databases, read **read-only** on **2026-10-11**.
No secret value is reproduced anywhere in this document — only configuration **key names**, table and
column names, and aggregate counts. No personal names, user names, host names, device identifiers or
network addresses appear here.

**Related documents:** [SYSTEM_ARCHITECTURE.md](./SYSTEM_ARCHITECTURE.md) ·
[INTEGRATION_AND_DATA_FLOWS.md](./INTEGRATION_AND_DATA_FLOWS.md) · [DATA_MODEL.md](./DATA_MODEL.md) ·
[OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md) · [OPERATIONS.md](./OPERATIONS.md)

---

## 1. The model in one picture

There are **two independent authentication worlds**, and they do not trust each other.

| | **WorkForce Pulse** | **ShiftKiosk** |
|---|---|---|
| Who signs in | Team leads, RTM, administrators | Agents (implicitly, by their Windows session) and supervisors |
| Credential | a KID typed into a sign-in form, exchanged for an HttpOnly session cookie | a per-computer device token (NG) or a shared legacy key (old clients); supervisors use password + TOTP |
| Authorisation model | four roles, plus always-on route gates | nine named flags, promoted in stages, plus credential scope |
| Where accounts live | the Pulse database | the kiosk database |
| Enforcement switch | one global flag (currently **off** for general RBAC) | per-flag profiles (four enforced, five observing) |

**The single most important thing to understand:** the Pulse global RBAC switch being off does **not**
mean nothing is protected. Three route families carry their own always-on gates — the roster
endpoints, the kiosk proxy endpoints, and the API documentation page — and the shift-write routes
enforce a session check inside the handler. The distinction between "the global flag" and "the
always-on gates" is where most misreadings of this system's security begin.

---

## 2. Pulse authentication and authorisation

### 2.1 Sign-in

The current live sign-in takes a **KID only** — no password and no second factor. The resolver tries,
in order: an active row in the login table with a non-empty matching KID; then an active employee whose
primary or secondary KID matches; then an active employee whose employee number matches. Anything else
is refused with a deliberately neutral error. **If an input matches more than one active row at any
step, it is refused and a warning is logged — the system never guesses.**

The endpoint is rate-limited (10 attempts per minute per client address). On success the server issues
an HttpOnly, SameSite-Lax session cookie with a 12-hour sliding expiry; the cookie's secure policy
follows the request, so sign-in works both over the tunnel and on plain loopback.

Two guards refuse to start the application in a broken state: an agent-role row without an employee
id, and zero active developer rows with a real KID. That second guard is what prevents a lockout of the
system's own administrators.

### 2.2 The four roles

| Role | Read | Write | Notes |
|---|---|---|---|
| `AGENT` | own records only, through a server-side allow-list | **none** | The session always carries a real employee id |
| `TEAM_LEAD` | everything | everything | |
| `RTM` | everything | everything | |
| `DEV` | everything | everything, plus `/api/admin/*` | Never restricted |

Role rows live in a dedicated login table. Non-employees (the developer, the RTMs) exist only there
with no employee id — there are no fake employee rows. Live distribution: **1 DEV, 2 RTM, 6 TEAM_LEAD,
0 AGENT**. Agent self-service works through employee-record matching rather than a role row.

### 2.3 The always-on gates (independent of the global switch)

| Gate | Who may pass | Behaviour for everyone else |
|---|---|---|
| `/api/roster/*` | RTM, TEAM_LEAD, DEV | 401 anonymous, 403 wrong role |
| `/api/kiosk/*` | authenticated DEV, RTM, TEAM_LEAD | 401 anonymous, 403 other roles — **before** any call to the kiosk server |
| `/swagger` | authenticated DEV | **404**, deliberately not 401, so the endpoint map's existence is not disclosed |
| Shift cell writes | authenticated DEV/RTM/TEAM_LEAD **with a non-empty session KID** | 401/403; the acting identity is taken from the session, never from the request body |

**One family is missing from that list, and it is the one named "admin".** The two `/api/admin/*`
command endpoints — the demo-data delete and the WIC orphan cleanup — carry **no gate of their own**.
They are protected only by the general role middleware, which is currently off. While it is off, both
are reachable without signing in. This is not a documentation nuance; it is the difference between
"developer-only" as a rule and "developer-only" as a fact.

### 2.4 What the global switch does when it is on

When `Auth:EnforceAuthorization` is enabled, the role middleware applies to every `/api/*` request:
anonymous requests get 401 except the two rate-limited anonymous endpoints; DEV bypasses everything;
team leads and RTMs are unrestricted; agent sessions are restricted to a documented GET allow-list,
each entry scoped to the session's **own** identifier; every agent write is refused; and `/api/admin/*`
is developer-only. Non-API paths — static files, `/health` and the single-page-app fallback — are
untouched, because the health endpoint must stay anonymous for the watchdog.

The allow-list is the part that matters: an agent cannot read another agent's row by editing an
identifier in a URL or query string. That is verified by the existing smoke-test script.

### 2.5 Recovery

There is a documented database-level recovery procedure for restoring developer access, plus a
documented way to disable enforcement if authorisation itself is the problem. Both are recorded as
operator actions and neither was executed for this documentation.

---

## 3. The kiosk device-token model

This is the centrepiece of the newer security work.

### 3.1 How a computer gets a token

An administrator runs the device-enrolment command with the computer's hostname. It:

1. generates a random device identifier and a random token;
2. stores **only the token's hash** in the database — the token itself is never stored server-side;
3. refuses to enrol if that hostname already has an active device (the check and the insert are one
   atomic operation);
4. writes the token to a file whose permissions allow SYSTEM and Administrators only.

### 3.2 How the token is used

- The token file is installed on the computer with restrictive permissions, and its hostname field must
  match the computer it is installed on.
- A privileged **broker** process runs as SYSTEM, holds the token in memory, and makes the outbound
  calls. The user-facing interface never sees the token.
- The broker derives the agent's identity **from the operating system**, by impersonating the caller of
  its named pipe — it does not trust the user name in the event body. This closes a spoofing path and
  has a dedicated regression test.
- The broker is not a general proxy: it forwards only a short allow-list of read paths plus the status
  write.
- The pipe itself is protected by a security descriptor that grants interactive users read/write and
  SYSTEM/Administrators full control.

### 3.3 Revocation

Revocation is per device: the token hash is marked revoked and any request presenting that token is
treated as unauthenticated. **Revoking a device does not uninstall an old client**, because an old
client can still authenticate with the shared key. Both actions are needed to truly remove a machine.

### 3.4 The legacy key and its retirement plan

The shared key is the original credential. It is now restricted to the six kiosk endpoints, and session
tokens are forbidden from those endpoints — a symmetric rule that closes the specific impersonation
chain found in the October review (an anonymous session token being used to write attendance as any
agent) **without** requiring agents to authenticate.

Why it cannot simply be switched off yet:

- roughly **sixty** computers still authenticate with it, and they are still sending events;
- the key is readable on those machines by design;
- it exists in git history in three early commits, so the repository must stay private.

The recorded retirement criterion is **no legacy inventory for 30 days**. The measurement for that
criterion is a purpose-built registry table keyed by hostname and credential type, which is why the
observation layer exists. Since legacy clients sent 438 events in the last 90 days, the clock has not
started.

---

## 4. Supervisor sign-in — implemented on the kiosk side only, and **not wired into Pulse**

This section carries a correction that matters more than anything else in this document.

**What exists.** The kiosk server contains a complete, hardened supervisor credential path:

| Element | Detail |
|---|---|
| Credential | username + password + a time-based one-time code |
| Password storage | PBKDF2 with a per-account salt and iteration count |
| Second factor | a TOTP secret stored encrypted |
| Lockout | five failures within a 15-minute window, **keyed per account, not per address** |
| Recovery | single-use recovery codes, plus a break-glass flag on the account |
| Account creation | only through the offline admin tool — **never over HTTP** |
| Where the accounts live | the kiosk database |
| Session | its own HttpOnly cookie on the kiosk dashboard, 8 hours |

**What does not exist.** The Pulse repository contains **no** supervisor sign-in integration at all.
A repository-wide search for the setting name, the provider interface and the client class that the
documentation names returns **zero matches**, and the Pulse configuration file contains no such key —
its authentication section holds exactly one setting, the global authorisation switch. Therefore:

- The three-state "off / shadow / enforce" supervisor mode that several documents describe as an
  operating switch **has no reader in the code**. It cannot be switched on, because nothing reads it.
- The documented "single seam" for a future identity provider — the interface Pulse would call to
  verify a supervisor — **is not implemented**. The kiosk server has an endpoint that would serve that
  purpose, but it has **no caller** in this repository.
- Consequently, the claim in the existing security documentation that supervisor enforcement would
  restrict Pulse sessions to three roles is **not true of the code**. In the current implementation
  any resolved identity receives a session cookie, including an agent.

**Current state of the account table: 0 rows.** The supervisor path is therefore doubly inactive — it
is not provisioned, and the application that would consume it does not call it.

**Why this is recorded here rather than quietly fixed.** Two existing documents instruct an operator to
set a configuration value that no code reads, and one of them describes a recovery procedure that
would not work. An operator following those instructions would believe supervision had been enforced
when nothing had changed. This is the highest-severity documentation defect found in the review, and it
is reported as such in [OPEN_ITEMS_AND_RISKS.md](./OPEN_ITEMS_AND_RISKS.md).

**The consequence to accept deliberately if the seam is ever built:** at enforcement, agents would lose
the Pulse self-service page, because they have no account and never authenticate, and Pulse sign-in
would then depend on the kiosk process being available.

---

## 5. The `sec_*` tables — what is recorded about whom

All of these live in the kiosk database. Row counts are as read on 2026-10-11.

| Table | Rows | What it records | Personal data? |
|---|---|---|---|
| `sec_devices` | 71 (67 active, 4 revoked) | One row per enrolled computer credential: device id, hostname, **token hash**, bound user name, creator, last source address, revocation | Yes — hostname, bound user |
| `sec_kiosk_registry` | 63 | Inventory of computers seen, with the credential type and client version used, first/last seen and request count. This is the table that answers "which machines still use the legacy path". | Yes — hostname |
| `sec_observation_log` | **282,745** | One row per request: credential type, source address, hostname, agent user name, client version, device id, method, path, status code, decision, rule id, reason | Yes — user, host, path |
| `sec_auth_events` | 76 | Authentication lifecycle: device enrolment and revocation today (the supervisor account events exist in code but have no rows) | Yes — user, host |
| `sec_anomalies` | 47 | Flagged detections — new host for a user (23), host/user not allowed (17), unknown user name (7), all severity "warning" | Yes — user, host |
| `sec_sessions` | 0 | Server-side supervisor sessions, so a cookie can be revoked before it expires | Yes |
| `sec_accounts` | **0** | Supervisor accounts (username, display name, role, KID, team leader, password material, TOTP material, lockout, active, break-glass, last sign-in) | Yes |
| `sec_recovery_codes` | 0 | Single-use recovery codes (stored hashed) | Yes |
| `sec_user_hosts` | 76 | Which user name has been seen on which host — the data behind host/user binding decisions | Yes — user, host |

**Observation decisions on 2026-10-11 (all time):** 268,952 `allow`, 13,740 `would_block`, 53 `block`.
The `would_block` figure is the one to understand: those are requests a shadow-mode rule would have
refused and which were allowed through anyway. Reading them as blocked requests is the most common
misinterpretation of this data.

**No retention or deletion path exists for any of these tables.** The observation log grows with every
request. This is an open item, not a solved one.

---

## 6. Rate limiting

**On the kiosk server** the algorithm is a fixed-window counter held in memory, keyed by client address,
with the forwarded-for header deliberately ignored. Four buckets exist: sign-in (10/min), session
issuance (30/min), status writes (1,200/min) and a "heavy" bucket (60/min) covering the history, audit,
export, report and import paths. On breach, an **enforced** bucket returns 429 with a JSON body; in
shadow mode the same verdict is recorded and the request proceeds. State is per process and resets on
restart.

**The heavy bucket does not cover the largest surface.** The endpoints that return every agent's
attendance, the agent list and the team list are **not** in the protected set. So even with rate
limiting enforced, the highest-volume and most sensitive reads remain unlimited. Supervisor sign-in
lockout is a separate, always-on control and is keyed per **account**, not per address — so it stops
password guessing against a named account but not enumeration across many accounts.

**Endpoints that are state-changing or bulk-reading but not rate-limited at all** — the honest list:
supervisor password change (which is also missing a session check), manual check-in and log-out,
emergency acknowledgement, the agent group patch, WIC-centre create/update/delete, the kiosk identity
and location lookups, and the bulk read endpoints (agents, attendance, teams, stats, WIC agents, WIC
coverage).

**On the Pulse side** the sign-in endpoint and the anonymous client-error endpoint are rate-limited
(10/min). The shift-write, roster and kiosk-proxy routes are not individually rate-limited; they rely
on the session gates.

---

## 7. Transport, secrets and what is committed

| Aspect | State |
|---|---|
| Transport | The kiosk server speaks plain HTTP; TLS is terminated at the dev tunnel. A second, unused HTTPS path was removed. |
| Certificate verification | The legacy client disables verification on every call. |
| Tunnel access | The tunnel permits anonymous connections at the port level, because the kiosk has no corporate account. The tunnel is therefore **not** an authentication boundary. |
| Signing | Session tokens are HMAC-SHA256 over a timestamp. They are not bound to an account, role, address or device, and carry no revocation list. |
| **Signing key reuse** | The session-token signing key **is the shared legacy API key**. The scope rule that restricts the raw key to the six kiosk endpoints does not restrict tokens *derived* from it on any other route. Anyone holding the shared key can therefore mint a token that reaches every other credential-gated route — history, audit, exports, imports. The key's own scope restriction is defeated by the key itself. Separating the signing secret from the shared key is recorded as a required future change and has not been done. |
| Master key comparison | Compared with a plain equality operator rather than in constant time. |
| Second-factor replay | A time-based code is accepted for its full 30-second step; there is **no replay prevention**. |
| Token material at rest | Only hashes are stored for device tokens, recovery codes and supervisor session identifiers. |
| Configuration files containing secrets | Not committed (the server configuration is gitignored). |
| Files that **are** committed and contain credential material | The kiosk client configurations and one test profile were reviewed; the shared key value is present in committed configuration files across both repositories' history, and the kiosk repository's README records that the key predates in three early commits. The repository must stay private. |
| Repository hygiene | `.gitignore` in the kiosk repository excludes token files, the live server configuration, break-glass text and generated logs. |

**Data-protection position recorded for this system:** named sickness data is the least-protected
special-category datum; historical attendance is **not** valid evidence of individual performance;
reachable-city data is used for reachability calculations.

---

## 8. What is closed — verified

1. The anonymous session token → attendance write chain is closed (the legacy key cannot call session
   endpoints and session tokens cannot write status).
2. The identifier leak on the Pulse side is bypassed: every kiosk call from the browser now goes through
   an authenticated proxy, and the environment variable that used to hold the kiosk address is no longer
   referenced anywhere in the frontend.
3. The API documentation page is not served to non-developers — it returns 404 to avoid disclosing the
   endpoint map.
4. Credentials cannot be placed in a URL query string.
5. Roster endpoints and kiosk-proxy endpoints are gated independently of the global RBAC flag.
6. Agent reads are scoped server-side to the session's own identifier.
7. Manual shift edits carry the acting identity; the identity is never accepted from the request body on
   the shift-write routes.
8. The privileged kiosk broker runs as SYSTEM, verifies the caller by OS impersonation, and is limited
   to an allow-list of paths.
9. Every request to the kiosk server is recorded with its credential kind and the rule that decided it,
   so enforcement decisions can be made on evidence.
10. The deploy and runtime directories are protected so only SYSTEM and Administrators can write.

---

## 9. What is open — the unvarnished list

1. **Anonymous reading on the kiosk server.** A session token can be obtained without any credential
   and used to read history, the audit log and the exports. Closed only at the final planned profile.
2. **Anonymous identifiers are still returned live.** The stripping rule is in shadow, so anonymous
   attendance and agent queries still return the very identifiers that, combined with the Pulse sign-in
   rule, constitute a working agent-level credential. This is the closure that must precede any
   enforcement of Pulse authorisation.
3. **The shared legacy key** remains valid and is used daily by roughly sixty machines. It is
   *also the session-token signing key*, so its effective reach is wider than its stated scope.
4. **No supervisor accounts exist, and the Pulse side that would consume them is not implemented.** The
   documented enforcement switch has no reader in the code.
5. **Two administrative command endpoints have no gate of their own** and are reachable while the
   general authorisation middleware is off.
6. **A device-token device cannot be force-migrated:** revoking a token does not stop a legacy client,
   because the fallback key still works.
7. **Two of the nine kiosk controls are still shadow in the plan and one is enforcing without having
   been promoted.** The recorded live configuration sets device binding to **enforce**, while every
   tracked profile and both canonical documents say it is still observing. A control is blocking in
   production that the recorded promotion gate says has not been promoted — the mismatch itself is the
   finding, whichever way it is resolved.
8. **Rate limiting is observation-only**, and the bucket designed to protect bulk reads does not cover
   the endpoints that return every agent's attendance and identifiers. Even enforced, it would not
   protect the largest surface.
9. **Anomaly detection is observation-only by design**, and its alert column is never written.
10. **A device token is not user authentication.** A local administrator can read it or bypass the
    broker.
11. **The kiosk transport is not private in transit** beyond the tunnel's TLS, and the legacy client
    does not verify the certificate. Cross-origin policy on the kiosk server allows any origin.
12. **Team-lead scoping exists in the kiosk dashboard only**, not across Pulse. Every team filter in
    Pulse is a caller-supplied parameter, not a bound scope.
13. **No retention or deletion rule** exists for the observation log and other personal-data stores;
    a used recovery code is the only row any code deletes from the security tables.
14. **The supervisor password-change endpoint** has neither a session requirement nor lockout.
15. **Pulse's general authorisation is off**, so on the endpoints that do not carry an always-on gate,
    any resolved session has full read and write rights regardless of role.
16. **A second-factor code can be replayed** within its time step.
17. **Default sign-in flows over plain loopback** in development, where the session cookie is
    deliberately not marked secure, and cross-origin policy is fully open.

---

## 10. Known limits stated in advance, so no one is surprised

- **This is an internal tool.** It is not designed for public exposure; the tunnels exist for operator
  convenience, not as a deployment platform.
- **No penetration test report exists** for either product. The October review was a code and
  configuration audit with a targeted exploitation check of one chain; it is not a substitute for a
  security assessment.
- **No SIEM, no alerting pipeline, no log shipping.** Detection data is written to a database and read
  by a human.
- **Personal data cannot be deleted on request** with any current code path — deletion would be a
  database operation, and no procedure is documented for it.
- **Two people's names appear in the running application's attribution line and in several existing
  documentation files.** They are deliberately excluded from this documentation set; the existing
  files are unchanged. A board-facing document should not be built by handing out those files as-is.
