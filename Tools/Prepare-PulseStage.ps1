param([switch]$EnrollProductionService)
$ErrorActionPreference = 'Stop'
$directory = 'C:\ProgramData\PulseStage'
$configuration = Join-Path $directory 'backend.json'
if (Test-Path -LiteralPath $configuration) { throw 'Stage configuration exists; do not enroll twice.' }
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
    $identity = New-Object System.Security.Principal.SecurityIdentifier($sid)
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $directory -AclObject $acl
if (!$EnrollProductionService) { throw 'Explicit -EnrollProductionService is required for one live service-device enrollment.' }
$temporary = Join-Path $directory 'enrollment.json'
if (Test-Path -LiteralPath $temporary) { throw 'Enrollment receipt exists; inspect privately, do not repeat enrollment.' }
& 'C:\Users\S69307\AppData\Local\Programs\Python\Python312-32\python.exe' `
    'C:\ShiftKiosk_secure\tools\admin_cli.py' --config 'C:\ShiftKiosk\server\server_config.ini' `
    --i-mean-production device enroll --hostname pulse-backend --out $temporary
if ($LASTEXITCODE) { throw 'Enrollment failed; do not retry without checking device state.' }
$device = Get-Content -LiteralPath $temporary -Raw | ConvertFrom-Json
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Backend\appsettings.json') -Raw | ConvertFrom-Json
$cs = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$cs.set_ConnectionString($source.ConnectionStrings.DefaultConnection)
$cs.set_InitialCatalog('GSDDashboardCopyTest')
@{
    ConnectionStrings = @{ DefaultConnection = $cs.get_ConnectionString() }
    Kiosk = @{ BaseUrl = 'http://localhost:8000'; ServiceDeviceId = $device.device_id; ServiceToken = $device.device_token }
    Auth = @{ CookieName = 'pulse_stage_auth' }
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configuration -Encoding UTF8
Remove-Item -LiteralPath $temporary -Force
Write-Output 'Service credential stored only in restricted backend.json; enrollment receipt deleted.'
