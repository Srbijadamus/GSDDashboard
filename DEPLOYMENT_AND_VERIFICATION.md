# GSDDashboard — Deployment & Verification Rules

> **Read this before claiming any frontend or backend change is "done".**
> These rules exist because of a recurring incident: changes were verified on
> localhost:5173 (Vite dev server) and reported as complete, while the user
> kept seeing the old UI on the tunnel.

---

## Definition of Done — Frontend Change

Before reporting a frontend task as complete, every box below must be checked:

- [ ] Source file edited
- [ ] `npm run build` run in `Frontend/` (writes directly into `Backend/wwwroot`)
- [ ] The new bundle file name appears in `Backend/wwwroot/assets/` (timestamp confirms freshness)
- [ ] `http://localhost:5000/` returns HTTP 200
- [ ] The served JS bundle at port 5000 contains the expected new text/content (grep or curl)
- [ ] **"first line = header" / old placeholder text is absent from the served bundle**
- [ ] User instructed to hard-refresh (Ctrl+Shift+R) — browsers cache old bundles

Only when all boxes are checked is a frontend change confirmed live.

---

## Rule 1 — The user's real URL is the tunnel

```
https://d2jn94qg-5000.euw.devtunnels.ms/
```

This tunnel fronts the backend on **port 5000**, which serves the **production
build** from `Backend/wwwroot`. This is the **only surface that counts as "what
the user sees"**. Every verification must happen against this URL (or its
equivalent at `http://localhost:5000`).

---

## Rule 2 — localhost:5173 is the Vite dev server only

`localhost:5173` runs a Vite HMR dev server. It does NOT serve the same bundle
as the tunnel. Passing on 5173 **does not mean the change is live for the user**.

**Never report a frontend change as done based on localhost:5173 alone.**

---

## Rule 3 — Frontend "done" = visible in the port-5000 served build

Source edits reach the user ONLY after this sequence is complete:

1. `cd Frontend && npm run build`
   — Vite builds and writes output **directly into `Backend/wwwroot/`**
     (`outDir: '../Backend/wwwroot'` in `vite.config.ts`); old files are wiped.
2. The running backend (port 5000) serves files from `Backend/wwwroot` live from
   disk — **no backend restart is needed for frontend-only changes**.
3. Verify: fetch the port-5000 bundle and confirm the new content is present.
4. Tell the user to hard-refresh (Ctrl+Shift+R) — browsers cache old bundles.

---

## Rule 4 — Always verify on the served build, not on 5173

After `npm run build`, run:

```bash
# Find the JS bundle file name
curl -s http://localhost:5000/ | grep -o 'assets/index-[^"]*\.js'

# Confirm new text is present
curl -s http://localhost:5000/assets/<bundle>.js | grep -o "expected new text"

# Confirm old text is absent
curl -s http://localhost:5000/assets/<bundle>.js | grep -o "old stale text"
# should return empty
```

Only report success when this output confirms the new content.

---

## Rule 5 — Use the existing build/deploy script

The canonical build+deploy script is:

```
C:\GSDDashboard\PS1_19_FinalBuildVerify.ps1
```

Do not invent a new deploy process. This script: builds frontend → stops any
running API process → copies dist to wwwroot → builds backend → starts server →
runs API and page verification.

For a quick frontend-only rebuild (no C# changes):

```powershell
cd C:\GSDDashboard\Frontend
npm run build
# No restart needed — backend serves wwwroot live from disk
```

Then verify on port 5000 as per Rule 4.

---

## Rule 6 — Hard-refresh after every deploy

Always tell the user:

> **Ctrl+Shift+R** (or Cmd+Shift+R on Mac) to hard-refresh — the browser may
> have cached the old JS bundle and will silently show the old UI otherwise.

---

## Rule 7 — Backend (C#) changes need a full EXE rebuild

Backend changes (`.cs` files) require:

1. Stop the `GSDDashboard-Backend` scheduled task — this kills the watchdog and
   releases the lock on `GSDDashboard.API.exe`:
   ```
   schtasks /end /tn "GSDDashboard-Backend"
   taskkill /IM GSDDashboard.API.exe /F
   ```
2. Build the backend:
   ```
   cd C:\GSDDashboard\Backend
   dotnet build --configuration Release
   ```
3. Restart the scheduled task (watchdog auto-restarts the EXE):
   ```
   schtasks /run /tn "GSDDashboard-Backend"
   ```
4. Wait ~8 seconds, then verify `http://localhost:5000/` returns HTTP 200.
5. Verify the relevant API endpoint returns the expected response.

The watchdog script (`C:\HealthCheck\watchdog_gsd_backend.ps1`) runs the EXE
with working directory `C:\GSDDashboard\Backend`, so `Backend/wwwroot` is always
the correct static-files root.

---

## Architecture quick-reference

| Surface | URL | Serves from | When to verify |
|---|---|---|---|
| Vite dev server | `http://localhost:5173` | HMR in-memory | Never — dev only |
| Backend (prod) | `http://localhost:5000` | `Backend/wwwroot` | Always |
| Tunnel (user-visible) | `https://d2jn94qg-5000.euw.devtunnels.ms/` | Same as port 5000 | Always |

---

*See also: `DEPLOY.md` for full deployment reference, `PS1_19_FinalBuildVerify.ps1` for the canonical build+verify script.*
