# PS1_30_ImportShifts.ps1
# Runs the Python shift import script against the uploaded Excel file.

$xlsxPath  = "C:\GSDDashboard\Shift_Plan_2026__9_.xlsx"
$pyScript  = "C:\GSDDashboard\import_shifts.py"

Write-Host ""
Write-Host "==========================================" -ForegroundColor Yellow
Write-Host "  PS1_30: Shift Plan Import" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

# ── 1. Check Excel file ──────────────────────────────────────────────────────
if (-not (Test-Path $xlsxPath)) {
    Write-Host ""
    Write-Host "ERROR: Excel file not found at:" -ForegroundColor Red
    Write-Host "  $xlsxPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Copy the file there first, then re-run this script." -ForegroundColor Yellow
    exit 1
}
Write-Host ""
Write-Host "Excel file found: $xlsxPath" -ForegroundColor Green

# ── 2. Check Python ──────────────────────────────────────────────────────────
$pyExe = $null
foreach ($candidate in @("python", "python3", "py")) {
    try {
        $v = & $candidate --version 2>&1
        if ($v -match "Python") { $pyExe = $candidate; break }
    } catch {}
}
if (-not $pyExe) {
    Write-Host ""
    Write-Host "ERROR: Python not found in PATH." -ForegroundColor Red
    Write-Host "Install Python 3 and ensure it is on PATH, then retry." -ForegroundColor Yellow
    exit 1
}
Write-Host "Python  : $pyExe  ($( & $pyExe --version 2>&1 ))" -ForegroundColor Green

# ── 3. Check / install dependencies ─────────────────────────────────────────
Write-Host ""
Write-Host "Checking Python packages..." -ForegroundColor Cyan

foreach ($pkg in @("openpyxl", "pyodbc")) {
    $check = & $pyExe -c "import $pkg; print('ok')" 2>&1
    if ($check -ne "ok") {
        Write-Host "  Installing $pkg ..." -ForegroundColor Yellow
        & $pyExe -m pip install $pkg --quiet
    } else {
        Write-Host "  $pkg  OK" -ForegroundColor Green
    }
}

# ── 4. Extract 'Shift Plan GSD DE' to clean xlsx via Excel COM ───────────────
$cleanPath  = "C:\GSDDashboard\ShiftPlanDE_clean.xlsx"
$csvPath    = "C:\GSDDashboard\ShiftPlanDE.csv"
$importPath = $xlsxPath   # fallback: use original if COM fails

Write-Host ""
Write-Host "Step 4: Extracting 'Shift Plan GSD DE' to clean xlsx via COM..." -ForegroundColor Cyan

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible       = $false
    $excel.DisplayAlerts = $false

    $wb = $excel.Workbooks.Open($xlsxPath)
    $ws = $wb.Sheets.Item('Shift Plan GSD DE')
    $ws.Copy()                                              # new single-sheet workbook
    $excel.ActiveWorkbook.SaveAs($cleanPath, 51)            # 51 = xlOpenXMLWorkbook
    $excel.ActiveWorkbook.Close($false)
    $wb.Close($false)
    $excel.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null

    Write-Host "  Saved clean xlsx: $cleanPath" -ForegroundColor Green
    $importPath = $cleanPath
} catch {
    Write-Host "  COM xlsx extraction failed: $_" -ForegroundColor Yellow
    Write-Host "  Falling back to original file (will use 'Resigned' sheet)" -ForegroundColor Yellow
}

# ── 5. Export clean xlsx as CSV via Excel COM ─────────────────────────────────
if ($importPath -eq $cleanPath) {
    Write-Host ""
    Write-Host "Step 5: Exporting clean xlsx as CSV via COM..." -ForegroundColor Cyan
    try {
        $excel2 = New-Object -ComObject Excel.Application
        $excel2.Visible       = $false
        $excel2.DisplayAlerts = $false

        $wb2 = $excel2.Workbooks.Open($cleanPath)
        $wb2.SaveAs($csvPath, 6)                            # 6 = xlCSV
        $wb2.Close($false)
        $excel2.Quit()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel2) | Out-Null

        Write-Host "  Saved CSV: $csvPath" -ForegroundColor Green
        $importPath = $csvPath
    } catch {
        Write-Host "  CSV export failed: $_" -ForegroundColor Yellow
        Write-Host "  Falling back to clean xlsx" -ForegroundColor Yellow
    }
}

# ── 6. Run import ────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Running import ($( Split-Path $importPath -Leaf ))..." -ForegroundColor Cyan
Write-Host ""

& $pyExe $pyScript $importPath

Write-Host ""
Write-Host "==========================================" -ForegroundColor Yellow
Write-Host "  PS1_30 complete" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow
