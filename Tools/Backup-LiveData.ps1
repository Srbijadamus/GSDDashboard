param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LiveDataTargets.ps1')
if ($OutputDirectory -notmatch '^C:\\ProgramData\\PulseDataProtection\\[0-9]{8}-[0-9]{6}$') {
    throw 'Use a timestamp directory under C:\ProgramData\PulseDataProtection.'
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Backup directory already exists; never overwrite.' }
$parent = Split-Path $OutputDirectory
if (!(Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
Set-DataProtectionAcl $parent
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
Set-DataProtectionAcl $OutputDirectory
$manifest = @()
try {
    foreach ($target in Get-LiveDataTargets) {
        $connection = New-Object System.Data.SqlClient.SqlConnection($target.Connection)
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            $command.CommandText = "SELECT service_account FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server (%'"
            $service = [string]$command.ExecuteScalar()
            if (!$service) { throw 'SQL service account unavailable.' }
            Set-DataProtectionAcl $OutputDirectory $service
            $file = Join-Path $OutputDirectory ($target.Name + '.bak')
            if (Test-Path -LiteralPath $file) { throw 'Backup file already exists.' }
            $name = '[' + $target.Name.Replace(']', ']]') + ']'
            $command = $connection.CreateCommand()
            $command.CommandTimeout = 1800
            $command.CommandText = "BACKUP DATABASE $name TO DISK=@path WITH COPY_ONLY, CHECKSUM"
            $command.Parameters.Add('@path', [System.Data.SqlDbType]::NVarChar, 4000).Value = $file
            $started = [DateTime]::UtcNow.ToString('o')
            [void]$command.ExecuteNonQuery()
            $command.CommandText = 'RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM'
            [void]$command.ExecuteNonQuery()
            $command.CommandText = 'RESTORE HEADERONLY FROM DISK=@path'
            $reader = $command.ExecuteReader()
            if (!$reader.Read()) { throw 'Backup header missing.' }
            $copyOnly = [bool]$reader['IsCopyOnly']
            $hasChecksum = [bool]$reader['HasBackupChecksums']
            $databaseName = [string]$reader['DatabaseName']
            $reader.Close()
            if (!$copyOnly -or !$hasChecksum -or $databaseName -ne $target.Name) {
                throw 'Backup header does not match requested database/options.'
            }
            Set-DataProtectionAcl $file
            $manifest += [pscustomobject]@{
                Database = $target.Name; Path = $file; Bytes = (Get-Item -LiteralPath $file).Length
                SHA256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
                CopyOnly = $copyOnly; BackupChecksums = $hasChecksum; VerifyOnly = 'PASSED'
                StartedUtc = $started; FinishedUtc = [DateTime]::UtcNow.ToString('o')
            }
            Write-Output ("{0}: COPY_ONLY/CHECKSUM and VERIFYONLY passed" -f $target.Name)
        } finally {
            $connection.Dispose()
            Set-DataProtectionAcl $OutputDirectory
            foreach ($fileItem in Get-ChildItem -LiteralPath $OutputDirectory -File) {
                Set-DataProtectionAcl $fileItem.FullName
            }
        }
    }
    $manifestPath = Join-Path $OutputDirectory 'backup-manifest.json'
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Set-DataProtectionAcl $manifestPath
    $manifest | Format-Table Database,Bytes,SHA256,VerifyOnly -AutoSize
    Write-Output "Manifest: $manifestPath"
} finally { Set-DataProtectionAcl $OutputDirectory }
