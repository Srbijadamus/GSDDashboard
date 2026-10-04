param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LiveDataTargets.ps1')
if (!(Test-Path -LiteralPath $OutputDirectory)) { throw 'Provide an existing protected output directory.' }
$directoryAcl = Get-Acl -LiteralPath $OutputDirectory
if (!$directoryAcl.AreAccessRulesProtected) { throw 'Output directory must have protected ACLs.' }
foreach ($rule in $directoryAcl.Access) {
    $sid = $rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
    if ($sid -notin @('S-1-5-18', 'S-1-5-32-544')) {
        throw 'Output directory must permit SYSTEM/Administrators only.'
    }
}
$started = [DateTime]::UtcNow.ToString('o')
$results = @()
foreach ($target in Get-LiveDataTargets) {
    $connection = New-Object System.Data.SqlClient.SqlConnection($target.Connection)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = @'
SELECT s.name, t.name
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name
'@
        $reader = $command.ExecuteReader()
        $tables = @()
        while ($reader.Read()) { $tables += @{ Schema = $reader.GetString(0); Table = $reader.GetString(1) } }
        $reader.Close()
        foreach ($table in $tables) {
            $qualified = '[' + $table.Schema.Replace(']', ']]') + '].[' + $table.Table.Replace(']', ']]') + ']'
            $command = $connection.CreateCommand()
            $command.CommandTimeout = 300
            $expression = if ($target.Checksum) { ', CHECKSUM_AGG(BINARY_CHECKSUM(*)) AS checksum_value' } else { '' }
            $command.CommandText = "SELECT COUNT_BIG(*) AS row_count$expression FROM $qualified"
            $tableStarted = [DateTime]::UtcNow.ToString('o')
            $reader = $command.ExecuteReader()
            if (!$reader.Read()) { throw "Missing aggregate result for $qualified." }
            $checksum = $null
            if ($target.Checksum -and !$reader.IsDBNull(1)) { $checksum = $reader.GetInt32(1) }
            $results += [pscustomobject]@{
                Database = $target.Name; Schema = $table.Schema; Table = $table.Table
                RowCount = $reader.GetInt64(0); BinaryChecksumAggregate = $checksum
                ChecksumRequested = $target.Checksum; StartedUtc = $tableStarted
                FinishedUtc = [DateTime]::UtcNow.ToString('o')
            }
            $reader.Close()
        }
    } finally { $connection.Dispose() }
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$path = Join-Path $OutputDirectory "table-baseline-$stamp.json"
if (Test-Path -LiteralPath $path) { throw 'Baseline output already exists.' }
@{
    StartedUtc = $started; FinishedUtc = [DateTime]::UtcNow.ToString('o')
    Consistency = 'Per-table single SELECT under default isolation; not a database-wide snapshot.'
    ChecksumLimit = '32-bit non-cryptographic aggregate; collisions and ignored/noncomparable columns are possible.'
    Tables = $results
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path -Encoding UTF8
Set-DataProtectionAcl $path
$csv = Join-Path $OutputDirectory "table-baseline-$stamp.csv"
$results | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
Set-DataProtectionAcl $csv
foreach ($group in $results | Group-Object Database) {
    Write-Output ("{0}: {1} tables; {2} rows" -f $group.Name, $group.Count,
        ($group.Group | Measure-Object RowCount -Sum).Sum)
}
Write-Output "Baseline JSON: $path"
Write-Output "Baseline CSV: $csv"
