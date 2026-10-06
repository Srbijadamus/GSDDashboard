function Get-LiveDataTargets {
    $pulsePath = 'C:\GSDDashboard\Backend\appsettings.json'
    $pulse = Get-Content -LiteralPath $pulsePath -Raw | ConvertFrom-Json
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder.set_ConnectionString($pulse.ConnectionStrings.DefaultConnection)
    if ($builder.get_InitialCatalog() -ne 'GSDDashboard') {
        throw 'Unexpected live Pulse database; stop and verify runtime configuration.'
    }
    if (!$builder.get_IntegratedSecurity()) { throw 'Integrated SQL authentication required.' }
    $releasePath = 'C:\GSDDashboard\Backend\bin\Release\net8.0\appsettings.json'
    $release = Get-Content -LiteralPath $releasePath -Raw | ConvertFrom-Json
    $other = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $other.set_ConnectionString($release.ConnectionStrings.DefaultConnection)
    if ($other.get_InitialCatalog() -ne $builder.get_InitialCatalog() -or
        $other.get_DataSource() -ne $builder.get_DataSource()) {
        throw 'Live source/release database settings disagree.'
    }
    foreach ($path in @('C:\ProgramData\PulseBackend\backend.json',
        'C:\GSDDashboard\Backend\appsettings.Production.json')) {
        if (Test-Path -LiteralPath $path) {
            $extra = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            if ($extra.PSObject.Properties['ConnectionStrings'] -and
                $extra.ConnectionStrings.PSObject.Properties['DefaultConnection'] -and
                $extra.ConnectionStrings.DefaultConnection) {
                throw 'Additional live connection override exists; review before backup.'
            }
        }
    }
    $database = @{}
    $inside = $false
    foreach ($line in Get-Content -LiteralPath 'C:\ShiftKiosk\server\server_config.ini') {
        if ($line -match '^\s*\[([^]]+)\]') { $inside = $Matches[1] -eq 'Database'; continue }
        if ($inside -and $line -match '^\s*(server|database)\s*=\s*(.*?)\s*$') {
            $database[$Matches[1].ToLowerInvariant()] = $Matches[2]
        }
    }
    if ($database.database -ne 'ShiftKioskDB' -or !$database.server) {
        throw 'Unexpected live ShiftKiosk database configuration.'
    }
    $kiosk = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $kiosk.set_DataSource($database.server)
    $kiosk.set_InitialCatalog('ShiftKioskDB')
    $kiosk.set_IntegratedSecurity($true)
    $kiosk.set_TrustServerCertificate($true)
    @(
        @{ Name = 'GSDDashboard'; Connection = $builder.get_ConnectionString(); Checksum = $true },
        @{ Name = 'ShiftKioskDB'; Connection = $kiosk.get_ConnectionString(); Checksum = $false }
    )
}

function Set-DataProtectionAcl([string]$Path, [string]$SqlServiceAccount = '') {
    $item = Get-Item -LiteralPath $Path
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse path rejected.' }
    if ($item.PSIsContainer) {
        $acl = New-Object System.Security.AccessControl.DirectorySecurity
    } else {
        $acl = New-Object System.Security.AccessControl.FileSecurity
    }
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($name in @('S-1-5-18', 'S-1-5-32-544', $SqlServiceAccount)) {
        if (!$name) { continue }
        if ($name -like 'S-1-*') {
            $identity = New-Object System.Security.Principal.SecurityIdentifier($name)
        } else {
            $identity = New-Object System.Security.Principal.NTAccount($name)
        }
        if ($item.PSIsContainer) {
            $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                $identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        } else {
            $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'Allow')
        }
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}
