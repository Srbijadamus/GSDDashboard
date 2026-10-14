# MultiCentre.Common.ps1
# Shared READ-ONLY helpers for the "agent works in two WIC centres on one day" fix
# of 2026-10-14/15 (RTM fact: 10-12 centre A + 13-15 centre B in one day).
#
# Rule this tooling encodes (the task's items 1, 2 and 4):
#   * every IsOnSite=1 WicShiftEntries row of that day keeps WorkingShift = the
#     working hours of ITS OWN centre for that date;
#   * the daily ShiftEntries row = earliest WIC start .. latest WIC end (the span).
#
# It never writes anything: each Apply-*.ps1 opens its own transaction.
$ErrorActionPreference = 'Stop'
. 'C:\GSDDashboard_secure\Tools\RtmRule20261014\RtmRule.Common.ps1'

$script:McMarker  = 'span 2 centra (DEV 14.10)'
$script:McFrom    = [datetime]'2026-10-14'   # "od danas nadalje" (server day Europe/Berlin)

# SupportLocation -> legacy bridge code, copied from Backend/Services/WicLocationMatcher.cs.
# The C# dictionary uses IgnoreCase|IgnoreNonSpace; a plain ordinal-ignore-case hashtable is
# used here and unmatched values are reported instead of silently skipped.
$script:McAliases = New-Object 'System.Collections.Hashtable' ([System.StringComparer]::OrdinalIgnoreCase)
@{
    'Essen BP1'               = 'DE_Essen_BP1'
    'Essen TK1'               = 'DE_Essen_TK1'
    'Essen - BP1'             = 'DE_Essen_BP1'
    'Essen - TK'              = 'DE_Essen_TK1'
    'Halle'                   = 'DE_Halle'
    'Berlin - Gaussstr'       = 'DE_Berlin_Gauss'
    'Furstenwalde'            = 'DE_Furstenwalde'
    'Munchen'                 = 'DE_Munchen'
    'Osnabruck'               = 'DE_Osnabruck'
    'Saarbrucken'             = 'DE_Saarbrucken'
    'Demmin - Am Hanseufer'   = 'DE_Demmin_Hanse'
    'Denbosch'                = 'NL_Denbosch'
    'Augsburg'                = 'DE_Augsburg'
    'Bamberg'                 = 'DE_Bamberg'
    'Brokdorf'                = 'DE_Brokdorf'
    'Dortmund'                = 'DE_Dortmund'
    'Emmerthal'               = 'DE_Emmerthal'
    'Essenbach'               = 'DE_Essenbach'
    'Grafenrheinfeld'         = 'DE_Grafenrheinfeld'
    'Hamburg'                 = 'DE_Hamburg'
    'Hannover'                = 'DE_Hannover'
    'Helmstedt'               = 'DE_Helmstedt'
    'Neu-Isenburg'            = 'DE_NeuIsenburg'
    'Pfaffenhofen'            = 'PFAFFENHOFEN'
    'Potsdam'                 = 'DE_Potsdam'
    'Quickborn'               = 'DE_Quickborn'
    'Regensburg'              = 'DE_Regensburg'
    'Rendsburg'               = 'RENDSBURG'
    'Salzgitter'              = 'DE_Salzgitter'
    'Stade'                   = 'DE_Stade'
    'Stadland'                = 'DE_Stadland'
    'Zwolle'                  = 'NL_Zwolle'
    'Essen (Bruesseler Pl.)'  = 'DE_Essen_BP1'
    'Essen (Brusseler Pl.)'   = 'DE_Essen_BP1'
    'Essen (ThyssenKrupp)'    = 'DE_Essen_TK1'
    'Demmin (Am Hanseufer)'   = 'DE_Demmin_Hanse'
    'Berlin (Koepenicker)'    = 'DE_Berlin_Kopenick'
    'Berlin (Kopenicker)'     = 'DE_Berlin_Kopenick'
    'Berlin - Kopenicker'     = 'DE_Berlin_Kopenick'
    'Berlin - Bruckenstrasse' = 'DE_Berlin_Kopenick'
    'Essen_BP1'               = 'DE_Essen_BP1'
    'Essen_TK1'               = 'DE_Essen_TK1'
    'Demmin_Wold'             = 'DE_Demmin_Wold'
    'Demmin_Hanse'            = 'DE_Demmin_Hanse'
}.GetEnumerator() | ForEach-Object { $script:McAliases[$_.Key] = $_.Value }

# Windows-1252 mis-decode repair, same 8 pairs as WicLocationMatcher.RepairDoubleEncoding.
function Repair-McEncoding([string]$s) {
    if (-not $s.Contains([char]0x00C3)) { return $s }
    # Written with explicit code points so the source file stays pure ASCII here.
    $pairs = @(
        @(([string][char]0x00C3 + [char]0x00BC), ([string][char]0x00FC)),   # A-tilde + 1/4      -> u-umlaut
        @(([string][char]0x00C3 + [char]0x00B6), ([string][char]0x00F6)),   # A-tilde + pilcrow  -> o-umlaut
        @(([string][char]0x00C3 + [char]0x00A4), ([string][char]0x00E4)),   # A-tilde + currency -> a-umlaut
        @(([string][char]0x00C3 + [char]0x0178), ([string][char]0x00DF)),   # A-tilde + Y-diaer. -> sharp-s
        @(([string][char]0x00C3 + [char]0x201E), ([string][char]0x00C4)),   # A-tilde + low-9    -> A-umlaut
        @(([string][char]0x00C3 + [char]0x2013), ([string][char]0x00D6)),   # A-tilde + en-dash  -> O-umlaut
        @(([string][char]0x00C3 + [char]0x0153), ([string][char]0x00DC)),   # A-tilde + oe-lig.  -> U-umlaut
        @(([string][char]0x00C3 + [char]0x00A9), ([string][char]0x00E9))    # A-tilde + copyright-> e-acute
    )
    foreach ($p in $pairs) { $s = $s.Replace($p[0], $p[1]) }
    return $s
}

# WicShiftEntry.SupportLocation -> WicLocations row, replicating WicLocationMatcher.MatchesSupportLocation:
#   ordinal equality on DisplayName, then on City, then the alias map against LocationCode /
#   LocationCodeLegacy. Returns $null when nothing matches (reported, never guessed).
function Resolve-McLocation($locations, [string]$supportLocation) {
    if ([string]::IsNullOrWhiteSpace($supportLocation)) { return $null }
    $s = Repair-McEncoding $supportLocation
    foreach ($l in $locations) { if ([string]::Equals($l.DisplayName, $s, [System.StringComparison]::Ordinal)) { return $l } }
    foreach ($l in $locations) { if ($l.City -and [string]::Equals($l.City, $s, [System.StringComparison]::Ordinal)) { return $l } }
    $code = $script:McAliases[$s]
    if ($code) {
        foreach ($l in $locations) {
            if ([string]::Equals($l.LocationCode, $code, [System.StringComparison]::OrdinalIgnoreCase)) { return $l }
            if ($l.LocationCodeLegacy -and [string]::Equals($l.LocationCodeLegacy, $code, [System.StringComparison]::OrdinalIgnoreCase)) { return $l }
        }
    }
    return $null
}

function Get-McLocations($conn) {
    return Invoke-RtmQuery $conn "SELECT Id, LocationCode, DisplayName, City, LocationCodeLegacy, IsActive FROM WicLocations"
}

function Get-McHours($conn) {
    return Invoke-RtmQuery $conn "SELECT Id, LocationCode, DayOfWeek, OpenTime, CloseTime, OpenTime2, CloseTime2, IsClosed, EffectiveFrom, ExceptionDate, ManualChangeAt, ChangeNote FROM WicOpeningHours"
}

function Get-McWicEntries($conn, [datetime]$from) {
    return Invoke-RtmQuery $conn ("SELECT Id, EmployeeId, ShiftDate, SupportLocation, WorkingShift, IsOnSite " +
        "FROM WicShiftEntries WHERE IsOnSite=1 AND ShiftDate >= '{0}' ORDER BY EmployeeId, ShiftDate, Id" -f $from.ToString('yyyy-MM-dd'))
}

function Get-McShiftEntries($conn, [datetime]$from) {
    return Invoke-RtmQuery $conn ("SELECT Id, EmployeeId, ShiftDate, ShiftType, ShiftStart, ShiftEnd, AgentTask, IsWicDuty, " +
        "AutoGenerated, SourceModule, ManualChangeBy, ManualChangeAt, ManualChangeNote " +
        "FROM ShiftEntries WHERE ShiftDate >= '{0}' ORDER BY EmployeeId, ShiftDate, Id" -f $from.ToString('yyyy-MM-dd'))
}

function Get-McEmployeeIds($conn) {
    return Invoke-RtmQuery $conn "SELECT EmployeeId, FullName, IsActive FROM Employees"
}

# The window for one centre on one date: resolved hours (exception day wins) then DutyWindow.
function Get-McCentreWindow($hours, $location, [datetime]$date) {
    return Get-DutyWindow (Get-ResolvedHours $hours $location.LocationCode $location.LocationCodeLegacy $date)
}

function Get-McMinutesOrNull($hhmm) {
    if ([string]::IsNullOrWhiteSpace($hhmm)) { return $null }
    $t = ([string]$hhmm).Trim()
    if ($t -notmatch '^\d{1,2}:\d{2}$') { return $null }
    return (Get-Minutes $t)
}

function Add-McNote([string]$existing) {
    if ([string]::IsNullOrWhiteSpace($existing)) { return $script:McMarker }
    if ($existing -like "*$($script:McMarker)*") { return $existing }
    return ($existing.TrimEnd() + ' | ' + $script:McMarker)
}

# ---------------------------------------------------------------------------------------
# The analysis. Returns one object per (EmployeeId, ShiftDate) that has 2+ IsOnSite WIC
# rows at DIFFERENT resolved centres. Flags 'SpanMissing' when the ShiftEntries window does
# not cover earliest-WIC-start .. latest-WIC-end.
# ---------------------------------------------------------------------------------------
function Get-McAnalysis($conn, [datetime]$from) {
    $locations = Get-McLocations $conn
    $hours     = Get-McHours $conn
    $wic       = Get-McWicEntries $conn $from
    $shifts    = Get-McShiftEntries $conn $from
    $emps      = Get-McEmployeeIds $conn

    $shiftByKey = @{}
    foreach ($s in $shifts) {
        $k = "$($s.EmployeeId)|$(([datetime]$s.ShiftDate).ToString('yyyy-MM-dd'))"
        if (-not $shiftByKey.ContainsKey($k)) { $shiftByKey[$k] = New-Object System.Collections.ArrayList }
        [void]$shiftByKey[$k].Add($s)
    }

    $resolvedRows = New-Object System.Collections.ArrayList
    $unresolved   = New-Object System.Collections.ArrayList
    foreach ($w in $wic) {
        $loc = Resolve-McLocation $locations $w.SupportLocation
        if (-not $loc) { [void]$unresolved.Add($w); continue }
        $date = [datetime]$w.ShiftDate
        $cw   = Get-McCentreWindow $hours $loc $date
        $sw   = $null
        if (-not [string]::IsNullOrWhiteSpace($w.WorkingShift)) {
            $sw = WicWorkingShift-Parse $w.WorkingShift
        }
        [void]$resolvedRows.Add([pscustomobject]@{
            WicId = $w.Id; EmployeeId = $w.EmployeeId; ShiftDate = $date; SupportLocation = $w.SupportLocation
            Loc = $loc; Stored = $w.WorkingShift; CentreWindow = $cw; StoredWindow = $sw
        })
    }

    $groups = $resolvedRows | Group-Object { "$($_.EmployeeId)|$($_.ShiftDate.ToString('yyyy-MM-dd'))" }
    $results = New-Object System.Collections.ArrayList

    foreach ($g in $groups) {
        $distinct = @($g.Group | ForEach-Object { $_.Loc.LocationCode } | Select-Object -Unique)
        if ($distinct.Count -lt 2) { continue }

        $date = $g.Group[0].ShiftDate
        $emp  = $g.Group[0].EmployeeId

        $parts = New-Object System.Collections.ArrayList
        foreach ($r in ($g.Group | Sort-Object WicId)) {
            $stored = $r.StoredWindow
            $cw     = $r.CentreWindow
            # The row's real on-site window: stored cut to the centre. When the stored value
            # is unparseable or does not touch the centre, the centre window stands in (that
            # is the value the procedure writes anyway).
            $part = $cw
            if ($stored -and $cw) {
                $ix = Get-Intersection $stored.Start $stored.End $cw.Start $cw.End
                if ($ix) { $part = $ix }
            }
            [void]$parts.Add([pscustomobject]@{ Row = $r; Part = $part })
        }

        $valid = @($parts | Where-Object { $_.Part })
        if ($valid.Count -eq 0) { continue }
        $spanStart = ($valid | ForEach-Object { Get-McMinutesOrNull $_.Part.Start } | Measure-Object -Minimum).Minimum
        $spanEnd   = ($valid | ForEach-Object { Get-McMinutesOrNull $_.Part.End   } | Measure-Object -Maximum).Maximum

        $k = "$emp|$($date.ToString('yyyy-MM-dd'))"
        $seRows = if ($shiftByKey.ContainsKey($k)) { @($shiftByKey[$k]) } else { @() }
        # The day's own shift row: the WIC_DUTY / WORKING row, never the OFF_WEEKEND filler.
        $se = @($seRows | Where-Object { $_.ShiftType -notlike 'OFF*' -and $_.ShiftType -notlike 'AL*' } | Select-Object -First 1)
        if ($se.Count -eq 0) { $se = @($seRows | Select-Object -First 1) }
        $se = if ($se.Count -gt 0) { $se[0] } else { $null }

        $shStart = if ($se) { Get-McMinutesOrNull $se.ShiftStart } else { $null }
        $shEnd   = if ($se) { Get-McMinutesOrNull $se.ShiftEnd   } else { $null }
        $spanMissing = ($null -eq $shStart) -or ($null -eq $shEnd) -or ($shStart -gt $spanStart) -or ($shEnd -lt $spanEnd)

        $wicFixes = @($parts | Where-Object {
            $_.Row.CentreWindow -and
            ($_.Row.Stored -ne ("{0}-{1}" -f $_.Row.CentreWindow.Start, $_.Row.CentreWindow.End))
        })

        $emp2 = $emps | Where-Object { $_.EmployeeId -eq $emp } | Select-Object -First 1

        # Structured per-row update plan (used by Apply-MultiCentre.ps1). Every WIC part of
        # the day is set to ITS OWN centre's window; the daily ShiftEntries row to the span.
        $wicUpdates = @($parts | Sort-Object { $_.Row.WicId } | ForEach-Object {
            $new = if ($_.Row.CentreWindow) { "{0}-{1}" -f $_.Row.CentreWindow.Start, $_.Row.CentreWindow.End } else { $null }
            [pscustomobject]@{
                WicId = $_.Row.WicId; SupportLocation = $_.Row.SupportLocation
                Old = $_.Row.Stored; New = $new
                Changed = ($null -ne $new) -and ($_.Row.Stored -ne $new)
            }
        })
        $oldNote = if ($se) { [string]$se.ManualChangeNote } else { '' }
        $newNote = Add-McNote $oldNote

        # Computed outside the literal: a bare `if` statement is not a valid hashtable value.
        $isActive  = if ($emp2) { [bool]$emp2.IsActive } else { $false }
        $seId      = if ($se) { $se.Id } else { $null }
        $oldShift  = if ($se) { "$($se.ShiftStart)-$($se.ShiftEnd)" } else { '<none>' }
        $seType    = if ($se) { [string]$se.ShiftType } else { '' }
        $seBy      = if ($se) { [string]$se.ManualChangeBy } else { '' }
        $wicDetail = (($parts | Sort-Object { $_.Row.WicId } | ForEach-Object {
                          $w = if ($_.Part) { "$($_.Part.Start)-$($_.Part.End)" } else { 'n/a' }
                          "{0}:{1}" -f $_.Row.Loc.LocationCode, $w }) -join ' ; ')
        $fixDetail = (($wicFixes | Sort-Object { $_.Row.WicId } | ForEach-Object {
                          "{0}:{1}->{2}-{3}" -f $_.Row.WicId, $_.Row.Stored, $_.Row.CentreWindow.Start, $_.Row.CentreWindow.End }) -join ' ; ')

        [void]$results.Add([pscustomobject]@{
            EmployeeId     = $emp
            IsActive       = $isActive
            ShiftDate      = $date.ToString('yyyy-MM-dd')
            DOW            = $date.DayOfWeek
            CentreCount    = $distinct.Count
            Centres        = (($g.Group | Sort-Object WicId | ForEach-Object { $_.SupportLocation }) -join ' + ')
            WicDetail      = $wicDetail
            WicUpdates     = $wicUpdates
            ShiftEntryId   = $seId
            OldShift       = $oldShift
            ShiftType      = $seType
            ManualChangeBy = $seBy
            OldNote        = $oldNote
            NewNote        = $newNote
            NewShift       = ("{0}-{1}" -f (Get-HHmm $spanStart), (Get-HHmm $spanEnd))
            SpanMissing    = $spanMissing
            WicFixCount    = $wicFixes.Count
            WicFixDetail   = $fixDetail
        })
    }

    return [pscustomobject]@{
        Results    = ($results | Sort-Object EmployeeId, ShiftDate)
        Unresolved = $unresolved
        Locations  = $locations
        WicCount   = $wic.Rows.Count
        ShiftCount = $shifts.Rows.Count
    }
}

# Parse "HH:mm-HH:mm" / "HH:mm - HH:mm" / two-block shapes. Mirrors WicWorkingShift.Parse.
function WicWorkingShift-Parse([string]$workingShift) {
    if ([string]::IsNullOrWhiteSpace($workingShift)) { return $null }
    $m = [regex]::Matches($workingShift, '\d{1,2}:\d{2}')
    if ($m.Count -lt 2) { return $null }
    $s = Get-McMinutesOrNull $m[0].Value
    $e = Get-McMinutesOrNull $m[$m.Count - 1].Value
    if ($null -eq $s -or $null -eq $e -or $e -le $s) { return $null }
    return [pscustomobject]@{ Start = (Get-HHmm $s); End = (Get-HHmm $e) }
}
