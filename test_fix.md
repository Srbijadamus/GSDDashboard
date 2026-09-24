ONE SMALL TASK: fix the failing roster tests.

Root cause is already established - do not re-investigate:
The tests use the EF Core InMemory provider, which throws on BeginTransactionAsync.
GenerateAsync uses a transaction, so the tests fail in setup, before DeleteBatchAsync is ever
reached. DeleteBatchAsync itself is fine - the real database is SQL Server where transactions
work.

Fix: in the test DbContext factory (NewDb in RosterServiceTests.cs), add
  .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))

Then run the full test suite and paste the output. Use cmd /c "... 2>&1" to capture stderr -
PowerShell redirection swallows it.

Change nothing else. Do not touch RosterService.cs. Do not deploy, do not touch the
enforcement flag, C:\HealthCheck, or ports 5016 and 8000.
