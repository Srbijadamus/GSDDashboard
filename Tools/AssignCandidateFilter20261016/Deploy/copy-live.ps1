param(
    [string]$Source        = 'C:\GSDDashboard_secure\Backend\bin\Release\net8.0',
    [string]$LiveRelease   = 'C:\GSDDashboard\Backend\bin\Release\net8.0',
    [string]$LiveRootWwwroot = 'C:\GSDDashboard\Backend\wwwroot',
    [string]$ExpectedDllSha256 = '',
    [string]$OutFile = 'C:\GSDDashboard_secure\Tools\AssignCandidateFilter20261016\Deploy\copy-receipt.json'
)
# Step 2 of the manual promote: copy the built binaries + static bundle over the live
# Release directory and BOTH live static roots. appsettings*.json are explicitly preserved
# (the live connection string is never replaced by a build artifact), and the old hashed
# assets are removed so no stale bundle stays servable.
$ErrorActionPreference = 'Stop'

function Sha([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash }

$before = [pscustomobject]@{
    dll      = Sha (Join-Path $LiveRelease 'GSDDashboard.API.dll')
    bundle   = (Get-ChildItem (Join-Path $LiveRelease 'wwwroot\assets') -Filter 'index-*.js').Name
    css      = (Get-ChildItem (Join-Path $LiveRelease 'wwwroot\assets') -Filter 'index-*.css').Name
    index    = Sha (Join-Path $LiveRelease 'wwwroot\index.html')
    appsettings = Sha (Join-Path $LiveRelease 'appsettings.json')
}
Write-Output "before: DLL $($before.dll)  bundle $($before.bundle)  css $($before.css)"

foreach ($f in (Get-ChildItem -LiteralPath $Source -File)) {
    if ($f.Name -like 'appsettings*.json') { Write-Output "preserved: $($f.Name)"; continue }
    Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $LiveRelease $f.Name) -Force
}
foreach ($d in (Get-ChildItem -LiteralPath $Source -Directory)) {
    if ($d.Name -eq 'wwwroot') { continue }
    Copy-Item -LiteralPath $d.FullName -Destination (Join-Path $LiveRelease $d.Name) -Recurse -Force
}

$relWww = Join-Path $LiveRelease 'wwwroot'
if (Test-Path -LiteralPath $relWww) { Remove-Item -LiteralPath $relWww -Recurse -Force }
New-Item -ItemType Directory -Path $relWww -Force | Out-Null
Copy-Item -Path (Join-Path $Source 'wwwroot\*') -Destination $relWww -Recurse -Force

if (Test-Path -LiteralPath $LiveRootWwwroot) { Remove-Item -LiteralPath $LiveRootWwwroot -Recurse -Force }
New-Item -ItemType Directory -Path $LiveRootWwwroot -Force | Out-Null
Copy-Item -Path (Join-Path $Source 'wwwroot\*') -Destination $LiveRootWwwroot -Recurse -Force

$after = [pscustomobject]@{
    dll      = Sha (Join-Path $LiveRelease 'GSDDashboard.API.dll')
    bundle   = (Get-ChildItem (Join-Path $LiveRelease 'wwwroot\assets') -Filter 'index-*.js').Name
    css      = (Get-ChildItem (Join-Path $LiveRelease 'wwwroot\assets') -Filter 'index-*.css').Name
    index    = Sha (Join-Path $LiveRelease 'wwwroot\index.html')
    appsettings = Sha (Join-Path $LiveRelease 'appsettings.json')
    rootIndex = Sha (Join-Path $LiveRootWwwroot 'index.html')
}
if ($ExpectedDllSha256 -and $after.dll -ne $ExpectedDllSha256) { throw "Copied DLL is $($after.dll), expected $ExpectedDllSha256" }
if ($after.appsettings -ne $before.appsettings) { throw 'appsettings.json changed during copy.' }

$receipt = [pscustomobject]@{ when = (Get-Date).ToString('o'); before = $before; after = $after; source = $Source }
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutFile -Encoding UTF8
$after | Format-List
"receipt: $OutFile"
