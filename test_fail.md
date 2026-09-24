ONE TASK: the failing test DeleteBatch_RemovesOwnedRows_SoftDeletesBatch.

Your previous session ended mid-investigation. The banner work is done and the frontend is
built and copied - do not redo it.

The test fails with System.InvalidOperationException. You said yourself you did not touch
DeleteBatchAsync, so it likely failed before your change too.

Find out:
1. the full exception and stack trace
2. whether the bug is in the test or in DeleteBatchAsync itself
3. whether deleting a roster batch actually works against the real database, or whether the
   delete button in the UI is broken

Report before fixing. If DeleteBatchAsync is genuinely broken, that matters - I have three
generated batches in the live database and no safe way to remove one.

To capture stderr from dotnet test, use: cmd /c "... 2>&1"  - PowerShell redirection has been
swallowing it.

Do not deploy, do not touch the enforcement flag, C:\HealthCheck, or ports 5016 and 8000.
