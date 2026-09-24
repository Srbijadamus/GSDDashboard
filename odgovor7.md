Your previous session got stuck in a loop: five identical editor calls on
PS1_AuthSmokeTest.ps1, each failing because old_text was null. Do not repeat that.

RULE: if the same tool call fails twice in a row, STOP. Do not retry a third time.
Report what you were trying to change and ask me. Repeating a failing call wastes my
paid credits and changes nothing.

If the editor tool keeps rejecting the edit, use a different method: read the whole file,
rewrite it completely with a single write, or hand me a PS1 command to run myself.

CURRENT STATE - verify it yourself before acting, do not trust this blindly:
- Nothing is deployed. Port 5000 still runs the OLD build (PID 11704).
- PS1_AuthSmokeTest.ps1 already has the -ExpectEnforcement param and the header warnings.
- What was unfinished: making the remaining checks conditional on -ExpectEnforcement,
  especially that the POST /api/bo-list write test is SKIPPED when the flag is OFF.

TASK
1. Show me the current state of PS1_AuthSmokeTest.ps1 - which checks are already
   conditional and which are not.
2. Finish the conditional logic in ONE clean rewrite of the file.
3. Verify by running it against nothing destructive - do NOT run it against port 5000 yet.
4. Then STOP and report. Deploy only after I say go again.
