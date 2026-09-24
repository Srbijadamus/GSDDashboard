# PS1_AuthSmokeTest.ps1 — end-to-end auth/RBAC verification against a RUNNING backend.
# Usage:
#   .\PS1_AuthSmokeTest.ps1 -BaseUrl http://localhost:5050 -ExpectEnforcement  # pre-deploy smoke test (flag ON)
#   .\PS1_AuthSmokeTest.ps1 -BaseUrl http://localhost:5000                     # post-deploy (flag OFF)
#   .\PS1_AuthSmokeTest.ps1 -BaseUrl https://<devtunnel>                       # post-deploy (flag OFF)
# -ExpectEnforcement: pass ONLY when the target runs with Auth:EnforceAuthorization=true.
#   Without it, RBAC block checks expect pass-through (flag OFF behaviour) and the
#   POST /api/bo-list write test is SKIPPED ENTIRELY — with the flag OFF that POST
#   would SUCCEED and write junk into the live database.
#   NEVER pass -ExpectEnforcement against port 5000 while the deployed
#   bin\Release\net8.0\appsettings.json still has "EnforceAuthorization": false.
# READ-ONLY against business data: the only write ever attempted (and only with
# -ExpectEnforcement) is one RBAC must REJECT.
param([string]$BaseUrl = "http://localhost:5050", [switch]$ExpectEnforcement)
$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')
Write-Host ("Target: {0}  |  Auth:EnforceAuthorization expected = {1}" -f $BaseUrl, $(if ($ExpectEnforcement) { 'ON' } else { 'OFF' })) -ForegroundColor Cyan
if ($ExpectEnforcement -and $BaseUrl -match 'localhost:5000') {
  Write-Host "WARNING: -ExpectEnforcement against port 5000. Confirm the deployed appsettings.json has EnforceAuthorization=true, otherwise abort (Ctrl+C)." -ForegroundColor Yellow
}

$script:pass = 0; $script:fail = 0
function Check($label, $cond, $detail = "") {
  if ($cond) { $script:pass++; Write-Host "PASS  $label" -ForegroundColor Green }
  else       { $script:fail++; Write-Host "FAIL  $label  $detail" -ForegroundColor Red }
}
function Try-Req($method, $url, $session = $null, $body = $null) {
  $p = @{ Method = $method; Uri = "$BaseUrl$url"; ErrorAction = 'Stop'; UseBasicParsing = $true }
  if ($session) { $p.WebSession = $session }
  if ($body)    { $p.Body = ($body | ConvertTo-Json); $p.ContentType = 'application/json' }
  try   { $r = Invoke-WebRequest @p; return @{ Status = [int]$r.StatusCode; Content = $r.Content; Headers = $r.Headers } }
  catch { $resp = $_.Exception.Response
          if ($resp) {
            $bodyText = ''
            try { $sr = New-Object System.IO.StreamReader($resp.GetResponseStream()); $bodyText = $sr.ReadToEnd() } catch {}
            return @{ Status = [int]$resp.StatusCode; Content = $bodyText; Headers = $resp.Headers }
          }
          throw }
}
function Login-Session($kid) {
  $r = Try-Req POST /api/auth/login $null @{ kid = $kid }
  $ws = New-Object Microsoft.PowerShell.Commands.WebRequestSession
  $m = [regex]::Match([string]$r.Headers['Set-Cookie'], 'gsd_auth=[^;]+')
  if ($m.Success) { $ws.Cookies.SetCookies($BaseUrl, $m.Value) }
  return @{ Response = $r; Session = $ws }
}

# Pick a real agent KID + employee id from the DB for the AGENT tests.
$cs = "Server=localhost\SQLEXPRESS;Database=GSDDashboard;Trusted_Connection=true;TrustServerCertificate=true;"
$cn = New-Object System.Data.SqlClient.SqlConnection $cs; $cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandText = "SELECT TOP 1 EmployeeId, PrimaryKid FROM Employees WHERE IsActive=1 AND PrimaryKid IS NOT NULL AND PrimaryKid<>'' ORDER BY EmployeeId"
$rd = $cmd.ExecuteReader(); $rd.Read() | Out-Null
$agentEmpId = $rd.GetString(0); $agentKid = $rd.GetString(1)
$rd.Close()
$cmd.CommandText = "SELECT TOP 1 EmployeeId FROM Employees WHERE IsActive=1 AND EmployeeId<>'$agentEmpId' ORDER BY EmployeeId"
$otherEmpId = [string]$cmd.ExecuteScalar()
$cmd.CommandText = "SELECT TOP 1 EmployeeId FROM Employees WHERE IsActive=1 AND (PrimaryKid IS NULL OR PrimaryKid='') AND (SecondaryKid IS NULL OR SecondaryKid='') ORDER BY EmployeeId"
$noKidEmp = [string]$cmd.ExecuteScalar()
$cn.Close()
Write-Host "Agent under test: $agentKid / $agentEmpId (foreign id: $otherEmpId, no-KID agent: $noKidEmp)" -ForegroundColor Cyan

# --- 0. health endpoint stays anonymous (watchdog) ---
$h = Try-Req GET /health
Check "GET /health anonymous -> 200" ($h.Status -eq 200)

# --- 1. unknown KID -> 401 neutral ---
$u = Try-Req POST /api/auth/login $null @{ kid = 'NO_SUCH_KID_1' }
Check "unknown KID -> 401" ($u.Status -eq 401)
Check "unknown KID message neutral" ($u.Content -match 'Unknown KID') $u.Content

# --- 2. DEV login S69307 ---
$d = Login-Session 'S69307'
Check "DEV login -> 200" ($d.Response.Status -eq 200) ($d.Response.Content)
$setCookie = [string]$d.Response.Headers['Set-Cookie']
Check "Set-Cookie gsd_auth present" ($setCookie -match 'gsd_auth=') ($setCookie)
if ($BaseUrl -like 'https:*') { Check "cookie Secure over https" ($setCookie -match '(?i)secure') ($setCookie) }
else                          { Check "cookie NOT Secure over http (works on http://localhost)" ($setCookie -notmatch '(?i)secure') ($setCookie) }
$me = Try-Req GET /api/auth/me $d.Session
Check "DEV /api/auth/me -> 200 role DEV" (($me.Status -eq 200) -and ($me.Content -match '"role":"DEV"')) ($me.Content)
$meEmp = Try-Req GET /api/employees $d.Session
Check "DEV GET /api/employees -> 200 (bypass)" ($meEmp.Status -eq 200)

# --- 2b. Swagger is DEV-only, ALWAYS (independent of Auth:EnforceAuthorization) ---
$swAnon = Try-Req GET /swagger/index.html
Check "anonymous /swagger UI -> 404 (endpoint map not leaked)" ($swAnon.Status -eq 404)
$swAnonJson = Try-Req GET /swagger/v1/swagger.json
Check "anonymous swagger.json -> 404" ($swAnonJson.Status -eq 404)
$swDev = Try-Req GET /swagger/v1/swagger.json $d.Session
Check "DEV swagger.json -> 200" ($swDev.Status -eq 200)

# --- 3. no session: flag ON -> 401; flag OFF -> pass-through 200 ---
$no = Try-Req GET /api/employees
if ($ExpectEnforcement) {
  Check "no session GET /api/employees -> 401" ($no.Status -eq 401) ($no.Content)
} else {
  Check "no session GET /api/employees -> 200 (flag OFF pass-through)" ($no.Status -eq 200) ($no.Content)
}

# --- 4. AGENT login + scoping ---
$a = Login-Session $agentKid
Check "AGENT login by KID -> 200 role AGENT" (($a.Response.Status -eq 200) -and ($a.Response.Content -match '"role":"AGENT"')) ($a.Response.Content)
$own   = Try-Req GET "/api/employees/$agentEmpId" $a.Session
Check "AGENT own record -> 200" ($own.Status -eq 200)
$other = Try-Req GET "/api/employees/$otherEmpId" $a.Session
if ($ExpectEnforcement) {
  Check "AGENT foreign employee record -> 403" ($other.Status -eq 403) ($other.Content)
} else {
  Check "AGENT foreign employee record -> 200 (flag OFF pass-through)" ($other.Status -eq 200) ($other.Content)
}
$list  = Try-Req GET /api/employees $a.Session
if ($ExpectEnforcement) {
  Check "AGENT employee roster -> 403" ($list.Status -eq 403) ($list.Content)
} else {
  Check "AGENT employee roster -> 200 (flag OFF pass-through)" ($list.Status -eq 200) ($list.Content)
}
$tl    = Try-Req GET "/api/employees/$agentEmpId/timeline" $a.Session
Check "AGENT own timeline -> 200" ($tl.Status -eq 200)
$vacOwn = Try-Req GET "/api/vacations?employeeId=$agentEmpId" $a.Session
Check "AGENT own vacations -> 200" ($vacOwn.Status -eq 200)
$vacAll = Try-Req GET /api/vacations $a.Session
if ($ExpectEnforcement) {
  Check "AGENT unfiltered vacations -> 403" ($vacAll.Status -eq 403) ($vacAll.Content)
} else {
  Check "AGENT unfiltered vacations -> 200 (flag OFF pass-through)" ($vacAll.Status -eq 200) ($vacAll.Content)
}
# The ONLY write in this script. With the flag ON, RBAC must REJECT it (403).
# With the flag OFF it would SUCCEED and write junk into the live DB -> SKIPPED ENTIRELY.
if ($ExpectEnforcement) {
  $write = Try-Req POST /api/bo-list $a.Session @{ employeeName = 'x'; entryDate = '2026-01-01' }
  Check "AGENT write -> 403" ($write.Status -eq 403) ($write.Content)
} else {
  Write-Host "SKIP  AGENT write POST /api/bo-list - flag OFF, write would SUCCEED (junk in live DB)" -ForegroundColor Yellow
}
$wicOwn = Try-Req GET "/api/wic?employeeId=$agentEmpId" $a.Session
Check "AGENT own WIC shifts -> 200" ($wicOwn.Status -eq 200)

# --- 5. employee-number login (rule c) for an agent WITHOUT a KID ---
$e = Try-Req POST /api/auth/login $null @{ kid = $noKidEmp }
Check "employee-number login (rule c) -> 200 role AGENT" (($e.Status -eq 200) -and ($e.Content -match '"role":"AGENT"') -and ($e.Content -match $noKidEmp)) ($e.Content)

# --- 6. TEAM_LEAD placeholder cannot log in (empty Kid) ---
$t = Try-Req POST /api/auth/login $null @{ kid = 'Tobias Rossberg' }
Check "TEAM_LEAD placeholder name -> 401" ($t.Status -eq 401)

# --- 7. rate limit: login is capped at 10/min/IP (earlier calls consumed budget) ---
$got429 = $false
1..20 | ForEach-Object {
  $r = Try-Req POST /api/auth/login $null @{ kid = "RL_TEST_$_" }
  if ($r.Status -eq 429) { $got429 = $true }
}
Check "rate limit kicks in (429 observed)" $got429

Write-Host ""
if ($script:fail -eq 0) {
  Write-Host ("RESULT: {0} passed, 0 failed" -f $script:pass) -ForegroundColor Green
} else {
  Write-Host ("RESULT: {0} passed, {1} failed" -f $script:pass, $script:fail) -ForegroundColor Red
  exit 1
}


