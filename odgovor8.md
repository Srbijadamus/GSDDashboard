CORRECTION to the state you were given:

The live backend PID is now 26796, NOT 11704. It restarted at some point - most likely the
watchdog (C:\HealthCheck\watchdog_gsd_backend.ps1) brought it back up. Verified by me:
netstat shows 127.0.0.1:5000 and [::1]:5000 in ABHOEREN (German Windows) under PID 26796,
with several established connections.

Two consequences:
1. Your netstat filters must match the GERMAN Windows output - ABHOEREN, not LISTENING.
   You already knew this (you used ':5050.*ABH' earlier). Use it consistently or the check
   silently returns nothing and you conclude the service is down when it is not.
2. The watchdog will restart the backend on its own. Before deploying, confirm how it
   behaves during your stop/build/start sequence - if it races you and restarts the old
   binary mid-deploy, the deploy silently fails. Report your plan for that before acting.

Also note: the backend binds to localhost only (127.0.0.1 and ::1). External access is via
the devtunnel. Keep it that way.

Continue with the PS1_AuthSmokeTest.ps1 rewrite. Do not deploy yet.
