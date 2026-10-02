# Live deploy runbook

Follow top to bottom. Every step has: the exact command, the expected output, the
**stop criterion**, and the rollback. If a stop criterion fires, roll back that
step and do not continue.

**Nothing in this branch has been deployed or run against a live system.** All
verification was against `ShiftKioskTest`, `GSDDashboardTest` and
`GSDDashboardCopyTest` on test ports.

Branch: `security/kiosk-hardening` in `C:\ShiftKiosk_secure` and `C:\GSDDashboard_secure`.

Shorthand used below:
```
PY  = C:\Users\S69307\AppData\Local\Programs\Python\Python312-32\python.exe
SK  = C:\ShiftKiosk_secure
CFG = C:\ShiftKiosk\server\server_config.ini
KEY = the current legacy kiosk api_key (read it out of CFG, do not paste it anywhere)
USR = any real windows_username from the agents table, e.g. one of your own agents
```

---

## Step 0 — Backups (you said these exist; here is how to confirm)

You have already taken the backups. Confirm they are real and restorable:

```powershell
Get-ChildItem C:\SQLBackup\*.bak | Sort-Object LastWriteTime -Descending |
    Select-Object Name, Length, LastWriteTime -First 5
```

**Expected:** a `.bak` for **both** `ShiftKioskDB` and `GSDDashboard`, dated tonight,
each more than a few MB.

Then verify each one is restorable (this reads the file, not the database):

```powershell
sqlcmd -S .\SQLEXPRESS -Q "RESTORE VERIFYONLY FROM DISK = N'C:\SQLBackup\<file>.bak' WITH CHECKSUM"
```

**Expected:** `The backup set on file 1 is valid.`
**STOP IF:** the file is missing, is from an earlier day, or `VERIFYONLY` fails.
A backup you have not verified is not a backup.

**Rollback:** n/a — nothing has changed yet.

---

## Step 1 — Preflight the scripts

```powershell
cd C:\ShiftKiosk_secure
& $PY tools\preflight_guard.py --all
```

**Expected:** `All clear: no live-database target found.` and one OWNER-ONLY line
for `backup_live_db.sql`.
**STOP IF:** any line says `FAIL`. This guard exists because
`Backend\schema.sql` line 15 is `USE GSDDashboard;` — it targets production
regardless of `sqlcmd -d`. Never run that file.

**How to confirm the guard is really there:** `& $PY tools\preflight_guard.py C:\GSDDashboard\Backend\schema.sql`
must print `BLOCKED`.

---

## Step 2 — Baseline, before anything changes

```powershell
& $PY tools\verify_live.py --base http://localhost:8000 --key "<KEY>" --username <USR>
```

**Expected:** the six kiosk calls `PASS`. The "must not reach" and "docs" blocks
will FAIL at this point — that is correct, nothing is enforced yet.
**STOP IF:** any of the six fails *now*. Then the problem predates this work.

Keep this output. It is your before-picture.

**Rollback:** n/a — read-only.

---

## Step 3 — Apply the migration

```powershell
sqlcmd -S .\SQLEXPRESS -d ShiftKioskDB -i C:\ShiftKiosk_secure\sql\001_security_additive.sql -b
sqlcmd -S .\SQLEXPRESS -d ShiftKioskDB -i C:\ShiftKiosk_secure\sql\002_seed_user_hosts.sql -b
```

**Expected:** a `+ ... added` / `+ ... created` line per object, then
`001_security_additive.sql complete.`; then a `seeded_user_host_pairs` count
(62 on the test copy).

**Confirm it is there:**
```powershell
sqlcmd -S .\SQLEXPRESS -d ShiftKioskDB -Q "SELECT COUNT(*) AS sec_tables FROM sys.tables WHERE name LIKE 'sec[_]%'"
```
**Expected:** `9`.

**STOP IF:** fewer than 9, or any statement errored.
**Rollback:** `sqlcmd -S .\SQLEXPRESS -d ShiftKioskDB -i C:\ShiftKiosk_secure\sql\001_security_additive_rollback.sql -b`
— but see the ordering warning in Step 5.

**Run step 2 again now.** The migration alone must change nothing for a kiosk.

---

## Step 4 — Copy the code, flags OFF

Copy from `C:\ShiftKiosk_secure\server\` to `C:\ShiftKiosk\server\`:

- `security\` (the whole folder)
- `server.py`

Then add the `dan0` profile to `CFG` and restart:

```powershell
cd C:\ShiftKiosk_secure
powershell -ExecutionPolicy Bypass -File tools\set_profile.ps1 -Profile dan0 -Config "<CFG>" -WhatIf
powershell -ExecutionPolicy Bypass -File tools\set_profile.ps1 -Profile dan0 -Config "<CFG>" -Restart
```

**Expected:** `Backup written: server_config.ini.bak-<stamp>`, `Profile dan0 applied.`,
`Restart requested.`

**Confirm it is there:**
```powershell
Select-String -Path "<CFG>" -Pattern '^\[Security\]' -Context 0,11
Get-Content C:\ShiftKiosk\server\server.log -Tail 5
```
**Expected in the log:** `schema probe: status_log extra columns=True, manual_actions actor columns=True`
and `security layer active | flags: ... legacy_scope=off ...`

**STOP IF:** the log shows `extra columns=False` — that means step 3 did not
take and you are about to run the new code against the old schema. (The code
handles it: the INSERT adapts and `/api/status` keeps working. But fix the cause
before going on.)

**Then run step 2 again.** All six must still PASS.

**Rollback:** `tools\set_profile.ps1 -Rollback -Config "<CFG>" -Restart`, and
restore the previous `server.py` / delete `security\`.

> **Ordering rule, do not break it:** code may run against the old schema (it
> adapts), but the rollback script must NOT run while the new code is live
> without reverting the code first. Revert code, then roll back SQL.

---

## Step 5 — Watch dan0 for a day

```powershell
cd C:\ShiftKiosk_secure
& $PY tools\admin_cli.py --config "<CFG>" --i-mean-production legacy-report
```

**Expected:** a `legacy_key` row per kiosk hostname, with `client_version 2.1.1`
and a recent `last_seen`. **This is the kiosk inventory that did not exist before.**

**Look at what the next steps would block, before they block it:**
```sql
SELECT TOP 50 ts, credential_type, source_ip, hostname, path, rule_id, reason
FROM sec_observation_log WHERE decision = 'would_block' ORDER BY id DESC;
```

**STOP IF:** a `would_block` row names one of the six kiosk paths, or a hostname
you recognise as a kiosk. Investigate before enforcing anything.

**Criterion to continue:** at least 24 h of data, every hostname in the report is
one you recognise, and no `would_block` on the six paths.

---

## Step 6 — dan1 (docs + key-in-URL)

```powershell
powershell -ExecutionPolicy Bypass -File tools\set_profile.ps1 -Profile dan1 -Config "<CFG>" -Restart
& $PY tools\verify_live.py --base http://localhost:8000 --key "<KEY>" --username <USR>
```

**Expected:** six kiosk calls PASS; the two docs checks now PASS (404).
**STOP IF:** any of the six fails.
**Rollback:** `tools\set_profile.ps1 -Rollback -Config "<CFG>" -Restart`.

Also open the dashboard in a browser and click through Live, Hours Report, WIC —
nothing should have changed.

---

## Step 7 — dan2 (scope the legacy credential)

**Precondition:** step 5 clean for 24 h.

```powershell
powershell -ExecutionPolicy Bypass -File tools\set_profile.ps1 -Profile dan2 -Config "<CFG>" -Restart
& $PY tools\verify_live.py --base http://localhost:8000 --key "<KEY>" --username <USR>
```

**Expected:** six PASS; `/api/history`, `/api/audit-log`,
`/api/agents/export/csv` now PASS (403).
**STOP IF:** any of the six fails, or the dashboard stops working.

> The dashboard is **not** affected: its session token is deliberately outside
> this rule (`security/core.py`, the `legacy_scope` block). That was found the
> hard way — the first version scoped session tokens too and returned 403 for all
> ten dashboard GETs. Proven fixed by `tests/test_consumers.py --profile dan2`.

**Rollback:** `-Rollback -Restart`.

---

## Step 8 — dan3 (strip identifiers) — **Pulse must be deployed first**

**HARD PRECONDITION.** The old Pulse frontend fetches the kiosk `/api/attendance`
directly and keys its maps on `employee_id` (`Attendance.tsx:98`,
`WicAttendance.tsx:811`). This flag removes that field. You must first deploy:

1. the Pulse backend with `Services/KioskProxyService.cs` and the
   `/api/kiosk/attendance` endpoint, and
2. the Pulse frontend build that calls it.

And configure the service credential:

```powershell
cd C:\ShiftKiosk_secure
& $PY tools\admin_cli.py --config "<CFG>" --i-mean-production device enroll --hostname pulse-backend --out C:\temp\pulse-backend.token.json
```

Put `device_id` and `device_token` from that file into Pulse's `appsettings.json`:

```json
"Kiosk": { "BaseUrl": "http://localhost:8000", "ServiceDeviceId": "...", "ServiceToken": "..." }
```

**Confirm it is there**, before flipping the flag:
```powershell
curl.exe -s "http://localhost:5000/api/kiosk/attendance" | Select-String "employee_id" | Select-Object -First 1
```
**Expected:** a match. **STOP IF:** empty, or the Pulse log says
`Kiosk:ServiceToken is not configured` — the proxy refuses to call anonymously
on purpose, because doing so would silently serve stripped data.

Then:

```powershell
powershell -ExecutionPolicy Bypass -File tools\set_profile.ps1 -Profile dan3 -Config "<CFG>" -Restart
& $PY tools\verify_live.py --base http://localhost:8000 --key "<KEY>" --username <USR>
```

**Expected:** six PASS. Open Pulse → Attendance and WIC Attendance: check-in times
and names still render.
**STOP IF:** either page shows everyone as "No Check-in" — that is the old
frontend still being served. Roll back immediately.

**Rollback:** `-Rollback -Restart`.

While `session_auth` is still shadow, the dashboard's Agents tab shows a dash in
the Employee-ID and Windows-user columns. Expected; they return at dan4.

---

## Step 9 — Supervisor accounts, and **test the escape first**

```powershell
cd C:\ShiftKiosk_secure\tools
powershell -ExecutionPolicy Bypass -File create_supervisors.ps1 -Config "<CFG>" -Production
```

**Expected:** `OK` for 9 accounts + `breakglass`, and
`Secrets written to: C:\ShiftKiosk\server\SUPERVISOR_SECRETS.txt` with the ACL
restricted to Administrators + SYSTEM.

Hand out the blocks, then **delete that file**.

### Test the escape BEFORE you enforce anything

This is mandatory and takes two minutes:

1. Edit `CFG`: `session_auth = off`, `supervisor_auth = off`. Restart the task.
2. Open `http://localhost:8000/dashboard` — it must open with no cookie.
3. Put them back to `shadow`. Restart.

If step 2 fails, do **not** proceed to dan4. The escape hatch is the only thing
standing between a misconfiguration and a locked-out dashboard.

### Then sign in yourself

```
http://localhost:8000/login
```

**Expected:** a sign-in form; your username + password + 6-digit code gets you to
the dashboard.
**STOP IF:** you cannot sign in. Stay at dan3.

---

## Step 10 — dan4 (require sign-in) — only when you are ready

**Preconditions:** step 9 done, escape tested, you have signed in yourself, and
the Pulse frontend with the username/password/TOTP fields is deployed.

```powershell
powershell -ExecutionPolicy Bypass -File tools\set_profile.ps1 -Profile dan4 -Config "<CFG>" -Restart
```

And in Pulse `appsettings.json` (**both** the source and
`bin\Release\net8.0\appsettings.json`): `"SupervisorMode": "enforce"`.

**Expected:** `/dashboard` without a cookie redirects to `/login`; Pulse refuses
KID-only sign-in; the dashboard's Agents tab shows the identifiers again.

**Consequence, accept it deliberately:** agents lose the Pulse `/my` page. You
specified three roles only.

**STOP IF:** you cannot sign in to either. **Rollback:** `-Rollback -Restart` and
`"SupervisorMode": "off"`.

---

## Step 11 — Leave the rest alone for now

`kiosk_min_data`, `device_binding` and `rate_limit` stay at `shadow`. They are the
only flags that can touch the six kiosk endpoints. Promote them only after 14 days
with zero `would_block` rows belonging to a kiosk hostname.

Note: `kiosk_min_data` is currently **declared but not implemented** — it is a
no-op in all three modes. It is a placeholder for scoping the kiosk endpoints to
the bound user, which needs device binding first.

---

## Rollback summary

| What | Command |
|---|---|
| Any flag step | `tools\set_profile.ps1 -Rollback -Config "<CFG>" -Restart` |
| All new behaviour | profile `dan0` |
| Pulse auth | `"SupervisorMode": "off"` in both appsettings |
| Pulse proxy | revert the frontend build; the old one calls the kiosk directly and works while `anon_identifiers` is not `enforce` |
| Server code | restore the previous `server.py`, delete `security\`, restart |
| Schema | revert the code **first**, then `001_security_additive_rollback.sql` |
| A kiosk laptop | `uninstall_kiosk_ng.bat` |
| A device token | `admin_cli.py device revoke --device-id <id>` — the kiosk degrades to the legacy key and keeps working |

## The one command you run after every step

```powershell
& $PY tools\verify_live.py --base http://localhost:8000 --key "<KEY>" --username <USR>
```

If the six kiosk calls are healthy, the 60 installed kiosks are fine. If they are
not, roll back that step before doing anything else.
