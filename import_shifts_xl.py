"""
import_shifts_xl.py
Opens Shift Plan Excel via COM, exports ALL sheets to CSV, parses shift
values, upserts into ShiftEntries for dates >= 2026-06-22.

Sheet layout expected:
  Rows 1-6  : header / info (skipped)
  Row  7    : column header (ID / Name / …)
  Row  8    : dates  e.g. "22-Jun-26"
  Row  9+   : employee data
    col 1 : row number
    col 2 : Employee ID
    col 3 : Name
    col 4 : Engagement
    col 5 : Primary Role
    col 6 : Secondary Role
    col 7 : Team Lead
    col 8+: shift value per date column

Sheets without a readable date row are silently skipped.
"""

import sys
import re
import csv
import io
import os
import tempfile
import shutil
import traceback
from datetime import date, timedelta, datetime

try:
    import pyodbc
except ImportError:
    print("ERROR: pyodbc not installed.  pip install pyodbc")
    sys.exit(1)

# ---------------------------------------------------------------------------
# Config
# ---------------------------------------------------------------------------
EXCEL_FILE = r"C:\Users\S69307\OneDrive - E.ON\Documents\Shift Plan 2026 (10).xlsx"
CUTOFF     = date(2026, 6, 22)

CONN_STR = (
    "DRIVER={SQL Server};"
    "SERVER=localhost\\SQLEXPRESS;"
    "DATABASE=GSDDashboard;"
    "Trusted_Connection=yes;"
)

# Known sheet name → clean SourceSheet code.
# Any sheet NOT listed here gets an auto-derived code (see sheet_to_source()).
SHEET_MAP = {
    'Shift Plan GSD DE': 'GSD_DE',
    'Shift Plan GSD NL': 'GSD_NL',
    'Shift Plan WIC':    'WIC',
    'Trainees':          'TRAINEES',
    'Resigned':          'RESIGNED',
}

# ---------------------------------------------------------------------------
# Sheet-name → SourceSheet code
# ---------------------------------------------------------------------------
def sheet_to_source(name):
    if name in SHEET_MAP:
        return SHEET_MAP[name]
    s = name
    for prefix in ('Shift Plan ', 'Shift '):
        if s.upper().startswith(prefix.upper()):
            s = s[len(prefix):]
            break
    code = re.sub(r'[^A-Z0-9]+', '_', s.upper().strip()).strip('_')
    return code[:20] or 'UNKNOWN'

# ---------------------------------------------------------------------------
# Date helpers
# ---------------------------------------------------------------------------
_EXCEL_EPOCH = date(1899, 12, 30)

def parse_date_string(s):
    """
    Parse dates in any format Excel COM may emit.
    Covers ISO, German locale (DE), US locale, and with/without time component.
    """
    if not s:
        return None
    s = s.strip()
    if not s:
        return None
    # Excel serial integer
    if s.isdigit():
        n = int(s)
        if 40000 < n < 60000:
            return _EXCEL_EPOCH + timedelta(days=n)
        return None
    # Strip trailing time component if present ("01.01.2026 00:00:00" → "01.01.2026")
    # so we can reuse the plain-date formats below without duplicating them.
    s_date = re.split(r'\s+\d{1,2}:\d{2}', s)[0].strip()
    for candidate in (s_date, s):      # try stripped-time version first, then original
        for fmt in (
            '%Y-%m-%d %H:%M:%S',       # ISO with time:    2026-01-01 00:00:00
            '%d.%m.%Y %H:%M:%S',       # DE with time:     01.01.2026 00:00:00
            '%d.%m.%y %H:%M:%S',       # DE short with time
            '%m/%d/%Y %H:%M:%S',       # US with time:     01/01/2026 00:00:00
            '%d/%m/%Y %H:%M:%S',       # EU with time
            '%d-%b-%y',                # 22-Jun-26
            '%d-%b-%Y',                # 22-Jun-2026
            '%d.%m.%Y',                # 01.01.2026  (DE)
            '%d.%m.%y',                # 01.01.26    (DE)
            '%m/%d/%Y',                # 01/01/2026  (US)
            '%m/%d/%y',
            '%d/%m/%Y',
            '%d-%m-%Y',
            '%Y-%m-%d',                # 2026-01-01  (ISO)
        ):
            try:
                return datetime.strptime(candidate, fmt).date()
            except ValueError:
                pass
    return None

# ---------------------------------------------------------------------------
# Shift value parser
# ---------------------------------------------------------------------------
_TIME_RE = re.compile(r'(\d{1,2}:\d{2})\s*[-–]\s*(\d{1,2}:\d{2})')

def parse_shift(raw):
    """
    Returns (shift_type, shift_start, shift_end, raw_value) or None to skip.
    OFF / OFFWE are skipped (not imported).
    """
    if raw is None:
        return None
    if isinstance(raw, date):
        return None

    s = str(raw).strip()
    if not s or s in ('/', '-', '0', 'None'):
        return None

    su = s.upper()

    if su.startswith('WIC'):                    # WIC  08:00 - 17:00  or just WIC
        m = _TIME_RE.search(s)
        if m:
            return ('WIC_DUTY', _fmt(m.group(1)), _fmt(m.group(2)), s)
        return ('WIC_DUTY', None, None, s)

    if 'TRAINING' in su:                        return ('TRAINING', None, None, s)
    if 'HAL'      in su:                        return ('HALF_AL',  None, None, s)  # HAL / HALF_AL
    if 'HSL'      in su:                        return ('SL',       None, None, s)
    if 'HCD'      in su:                        return ('CD',       None, None, s)

    if su in ('OFF', 'OFFWE', 'OFF_WEEKEND', 'OFF*', 'OFFDAY'):
        return None                             # skip – do not import

    if su in ('AL',  'AL*',  '*AL'):            return ('AL',       None, None, s)
    if su in ('SL',  'SL*',  '*SL',  'SICK'):   return ('SL',       None, None, s)
    if su in ('UL',  'UL*',  '*UL'):            return ('UL',       None, None, s)
    if su in ('CD',  'CD*',  '*CD'):            return ('CD',       None, None, s)
    if su in ('PH',  'PH*'):                    return ('PH',       None, None, s)
    if su in ('LPH',):                          return ('LPH',      None, None, s)
    if su in ('CO',):                           return ('CO',       None, None, s)
    if su in ('OL',):                           return ('OL',       None, None, s)
    if su in ('RESIGNED',):                     return ('RESIGNED', None, None, s)

    m = _TIME_RE.search(s)                      # plain time range → WORKING
    if m:
        return ('WORKING', _fmt(m.group(1)), _fmt(m.group(2)), s)

    return None

def _fmt(t):
    h, m = t.split(':')
    return f"{int(h):02d}:{m}"

# ---------------------------------------------------------------------------
# Date-row auto-detection
# ---------------------------------------------------------------------------
def _count_dates_in_row(row):
    """Return {col_idx: date} for all parseable date cells from col 8 (index 7) onward."""
    cols = {}
    for col_idx in range(7, len(row)):
        v = row[col_idx].strip() if isinstance(row[col_idx], str) else ''
        if not v:
            continue
        d = parse_date_string(v)
        if d:
            cols[col_idx] = d
    return cols


def find_date_row(all_rows):
    """
    Try row 8 (index 7) first – that is where GSD DE stores dates.
    If fewer than 3 dates are found there, fall back to row 7 (index 6) –
    that is where WIC / GSD NL / Trainees store dates.

    Returns (row_idx, {col_idx: date}) or (None, {}).
    Prints raw cell samples from both rows when nothing is found so the
    exact format is visible in the output for debugging.
    """
    for row_idx in (7, 6):
        if row_idx >= len(all_rows):
            continue
        cols = _count_dates_in_row(all_rows[row_idx])
        if len(cols) >= 3:
            return row_idx, cols

    # Nothing found – dump raw samples to help diagnose format issues
    for row_idx in (7, 6):
        if row_idx < len(all_rows):
            sample = all_rows[row_idx][7:13]
            print(f"\n    [diag] row idx={row_idx} cols 8-13: {sample}")
    return None, {}

# ---------------------------------------------------------------------------
# COM: open workbook, export ALL sheets to individual CSVs
# ---------------------------------------------------------------------------
def extract_all_sheets_via_com(excel_path):
    """
    Opens the workbook via Excel COM, discovers every sheet, and saves each
    one as a CSV in a fresh temp directory.

    Returns (tmp_dir, {sheet_name: csv_path}).
    Caller must shutil.rmtree(tmp_dir) when done.
    """
    try:
        import win32com.client
    except ImportError:
        print("ERROR: pywin32 not installed.  pip install pywin32")
        sys.exit(1)

    tmp_dir = tempfile.mkdtemp(prefix='gsd_xl_')
    results = {}

    excel = win32com.client.Dispatch("Excel.Application")
    excel.DisplayAlerts = False
    excel.Visible       = False

    try:
        abs_path = os.path.abspath(excel_path)
        print(f"  Opening : {abs_path}")

        # First pass: get sheet names without holding the workbook open
        wb = excel.Workbooks.Open(abs_path, UpdateLinks=0, ReadOnly=True)
        sheet_names = [ws.Name for ws in wb.Worksheets]
        wb.Close(False)
        print(f"  Sheets  : {sheet_names}")

        # Second pass: export each sheet one at a time.
        # Reopen the workbook per sheet because wb.SaveAs changes the wb path,
        # so closing it after each export is the cleanest reset.
        # Opening without ReadOnly avoids SaveAs failures on protected sheets.
        for name in sheet_names:
            safe     = re.sub(r'[^\w]', '_', name)
            csv_path = os.path.join(tmp_dir, f"{safe}.csv")

            try:
                wb = excel.Workbooks.Open(abs_path, UpdateLinks=0)
                wb.Worksheets(name).Activate()      # make target sheet active
                wb.SaveAs(csv_path, FileFormat=6)   # 6=xlCSV, saves active sheet only
                wb.Close(False)                     # close without re-saving original

                if os.path.exists(csv_path):
                    results[name] = csv_path
                    print(f"    '{name}'  →  {os.path.basename(csv_path)}")
                else:
                    print(f"    '{name}'  →  EXPORT FAILED (no file created)")

            except Exception as ex:
                print(f"    '{name}'  →  ERROR: {ex}")
                try:
                    wb.Close(False)
                except Exception:
                    pass

    except Exception as ex:
        print(f"  COM ERROR: {ex}")
        traceback.print_exc()
    finally:
        try:
            excel.Quit()
        except Exception:
            pass

    return tmp_dir, results

# ---------------------------------------------------------------------------
# CSV reader – auto-detect encoding and delimiter
# ---------------------------------------------------------------------------
def read_csv_file(path):
    for enc in ('utf-8-sig', 'utf-8', 'cp1252', 'latin-1'):
        try:
            with open(path, encoding=enc, newline='') as f:
                content = f.read()
            first = next((ln for ln in content.splitlines() if ln.strip()), '')
            delim = ';' if first.count(';') > first.count(',') else ','
            return list(csv.reader(io.StringIO(content), delimiter=delim))
        except UnicodeDecodeError:
            continue
    print(f"  ERROR: cannot decode {path}")
    return []

# ---------------------------------------------------------------------------
# Parse one exported sheet CSV
# ---------------------------------------------------------------------------
def parse_sheet_csv(csv_path, sheet_name, source_sheet):
    """
    Returns list of (emp_id, shift_date, shift_type, shift_start, shift_end, raw_value).
    Sheets that don't match the expected layout return [].
    """
    all_rows = read_csv_file(csv_path)
    print(f"  Rows: {len(all_rows)}", end='')

    if len(all_rows) < 7:
        print("  → too few rows, skipped")
        return []

    # Auto-detect which row (Excel rows 6-9, indices 5-8) contains the dates.
    # Different sheets use different layouts:
    #   GSD DE  → index 7  "22-Jun-26"
    #   WIC/NL/Trainees → index 6  "2026-01-01 00:00:00"
    date_row_idx, all_date_cols = find_date_row(all_rows)

    if not all_date_cols:
        sample = [all_rows[r][7:12] for r in range(5, min(9, len(all_rows)))]
        print(f"  → no date row found in rows 6-9, skipped  sample={sample}")
        return []

    # Filter to dates on or after the cutoff
    date_cols = {k: v for k, v in all_date_cols.items() if v >= CUTOFF}
    if not date_cols:
        all_sorted = sorted(all_date_cols.values())
        print(f"  → date row at idx {date_row_idx} but all dates < {CUTOFF} "
              f"({all_sorted[0]} → {all_sorted[-1]}), skipped")
        return []

    dates_sorted = sorted(date_cols.values())
    print(f"  | date row=idx{date_row_idx}  cols={len(date_cols)} "
          f"({dates_sorted[0]} → {dates_sorted[-1]})")

    shifts        = []
    skip_no_id    = 0
    skip_blank    = 0
    unknown_vals  = {}

    for row in all_rows[date_row_idx + 1:]:   # data starts one row after date row
        if not row:
            skip_no_id += 1
            continue
        raw_id = row[1].strip() if len(row) > 1 else ''
        if not raw_id:
            skip_no_id += 1
            continue
        emp_id = raw_id.split('.')[0].strip()
        if not emp_id or emp_id in ('0', 'None'):
            skip_no_id += 1
            continue

        for col_idx, shift_date in date_cols.items():
            if col_idx >= len(row):
                skip_blank += 1
                continue
            cell_val = row[col_idx].strip() or None
            parsed   = parse_shift(cell_val)
            if parsed is None:
                if cell_val and cell_val not in ('/', '-'):
                    unknown_vals[cell_val] = unknown_vals.get(cell_val, 0) + 1
                skip_blank += 1
                continue
            shifts.append((emp_id, shift_date) + parsed)

    print(f"  Parsed: {len(shifts)}  skipped: {skip_no_id} (no ID) / {skip_blank} (blank/OFF)")
    if unknown_vals:
        top = sorted(unknown_vals.items(), key=lambda x: -x[1])[:10]
        print(f"  Unknown values: {top}")

    return shifts

# ---------------------------------------------------------------------------
# Upsert  (key = EmployeeId + ShiftDate + SourceSheet)
# ---------------------------------------------------------------------------
def upsert_all(conn, shifts_by_source):
    cursor   = conn.cursor()
    inserted = 0
    updated  = 0
    errors   = 0
    done     = 0
    total    = sum(len(v) for v in shifts_by_source.values())

    for source_sheet, shifts in shifts_by_source.items():
        print(f"\n  [{source_sheet}]  {len(shifts)} records")
        for emp_id, shift_date, shift_type, shift_start, shift_end, raw_value in shifts:
            done += 1
            if done % 100 == 0:
                print(f"    {done}/{total}  ins={inserted}  upd={updated}  err={errors}")

            d_str  = shift_date.strftime('%Y-%m-%d')
            is_wic = 1 if shift_type == 'WIC_DUTY' else 0

            try:
                cursor.execute(
                    "SELECT Id FROM ShiftEntries "
                    "WHERE EmployeeId=? AND ShiftDate=? AND SourceSheet=?",
                    emp_id, d_str, source_sheet
                )
                existing = cursor.fetchone()

                if existing:
                    cursor.execute(
                        "UPDATE ShiftEntries "
                        "SET RawValue=?, ShiftType=?, ShiftStart=?, ShiftEnd=?, IsWicDuty=? "
                        "WHERE Id=?",
                        raw_value, shift_type, shift_start, shift_end, is_wic, existing[0]
                    )
                    updated += 1
                else:
                    cursor.execute(
                        "INSERT INTO ShiftEntries "
                        "(EmployeeId, ShiftDate, RawValue, ShiftType, "
                        " ShiftStart, ShiftEnd, IsWicDuty, SourceSheet) "
                        "VALUES (?,?,?,?,?,?,?,?)",
                        emp_id, d_str, raw_value, shift_type,
                        shift_start, shift_end, is_wic, source_sheet
                    )
                    inserted += 1

            except Exception as ex:
                errors += 1
                if errors <= 5:
                    print(f"    ERR {emp_id} / {d_str} / {source_sheet}: {ex}")

    conn.commit()
    cursor.close()
    return inserted, updated, errors

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def main():
    print()
    print("=" * 60)
    print("  GSD Shift Plan Import  (import_shifts_xl.py)")
    print("=" * 60)
    print(f"  File   : {EXCEL_FILE}")
    print(f"  Cutoff : {CUTOFF}")
    print()

    if not os.path.exists(EXCEL_FILE):
        print(f"ERROR: file not found: {EXCEL_FILE}")
        sys.exit(1)

    # ── 1. COM: export all sheets to temp CSVs ──────────────────────────────
    print("Step 1: Exporting all sheets via COM...")
    tmp_dir, csv_files = extract_all_sheets_via_com(EXCEL_FILE)
    print()

    if not csv_files:
        print("ERROR: no sheets exported.")
        shutil.rmtree(tmp_dir, ignore_errors=True)
        sys.exit(1)

    # ── 2. Parse each CSV ───────────────────────────────────────────────────
    print("Step 2: Parsing CSV data...")
    shifts_by_source = {}

    try:
        for sheet_name, csv_path in csv_files.items():
            source = sheet_to_source(sheet_name)
            print(f"\n  '{sheet_name}'  →  SourceSheet='{source}'")
            rows = parse_sheet_csv(csv_path, sheet_name, source)
            if rows:
                # Accumulate (same source code may appear on multiple sheets)
                shifts_by_source.setdefault(source, []).extend(rows)
    finally:
        shutil.rmtree(tmp_dir, ignore_errors=True)

    total = sum(len(v) for v in shifts_by_source.values())
    print(f"\n  Total records to import: {total}")
    print()

    if not total:
        print("Nothing to import.")
        return

    # ── 3. Connect ──────────────────────────────────────────────────────────
    print("Step 3: Connecting to SQL Server...")
    try:
        conn = pyodbc.connect(CONN_STR)
        conn.autocommit = False
        print("  Connected.")
    except Exception as ex:
        print(f"ERROR connecting: {ex}")
        sys.exit(1)

    # ── 4. Upsert ───────────────────────────────────────────────────────────
    print("\nStep 4: Upserting...")
    try:
        inserted, updated, errors = upsert_all(conn, shifts_by_source)
    except Exception as ex:
        conn.rollback()
        conn.close()
        print(f"ERROR: {ex}")
        traceback.print_exc()
        sys.exit(1)
    else:
        conn.close()

    print()
    print("=" * 60)
    print("  Import complete")
    print(f"  Inserted : {inserted}")
    print(f"  Updated  : {updated}")
    if errors:
        print(f"  Errors   : {errors}  (first 5 shown above)")
    print("=" * 60)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        sys.exit(1)
