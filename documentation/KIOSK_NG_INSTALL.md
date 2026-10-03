# Kiosk NG installation - canonical instructions

The former test-laptop procedure in this file is superseded. Use
**`C:\ShiftKiosk_secure\documentation\KIOSK_NG_ROLLOUT.md`** as the single source
for package composition, hostname enrollment, Downloads/admin cmd/LAPSADMIN
installation, code/token ACLs, per-user logs, old-client removal and rollback.

Current secure-copy installer requires no requests/pip/shared vendor, gives the
UI Limited privileges, uses protected code and a separate NG public shortcut.
It has **not** been tested on a clean machine. The original DESKTOP-1302198
cycle is proven; no new clean-machine result, hotkey test or live deployment
is claimed.

Live state and server/Pulse ordering are maintained in
[KIOSK_HARDENING_DEPLOY_NOTE.md](KIOSK_HARDENING_DEPLOY_NOTE.md) and
[LIVE_DEPLOY_RUNBOOK.md](LIVE_DEPLOY_RUNBOOK.md): dan1 now, Pulse proxy/service
credential first, supervisor preparation, then dan2/dan3.
