# PS1_67_Phase1Resolve.ps1  (v2)
# READ-ONLY Phase 1: resolve all 75 entries from the 2026-07-06 availability list.
# Prints full plan and STOPS. Does not insert anything.
# Connection read from appsettings.json. No direct SQL tool invocations.
#
# Confirmed decisions baked in:
#   OFF/OL/CD group (7) -> ShiftType = OFF
#   Night (Asal Wardaastiani Azar) -> WORKING, ShiftStart=22:00 ShiftEnd=07:00
#   Viktor Winter -> AL only; must NOT appear in any working list
#   Mohammad Al Masalama -> current name as-is (Neu-Isenburg, WIC)
#   Danny Bendig "Essen" -> AMBIGUOUS (BP1 vs TK1); flagged, not guessed

$appSettingsPath = "C:\GSDDashboard\Backend\appsettings.json"
$shiftDate       = "2026-07-06"

Add-Type -AssemblyName "System.Data"

$cfg = Get-Content $appSettingsPath | ConvertFrom-Json
$cs  = $cfg.ConnectionStrings.DefaultConnection

# ==== Helpers ================================================================

function Open-Conn {
    $c = New-Object System.Data.SqlClient.SqlConnection($script:cs)
    $c.Open()
    return $c
}

function Invoke-Rows([string]$sql, [hashtable]$params = @{}) {
    $conn = Open-Conn
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sql
        foreach ($kv in $params.GetEnumerator()) {
            [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
        }
        $rows = @()
        $rdr  = $cmd.ExecuteReader()
        while ($rdr.Read()) {
            $row = @{}
            for ($i = 0; $i -lt $rdr.FieldCount; $i++) {
                $v      = $rdr.GetValue($i)
                $mapped = $v
                if ($v -is [System.DBNull]) { $mapped = $null }
                $row[$rdr.GetName($i)] = $mapped
            }
            $rows += [PSCustomObject]$row
        }
        $rdr.Close()
        return $rows
    } finally { $conn.Close() }
}

# CI_AI comparison (.NET IgnoreCase + IgnoreNonSpace): handles u-umlaut u, o-umlaut o, etc.
function Match-CI-AI([string]$a, [string]$b) {
    $ci   = [System.Globalization.CultureInfo]::InvariantCulture
    $opts = [System.Globalization.CompareOptions]::IgnoreCase -bor [System.Globalization.CompareOptions]::IgnoreNonSpace
    return $ci.CompareInfo.Compare($a, $b, $opts) -eq 0
}

# Strip diacritical marks (NFD decompose, drop combining chars) then lowercase + collapse spaces.
# Then apply German romanization: ue->u, oe->o, ae->a.
# Result: "Goerguen" -> "gorgun", "Goerguen" same as "Gorgeun"...
# Pattern: both "Görgün" and "Goerguen" normalize to "gorgun".
function Normalize-ForMatch([string]$s) {
    $lower      = $s.ToLower().Trim() -replace '\s+', ' '
    $nfd        = $lower.Normalize([System.Text.NormalizationForm]::FormKD)
    $sb         = [System.Text.StringBuilder]::new()
    foreach ($c in $nfd.ToCharArray()) {
        if ([System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($c) -ne `
            [System.Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$sb.Append($c)
        }
    }
    $stripped = $sb.ToString()
    # Apply German romanization substitution AFTER stripping diacritics
    $stripped = $stripped -replace 'ue', 'u' -replace 'oe', 'o' -replace 'ae', 'a'
    return $stripped
}

# ==== Input Data =============================================================

$slNames    = @(
    "Pascal Dutz", "Mark Bachmann", "Sebastian Hoeck"
)
$alNames    = @(
    "Yiting Qiang", "Christian Koch", "Aleksandrina Dencheva", "Adnan Lelic",
    "Kevin Heynen", "Francois Sicot", "Ion Bodnariuc", "Kavinraj Pathmanathan",
    "Viktor Winter"
)
# Confirmed: all 7 get ShiftType = OFF
$offNames   = @(
    "Mustafa Deveci", "Kemal Sener", "Veronika Kouwui", "Anas Daba",
    "Zehra Sila Goerguen", "Amir Nassri", "Negin Bazmi"
)
# Confirmed: WORKING 22:00-07:00
$nightNames = @("Asal Wardaastiani Azar")
$voiceNames = @(
    "Eva-Liane Schliwa", "Tri Toan Nguyen", "Elena Schlosser", "Vincent Grunzel",
    "Meik Schuelgen", "Kolja Christlieb", "Christian Pastors", "Darjusch Dropczinsky",
    "Walter Buxbaum", "Annabela Scavo", "Yevgeni Frenkel", "Boris Kostov",
    "Sam Alisha Metzner", "Mitko Kilogramski", "Arevig Ketenjian", "Tarek Tabbara",
    "Jonathan Freudenthaler", "Ralf Turski"
)
$vwicNames  = @(
    "Duc Quy Huynh", "Gunter Dinkelmann", "Isloodien Hurchem Lawrence", "Tim Nguyen"
)

# 33 WIC entries: InputLoc -> engineer name
$wicList = @(
    [PSCustomObject]@{ InputLoc = "Essen";                 Name = "Danny Bendig"                },
    [PSCustomObject]@{ InputLoc = "Salzgitter";            Name = "Aakash Som"                  },
    [PSCustomObject]@{ InputLoc = "Stade";                 Name = "Abdulrahman Aldera"          },
    [PSCustomObject]@{ InputLoc = "Essenbach";             Name = "Adam Szilvagyi"              },
    [PSCustomObject]@{ InputLoc = "Denbosch";              Name = "Ivan Leurs"                  },
    [PSCustomObject]@{ InputLoc = "Denbosch";              Name = "Ayten Karatas"               },
    [PSCustomObject]@{ InputLoc = "Pfaffenhofen";          Name = "Binod Dutta"                 },
    [PSCustomObject]@{ InputLoc = "Hamburg";               Name = "Bishal Maharjan"             },
    [PSCustomObject]@{ InputLoc = "Dortmund";              Name = "Christian Martino"           },
    [PSCustomObject]@{ InputLoc = "Potsdam";               Name = "Dennis Markus"               },
    [PSCustomObject]@{ InputLoc = "Essen BP1";             Name = "Erdal Coskun"                },
    [PSCustomObject]@{ InputLoc = "Essen BP1";             Name = "Holger Kuhlmann"             },
    [PSCustomObject]@{ InputLoc = "Essen BP1";             Name = "Angelika Weber"              },
    [PSCustomObject]@{ InputLoc = "Muenchen";              Name = "Eyup Akyurek"                },
    [PSCustomObject]@{ InputLoc = "Rendsburg";             Name = "Hamza Forrousso"             },
    [PSCustomObject]@{ InputLoc = "Saarbruecken";          Name = "Hesham Montasser"            },
    [PSCustomObject]@{ InputLoc = "Emmerthal";             Name = "Holger Petzholdt"            },
    [PSCustomObject]@{ InputLoc = "Hannover";              Name = "Olaf Wittenberg"             },
    [PSCustomObject]@{ InputLoc = "Brokdorf";              Name = "Jannik Borner"               },
    [PSCustomObject]@{ InputLoc = "Stadland";              Name = "Joel Broring"                },
    [PSCustomObject]@{ InputLoc = "Berlin - Gaussstr.";    Name = "Erik Goecks"                 },
    [PSCustomObject]@{ InputLoc = "Essen TK1";             Name = "Kaan Arslan"                 },
    [PSCustomObject]@{ InputLoc = "Augsburg";              Name = "Kamil Filipowicz"            },
    [PSCustomObject]@{ InputLoc = "Fuerstenwalde";         Name = "Klaus Friedrich"             },
    [PSCustomObject]@{ InputLoc = "Osnabrueck";            Name = "Mahboubeh Abdighara"         },
    [PSCustomObject]@{ InputLoc = "Regensburg";            Name = "Marcus Rusch"                },
    [PSCustomObject]@{ InputLoc = "Bamberg";               Name = "Mariusz Kozinski"            },
    [PSCustomObject]@{ InputLoc = "Helmstedt";             Name = "Merlin Voss"                 },
    [PSCustomObject]@{ InputLoc = "Helmstedt";             Name = "Senthuran Shanmugalingam"    },
    [PSCustomObject]@{ InputLoc = "Demmin - Am Hanseufer"; Name = "Rene Altmeyer"               },
    [PSCustomObject]@{ InputLoc = "Neu-Isenburg";          Name = "Mohammad Al Masalama"        },
    [PSCustomObject]@{ InputLoc = "Grafenrheinfeld";       Name = "Tim Boger"                   },
    [PSCustomObject]@{ InputLoc = "Zwolle";                Name = "Elliot van Staveren Kuster"  }
)

# ==== Load DB ================================================================

Write-Host ""
Write-Host "=== PS1_67 v2 Phase 1 Resolve ($shiftDate) ===" -ForegroundColor Yellow
Write-Host ""
Write-Host "Loading DB (Employees + WicLocations) ..." -ForegroundColor Cyan

$allEmployees = Invoke-Rows "SELECT EmployeeId, FullName FROM Employees WHERE IsActive = 1"

# Include LocationCodeLegacy so alias codes that map to the legacy column still resolve.
$allLocations = Invoke-Rows (
    "SELECT LocationCode, " +
    "       ISNULL(LocationCodeLegacy, '') AS LocationCodeLegacy, " +
    "       DisplayName, " +
    "       ISNULL(City, '') AS City " +
    "FROM WicLocations WHERE IsActive = 1"
)

Write-Host ("  {0} active employees  |  {1} active WicLocations" -f $allEmployees.Count, $allLocations.Count) -ForegroundColor Green
Write-Host ""

# ==== Alias map ==============================================================
# Mirrors WicLocationMatcher._aliases (lowercase keys).
# Includes BOTH u-form (canonical, resolves umlaut DisplayNames via IgnoreNonSpace)
# AND ue-form variants for inputs that use German romanization (Muenchen, Saarbruecken...).
# Also handles "Berlin - Gaussstr." trailing-period variant.

$locAlias = @{
    # ---- Bucket A: canonical aliases (u/o-stripped) -------------------------
    "essen bp1"              = "DE_Essen_BP1"
    "essen tk1"              = "DE_Essen_TK1"
    "halle"                  = "DE_Halle"
    "berlin - gaussstr"      = "DE_Berlin_Gauss"
    "furstenwalde"           = "DE_Furstenwalde"
    "munchen"                = "DE_Munchen"
    "osnabruck"              = "DE_Osnabruck"
    "saarbrucken"            = "DE_Saarbrucken"
    "demmin - am hanseufer"  = "DE_Demmin_Hanse"
    "denbosch"               = "NL_Denbosch"
    "augsburg"               = "DE_Augsburg"
    "bamberg"                = "DE_Bamberg"
    "brokdorf"               = "DE_Brokdorf"
    "dortmund"               = "DE_Dortmund"
    "emmerthal"              = "DE_Emmerthal"
    "essenbach"              = "DE_Essenbach"
    "grafenrheinfeld"        = "DE_Grafenrheinfeld"
    "hamburg"                = "DE_Hamburg"
    "hannover"               = "DE_Hannover"
    "helmstedt"              = "DE_Helmstedt"
    "neu-isenburg"           = "DE_NeuIsenburg"
    "pfaffenhofen"           = "PFAFFENHOFEN"
    "potsdam"                = "DE_Potsdam"
    "quickborn"              = "DE_Quickborn"
    "regensburg"             = "DE_Regensburg"
    "rendsburg"              = "RENDSBURG"
    "salzgitter"             = "DE_Salzgitter"
    "stade"                  = "DE_Stade"
    "stadland"               = "DE_Stadland"
    "zwolle"                 = "NL_Zwolle"
    # ---- Bucket B: ue-romanized forms (input uses "ue" where app stores u or umlaut) -
    "muenchen"               = "DE_Munchen"
    "saarbruecken"           = "DE_Saarbrucken"
    "fuerstenwalde"          = "DE_Furstenwalde"
    "osnabrueck"             = "DE_Osnabruck"
    # ---- Period variant for Gaussstrasse ------------------------------------
    "berlin - gaussstr."     = "DE_Berlin_Gauss"
}

# ==== Location resolver ======================================================

function Find-LocByCode([string]$code) {
    # Check both LocationCode and LocationCodeLegacy columns.
    $loc = @($script:allLocations | Where-Object {
        $_.LocationCode -eq $code -or $_.LocationCodeLegacy -eq $code
    })
    return $loc
}

function Resolve-WicLocation([string]$inputLoc) {
    $key = $inputLoc.ToLower().Trim()

    # 1. Alias map (exact lowercase key -> LocationCode or LocationCodeLegacy)
    if ($locAlias.ContainsKey($key)) {
        $code = $locAlias[$key]
        $loc  = Find-LocByCode $code
        if ($loc.Count -eq 1) {
            return [PSCustomObject]@{ Method = "alias"; Code = $loc[0].LocationCode; DisplayName = $loc[0].DisplayName; Ambiguous = $false }
        }
        if ($loc.Count -gt 1) {
            return [PSCustomObject]@{ Method = "alias-AMBIGUOUS"; Code = ($loc | ForEach-Object { $_.LocationCode }) -join ","; DisplayName = ($loc | ForEach-Object { $_.DisplayName }) -join " / "; Ambiguous = $true }
        }
        # Alias key found but no DB row matched - fall through to DisplayName/City search
    }

    # 2. DisplayName match (CI_AI via .NET IgnoreNonSpace)
    $byDisplay = @($script:allLocations | Where-Object { Match-CI-AI $_.DisplayName $inputLoc })
    if ($byDisplay.Count -eq 1) {
        return [PSCustomObject]@{ Method = "displayname-CI_AI"; Code = $byDisplay[0].LocationCode; DisplayName = $byDisplay[0].DisplayName; Ambiguous = $false }
    }
    if ($byDisplay.Count -gt 1) {
        return [PSCustomObject]@{ Method = "displayname-AMBIGUOUS"; Code = ($byDisplay | ForEach-Object { $_.LocationCode }) -join ","; DisplayName = ($byDisplay | ForEach-Object { $_.DisplayName }) -join " / "; Ambiguous = $true }
    }

    # 3. City match (CI_AI)
    $byCity = @($script:allLocations | Where-Object { $_.City -ne '' -and (Match-CI-AI $_.City $inputLoc) })
    if ($byCity.Count -eq 1) {
        return [PSCustomObject]@{ Method = "city-CI_AI"; Code = $byCity[0].LocationCode; DisplayName = $byCity[0].DisplayName; Ambiguous = $false }
    }
    if ($byCity.Count -gt 1) {
        return [PSCustomObject]@{ Method = "city-AMBIGUOUS"; Code = ($byCity | ForEach-Object { $_.LocationCode }) -join ","; DisplayName = ($byCity | ForEach-Object { $_.DisplayName }) -join " / "; Ambiguous = $true }
    }

    return $null
}

# ==== Employee resolver ======================================================

function Resolve-Employee([string]$inputName) {
    # Elliot: stored name may differ (Kuste vs Kuster); fuzzy on distinctive token
    if ($inputName -match 'Staveren') {
        return @($script:allEmployees | Where-Object { $_.FullName -match 'Staveren' })
    }

    # Pass 1: trim + collapse spaces, then CI_AI (handles u-umlaut, o-umlaut etc.)
    $inputClean = ($inputName.Trim() -replace '\s+', ' ')
    $hits = @($script:allEmployees | Where-Object { Match-CI-AI $_.FullName $inputClean })
    if ($hits.Count -gt 0) { return $hits }

    # Pass 2: German romanization normalization (ue->u, oe->o, ae->a) + diacritic strip.
    # Catches: Hoeck=Hock, Goerguen=Gorgeun->Gorgun, Schuelgen=Schulgen, double-space names.
    $inputNorm = Normalize-ForMatch $inputClean
    $hits = @($script:allEmployees | Where-Object {
        (Normalize-ForMatch $_.FullName) -eq $inputNorm
    })
    if ($hits.Count -gt 0) { return $hits }

    return @()
}

# ==== Print category mapping =================================================

Write-Host "=== CATEGORY -> SHIFTTYPE MAPPING (confirmed) ===" -ForegroundColor Yellow
Write-Host "  SL        -> ShiftType='SL'       ShiftEntry only"
Write-Host "  AL        -> ShiftType='AL'       ShiftEntry only"
Write-Host "  OFF group -> ShiftType='OFF'      ShiftEntry only  [CONFIRMED: all 7 get OFF]"
Write-Host "  Night     -> ShiftType='WORKING'  ShiftStart=22:00 ShiftEnd=07:00  [CONFIRMED]"
Write-Host "  Voice     -> ShiftType='WORKING'  ShiftEntry only"
Write-Host "  vWIC      -> ShiftType='WORKING'  ShiftEntry + WicShiftEntry(SupportLocation='VWIC' Task='VWIC' IsOnSite=1)"
Write-Host "  WIC       -> ShiftType='WIC_DUTY' ShiftEntry(IsWicDuty=1) + WicShiftEntry(IsOnSite=1 SupportLocation=DisplayName)"
Write-Host ""

# ==== Build plan =============================================================

$planRows  = [System.Collections.Generic.List[PSCustomObject]]::new()
$unmatched = [System.Collections.Generic.List[string]]::new()

function Add-Plan([string]$name, [string]$cat, [string]$shiftType,
                  [string]$shiftStart, [string]$shiftEnd,
                  [string]$inputLoc, $locResolved) {

    $hits  = Resolve-Employee $name
    $empId = "???"
    $empStatus = "UNMATCHED"
    $matchedName = ""

    if ($hits.Count -eq 1) {
        $empId       = $hits[0].EmployeeId
        $matchedName = $hits[0].FullName
        $empStatus   = "OK"
    } elseif ($hits.Count -gt 1) {
        $empId       = ($hits | ForEach-Object { $_.EmployeeId }) -join "/"
        $matchedName = ($hits | ForEach-Object { $_.FullName }) -join "/"
        $empStatus   = "AMBIGUOUS"
    } else {
        $script:unmatched.Add("$name  [$cat]")
    }

    $locDisplay = $inputLoc
    $locStatus  = ""
    if ($inputLoc -ne "" -and $null -ne $locResolved) {
        $locDisplay = $locResolved.DisplayName
        $locStatus  = "OK"
        if ($locResolved.Ambiguous) { $locStatus = "LOC-AMBIGUOUS" }
    } elseif ($inputLoc -ne "" -and $null -eq $locResolved) {
        $locStatus = "LOC-UNRESOLVED"
    }

    $rowStatus = $empStatus
    if ($locStatus -eq "LOC-UNRESOLVED")  { $rowStatus = "LOC-UNRESOLVED" }
    if ($locStatus -eq "LOC-AMBIGUOUS")   { $rowStatus = "LOC-AMBIGUOUS"  }
    if ($empStatus -eq "UNMATCHED")       { $rowStatus = "UNMATCHED"      }

    $script:planRows.Add([PSCustomObject]@{
        InputName    = $name
        MatchedName  = $matchedName
        Category     = $cat
        ShiftType    = $shiftType
        ShiftStart   = $shiftStart
        ShiftEnd     = $shiftEnd
        InputLoc     = $inputLoc
        LocDisplay   = $locDisplay
        EmployeeId   = $empId
        Status       = $rowStatus
    })
}

foreach ($n in $slNames)    { Add-Plan $n "SL"    "SL"        "" "" "" $null }
foreach ($n in $alNames)    { Add-Plan $n "AL"    "AL"        "" "" "" $null }
foreach ($n in $offNames)   { Add-Plan $n "OFF"   "OFF"       "" "" "" $null }
foreach ($n in $nightNames) { Add-Plan $n "Night" "WORKING" "22:00" "07:00" "" $null }
foreach ($n in $voiceNames) { Add-Plan $n "Voice" "WORKING"   "" "" "" $null }
foreach ($n in $vwicNames)  { Add-Plan $n "vWIC"  "WORKING"   "" "" "VWIC" $null }

# Pre-resolve WIC locations (deduplicated)
$locResolutionMap = @{}
foreach ($entry in $wicList) {
    $loc = $entry.InputLoc
    if (-not $locResolutionMap.ContainsKey($loc)) {
        $locResolutionMap[$loc] = Resolve-WicLocation $loc
    }
}

foreach ($entry in $wicList) {
    $resolved = $locResolutionMap[$entry.InputLoc]
    Add-Plan $entry.Name "WIC" "WIC_DUTY" "" "" $entry.InputLoc $resolved
}

# ==== Print full plan ========================================================

Write-Host "=== FULL PLAN ===" -ForegroundColor Yellow
Write-Host ("  {0,-38} {1,-6} {2,-10} {3,-12} {4,-45} {5,-15} {6}" -f `
    "Input Name", "Cat", "ShiftType", "Times", "Location (WIC -> stored DisplayName)", "EmployeeId", "Status")
Write-Host ("  " + ("-" * 155))

foreach ($r in $planRows) {
    $color = "White"
    if ($r.Status -eq "UNMATCHED")        { $color = "Red"    }
    if ($r.Status -eq "AMBIGUOUS")         { $color = "Yellow" }
    if ($r.Status -eq "LOC-UNRESOLVED")    { $color = "Red"    }
    if ($r.Status -eq "LOC-AMBIGUOUS")     { $color = "Yellow" }

    $times = ""
    if ($r.ShiftStart -ne "") { $times = "$($r.ShiftStart)-$($r.ShiftEnd)" }

    $locField = ""
    if ($r.InputLoc -ne "") { $locField = "$($r.InputLoc) -> $($r.LocDisplay)" }

    Write-Host ("  {0,-38} {1,-6} {2,-10} {3,-12} {4,-45} {5,-15} {6}" -f `
        $r.InputName, $r.Category, $r.ShiftType, $times, $locField, $r.EmployeeId, $r.Status) -ForegroundColor $color
}

Write-Host ""

# ==== Location resolution detail =============================================

Write-Host "=== WIC LOCATION RESOLUTION DETAIL ===" -ForegroundColor Yellow
Write-Host ("  {0,-28} {1,-28} {2,-35} {3}" -f "Input", "LocationCode", "DisplayName (to store in WicShiftEntry)", "Method")
Write-Host ("  " + ("-" * 115))

foreach ($locKey in ($locResolutionMap.Keys | Sort-Object)) {
    $r = $locResolutionMap[$locKey]
    if ($null -eq $r) {
        Write-Host ("  {0,-28} {1,-28} {2,-35} {3}" -f $locKey, "???", "???", "UNRESOLVED") -ForegroundColor Red
    } elseif ($r.Ambiguous) {
        Write-Host ("  {0,-28} {1,-28} {2,-35} {3}" -f $locKey, $r.Code, $r.DisplayName, $r.Method) -ForegroundColor Yellow
    } else {
        Write-Host ("  {0,-28} {1,-28} {2,-35} {3}" -f $locKey, $r.Code, $r.DisplayName, $r.Method) -ForegroundColor Green
    }
}

Write-Host ""

# ==== Unmatched employees ====================================================

if ($unmatched.Count -gt 0) {
    Write-Host "=== UNMATCHED ($($unmatched.Count)) ===" -ForegroundColor Red
    foreach ($u in $unmatched) {
        Write-Host "  UNMATCHED: $u" -ForegroundColor Red
    }
    Write-Host ""
}

# ==== Viktor Winter check ====================================================

$viktorWorking = ($voiceNames + $vwicNames + ($wicList | ForEach-Object { $_.Name })) |
    Where-Object { $_ -match 'Viktor' -or $_ -match 'Winter' }
if (@($viktorWorking).Count -gt 0) {
    Write-Host "CRITICAL: Viktor Winter found in a working category." -ForegroundColor Red
} else {
    Write-Host "Viktor Winter: AL only - not in Voice/vWIC/WIC. OK." -ForegroundColor Green
}

# ==== Danny Bendig Essen ambiguity ===========================================

Write-Host ""
Write-Host "AWAITING DECISION: 'Essen' (Danny Bendig) - ambiguous (BP1 vs TK1)." -ForegroundColor Yellow
Write-Host "  Please specify which location. This entry is flagged; Phase 2 will skip it without your answer." -ForegroundColor Yellow

# ==== Summary ================================================================

$okCount         = ($planRows | Where-Object { $_.Status -eq "OK"             }).Count
$unmatchedCount  = ($planRows | Where-Object { $_.Status -eq "UNMATCHED"      }).Count
$ambigEmpCount   = ($planRows | Where-Object { $_.Status -eq "AMBIGUOUS"      }).Count
$locUnresCount   = ($planRows | Where-Object { $_.Status -eq "LOC-UNRESOLVED" }).Count
$locAmbigCount   = ($planRows | Where-Object { $_.Status -eq "LOC-AMBIGUOUS"  }).Count

Write-Host ""
Write-Host "=== SUMMARY ===" -ForegroundColor Yellow
Write-Host ("  Total plan rows : {0}" -f $planRows.Count)
Write-Host ("  OK              : {0}" -f $okCount)
Write-Host ("  UNMATCHED emp   : {0}" -f $unmatchedCount)
Write-Host ("  AMBIGUOUS emp   : {0}" -f $ambigEmpCount)
Write-Host ("  LOC-UNRESOLVED  : {0}  (Essen/Danny Bendig awaiting decision)" -f $locUnresCount)
Write-Host ("  LOC-AMBIGUOUS   : {0}" -f $locAmbigCount)
Write-Host ""
Write-Host "=== PHASE 1 COMPLETE -- STOPPED. Confirm to proceed to Phase 2. ===" -ForegroundColor Cyan
Write-Host ""
