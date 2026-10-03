# ShiftKiosk — Kiosk Client Deployment (AS-IS)

> **HISTORICAL LEGACY-CLIENT SNAPSHOT, not current NG installation instructions.**
> Original findings below retain their original evidence/line numbers.
> Canonical current state: [KIOSK_HARDENING_DEPLOY_NOTE.md](KIOSK_HARDENING_DEPLOY_NOTE.md).
> Client evidence/install: `C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md`.

How a kiosk is installed, configured and updated; what it sends and when; what it stores
locally; and what the person using it can read or change. Sources are
`C:\ShiftKiosk\KIOSK_PACKAGE\install_kiosk.bat`, `C:\ShiftKiosk\deploy\Deploy-Application.ps1`,
`C:\ShiftKiosk\kiosk\main_kiosk.py`, `C:\ShiftKiosk\kiosk\config.ini` and
`C:\ShiftKiosk\DEPLOY_AGENT_LAPTOPS.md`.

**Scale (from the system owner, not from code):** ~60 kiosks installed and in use, ~70 more
planned. ASSUMPTION for planning purposes; the repository contains no inventory that
confirms it (§7).

---

## 1. What a "kiosk" actually is

CONFIRMED — a **tkinter desktop application that runs on the agent's own Windows laptop**
under the agent's own logon session. It is not dedicated terminal hardware.

| Property | Value | Source |
|---|---|---|
| Entry point | `C:\ShiftKiosk\kiosk\main_kiosk.py` | `install_kiosk.bat:103` |
| Host process | `C:\Program Files\Python312\pythonw.exe` (console-less) | `install_kiosk.bat:11`, `Deploy-Application.ps1:165` |
| UI | Phase 1 fullscreen chooser → Phase 2 floating widget | `CODE_OVERVIEW.md` § `kiosk/main_kiosk.py` |
| Identity source | `os.getlogin()` and `socket.gethostname()`, computed locally | `main_kiosk.py:302-303` |

> **Contradiction with existing documentation.** `PROJECT_BLUEPRINT.md:292` describes
> kiosks as terminals where agents "check in … via QR code or card scan", and
> `PROJECT_BLUEPRINT.md:298` and `:450` state that "Zone B kiosk hardware is not yet
> deployed" and that live data "is absent until the terminals are installed". **The code
> reflects neither.** There is no QR or card-scan code path anywhere in `main_kiosk.py`;
> identity is the Windows username. See
> [KIOSK_GAPS_AND_CONTRADICTIONS.md](KIOSK_GAPS_AND_CONTRADICTIONS.md) § C1.

---

## 2. Installation

CONFIRMED — `KIOSK_PACKAGE\install_kiosk.bat`, a six-step attended installer that must be
run **as Administrator** on each laptop (`install_kiosk.bat:24-27`).

| Step | Action | Source |
|---|---|---|
| 1/6 | Ensure Python 3.12 at the fixed machine-wide path; install from the bundled `python-3.12.10-amd64.exe` if absent | `install_kiosk.bat:34-59` |
| 2/6 | `pip install` dependencies into the shared `C:\ShiftKiosk\vendor` | `install_kiosk.bat:66-78` |
| 3/6 | Create `C:\ShiftKiosk\kiosk` | `install_kiosk.bat:87` |
| 4/6 | Copy `main_kiosk.py`, `theme.py`, **`config.ini`**; seed `pending_queue.json`; **grant `BUILTIN\Users` RX recursively** | `install_kiosk.bat:103-118` |
| 5/6 | Register scheduled task `ShiftKiosk` + a common-desktop shortcut | `install_kiosk.bat:131-150` |
| 6/6 | Run `kiosk_selfcheck.py` under the registered `pythonw.exe` | `install_kiosk.bat:157+` |

`deploy\Deploy-Application.ps1` is a parallel PowerShell implementation of the same thing
(`Deploy-Application.ps1:115-117` copy, `:173` task registration) and additionally writes
`HKLM:\SOFTWARE\ShiftKiosk` (`Deploy-Application.ps1:178-180`).

### 2.1 Autostart

CONFIRMED — `install_kiosk.bat:137-141` / `Deploy-Application.ps1:165-173`:

- Trigger: **at logon**, delayed `PT1M`
- Settings: `RestartCount 3`, restart interval 1 min, no execution time limit, `MultipleInstances IgnoreNew`, runs on battery
- Principal: `-GroupId "S-1-5-32-545"` (**BUILTIN\Users**) with `-RunLevel Highest`

The group principal is what makes the kiosk run in **the logged-on agent's** context. That
is the design requirement that forces §4.1.

---

## 3. Configuration

CONFIRMED — `C:\ShiftKiosk\kiosk\config.ini`, plaintext INI, copied verbatim from the
install package (`install_kiosk.bat:107`). It is identical on every machine; no
per-device value is generated at install time anywhere in `install_kiosk.bat` or
`Deploy-Application.ps1`.

```ini
[Company]
name = Infosys - E.ON

[Server]
url = https://ssr7tm2l-8000.euw.devtunnels.ms
api_key = <the single shared key>

[Security]
supervisor_exit_shortcut = ctrl+shift+alt+a
require_confirmation = True

[settings]
language = auto
```

### 3.1 Which keys the code actually reads

CONFIRMED — only four:

| Key | Read at | Effective? |
|---|---|---|
| `[Company] name` | `main_kiosk.py:299` | yes |
| `[Server] url` | `main_kiosk.py:300` | yes |
| `[Server] api_key` | `main_kiosk.py:301` | yes |
| `[settings] language` | `main_kiosk.py:242` (separate parser in `get_lang()`) | yes |
| `[Security] supervisor_exit_shortcut` | — | **NO — dead config.** The shortcut is hardcoded as `<Control-Shift-Alt-a>` at `main_kiosk.py:1102-1103` and `1392-1393`. Editing this value changes nothing. |
| `[Security] require_confirmation` | — | **NO — dead config.** No read site exists in `main_kiosk.py`. |

Recorded because the `[Security]` section reads like a control surface and is not one.

---

## 4. What is stored locally on a kiosk

| File | Contents | Source |
|---|---|---|
| `C:\ShiftKiosk\kiosk\config.ini` | **the shared API key**, server URL, language | §3 |
| `C:\ShiftKiosk\kiosk\main_kiosk.py` | the full client source, in plain Python | `install_kiosk.bat:103` |
| `C:\ShiftKiosk\kiosk\pending_queue.json` | offline queue of unsent status payloads | `main_kiosk.py:60`, `349-384` |
| `C:\ShiftKiosk\kiosk\kiosk.log` | timestamped log of every send, queue op and error — includes the username and hostname | `main_kiosk.py:22, 61, 270`; e.g. `main_kiosk.py:1116, 1406, 1416` |
| `HKLM:\SOFTWARE\ShiftKiosk` | install marker (PowerShell path only) | `Deploy-Application.ps1:178-180` |

**The username is not stored locally.** It is computed fresh at each start from
`os.getlogin()` (`main_kiosk.py:302`).

### 4.1 The agent can read the shared key — mandatory, not accidental

CONFIRMED — `install_kiosk.bat:115-118`:

```bat
icacls "C:\ShiftKiosk" /grant "*S-1-5-32-545:(OI)(CI)RX" /T
if errorlevel 1 (
    call :fail "icacls grant failed. The agent will get PermissionError at logon."
)
```

`BUILTIN\Users` gets **Read+Execute, inherited, recursive** over `C:\ShiftKiosk`,
including `kiosk\config.ini`. The installer **aborts** if this fails, because the kiosk
process runs as the agent (§2.1) and must read its own configuration.

Consequence, stated plainly: **every one of the ~60 agents can open `config.ini` in
Notepad and read the shared key.** No amount of hardening changes this while the credential
is a file-resident bearer secret.

---

## 5. What a kiosk operator can do locally

CONFIRMED, all from read access granted in §4.1 plus the fact that the client is
interpreted source:

| Capability | Why |
|---|---|
| Read the shared API key | `config.ini` is world-readable to `Users` (§4.1) |
| Read the full client logic | `main_kiosk.py` is plain `.py`, not compiled |
| Read `kiosk.log` — own send history | §4 |
| Point the kiosk at a different server | edit `[Server] url` — *if* the agent can write the file. The ACL grants `Users` **RX**, not write; write depends on inherited NTFS permissions on `C:\ShiftKiosk`, which the installer does not set. **ASSUMPTION:** a standard (non-admin) agent cannot modify files under `C:\ShiftKiosk`. Not verified — verifying it requires inspecting the live ACL on an installed machine. |
| **Run a modified copy from elsewhere** | This is the important one and needs **no** write access to `C:\ShiftKiosk`: the agent can copy `main_kiosk.py` + `config.ini` to their own profile, change `USERNAME`/`HOSTNAME` (`main_kiosk.py:302-303`), and run it. This is precisely what the security tester did, and no server-side control detects it ([KIOSK_AUTH_IDENTITY_ASIS.md](KIOSK_AUTH_IDENTITY_ASIS.md) §4.3). |
| **Exit the kiosk silently** | `Ctrl+Shift+Alt+A` → `_supervisor_exit()` → `_cancel_auto_timer(); self._exit()` (`main_kiosk.py:1102-1112`, `1392-1395`). There is **no password prompt and no confirmation**, and **no status event is sent** — so the shift simply stops being tracked, with no `OFF` or `AUTO_LOGOUT` row. The shortcut is hardcoded and identical on every machine (§3.1). |

---

## 6. Update mechanism

CONFIRMED — **there is none.** No auto-update, no version check, no remote push.

- No updater code exists in `main_kiosk.py`, `install_kiosk.bat` or
  `Deploy-Application.ps1` — CONFIRMED by searching all three for update/version-check
  logic; nothing found.
- `DEPLOY_AGENT_LAPTOPS.md` prescribes the manual process in three separate places:
  "transfer updated files and re-run" (`:210`), "deploy the updated `main_kiosk.py`"
  (`:318-320`), "Push the updated `deploy/Files/main_kiosk.py` and re-run the installer"
  (`:345`).
- The server cannot tell which build a kiosk is running: `client_version` is accepted in
  the payload (`server.py:966`) and **never stored** (`server.py:98-107`).

**Updating a kiosk therefore means an administrator touching that laptop.** For ~60
machines this is the dominant cost in any migration, and the reason the phased plan in
[SECURITY_MIGRATION_PROPOSAL.md](SECURITY_MIGRATION_PROPOSAL.md) front-loads every
server-only change.

---

## 7. Inventory of installed kiosks

NOT FOUND — no machine-readable inventory exists.

- `DEPLOY_AGENT_LAPTOPS.md:482-495` ("B9 — Per-laptop install record") provides a table
  with hostname / agent / date / result columns and instructs "Update this table after each
  laptop is done." **Every row is empty.**
- The server records `agent_hostname` per status event (`server.py:100`), so the set of
  hostnames seen in `status_log` is the only *de facto* inventory. It is observational:
  it lists machines that have reported, not machines that are installed, and it is
  attacker-controlled (§5).

This is the single biggest obstacle to Phase 3 ("switch the legacy path off when the last
old kiosk is gone"): today there is no way to answer "how many are left?" except by
querying hostnames seen in `status_log`.

---

## 8. Runtime behaviour relevant to security

| Behaviour | Value | Source |
|---|---|---|
| Auto-logout timer | **11 hours** (`AUTO_LOGOUT_SECONDS = 11 * 3600`) | `main_kiosk.py:784` |
| Timer basis | real shift start from `GET /api/shift-status`, else process start | `main_kiosk.py:796-806` |
| Offline behaviour | failed POSTs are queued to `pending_queue.json` and retried | `main_kiosk.py:349-384` |
| TLS verification | **disabled on every request** (`verify=False`) | `main_kiosk.py:389, 400, 417, 451, 706, 821` |
| Credential transport | `X-API-Key` header only — never the `?token=` query form | `main_kiosk.py:389, 417, 451, 706, 821` |

> **Contradiction with existing documentation.** `ShiftKiosk/CODE_OVERVIEW.md` states a
> "9-hour auto-logout" in three places (`_start_auto_timer`, `Phase1Kiosk`,
> `Phase2Widget`). The code says **11 hours** (`main_kiosk.py:784`). The code is
> authoritative. See [KIOSK_GAPS_AND_CONTRADICTIONS.md](KIOSK_GAPS_AND_CONTRADICTIONS.md) § C2.

The `verify=False` finding and the header-only credential transport are both load-bearing
for the migration: the first is a standalone defect to fix during Phase 1, the second is
the evidence that disabling `?token=` authentication in Phase 0 breaks nothing.
