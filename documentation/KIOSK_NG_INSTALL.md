# New kiosk client — test laptop procedure

One page. The laptop has admin rights. The **old kiosk keeps running throughout** —
this installs beside it, into a different folder, with differently named tasks.

Package: `C:\ShiftKiosk_secure\kiosk-ng\`

---

## Before you leave the server

Issue a device token for that specific laptop:

```powershell
cd C:\ShiftKiosk_secure
& $PY tools\admin_cli.py --config C:\ShiftKiosk\server\server_config.ini --i-mean-production `
      device enroll --hostname <LAPTOP-NAME> --out C:\temp\<LAPTOP-NAME>.token.json
```

Only the SHA-256 hash is stored on the server. Copy the `.json` to the laptop and
delete your copy afterwards.

---

## On the laptop (as Administrator)

### 1. Copy the package

Copy the whole `kiosk-ng` folder and the token file, e.g. to `C:\temp\kiosk-ng\`.

### 2. Set the server address

Edit `C:\temp\kiosk-ng\config.ini`:

```ini
[Server]
url = http://<your server>:8000      ; the address the kiosks use today
api_key = <the current legacy key>   ; FALLBACK only - see note below
verify_tls = true                    ; false only for a plain-http test server
```

**The address lives here on purpose.** When the server moves to its real host you
change this one line and restart the broker task — no rebuild, no reinstall.

**Why the legacy key is still in the file:** it is the fallback. If the device
token is revoked or the broker cannot start, the client keeps working on the old
credential. An agent is never blocked from starting a shift. That is the rule.

### 3. Install

```cmd
cd C:\temp\kiosk-ng
install_kiosk_ng.bat C:\temp\<LAPTOP-NAME>.token.json
```

Installs to `C:\ShiftKioskNG`. Registers two **new** tasks:
`ShiftKioskNG-Broker` (SYSTEM, at startup) and `ShiftKioskNG-UI` (at logon).
`C:\ShiftKiosk` and the old `ShiftKiosk` task are not touched.

Then delete your copy of the token file.

### 4. Collect the facts I still do not have

```powershell
powershell -ExecutionPolicy Bypass -File C:\temp\kiosk-ng\collect_laptop_facts.ps1
```

Writes one file to your Desktop. **Send me that file.** It answers three things I
could not determine from the repository: whether the laptop is domain/Entra
joined, whether there is a GPO/Intune/SCCM to push software with, and whether a
standard agent can write to `C:\ShiftKiosk`. It contains no passwords and no
device token — only the ACL of the token file, not its contents.

### 5. Log off and back on

The UI task starts one minute after logon.

---

## Smoke test

| # | Check | How | Expected |
|---|---|---|---|
| 1 | Phase 1 appears | log on, wait ~1 min | the same fullscreen screen as today |
| 2 | ON SHIFT works | click it | Phase 2 widget appears. **No prompt, no password, nothing new for the agent** |
| 3 | Broker is up | `powershell -c "[System.IO.Directory]::GetFiles('\\.\pipe\') -like '*ShiftKioskBroker*'"` | one match |
| 4 | Transport is on the new path | `& "C:\Program Files\Python312\python.exe" -c "import sys;sys.path.insert(0,r'C:\ShiftKioskNG');import ngtransport as t;print(t.diagnostics())"` | `broker: True`, `mode: device-token`, `identity: <the Windows user>` |
| 5 | The server sees it | on the server: `SELECT TOP 5 agent_username, agent_hostname, client_version, device_id FROM status_log ORDER BY id DESC` | real username, real hostname, `3.0.0-ng`, a device id |
| 6 | **Identity is OS-derived** | nothing to do — check row 5 | `agent_username` is the Windows user, not something the client chose |
| 7 | Outage survival | unplug the network, click SICK LEAVE, plug back in | `C:\ShiftKioskNG\broker.log`: `queued`, then `flushed 1 queued event(s)`; the row appears in the DB |
| 8 | **Hotkey sends a status** | press `Ctrl+Shift+Alt+A` | the client exits **and** an `OFF` row appears with note `Supervisor exit (Ctrl+Shift+Alt+A)`. The old client exited silently |
| 9 | Token revocation degrades, never blocks | server: `admin_cli.py device revoke --device-id <id>`; wait 60 s; click a status on the laptop | still works, now on the legacy key. `broker.log`: `device token refused ... falling back` |
| 10 | Reboot | restart the laptop | both tasks return, no crash loop in `broker.log` |
| 11 | Old kiosk untouched | `schtasks /Query /TN "ShiftKiosk"` | still registered, still running as before |

---

## Uninstall / rollback

```cmd
cd C:\temp\kiosk-ng
uninstall_kiosk_ng.bat
```

Removes the two NG tasks, kills the NG processes, deletes `C:\ShiftKioskNG`, and
prints a confirmation that the old kiosk is still registered. It never touches
`C:\ShiftKiosk`, Python or the vendor folder.

Revoke the device on the server if the laptop is being retired:
`admin_cli.py device revoke --device-id <id>`.

---

## What this does and does not achieve — read before judging it

**Does:** the device is authenticated with a token the agent cannot read
(`device.token.json` is ACL'd to SYSTEM + Administrators), and the username is
taken from the OS by the broker via named-pipe impersonation rather than from the
request body. A client that sends `agent_username: "ATTACKER-CHOSEN"` is recorded
as the real Windows user — verified by `tests/test_broker_identity.py`.

**Does not:** stop a **local administrator**. On this test laptop the user has
admin rights, so they can read the token file, stop the broker, or run code as
SYSTEM. For that machine this is device binding plus evidence, nothing more.

**Is not user authentication.** Whoever is logged on at a kiosk is who the server
will believe. That is the ceiling of "agents never authenticate", which is the
constraint you set. I am not going to describe it as more than it is.

---

## Not in this build, and what each would need

| Later | Needs |
|---|---|
| Client certificates | a CA, per-device enrollment at install, a reverse proxy doing mTLS. The protocol does not change: swap the device-token check in `security/core.py:classify` for a certificate thumbprint; `sec_devices` already has the column shape |
| Kerberos / Windows SSO | the laptops domain-joined (step 4 answers this), the server domain-joined with an SPN, Negotiate at a reverse proxy, and a domain path for home workers |
| Signed self-updater | a signing key, and a `GET /api/client-package` endpoint. The updater must run as SYSTEM in its own task because the UI task runs as Users and cannot replace its own files. **Signature verification is mandatory** — an updater is a remote-code-execution channel onto 130 laptops |
