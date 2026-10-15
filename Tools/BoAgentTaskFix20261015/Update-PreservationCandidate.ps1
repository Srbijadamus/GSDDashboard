# Update-PreservationCandidate.ps1
# Regenerates ONLY the "Candidate" half of Tools\pulse-preservation-manifest.json for
# C:\GSDDashboard_secure, using exactly the inventory rule Assert-PulsePreservation.ps1
# applies (git ls-files --cached --others --exclude-standard + the source-file regex).
# The "Live" half (C:\GSDDashboard) and CreatedAtUtc are never touched.
#
# Run this after an intentional candidate source change and BEFORE the build; the
# preservation assert refuses a build otherwise.
param([string]$Root = 'C:\GSDDashboard_secure')
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $Root 'Tools\pulse-preservation-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

function Get-SourcePaths([string]$R) {
    $paths = @(& git -C $R ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE) { throw 'Cannot inventory source files.' }
    @($paths | Where-Object {
        $_ -match '^(Backend/.*\.(cs|csproj)|Backend.Tests/.*\.(cs|csproj)|Frontend/(src/.*|[^/]+\.(json|ts|js))|import_shifts\.py|import_shifts_xl\.py|shift_import_guard\.py)$' -and
        (Test-Path -LiteralPath (Join-Path $R $_) -PathType Leaf)
    } | Sort-Object -Unique)
}

$before = @{}
foreach ($e in $manifest.Candidate) { $before[$e.Path] = $e.Hash }

$candidate = @(Get-SourcePaths $Root | ForEach-Object {
    [pscustomobject]@{ Path = $_; Hash = (Get-FileHash -LiteralPath (Join-Path $Root $_) -Algorithm SHA256).Hash }
})

$changed = @($candidate | Where-Object { -not $before.ContainsKey($_.Path) -or $before[$_.Path] -ne $_.Hash })
$removed = @($before.Keys | Where-Object { -not ($candidate.Path -contains $_) })

$out = [pscustomobject]@{
    LiveRoot     = $manifest.LiveRoot
    CreatedAtUtc = $manifest.CreatedAtUtc
    Live         = $manifest.Live
    Candidate    = $candidate
}
$out | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Output ("candidate entries : {0} (was {1})" -f $candidate.Count, $manifest.Candidate.Count)
Write-Output ("changed/added     : {0}" -f $changed.Count)
$changed | ForEach-Object { Write-Output ("  {0}  {1}" -f $_.Hash.Substring(0, 12), $_.Path) }
Write-Output ("removed           : {0}" -f $removed.Count)
$removed | ForEach-Object { Write-Output ("  {0}" -f $_) }
