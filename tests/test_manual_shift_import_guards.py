"""Execute the production importer write loops with fake cursors, never SQL/Excel."""
import ast
import contextlib
import io
from datetime import date
from pathlib import Path
import unittest

from shift_import_guard import preserve_manual_shift


ROOT = Path(__file__).resolve().parents[1]


class Cursor:
    def __init__(self, manual=False, existing=False):
        self.manual = manual
        self.existing = existing
        self.calls = []
        self.rowcount = 1
        self.row = None

    def execute(self, sql, *args):
        self.calls.append((sql, args))
        if sql.startswith("SELECT TOP"):
            self.row = (99,) if self.manual else None
        elif sql.startswith("SELECT Id"):
            self.row = (77,) if self.existing else None
        return self

    def fetchone(self):
        return self.row

    def close(self):
        pass


class Connection:
    def __init__(self, cursor):
        self.fake_cursor = cursor
        self.committed = False

    def cursor(self):
        return self.fake_cursor

    def commit(self):
        self.committed = True


def execute_importer(filename, cursor):
    tree = ast.parse((ROOT / filename).read_text(encoding="utf-8-sig"))
    env = {"preserve_manual_shift": preserve_manual_shift}
    output = io.StringIO()
    conn = Connection(cursor)
    with contextlib.redirect_stdout(output):
        if filename == "import_shifts_xl.py":
            function = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == "upsert_all")
            exec(compile(ast.Module(body=[function], type_ignores=[]), filename, "exec"), env)
            result = env["upsert_all"](conn, {"OTHER_SHEET": [("E1", date(2026, 10, 5), "SL", None, None, "SL")]})
        else:
            main = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == "main")
            loop = next(n for n in ast.walk(main) if isinstance(n, ast.For) and
                        isinstance(n.target, ast.Tuple) and
                        [getattr(e, "id", None) for e in n.target.elts] ==
                        ["emp_id", "shift_date", "shift_type", "shift_start", "shift_end"])
            env.update(cursor=cursor, shifts=[("E1", date(2026, 10, 5), "SL", None, None)],
                       inserted=0, updated=0, errors=0, manual_preserved=0,
                       SOURCE_SHEET="OTHER_SHEET", SOURCE_MOD="EXCEL_IMPORT")
            exec(compile(ast.Module(body=[loop], type_ignores=[]), filename, "exec"), env)
            result = env["inserted"], env["updated"], env["errors"]
    return result, output.getvalue()


class ManualShiftImportGuardTests(unittest.TestCase):
    def test_both_importers_preserve_manual_rows_across_source_sheets(self):
        for filename in ("import_shifts.py", "import_shifts_xl.py"):
            with self.subTest(filename=filename):
                cursor = Cursor(manual=True, existing=True)
                result, output = execute_importer(filename, cursor)
                self.assertEqual((0, 0, 0), result)
                self.assertEqual(1, len(cursor.calls))
                sql, args = cursor.calls[0]
                self.assertIn("WITH (UPDLOCK, HOLDLOCK)", sql)
                self.assertIn("ManualChangeBy", sql)
                self.assertNotIn("SourceSheet", sql)
                self.assertEqual(("E1", "2026-10-05"), args)
                self.assertIn("MANUAL PRESERVED", output)

    def test_both_importers_keep_automatic_updates_and_inserts(self):
        for filename in ("import_shifts.py", "import_shifts_xl.py"):
            for existing in (True, False):
                with self.subTest(filename=filename, existing=existing):
                    cursor = Cursor(existing=existing)
                    result, _ = execute_importer(filename, cursor)
                    self.assertEqual((0, 1, 0) if existing else (1, 0, 0), result)
                    writes = [sql for sql, _ in cursor.calls if sql.startswith(("UPDATE", "INSERT"))]
                    self.assertEqual(1, len(writes))
                    if existing:
                        self.assertIn("NULLIF(LTRIM(RTRIM(ManualChangeBy)), '') IS NULL", writes[0])

    def test_missing_marker_column_fails_closed_instead_of_writing(self):
        class MissingColumnCursor(Cursor):
            def execute(self, sql, *args):
                raise RuntimeError("Invalid column name ManualChangeBy")
        for filename in ("import_shifts.py", "import_shifts_xl.py"):
            with self.subTest(filename=filename):
                result, output = execute_importer(filename, MissingColumnCursor())
                self.assertEqual((0, 0, 1), result)
                self.assertIn("ManualChangeBy", output)

    def test_refused_update_is_reported_and_not_counted_as_saved(self):
        for filename in ("import_shifts.py", "import_shifts_xl.py"):
            with self.subTest(filename=filename):
                cursor = Cursor(existing=True)
                cursor.rowcount = 0
                result, output = execute_importer(filename, cursor)
                self.assertEqual((0, 0, 0), result)
                self.assertIn("NOT UPDATED", output)


if __name__ == "__main__":
    unittest.main()
