# TASK: Add KID-only login + role-based access control to GSDDashboard
# PHASE 1 IS READ-ONLY. Do NOT write any code until Phase 1 is reported and approved.

## PROJECT CONTEXT
- Project  : C:\GSDDashboard  (ASP.NET Core 8 backend + React/Vite frontend)
- Backend  : C:\GSDDashboard\Backend  (port 5000, serves frontend from wwwroot)
- Live     : runs from Backend\bin\Release\net8.0 via scheduled task GSDDashboard-Backend
             (watchdog C:\HealthCheck\watchdog_gsd_backend.ps1)
- Frontend : C:\GSDDashboard\Frontend\src
- DB       : SQL Server Express, database GSDDashboard
- Docs     : C:\GSDDashboard\documentation\  (handoff_prompt1..4.md, handoff_final.md)
- WARNING: other projects run on this machine (ports 5016 and 8000 - WIC Asset Tracker,
  Shift Kiosk). Touch NOTHING outside C:\GSDDashboard. Never kill their processes.
- WARNING: the Shift Kiosk feeds this app (WicAttendance.tsx polls the kiosk API every
  60s). Machine-to-machine traffic must still work after auth is added.

=====================================================================
## PHASE 1 - READ AND REPORT ONLY. NO CODE CHANGES. STOP AT THE END.
=====================================================================
1. Read ALL documentation in C:\GSDDashboard\documentation\ (every handoff_*.md), plus
   CLAUDE.md / README.md if present. Summarise what they say about employees, team
   leads, roles, and any existing authentication.

2. Does ANY authentication exist today? Grep the backend for: [Authorize],
   AddAuthentication, JwtBearer, Cookie, Session, Login, Identity, and read the
   Program.cs middleware pipeline. Report what is actually there, as a finding from
   the grep, not an assumption.

3. DB schema - report REAL column names, verified by query:
   - Employees: full column list. Which column holds the "KID"?
     NOTE: this ecosystem has two identifier styles - numeric EmployeeId (e.g. 9074573)
     and KID-style codes (e.g. K37144, S60028, Y6525). Confirm whether a KID column
     exists here, its exact name, whether it is unique, whether it is populated for all
     active rows. If KIDs are NOT stored in this DB at all, say so plainly - that
     changes the whole design and I must know before you build anything.
   - Is there a TeamLead / TeamLeadName / role column? Any Users or Roles table?
   - Is there an active/IsActive flag?
   - Run and report the rows for: S60028, Y6525, S69307.

4. Enumerate the API surface: every controller and endpoint, each marked READ (GET) or
   WRITE (POST/PUT/PATCH/DELETE). List every frontend route/page in Frontend\src.

5. Identify which endpoints are called by the kiosk or any non-browser client, so they
   can be exempted or given a service token instead of a user session.

6. Report a PLAN for Phase 2 based on what you actually found - especially where role
   data should live (a column on Employees vs. a small AppUserRoles table) and why.
   Then STOP and wait for my approval.

=====================================================================
## PHASE 2 - IMPLEMENT (only after I approve the Phase 1 report)
=====================================================================

### ROLE MODEL - exactly four roles
  AGENT      -> sees ONLY their own data. Read-only. Zero write access anywhere.
  TEAM_LEAD  -> sees everything, may edit everything.
  RTM        -> sees everything, may edit everything (same rights as TEAM_LEAD).
  DEV        -> full access to everything, always. Never restricted, never locked out.

### LOGIN - KID only, no password
- One input field ("KID"). POST /api/auth/login { kid }.
- Unknown or inactive KID -> 401 with a neutral message ("Unknown KID"). Do not reveal
  whether the KID exists.
- On success create a server-side session in an httpOnly, SameSite cookie carrying
  kid + employeeId + resolved role. No password field anywhere in the UI.
- Add POST /api/auth/logout and GET /api/auth/me (returns kid, name, role).
- Rate-limit /api/auth/login per IP (about 10 attempts per minute). KID-only login has
  no second factor, so this is the only barrier between a guessed KID and the data.

### ROLE SEED
- DEV       : S69307   (my developer account)
- RTM       : S60028   Silvija Angelova
- RTM       : Y6525    Yiting Qiang
- TEAM_LEAD : derive from the existing team-lead data in the DB. Report exactly how you
  identified them and list the resulting names + KIDs for my confirmation BEFORE seeding.
- Every other valid KID -> AGENT (this is the default, do not hand-maintain a list).
- If any of the three KIDs above has no matching Employees row, STOP and report it.
  Do not invent rows, do not fuzzy-match names.

### ENFORCEMENT - SERVER SIDE, NOT JUST THE UI
Hiding buttons in React is NOT access control. All of this is backend work:
- Every endpoint gets an explicit authorization policy. Default DENY: any endpoint
  without an explicit policy must reject AGENT writes.
- AGENT: every WRITE endpoint returns 403. Every READ endpoint is filtered server-side
  to that agent own EmployeeId - shifts, WIC assignments, absences (AL/SL/OL/UL),
  attendance, everything. An AGENT must not be able to read another agent row by
  changing an id in the URL or a query parameter. Test this explicitly.
- TEAM_LEAD and RTM: unrestricted read and write on the existing functional endpoints.
- DEV: bypasses all policies.
- Kiosk / service traffic: use the exemption or service token you proposed in Phase 1.
  Do not leave those endpoints wide open by accident.

### FRONTEND
- Login page with a single KID field. Unauthenticated users are redirected there.
- Auth context holding the role. Nav items and edit controls hidden per role.
- AGENT view: own shift, own WIC assignment, own absences - rendered read-only
  (no edit buttons, no forms, no drag and drop).
- 403 responses show a clear message instead of a blank screen or a silent failure.

=====================================================================
## PHASE 3 - DEPLOY AND VERIFY (report real output only)
=====================================================================
Deploy the standard way: back up bin\Release\net8.0 to C:\Temp\gsd_auth_backup first,
schtasks /end /tn GSDDashboard-Backend, build -c Release, schtasks /run, health check.
On failure, roll back and STOP.

Then test each role end to end and paste the actual responses:
1. Normal agent KID -> reads own data (200), gets 403 on a write endpoint, and gets
   403 or filtered output when requesting another agent EmployeeId.
2. S60028 and Y6525 -> full read, and one real write succeeds.
3. A team lead KID -> full read and write.
4. S69307 -> everything works, nothing blocked.
5. No session -> protected endpoints return 401, login page renders.
6. Unknown KID -> 401, neutral message.
7. Kiosk integration still works: WicAttendance.tsx still receives data.
8. Build clean (0 errors, 0 warnings), backend healthy on port 5000, ports 5016 and
   8000 untouched and still running.

Write the result into C:\GSDDashboard\documentation\auth_rbac.md: role matrix, where
roles are stored, how to change someone role, and how to recover if I lock myself out.

## RULES
- Phase 1 is read-only. No edits until I approve.
- Never invent a KID, an employee, or a column name. If data is missing, STOP and report.
- Never commit pristup.md, zadatak.txt, analiza.txt, nastavi.txt.
- Do not claim anything works unless you have the command output proving it.
