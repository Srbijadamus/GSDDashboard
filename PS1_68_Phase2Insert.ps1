# PS1_68_Phase2Insert.ps1
# Phase 2: insert 2026-07-06 availability list into ShiftEntries + WicShiftEntries.
# IDEMPOTENT: upserts on (EmployeeId,ShiftDate) for ShiftEntries and
#             (EmployeeId,ShiftDate,SupportLocation) for WicShiftEntries.
# Transaction + XACT_ABORT ON; rolls back on any error.
# Connection read from appsettings.json. No direct SQL tool invocations.
#
# Skips: Yiting Qiang (not in Employees - confirmed absent).
# Confirmed:
#   OFF group (7 people) -> ShiftType=OFF
#   Night (Asal Wardaastiani Azar) -> WORKING ShiftStart=22:00 ShiftEnd=07:00
#   vWIC -> ShiftEntry WORKING + WicShiftEntry(SupportLocation=VWIC Task=VWIC IsOnSite=1)
#   Danny Bendig Essen -> DE_Essen_BP1

$appSettingsPath = "C:\GSDDashboard\Backend\appsettings.json"
$baseUrl         = "http://localhost:5000"
$shiftDate       = "2026-07-06"

Add-Type -AssemblyName "System.Data"

$cfg = Get-Content $appSettingsPath | ConvertFrom-Json
$cs  = $cfg.ConnectionStrings.DefaultConnection
$dow = [datetime]::Parse($shiftDate).DayOfWeek.ToString()   # "Monday"

# ==== Helpers ================================================================

function Open-Conn {
    $c = New-Object System.Data.SqlClient.SqlConnection($script:cs)
    $c.Open()
    return $c
}

function Invoke-Rows([string]$sql) {
    $conn = Open-Conn
    try {
        $cmd  = $conn.CreateCommand()
        $cmd.CommandText = $sql
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

# Exec helpers for use inside the open transaction connection
function Exec-Scalar([string]$sql, [hashtable]$params,
                     [System.Data.SqlClient.SqlConnection]$conn,
                     [System.Data.SqlClient.SqlTransaction]$tx) {
    $cmd = $conn.CreateCommand()
    $cmd.Transaction = $tx
    $cmd.CommandText = $sql
    foreach ($kv in $params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    $v = $cmd.ExecuteScalar()
    $result = $v
    if ($v -is [System.DBNull]) { $result = $null }
    return $result
}

function Exec-NonQuery([string]$sql, [hashtable]$params,
                       [System.Data.SqlClient.SqlConnection]$conn,
                       [System.Data.SqlClient.SqlTransaction]$tx) {
    $cmd = $conn.CreateCommand()
    $cmd.Transaction = $tx
    $cmd.CommandText = $sql
    foreach ($kv in $params.GetEnumerator()) {
        [void]$cmd.Parameters.AddWithValue("@$($kv.Key)", $kv.Value)
    }
    return $cmd.ExecuteNonQuery()
}

# Converts $null / empty string to DBNull for SQL parameters
function PV($v) {
    if ($null -eq $v -or $v -eq '') { return [System.DBNull]::Value }
    return $v
}

function Match-CI-AI([string]$a, [string]$b) {
    $ci   = [System.Globalization.CultureInfo]::InvariantCulture
    $opts = [System.Globalization.CompareOptions]::IgnoreCase -bor [System.Globalization.CompareOptions]::IgnoreNonSpace
    return $ci.CompareInfo.Compare($a, $b, $opts) -eq 0
}

function Normalize-ForMatch([string]$s) {
    $lower = $s.ToLower().Trim() -replace '\s+', ' '
    $nfd   = $lower.Normalize([System.Text.NormalizationForm]::FormKD)
    $sb    = [System.Text.StringBuilder]::new()
    foreach ($c in $nfd.ToCharArray()) {
        if ([System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($c) -ne `
            [System.Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$sb.Append($c)
        }
    }
    $stripped = $sb.ToString()
    $stripped = $stripped -replace 'ue', 'u' -replace 'oe', 'o' -replace 'ae', 'a'
    return $stripped
}

function Resolve-Employee([string]$inputName) {
    if ($inputName -match 'Staveren') {
        return @($script:allEmployees | Where-Object { $_.FullName -match 'Staveren' })
    }
    $inputClean = ($inputName.Trim() -replace '\s+', ' ')
    $hits = @($script:allEmployees | Where-Object { Match-CI-AI $_.FullName $inputClean })
    if ($hits.Count -gt 0) { return $hits }
    $inputNorm = Normalize-ForMatch $inputClean
    $hits = @($script:allEmployees | Where-Object {
        (Normalize-ForMatch $_.FullName) -eq $inputNorm
    })
    return $hits
}

# ==== Alias map (mirrors WicLocationMatcher._aliases) ========================

$locAlias = @{
    "essen bp1"              = "DE_Essen_BP1"
    "essen tk1"              = "DE_Essen_TK1"
    "halle"                  = "DE_Halle"
    "berlin - gaussstr"      = "DE_Berlin_Gauss"
    "berlin - gaussstr."     = "DE_Berlin_Gauss"
    "furstenwalde"           = "DE_Furstenwalde"
    "fuerstenwalde"          = "DE_Furstenwalde"
    "munchen"                = "DE_Munchen"
    "muenchen"               = "DE_Munchen"
    "osnabruck"              = "DE_Osnabruck"
    "osnabrueck"             = "DE_Osnabruck"
    "saarbrucken"            = "DE_Saarbrucken"
    "saarbruecken"           = "DE_Saarbrucken"
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
    "regensburg"             = "DE_Regensburg"
    "rendsburg"              = "RENDSBURG"
    "salzgitter"             = "DE_Salzgitter"
    "stade"                  = "DE_Stade"
    "stadland"               = "DE_Stadland"
    "zwolle"                 = "NL_Zwolle"
}

function Find-LocByCode([string]$code) {
    return @($script:allLocations | Where-Object {
        $_.LocationCode -eq $code -or $_.LocationCodeLegacy -eq $code
    })
}

function Resolve-WicDisplayName([string]$inputLoc) {
    $key = $inputLoc.ToLower().Trim()
    if ($locAlias.ContainsKey($key)) {
        $code = $locAlias[$key]
        $loc  = Find-LocByCode $code
        if ($loc.Count -gt 0) { return $loc[0].DisplayName }
    }
    $byDisplay = @($script:allLocations | Where-Object { Match-CI-AI $_.DisplayName $inputLoc })
    if ($byDisplay.Count -gt 0) { return $byDisplay[0].DisplayName }
    $byCity = @($script:allLocations | Where-Object { $_.City -ne $null -and (Match-CI-AI $_.City $inputLoc) })
    if ($byCity.Count -gt 0) { return $byCity[0].DisplayName }
    return $null
}

# ==== Input data =============================================================
# Each entry: Name, Category, ShiftType, ShiftStart, ShiftEnd, IsWicDuty,
#             WicInputLoc (null = no WicShiftEntry), WicTask, Skip

$allEntries = @(
    # -- SL --
    [PSCustomObject]@{ Name="Pascal Dutz";               Cat="SL";    ST="SL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Mark Bachmann";             Cat="SL";    ST="SL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Sebastian Hoeck";           Cat="SL";    ST="SL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    # -- AL --
    [PSCustomObject]@{ Name="Yiting Qiang";              Cat="AL";    ST="AL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$true  },
    [PSCustomObject]@{ Name="Christian Koch";            Cat="AL";    ST="AL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Aleksandrina Dencheva";     Cat="AL";    ST="AL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Adnan Lelic";               Cat="AL";    ST="AL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Kevin Heynen";              Cat="AL";    ST="AL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Francois Sicot";            Cat="AL";    ST="AL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Ion Bodnariuc";             Cat="AL";    ST="AL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Kavinraj Pathmanathan";     Cat="AL";    ST="AL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Viktor Winter";             Cat="AL";    ST="AL";        SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    # -- OFF (all 7 confirmed -> OFF) --
    [PSCustomObject]@{ Name="Mustafa Deveci";            Cat="OFF";   ST="OFF";       SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Kemal Sener";               Cat="OFF";   ST="OFF";       SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Veronika Kouwui";           Cat="OFF";   ST="OFF";       SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Anas Daba";                 Cat="OFF";   ST="OFF";       SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Zehra Sila Goerguen";       Cat="OFF";   ST="OFF";       SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Amir Nassri";               Cat="OFF";   ST="OFF";       SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Negin Bazmi";               Cat="OFF";   ST="OFF";       SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    # -- Night (confirmed: WORKING 22:00-07:00) --
    [PSCustomObject]@{ Name="Asal Wardaastiani Azar";    Cat="Night"; ST="WORKING";   SS="22:00";  SE="07:00";  IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    # -- Voice --
    [PSCustomObject]@{ Name="Eva-Liane Schliwa";         Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Tri Toan Nguyen";           Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Elena Schlosser";           Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Vincent Grunzel";           Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Meik Schuelgen";            Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Kolja Christlieb";          Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Christian Pastors";         Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Darjusch Dropczinsky";      Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Walter Buxbaum";            Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Annabela Scavo";            Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Yevgeni Frenkel";           Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Boris Kostov";              Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Sam Alisha Metzner";        Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Mitko Kilogramski";         Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Arevig Ketenjian";          Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Tarek Tabbara";             Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Jonathan Freudenthaler";    Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    [PSCustomObject]@{ Name="Ralf Turski";               Cat="Voice"; ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc=$null;                 WicTask=$null;  Skip=$false },
    # -- vWIC: ShiftType=WORKING + WicShiftEntry(SupportLocation=VWIC, Task=VWIC, IsOnSite=1) --
    [PSCustomObject]@{ Name="Duc Quy Huynh";             Cat="vWIC";  ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc="VWIC";                WicTask="VWIC"; Skip=$false },
    [PSCustomObject]@{ Name="Gunter Dinkelmann";         Cat="vWIC";  ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc="VWIC";                WicTask="VWIC"; Skip=$false },
    [PSCustomObject]@{ Name="Isloodien Hurchem Lawrence";Cat="vWIC";  ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc="VWIC";                WicTask="VWIC"; Skip=$false },
    [PSCustomObject]@{ Name="Tim Nguyen";                Cat="vWIC";  ST="WORKING";   SS=$null;    SE=$null;    IWD=0; WicLoc="VWIC";                WicTask="VWIC"; Skip=$false },
    # -- WIC: ShiftType=WIC_DUTY (IsWicDuty=1) + WicShiftEntry(IsOnSite=1, SupportLocation=DisplayName) --
    [PSCustomObject]@{ Name="Danny Bendig";              Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Essen BP1";           WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Aakash Som";                Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Salzgitter";          WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Abdulrahman Aldera";        Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Stade";               WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Adam Szilvagyi";            Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Essenbach";           WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Ivan Leurs";                Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Denbosch";            WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Ayten Karatas";             Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Denbosch";            WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Binod Dutta";               Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Pfaffenhofen";        WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Bishal Maharjan";           Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Hamburg";             WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Christian Martino";         Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Dortmund";            WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Dennis Markus";             Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Potsdam";             WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Erdal Coskun";              Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Essen BP1";           WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Holger Kuhlmann";           Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Essen BP1";           WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Angelika Weber";            Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Essen BP1";           WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Eyup Akyurek";              Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Muenchen";            WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Hamza Forrousso";           Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Rendsburg";           WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Hesham Montasser";          Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Saarbruecken";        WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Holger Petzholdt";          Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Emmerthal";           WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Olaf Wittenberg";           Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Hannover";            WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Jannik Borner";             Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Brokdorf";            WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Joel Broring";              Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Stadland";            WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Erik Goecks";               Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Berlin - Gaussstr.";  WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Kaan Arslan";               Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Essen TK1";           WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Kamil Filipowicz";          Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Augsburg";            WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Klaus Friedrich";           Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Fuerstenwalde";       WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Mahboubeh Abdighara";       Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Osnabrueck";          WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Marcus Rusch";              Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Regensburg";          WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Mariusz Kozinski";          Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Bamberg";             WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Merlin Voss";               Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Helmstedt";           WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Senthuran Shanmugalingam";  Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Helmstedt";           WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Rene Altmeyer";             Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Demmin - Am Hanseufer";WicTask="WIC"; Skip=$false },
    [PSCustomObject]@{ Name="Mohammad Al Masalama";      Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Neu-Isenburg";        WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Tim Boger";                 Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Grafenrheinfeld";     WicTask="WIC";  Skip=$false },
    [PSCustomObject]@{ Name="Elliot van Staveren Kuster";Cat="WIC";   ST="WIC_DUTY";  SS=$null;    SE=$null;    IWD=1; WicLoc="Zwolle";              WicTask="WIC";  Skip=$false }
)

# ==== Load DB (read-only, outside transaction) ===============================

Write-Host ""
Write-Host "=== PS1_68 Phase 2 Insert ($shiftDate  DayOfWeek=$dow) ===" -ForegroundColor Yellow
Write-Host ""
Write-Host "Loading Employees and WicLocations ..." -ForegroundColor Cyan

$allEmployees = Invoke-Rows "SELECT EmployeeId, FullName FROM Employees WHERE IsActive = 1"
$allLocations = Invoke-Rows (
    "SELECT LocationCode, ISNULL(LocationCodeLegacy,'') AS LocationCodeLegacy, " +
    "       DisplayName, ISNULL(City,'') AS City FROM WicLocations WHERE IsActive = 1"
)

Write-Host ("  {0} employees  |  {1} locations loaded." -f $allEmployees.Count, $allLocations.Count) -ForegroundColor Green

# ==== Pre-resolve all names + WIC locations ==================================

Write-Host "Resolving names and locations ..." -ForegroundColor Cyan

$resolvedQueue = [System.Collections.Generic.List[PSCustomObject]]::new()
$preErrors     = [System.Collections.Generic.List[string]]::new()
$skipList      = [System.Collections.Generic.List[string]]::new()

foreach ($e in $allEntries) {
    if ($e.Skip) {
        $skipList.Add("$($e.Name)  [$($e.Cat)]  (explicit skip)")
        continue
    }

    # Resolve employee
    $hits = Resolve-Employee $e.Name
    if ($hits.Count -eq 0) {
        $preErrors.Add("UNMATCHED emp: $($e.Name)  [$($e.Cat)]")
        continue
    }
    if ($hits.Count -gt 1) {
        $preErrors.Add("AMBIGUOUS emp: $($e.Name)  [$($e.Cat)]  -> " + (($hits | ForEach-Object { $_.EmployeeId }) -join "/"))
        continue
    }
    $empId = $hits[0].EmployeeId

    # Resolve WIC location (if needed)
    $wicDisplayName = $null
    if ($null -ne $e.WicLoc -and $e.WicLoc -ne '' -and $e.WicLoc -ne 'VWIC') {
        $wicDisplayName = Resolve-WicDisplayName $e.WicLoc
        if ($null -eq $wicDisplayName) {
            $preErrors.Add("UNRESOLVED loc: '$($e.WicLoc)' for $($e.Name)")
            continue
        }
    } elseif ($e.WicLoc -eq 'VWIC') {
        $wicDisplayName = 'VWIC'   # literal marker, not a DB lookup
    }

    $resolvedQueue.Add([PSCustomObject]@{
        Name        = $e.Name
        Category    = $e.Cat
        EmployeeId  = $empId
        ShiftType   = $e.ST
        ShiftStart  = $e.SS
        ShiftEnd    = $e.SE
        IsWicDuty   = $e.IWD
        WicLoc      = $wicDisplayName
        WicTask     = $e.WicTask
    })
}

if ($preErrors.Count -gt 0) {
    Write-Host ""
    Write-Host "PRE-RESOLVE ERRORS -- cannot proceed:" -ForegroundColor Red
    foreach ($pe in $preErrors) { Write-Host "  $pe" -ForegroundColor Red }
    Write-Host ""
    Write-Host "Fix errors above and re-run. No data was written." -ForegroundColor Red
    exit 1
}

Write-Host ("  {0} to insert/update  |  {1} skipped" -f $resolvedQueue.Count, ($skipList.Count + $preErrors.Count)) -ForegroundColor Green
foreach ($s in $skipList) { Write-Host "  SKIP: $s" -ForegroundColor DarkGray }
Write-Host ""

# ==== Transaction =============================================================

$catStats = @{}

$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
$tx = $null

try {
    # SET XACT_ABORT ON before beginning the transaction
    $xaCmd = $conn.CreateCommand()
    $xaCmd.CommandText = "SET XACT_ABORT ON"
    [void]$xaCmd.ExecuteNonQuery()

    $tx = $conn.BeginTransaction()

    $seInserted  = 0
    $seUpdated   = 0
    $wicInserted = 0
    $wicUpdated  = 0

    foreach ($r in $resolvedQueue) {
        # ---- ShiftEntry upsert on (EmployeeId, ShiftDate) -------------------
        $seExists = [int](Exec-Scalar `
            "SELECT COUNT(*) FROM ShiftEntries WHERE EmployeeId = @e AND ShiftDate = @d" `
            @{ e = $r.EmployeeId; d = $shiftDate } $conn $tx) -gt 0

        if ($seExists) {
            [void](Exec-NonQuery `
                ("UPDATE ShiftEntries " +
                 "SET ShiftType = @st, ShiftStart = @ss, ShiftEnd = @se, " +
                 "    IsWicDuty = @iwd, SourceModule = 'PS1_68' " +
                 "WHERE EmployeeId = @e AND ShiftDate = @d") `
                @{ e   = $r.EmployeeId; d  = $shiftDate
                   st  = $r.ShiftType;  ss = (PV $r.ShiftStart); se = (PV $r.ShiftEnd)
                   iwd = $r.IsWicDuty } $conn $tx)
            $seUpdated++
        } else {
            [void](Exec-NonQuery `
                ("INSERT INTO ShiftEntries " +
                 "(EmployeeId, ShiftDate, ShiftType, ShiftStart, ShiftEnd, IsWicDuty, SourceModule) " +
                 "VALUES (@e, @d, @st, @ss, @se, @iwd, 'PS1_68')") `
                @{ e   = $r.EmployeeId; d  = $shiftDate
                   st  = $r.ShiftType;  ss = (PV $r.ShiftStart); se = (PV $r.ShiftEnd)
                   iwd = $r.IsWicDuty } $conn $tx)
            $seInserted++
        }

        # ---- WicShiftEntry upsert on (EmployeeId, ShiftDate, SupportLocation)
        if ($null -ne $r.WicLoc) {
            $wicExists = [int](Exec-Scalar `
                ("SELECT COUNT(*) FROM WicShiftEntries " +
                 "WHERE EmployeeId = @e AND ShiftDate = @d AND SupportLocation = @loc") `
                @{ e = $r.EmployeeId; d = $shiftDate; loc = $r.WicLoc } $conn $tx) -gt 0

            if ($wicExists) {
                [void](Exec-NonQuery `
                    ("UPDATE WicShiftEntries " +
                     "SET IsOnSite = 1, Task = @task, DayOfWeek = @dow " +
                     "WHERE EmployeeId = @e AND ShiftDate = @d AND SupportLocation = @loc") `
                    @{ e = $r.EmployeeId; d = $shiftDate; loc = $r.WicLoc
                       task = $r.WicTask; dow = $script:dow } $conn $tx)
                $wicUpdated++
            } else {
                [void](Exec-NonQuery `
                    ("INSERT INTO WicShiftEntries " +
                     "(EmployeeId, ShiftDate, DayOfWeek, SupportLocation, IsOnSite, IsGSDDay, IsOffDay, Task) " +
                     "VALUES (@e, @d, @dow, @loc, 1, 0, 0, @task)") `
                    @{ e = $r.EmployeeId; d = $shiftDate; dow = $script:dow
                       loc = $r.WicLoc; task = $r.WicTask } $conn $tx)
                $wicInserted++
            }
        }

        # Track per-category stats
        $cat = $r.Category
        if (-not $catStats.ContainsKey($cat)) {
            $catStats[$cat] = [PSCustomObject]@{ Inserted=0; Updated=0 }
        }
        if ($seExists) { $catStats[$cat].Updated++ } else { $catStats[$cat].Inserted++ }
    }

    $tx.Commit()
    Write-Host "Transaction committed successfully." -ForegroundColor Green

} catch {
    if ($null -ne $tx) { try { $tx.Rollback() } catch {} }
    Write-Host ""
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host "Transaction rolled back. No data was written." -ForegroundColor Red
    throw
} finally {
    $conn.Close()
}

# ==== Report =================================================================

Write-Host ""
Write-Host "=== INSERT REPORT ===" -ForegroundColor Yellow

foreach ($cat in ($catStats.Keys | Sort-Object)) {
    $s = $catStats[$cat]
    Write-Host ("  {0,-8} SE inserted={1}  SE updated={2}" -f $cat, $s.Inserted, $s.Updated)
}

Write-Host ""
Write-Host ("  ShiftEntry totals   : {0} inserted  {1} updated" -f $seInserted,  $seUpdated)
Write-Host ("  WicShiftEntry totals: {0} inserted  {1} updated" -f $wicInserted, $wicUpdated)

foreach ($s in $skipList) { Write-Host "  SKIPPED: $s" -ForegroundColor DarkGray }

# ==== Verification query =====================================================

Write-Host ""
Write-Host "=== VERIFICATION ===" -ForegroundColor Yellow

$countConn = Open-Conn
try {
    $verCmd = $countConn.CreateCommand()

    $verCmd.CommandText = "SELECT COUNT(*) FROM ShiftEntries WHERE ShiftDate = @d"
    [void]$verCmd.Parameters.AddWithValue("@d", $shiftDate)
    $totalSE = [int]$verCmd.ExecuteScalar()
    Write-Host ("  ShiftEntries for {0}: {1} total rows" -f $shiftDate, $totalSE) -ForegroundColor Green

    $verCmd.Parameters.Clear()
    $verCmd.CommandText = (
        "SELECT COUNT(*) FROM WicShiftEntries WHERE ShiftDate = @d AND IsOnSite = 1")
    [void]$verCmd.Parameters.AddWithValue("@d", $shiftDate)
    $totalWic = [int]$verCmd.ExecuteScalar()
    Write-Host ("  WicShiftEntries IsOnSite=1 for {0}: {1} total rows" -f $shiftDate, $totalWic) -ForegroundColor Green

    $verCmd.Parameters.Clear()
    $verCmd.CommandText = (
        "SELECT ShiftType, COUNT(*) AS Cnt FROM ShiftEntries WHERE ShiftDate = @d " +
        "GROUP BY ShiftType ORDER BY ShiftType")
    [void]$verCmd.Parameters.AddWithValue("@d", $shiftDate)
    $rdr = $verCmd.ExecuteReader()
    Write-Host "  Breakdown by ShiftType:" -ForegroundColor Cyan
    while ($rdr.Read()) {
        Write-Host ("    {0,-15} {1}" -f $rdr.GetString(0), $rdr.GetInt32(1))
    }
    $rdr.Close()
} finally { $countConn.Close() }

# ==== API smoke-check =========================================================

Write-Host ""
Write-Host "=== API SMOKE-CHECK ===" -ForegroundColor Yellow
try {
    $resp = Invoke-WebRequest -Uri "$baseUrl/api/wic/open?date=$shiftDate" `
            -Method GET -TimeoutSec 10 -UseBasicParsing
    if ($resp.StatusCode -eq 200) {
        $json   = $resp.Content | ConvertFrom-Json
        $onSite = @($json | Where-Object { $_.isOnSite -eq $true })
        Write-Host ("  /api/wic/open?date=$shiftDate  HTTP 200  -- {0} on-site agent(s) returned." -f $onSite.Count) -ForegroundColor Green
    } else {
        Write-Host ("  HTTP {0} from /api/wic/open" -f $resp.StatusCode) -ForegroundColor Yellow
    }
} catch {
    Write-Host "  Server not reachable -- start the app first if you want to smoke-check." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== PS1_68 complete ===" -ForegroundColor Cyan
Write-Host ""
