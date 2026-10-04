param(
    [ValidateSet('Build', 'Stage', 'Promote', 'Rollback')][string]$Mode = 'Build',
    [int]$Port = 5057,
    [string]$RuntimeConfig = 'C:\ProgramData\PulseStage\backend.json',
    [switch]$SkipBuild,
    [string]$Snapshot
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$frontend = Join-Path $root 'Frontend'
$backend = Join-Path $root 'Backend'
$wwwroot = Join-Path $backend 'wwwroot'
$release = Join-Path $backend 'bin\Release\net8.0'

if ($Mode -in @('Promote', 'Rollback')) {
    & (Join-Path $root 'tools\Invoke-PulsePromotion.ps1') -Mode $Mode -Snapshot $Snapshot
    return
}
if ($Mode -eq 'Stage') {
    if ($root -ne 'C:\GSDDashboard_secure') { throw 'Stage must run from the secure repository.' }
    if ($Port -in @(5000, 8000) -or $Port -lt 1024) { throw 'Unsafe stage port.' }
    if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
        throw 'Stage port already in use.'
    }
    $config = Get-Content -LiteralPath $RuntimeConfig -Raw | ConvertFrom-Json
    $cs = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $cs.set_ConnectionString($config.ConnectionStrings.DefaultConnection)
    if ($cs.get_InitialCatalog() -notin @('GSDDashboardTest', 'GSDDashboardCopyTest')) {
        throw 'Stage requires a Pulse test database.'
    }
}

if (!$SkipBuild) {
Push-Location $frontend
try {
    npm run build
    if ($LASTEXITCODE) { throw 'Frontend build failed.' }
} finally { Pop-Location }
if (!(Test-Path -LiteralPath (Join-Path $wwwroot 'index.html'))) {
    throw 'Vite output missing from Backend\wwwroot.'
}
$bad = Get-ChildItem -LiteralPath (Join-Path $wwwroot 'assets') -Filter '*.js' |
    Select-String -Pattern 'ssr7tm2l-8000|X-API-Key|KIOSK_KEY' -Quiet
if ($bad) { throw 'Forbidden direct-kiosk reference in JavaScript.' }

dotnet build (Join-Path $backend 'GSDDashboard.API.csproj') --configuration Release --nologo
if ($LASTEXITCODE) { throw 'Backend build failed.' }
if (Test-Path -LiteralPath (Join-Path $release 'wwwroot')) {
    Remove-Item -LiteralPath (Join-Path $release 'wwwroot') -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $release 'wwwroot') -Force | Out-Null
Copy-Item -Path (Join-Path $wwwroot '*') -Destination (Join-Path $release 'wwwroot') -Recurse -Force
Write-Output "Build ready: $release"
}

if ($Mode -eq 'Stage') {
    $previous = $env:PULSE_RUNTIME_CONFIG
    try {
        $env:PULSE_RUNTIME_CONFIG = $RuntimeConfig
        $process = Start-Process -FilePath 'dotnet' -WorkingDirectory $release -PassThru `
            -ArgumentList @((Join-Path $release 'GSDDashboard.API.dll'), '--urls', "http://127.0.0.1:$Port") `
            -RedirectStandardOutput (Join-Path (Split-Path $RuntimeConfig) 'stage.stdout.log') `
            -RedirectStandardError (Join-Path (Split-Path $RuntimeConfig) 'stage.stderr.log')
    } finally { $env:PULSE_RUNTIME_CONFIG = $previous }
    try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw 'Stage exited; inspect restricted stage logs.' }
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch [System.Net.WebException] { Start-Sleep -Seconds 2 }
          catch [System.Net.Http.HttpRequestException] { Start-Sleep -Seconds 2 }
          catch [System.Threading.Tasks.TaskCanceledException] { Start-Sleep -Seconds 2 }
    }
    if (!$ready) {
        Stop-Process -Id $process.Id -Force
        throw 'Stage readiness timed out.'
    }
    $process.Id | Set-Content -LiteralPath (Join-Path (Split-Path $RuntimeConfig) 'stage.pid')
    Write-Output "Stage PID $($process.Id): http://127.0.0.1:$Port/wic-attendance"
    Wait-Process -Id $process.Id
    }
    finally {
        if (!$process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}
