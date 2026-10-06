"""Manual-master guard shared by the offline Excel importers."""


def preserve_manual_shift(cursor, employee_id, shift_date):
    # Hold key/range locks until the import transaction commits; a human edit
    # racing the import cannot slip between the guard and its upsert.
    cursor.execute(
        "SELECT TOP (1) Id FROM ShiftEntries WITH (UPDLOCK, HOLDLOCK) "
        "WHERE EmployeeId=? AND ShiftDate=? "
        "AND NULLIF(LTRIM(RTRIM(ManualChangeBy)), '') IS NOT NULL",
        employee_id, shift_date,
    )
    row = cursor.fetchone()
    if row is None:
        return False
    print(f"  MANUAL PRESERVED: {employee_id} / {shift_date} / Id={row[0]}")
    return True
