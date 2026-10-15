# Regenerates ONLY the `Candidate` half of Tools/pulse-preservation-manifest.json.
#
# Assert-PulsePreservation.ps1 refuses to build or promote when the secure worktree's source
# inventory or any hash differs from the manifest ("Source changed: ... Review before building
# or promoting"), and the documented remedy is to review the change and deliberately regenerate
# the manifest. The `Live` half (C:\GSDDashboard, the live tree) and LiveRoot are never touched
# here: only the candidate inputs of C:\GSDDashboard_secure are re-inventoried and re-hashed,
# with exactly the same selection rule the assert uses.
#
# It writes FILES only — it never touches the database or the running application.
[CmdletBinding()]
param(
    [string]$Root = 'C:\GSDDashboard_secure',
    [switch]$WhatIf
)
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $Root 'Tools\pulse-preservation-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

function Get-SourcePaths([string]$Base) {
    $paths = @(& git -C $Base ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE) { throw 'Cannot inventory source files.' }
    @($paths | Where-Object {
            $_ -match '^(Backend/.*\.(cs|csproj)|Backend.Tests/.*\.(cs|csproj)|Frontend/(src/.*|[^/]+\.(json|ts|js))|import_shifts\.py|import_shifts_xl\.py|shift_import_guard\.py)$' -and
            (Test-Path -LiteralPath (Join-Path $Base $_) -PathType Leaf)
        } | Sort-Object -Unique)
}

$candidate = @(Get-SourcePaths $Root | ForEach-Object {
        [pscustomobject]@{
            Path = $_
            Hash = (Get-FileHash -LiteralPath (Join-Path $Root $_) -Algorithm SHA256).Hash
        }
    })

Write-Output "candidate inputs: was $($manifest.Candidate.Count), now $($candidate.Count)"
$oldByPath = @{}
foreach ($e in $manifest.Candidate) { $oldByPath[$e.Path] = $e.Hash }
foreach ($e in $candidate) {
    if (-not $oldByPath.ContainsKey($e.Path)) { Write-Output "  + $($e.Path)" }
    elseif ($oldByPath[$e.Path] -ne $e.Hash) { Write-Output "  ~ $($e.Path)" }
}
foreach ($e in $manifest.Candidate) {
    if (-not ($candidate.Path -contains $e.Path)) { Write-Output "  - $($e.Path)" }
}

if ($WhatIf) { Write-Output 'WhatIf: manifest not written.'; return }

$updated = [pscustomobject]@{
    LiveRoot     = $manifest.LiveRoot
    CreatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Live         = $manifest.Live
    Candidate    = $candidate
}
$updated | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output "manifest written: $manifestPath (Live untouched: $($manifest.Live.Count) entries)"
