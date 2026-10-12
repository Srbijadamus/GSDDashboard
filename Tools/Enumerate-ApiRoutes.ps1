# Enumerate-ApiRoutes.ps1
#
# READ ONLY. Rebuilds the API surface from the source: every MapGet/MapPost/MapPut/
# MapPatch/MapDelete registration, resolved against the MapGroup prefix that encloses it,
# plus the verb-less Map(...) registrations.
#
# No HTTP call is made from here: probing 150+ endpoints live would need a write-capable
# method on a production instance, and this task is a database backfill whose only agreed
# write is the 21 WicShiftEntries rows.
#
# Usage: pwsh -File Tools\Enumerate-ApiRoutes.ps1 [-Json]

[CmdletBinding()]
param(
    [string] $BackendRoot = "",
    [string] $OutFile = "",
    [switch] $Json
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($BackendRoot)) {
    $BackendRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "Backend"
}

$files = Get-ChildItem -LiteralPath $BackendRoot -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }
$files = $files + (Get-ChildItem -LiteralPath $BackendRoot -Filter *.cs -File)

$routes = New-Object System.Collections.ArrayList
$groups = @{}
foreach ($f in ($files | Sort-Object FullName -Unique)) {
    $lines = Get-Content -LiteralPath $f.FullName -Encoding UTF8
    foreach ($ln in $lines) {
        # var grp = app.MapGroup("/api/x").WithTags(...)   (single-line declaration)
        $mg = [regex]::Match($ln, '(?:var\s+)?(\w+)\s*=\s*app\.MapGroup\(\s*"([^"]+)"')
        if ($mg.Success) { $groups[$mg.Groups[1].Value] = $mg.Groups[2].Value }
    }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        # a new method resets the group map (each mapper method declares its own groups)
        if ($l -match '^\s*(public|private|internal|static)\b.*\(.*\)\s*$' -or $l -match '^\s*public static class') { }
        # MapX on a group variable: grp.MapGet("/x", ...)
        foreach ($m in [regex]::Matches($l, '(\w+)\.Map(Get|Post|Put|Patch|Delete)\(\s*"([^"]*)"')) {
            $var = $m.Groups[1].Value
            if ($var -eq 'app') { continue }
            $prefix = if ($groups.ContainsKey($var)) { $groups[$var] } else { "" }
            [void]$routes.Add([pscustomobject]@{
                File = $f.Name; Line = $i + 1; Method = $m.Groups[2].Value.ToUpper()
                Full = ($prefix + $m.Groups[3].Value); Raw = $m.Groups[3].Value; Prefix = $prefix
            })
        }
        # app.MapGet("/x", ...) with an absolute or relative path
        foreach ($m in [regex]::Matches($l, '(?<![\w.])app\.Map(Get|Post|Put|Patch|Delete)\(\s*"([^"]*)"')) {
            [void]$routes.Add([pscustomobject]@{
                File = $f.Name; Line = $i + 1; Method = $m.Groups[1].Value.ToUpper()
                Full = $m.Groups[2].Value; Raw = $m.Groups[2].Value; Prefix = ""
            })
        }
        # verb-less app.Map("/x", ...)
        foreach ($m in [regex]::Matches($l, '(?<![\w.])app\.Map\(\s*"([^"]*)"')) {
            [void]$routes.Add([pscustomobject]@{
                File = $f.Name; Line = $i + 1; Method = "ANY"; Full = $m.Groups[1].Value
                Raw = $m.Groups[1].Value; Prefix = ""
            })
        }
    }
}

$final = New-Object System.Collections.ArrayList
foreach ($r in $routes) {
    if ($r.Raw -eq "/" -and $r.Prefix -eq "") {
        # a root route whose group prefix was not resolved - keep it as-is and flag it
        $full = "/?unresolved"
    } else {
        $full = $r.Full
    }
    [void]$final.Add([pscustomobject]@{ Method = $r.Method; Path = $full; File = $r.File; Line = $r.Line })
}

if ($Json) {
    $txt = ($final | ConvertTo-Json -Depth 4)
} else {
    $o = New-Object System.Collections.ArrayList
    [void]$o.Add("API SURFACE (rebuilt from source, read only)")
    [void]$o.Add("source root : $BackendRoot")
    [void]$o.Add("registrations: $($final.Count)")
    [void]$o.Add("")
    [void]$o.Add(("{0,-7} {1,-64} {2}:{3}" -f "method", "path", "file", "line"))
    [void]$o.Add(("{0,-7} {1,-64} {2}:{3}" -f ("-"*7), ("-"*64), ("-"*14), ("-"*5)))
    foreach ($r in ($final | Sort-Object Path, Method)) {
        [void]$o.Add(("{0,-7} {1,-64} {2}:{3}" -f $r.Method, $r.Path, $r.File, $r.Line))
    }
    [void]$o.Add("")
    [void]$o.Add("by method:")
    foreach ($g in ($final | Group-Object Method | Sort-Object Name)) {
        [void]$o.Add(("  {0,-7} {1}" -f $g.Name, $g.Count))
    }
    $txt = ($o -join "`r`n")
}

if (-not [string]::IsNullOrWhiteSpace($OutFile)) { $txt | Set-Content -LiteralPath $OutFile -Encoding UTF8 }
$txt
