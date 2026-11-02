# PulseOps — server scripts and scheduled-task definitions

Versioned copies of the operational scripts and the two scheduled-task definitions that
live on the Pulse cloud PC, for restoring them onto a new server.

These are disaster-recovery snapshots. Editing them here does **not** change the live
server; copy them to `C:\PulseOps\` on the target server to restore.

## Files

| File | Lives on server | Purpose |
|---|---|---|
| `notify_restart.py` | `C:\PulseOps\notify_restart.py` | One-shot Teams/Power Automate "Pulse server restartovan" notification after OS boot. |
| `Smoke-Pulse.ps1` | `C:\PulseOps\Smoke-Pulse.ps1` | Read-only post-restart smoke test (boot/uptime, restart-notify OK line, service tasks, ports, tunnels, SQL, event log, sentinel, health log). |
| `SmokeAfterLogon.ps1` | `C:\PulseOps\SmokeAfterLogon.ps1` | Runs `Smoke-Pulse.ps1` at the first logon after a restart (uptime < 2 h) and opens the latest result in Notepad. |
| `Deploy-Pulse.ps1` | `C:\PulseOps\Deploy-Pulse.ps1` | Self-contained Pulse backend/frontend deploy with automatic rollback (sentinel → stop the exact port-5000 owner → copy → restart task → wait `/health`). Must run elevated. |
| `tasks/PulseRestartNotify.xml` | scheduled task `\PulseRestartNotify` | SYSTEM task (`AtStartup`, 3 min delay, `MultipleInstances=IgnoreNew`) that runs `python.exe notify_restart.py`. |
| `tasks/PulseSmokeAfterLogon.xml` | scheduled task `\PulseSmokeAfterLogon` | Interactive `S69307` task (`AtLogOn`, 5 min delay, `MultipleInstances=IgnoreNew`) that runs `powershell.exe SmokeAfterLogon.ps1`. |

## Details

### notify_restart.py
- Runs as **SYSTEM** via the `PulseRestartNotify` task.
- Reads the webhook URL from `C:\PulseOps\notify_webhook.url` (a secret, never stored in this repo).
- Appends each attempt to `C:\PulseOps\notify_restart.log`: timestamp (UTC), attempt number, `OK` or error type + HTTP status. It never writes the URL.
- Retries up to 10× every 3 minutes; any HTTP 2xx counts as success.
- The AdaptiveCard is delivered through the Power Automate flow **"Webhookwarnungen an test senden"**.

### Smoke-Pulse.ps1
- Read-only: it does not restart, stop or modify anything.
- Writes its result to `C:\PulseOps\smoke\smoke_<yyyyMMdd-HHmmss>.txt` and prints `UKUPNO: PASS/FAIL`.

### Deploy-Pulse.ps1
- Usage (elevated): `powershell -NoProfile -ExecutionPolicy Bypass -File C:\PulseOps\Deploy-Pulse.ps1 -Source "C:\GSDDashboard_secure\Backend\bin\Release\net8.0"`
- Snapshots to `C:\ProgramData\PulseRollback\<ts>` before replacing files; `appsettings*.json` are never replaced. On failure it restores its own snapshot and exits non-zero.

## Restoring on a new server

1. Copy the four scripts to `C:\PulseOps\`.
2. Create the webhook secret file manually:
   - Put the webhook URL into `C:\PulseOps\notify_webhook.url` (plain text, single line). Copy it from the
     Power Automate flow **"Webhookwarnungen an test senden"** → trigger → **Webhooklink kopieren**.
   - Restrict the ACL to SYSTEM read and Administrators full:
     ```powershell
     icacls C:\PulseOps\notify_webhook.url /inheritance:r /grant "SYSTEM:(R)" "Administrators:(F)"
     ```
   - Never store this file in the repository.
3. Register the two scheduled tasks from the XML definitions:
   ```powershell
   Register-ScheduledTask -TaskName 'PulseRestartNotify'   -Xml (Get-Content 'C:\PulseOps\tasks\PulseRestartNotify.xml' -Raw)
   Register-ScheduledTask -TaskName 'PulseSmokeAfterLogon' -Xml (Get-Content 'C:\PulseOps\tasks\PulseSmokeAfterLogon.xml' -Raw)
   ```

   `PulseSmokeAfterLogon.xml` is a genuine `Export-ScheduledTask` export. `PulseRestartNotify.xml` was
   reconstructed from the registration script, because a SYSTEM task cannot be exported from a
   non-elevated session.

## Not in this repo (by design)

`notify_webhook.url` (secret), `C:\PulseOps\backup\` (employee data), `*.log`, `*.out`,
`C:\PulseOps\smoke\` results, and one-off/temporary scripts are intentionally excluded.
