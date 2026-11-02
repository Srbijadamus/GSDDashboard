# ops/ — operational scripts and scheduled-task definitions

Disaster-recovery copies of the scripts and the scheduled-task definitions that run on the
Pulse cloud PC. This is the reference for rebuilding the operational layer on a new server.

Editing these files does **not** change the live server — copy them to the target server to restore.

## Folder map

| Folder | Contents | Lives on the server |
|---|---|---|
| `tasks/` | XML definitions of the 8 service scheduled tasks | Task Scheduler, root folder `\` |
| `pulsebackend/` | Active Pulse backend watchdog (`watchdog_gsd_backend.ps1`) | `C:\ProgramData\PulseBackend\` |
| `healthcheck/` | Independent health check and the watchdog scripts (9 files) | `C:\HealthCheck\` |
| `pulseops/` | PulseOps scripts (notify / smoke / deploy) and their two task XMLs | `C:\PulseOps\` |

## Which task runs which script

| Scheduled task | Command it runs |
|---|---|
| `GSDDashboard-Backend` | `powershell.exe … -File "C:\ProgramData\PulseBackend\watchdog_gsd_backend.ps1"` (SYSTEM) |
| `ShiftKioskServer` | `powershell.exe … -File "C:\HealthCheck\watchdog_shiftkiosk_server.ps1"` |
| `ShiftKioskTunnel` | `powershell.exe … -File "C:\HealthCheck\watchdog_shiftkiosk_tunnel.ps1"` |
| `GSDDashboard-Tunnel-v2` | `powershell.exe … -File "C:\HealthCheck\watchdog_gsd_tunnel.ps1"` |
| `PuzzledPlaneTunnel` | `powershell.exe … -File "C:\HealthCheck\watchdog_puzzledplane_tunnel.ps1"` |
| `LaptopTracker-App` | `powershell.exe … -File "C:\HealthCheck\watchdog_laptoptracker.ps1"` |
| `ServiceHealthCheck` | `powershell.exe … -ExecutionPolicy Bypass -File "C:\HealthCheck\health_check.ps1"` |
| `PulseRestartNotify` | `python.exe C:\PulseOps\notify_restart.py` (SYSTEM) |
| `PulseSmokeAfterLogon` | `powershell.exe … -File "C:\PulseOps\SmokeAfterLogon.ps1"` |

`GSDDashboard-Backend` and `PulseRestartNotify` run as SYSTEM. The other seven run as the operator
account (interactive logon, highest available rights) — see each XML for the exact principal.

## Backend watchdog — old vs active copy

`healthcheck\watchdog_gsd_backend.ps1` is an **old copy**. The active backend watchdog is
`pulsebackend\watchdog_gsd_backend.ps1` (launched from `C:\ProgramData\PulseBackend\`), as confirmed
by the `GSDDashboard-Backend` task action. The two files differ in content.

## Restore order on a new server

1. Recreate the directories `C:\PulseOps\`, `C:\HealthCheck\` and `C:\ProgramData\PulseBackend\`.
2. Copy the scripts into place:
   - `healthcheck\*.ps1` → `C:\HealthCheck\`
   - `pulsebackend\watchdog_gsd_backend.ps1` → `C:\ProgramData\PulseBackend\`
   - `pulseops\notify_restart.py`, `Smoke-Pulse.ps1`, `SmokeAfterLogon.ps1`, `Deploy-Pulse.ps1` → `C:\PulseOps\`
3. Create the webhook URL file and register `PulseRestartNotify` and `PulseSmokeAfterLogon` — see
   `pulseops/README.md`.
4. Register the eight service tasks from their XMLs:
   ```powershell
   Register-ScheduledTask -TaskName '<name>' -Xml (Get-Content 'tasks\<name>.xml' -Raw)
   ```
   for `GSDDashboard-Backend`, `ShiftKioskServer`, `ShiftKioskTunnel`, `GSDDashboard-Tunnel-v2`,
   `PuzzledPlaneTunnel`, `LaptopTracker-App`, `ServiceHealthCheck`, `PulseRestartNotify`.
5. Verify: `/health` on ports 5000 and 8000, the three dev tunnels, then run `Smoke-Pulse.ps1`.
