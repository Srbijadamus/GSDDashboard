Plan approved. Go ahead with (a) report, (b) single-write rewrite, (c) parse check, (d) stop.

Answer to your question: NO, I do not know what restarted it. Identify PID 26796 as part of
this pass - full path, command line, start time, and whether it is the scheduled task
GSDDashboard-Backend or the watchdog C:\HealthCheck\watchdog_gsd_backend.ps1 that owns it.
Read-only inspection, do not kill it.

This matters for deploy: if the watchdog restarts the backend on its own, it can race your
stop/build/start sequence and bring the OLD binary back up mid-deploy - the deploy then
reports success while I keep running old code. We have been burned by exactly this before
with scheduled tasks. Before you deploy anything, give me your plan for handling the watchdog
during the deploy window.

Two more rules for the rewrite:
- After writing, grep the file yourself and show me EVERY line where $ExpectEnforcement
  appears, so I can see the logic landed below line 19. A read-back claim is not enough.
- Make sure the header comment and the code now agree. A comment promising a skip that the
  code does not perform is worse than no comment.
