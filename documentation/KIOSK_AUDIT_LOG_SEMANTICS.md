# ShiftKiosk — Audit Log Semantics (AS-IS)

> **HISTORICAL SNAPSHOT (before dan1), not current deployment instructions.**
> Original findings below retain their original evidence/line numbers.
> Canonical current state: [KIOSK_HARDENING_DEPLOY_NOTE.md](KIOSK_HARDENING_DEPLOY_NOTE.md).
> Client evidence/install: `C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md`.

What is logged, who is recorded as the actor, and whether that actor is server-derived or
client-supplied. Source: `C:\ShiftKiosk\server\server.py`.

**One-sentence answer: every actor recorded anywhere in this system is client-supplied.
There is no server-derived actor, because authentication carries no identity**
([KIOSK_AUTH_IDENTITY_ASIS.md](KIOSK_AUTH_IDENTITY_ASIS.md) §2 item 4).

---

## 1. The three audit surfaces

| Surface | What it holds | Written by |
|---|---|---|
| `manual_actions` table | supervisor check-ins / log-outs | `server.py:1081-1082`, `1098-1099` |
| `status_log.emergency_acked_by` | who acknowledged an emergency | `server.py:1109-1110` |
| `server.log` (text file) | application log | `logging` config, `server.py:16-23` |

There is no audit table for reads, exports, imports or authentication (§5).

---

## 2. `manual_actions` — the audit log proper

### 2.1 What is written

CONFIRMED — `server.py:1081-1082` (check-in) and `server.py:1098-1099` (logout):

```python
cur.execute("INSERT INTO manual_actions (agent_id,action,performed_by,note) VALUES (?,?,?,?)",
            payload.agent_id, 'MANUAL_CHECKIN', payload.performed_by, note)
```

| Column | Value | Server-derived or client-supplied? |
|---|---|---|
| `agent_id` | `payload.agent_id` | **client-supplied** — the subject of the action |
| `action` | literal `'MANUAL_CHECKIN'` / `'MANUAL_LOGOUT'` | server-derived (constant per endpoint) |
| `performed_by` | `payload.performed_by` | **client-supplied** — the claimed actor |
| `note` | `f"Manual check-in by {payload.performed_by} / Manuell durch {payload.performed_by}"` | server-formatted around **client-supplied** text (`server.py:1076`, `1095`) |
| `performed_at` | DB default | server-derived (ordered by it at `server.py:494`) |

Each manual action also writes a companion `status_log` row with `agent_hostname` set to
the literal string `'MANUAL'` (`server.py:1077-1081`, `1096-1098`) — that literal is the
only reliable marker distinguishing a dashboard-originated event from a kiosk one.

### 2.2 The actor is a free-text string, validated only for plausibility

CONFIRMED — `server.py:974-983`:

```python
@field_validator("performed_by")
@classmethod
def _require_real_name(cls, v: str) -> str:
    v = (v or "").strip()
    if not v or v.lower() == "supervisor":
        raise ValueError('performed_by must be a real name, not empty or the default "Supervisor"')
    return v
```

The in-code comment dates this to a "GRC fix (2026-09-15)" whose stated goal was that
"manual actions are attributable in the audit log" (`server.py:971-973`).

**It does not achieve attribution.** The validator rejects exactly two values — empty and
the literal `"supervisor"` (case-insensitive). Any other string passes: a real colleague's
name, a misspelling, `"."`, or `"x"`. The field is never compared against `agents`,
`AppUserRoles`, or any other list of people. CONFIRMED — `server.py:974-983` is the only
code that touches `performed_by` before it is stored.

So `manual_actions.performed_by` records **a name the caller chose to type**, not the
identity of the caller. An attacker with the shared key — or with nothing at all, via the
anonymous session token ([KIOSK_AUTH_IDENTITY_ASIS.md](KIOSK_AUTH_IDENTITY_ASIS.md) §2.1) —
can attribute a manual check-in or logout to any named person. The audit log will show
that person as the actor, with no indicator that the entry is forged.

ASSUMPTION — the dashboard prompts a human for their name and sends it. Not verified: the
dashboard JavaScript lives inside the `DASHBOARD_HTML` string and its prompt flow was not
read. The validator's rejection of the literal `"Supervisor"` implies the dashboard once
sent a hardcoded placeholder.

### 2.3 Emergency acknowledgement

CONFIRMED — `server.py:1107-1110`:

```python
cur.execute("""UPDATE status_log SET emergency_acked=1, emergency_acked_by=?, emergency_acked_at=GETDATE()
               WHERE id=?""", payload.acked_by, status_log_id)
```

`acked_by` carries the same validator (`server.py:987-994`) and the same weakness.
`emergency_acked_by` is returned by the **unauthenticated** `/api/attendance`
(`server.py:415`), so a forged acknowledger name becomes publicly readable.

The handler then calls `manual_logout` internally with
`performed_by=payload.acked_by` (`server.py:1114-1116`), so one acknowledgement produces a
second `manual_actions` row with the same unverified name, and a Teams message naming them
(`server.py:1118-1122`).

### 2.4 No reconciliation between the two actor fields

CONFIRMED — `performed_by` (in `manual_actions`) and `agent_username` (in `status_log`)
are written independently and never cross-checked. A `status_log` row can name agent A
while the `manual_actions` row for the same event names actor B, with no constraint
relating them.

---

## 3. What is *not* audited

NOT FOUND IN CODE OR DOCS — no code exists for any of the following:

| Not logged | Consequence |
|---|---|
| **Reads** — `/api/history`, `/api/audit-log`, `/api/attendance`, every export | The security tester's bulk download of names, hostnames, session ids and `SICK_LEAVE` statuses **left no record on the server**. The same is true for any future exfiltration. |
| **Authentication events** | No log of a successful or failed key use. `verify_api_key` (`server.py:955-962`) raises `HTTPException` without logging. Brute force or credential misuse is invisible. |
| **Session-token issuance** | `/api/session` (`server.py:1011-1013`) has no `log` call. There is no record of how many tokens were minted, or to whom. |
| **`/api/import-agents`** | A log line records only counts — `log.info(f"Import: inserted=… updated=…")` (`server.py:1049`). Neither the uploader nor the file is recorded, and there is no `manual_actions` row. A bulk rewrite of the agent master table is effectively anonymous. |
| **WIC-centre create / update / delete** | `server.py:1241-1273` — no audit row, no log line. |
| **Caller IP** | Always `''` in `status_log` (`server.py:1021`), and absent from every other table. Nothing records where a request came from. |
| **Deletions** | No delete endpoint exists for `status_log` or `manual_actions`, so there is also no tamper trail — but equally no retention control ([KIOSK_DATA_EXPOSURE_INVENTORY.md](KIOSK_DATA_EXPOSURE_INVENTORY.md) §3). |

---

## 4. `server.log`

CONFIRMED — `server.py:16-23`: `logging.basicConfig(level=INFO)` to
`C:/ShiftKiosk/server/server.log` **and** stdout.

| Event | Line logged | Source |
|---|---|---|
| Status received | `Status: {agent_username} -> {status}` | `server.py:1022` |
| Import | `Import: inserted=… updated=…` | `server.py:1049` |
| Manual check-in | `Manual checkin: {full_name} by {performed_by}` | `server.py:1083` |
| Manual logout | `Manual logout: {full_name} by {performed_by}` | `server.py:1100` |
| Emergency ack | `Emergency acked: id=… by {acked_by}` | `server.py:1117` |
| WebSocket connect | `WS connected. Total: …` | `server.py:928` |

Properties, all CONFIRMED:

- It is **append-only plain text with no rotation configured** — a single
  `logging.FileHandler` (`server.py:20`), no `RotatingFileHandler`. The file observed on
  disk was ~346 KB.
- It records the **same client-supplied names** as the database, so it is corroboration,
  not independent evidence.
- It contains **no read events**, so it does not close the §3 gap.
- `log.info(f"Status: {payload.agent_username} …")` (`server.py:1022`) writes an
  unsanitised client-controlled string into the log, so log contents can be shaped by a
  caller (newline injection into a line-oriented log).

---

## 5. Summary table — actor provenance

| Recorded actor field | Endpoint | Provenance | Verifiable? |
|---|---|---|---|
| `status_log.agent_username` | `POST /api/status` | request body | **No** |
| `status_log.agent_hostname` | `POST /api/status` | request body | **No** |
| `manual_actions.performed_by` | `/api/manual-checkin`, `/api/manual-logout` | request body | **No** — only "not empty, not 'supervisor'" |
| `status_log.emergency_acked_by` | `/api/emergency/ack/{id}` | request body | **No** — same validator |
| `manual_actions.agent_id` | manual endpoints | request body | Existence is checked (`server.py:1071-1073`), but not the caller's right to act on it |
| *anything at all* | `/api/import-agents`, `/api/wic-centers` (POST/PUT/DELETE) | — | **Not recorded** |

**Conclusion, CONFIRMED:** the audit log is a record of *what was claimed*, not of *who
did it*. It is usable for operational reconstruction of ordinary activity and is **not**
usable as evidence against anyone who holds the key or can reach `/api/session`. Making it
evidential requires the actor to be derived from authentication, which is the core
requirement in [SECURITY_MIGRATION_PROPOSAL.md](SECURITY_MIGRATION_PROPOSAL.md) §2.
