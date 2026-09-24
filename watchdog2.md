Approved - execute task 1 exactly as planned, including your addition: stop the
script-started verification server in the finally block so the watchdog is the single
supervisor that brings the final exe up. That catch is the right call - the 2026-09-01
crash-loop is exactly what we are trying to avoid.

Previous session died with "Error: Unknown session" right after presenting this plan.
Nothing was executed. Re-verify state before acting.

Execute steps 1-7:
- back up watchdog_gsd_backend.ps1 before editing
- sentinel pause + 30 min stale valve with the log line
- PS1_19_FinalBuildVerify.ps1: sentinel first, try/finally removes it AND stops the
  script-started server
- restart the scheduled task so the new watchdog logic is loaded
- run all three tests (pause / resume / stale valve) and show me the watchdog log for each
- update DEPLOYMENT_AND_VERIFICATION.md
- then STOP

I accept the 1-2 minute outage during the pause test. Do it now rather than later.
Do not start task 2.
