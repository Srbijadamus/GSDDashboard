param(
    [switch]$RecordBuild,
    [switch]$RequireBuild
)
$ErrorActionPreference = 'Stop'
if ($RecordBuild -and $RequireBuild) { throw 'Choose RecordBuild or RequireBuild, not both.' }
$root = Split-Path $PSScriptRoot
$manifestPath = Join-Path $PSScriptRoot 'pulse-preservation-manifest.json'
$receiptPath = Join-Path $root '.pulse-build-receipt.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

function Get-SourcePaths([string]$Root) {
    $paths = @(& git -C $Root ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE) { throw 'Cannot inventory source files.' }
    @($paths | Where-Object {
        $_ -match '^(Backend/.*\.(cs|csproj)|Backend.Tests/.*\.(cs|csproj)|Frontend/(src/.*|[^/]+\.(json|ts|js))|import_shifts\.py|import_shifts_xl\.py|shift_import_guard\.py)$' -and
        (Test-Path -LiteralPath (Join-Path $Root $_) -PathType Leaf)
    } | Sort-Object -Unique)
}

function Assert-Source([string]$Root, $Entries) {
    $expected = @($Entries | ForEach-Object { $_.Path } | Sort-Object)
    $actual = @(Get-SourcePaths $Root)
    if (Compare-Object $expected $actual) { throw "Source file inventory changed: $Root. Review and regenerate the preservation manifest." }
    foreach ($entry in $Entries) {
        $path = Join-Path $Root $entry.Path
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Hash) {
            throw "Source changed: $($entry.Path). Review before building or promoting."
        }
    }
}

Assert-Source $manifest.LiveRoot $manifest.Live
Assert-Source $root $manifest.Candidate

function Get-BuildFiles {
    foreach ($relative in @('Backend\wwwroot', 'Backend\bin\Release\net8.0')) {
        $directory = Join-Path $root $relative
        if (!(Test-Path -LiteralPath $directory)) { throw 'Complete build output missing.' }
        Get-ChildItem -LiteralPath $directory -Recurse -File | ForEach-Object {
            [pscustomobject]@{
                Path = $_.FullName.Substring($root.Length + 1)
                Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        }
    }
}

if ($RecordBuild -or $RequireBuild) {
    if (!(Test-Path -LiteralPath (Join-Path $root 'Backend\bin\Release\net8.0\GSDDashboard.API.dll')) -or
        !(Test-Path -LiteralPath (Join-Path $root 'Backend\wwwroot\index.html'))) {
        throw 'Backend and frontend build required.'
    }
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    $files = @(Get-BuildFiles | Sort-Object Path)
    if ($RecordBuild) {
        [pscustomobject]@{
            ManifestHash = $manifestHash
            BuiltAtUtc = [DateTime]::UtcNow.ToString('o')
            Files = $files
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
    } else {
        $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
        $expectedBuild = @($receipt.Files | ForEach-Object { "$($_.Path)|$($_.Hash)" })
        $actualBuild = @($files | ForEach-Object { "$($_.Path)|$($_.Hash)" })
        if ($receipt.ManifestHash -ne $manifestHash -or
            (Compare-Object $expectedBuild $actualBuild)) {
            throw 'Build output is stale or changed. Rebuild before promotion.'
        }
    }
}
Write-Output "Preservation verified: $($manifest.Live.Count) live source inputs; $($manifest.Candidate.Count) candidate inputs."
