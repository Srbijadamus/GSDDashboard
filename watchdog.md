The fix works - I can see the shifts now. Thanks for flagging the watchdog race honestly
instead of glossing over it.

PRIORITY CHANGE: do the watchdog sentinel FIRST, before any new features.

Your build at 18:54 was overtaken by the watchdog relaunching the old exe mid-copy. It only
came out right because you noticed and redid it. That exact failure mode - deploy looks fine,
old code keeps running - has bitten this project before. Fix it structurally now.

TASK 1 - watchdog pause sentinel
- In C:\HealthCheck\watchdog_gsd_backend.ps1, at the top of the loop: if
  C:\HealthCheck\DEPLOY_IN_PROGRESS exists, log "DEPLOY PAUSED (sentinel present)",
  sleep 30, continue - do not launch the exe.
- Back up the watchdog script before editing it. This is shared infrastructure.
- Update build-deploy.ps1 (or whatever the canonical deploy path is) to create the sentinel
  first and remove it in a finally block, so a failed deploy cannot leave it behind forever.
- Add a safety valve: if the sentinel is older than 30 minutes, the watchdog ignores it and
  resumes normally. A forgotten sentinel must never leave the backend down indefinitely.
- Test it: create the sentinel, stop the API, confirm the watchdog does NOT relaunch it,
  remove the sentinel, confirm it comes back. Show me the watchdog log for both.

TASK 2 - only after task 1 is proven
Then build the WIC location column as you proposed. But find an agent who actually HAS WIC
duty in the future and test with that one - an empty column on Anas proves nothing.

Do not deploy anything else. Report after task 1 and stop.
