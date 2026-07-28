# GSDDashboard — Deployment Guide

## Frontend build

```bash
cd Frontend
npm run build
```

`tsc` type-checks first, then Vite builds and writes output **directly into
`Backend/wwwroot/`** (configured in `vite.config.ts` via
`build.outDir: '../Backend/wwwroot'`). The .NET backend serves the new files
immediately — no copy step, no backend restart needed.

### ⚠️ Do not put files in wwwroot manually

`npm run build` runs with `emptyOutDir: true`, which **wipes `Backend/wwwroot/`
before every build**. Anything placed there by hand will be deleted.

If you need a static asset in the build (logo, icon, config file), place it in
**`Frontend/public/`** — Vite copies that directory into every build output.

## Where static files are served from

`Backend/Program.cs` calls:
```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
```
All three read from **`Backend/wwwroot/`** (the ASP.NET Core default static
root). There is no other serving location.

## Development (no build step)

```bash
# Terminal 1 — backend API
cd Backend && dotnet run

# Terminal 2 — Vite dev server with hot-reload
cd Frontend && npm run dev
```

Vite proxies `/api/*` to `http://localhost:5000` (see `vite.config.ts`).
Open `http://localhost:5173` in the browser.

---

## Assistant API

### POST /api/assistant/ask

Primary assistant endpoint. Routes the question to the highest-scoring domain
handler (10 domains) and returns a structured response.

**Request:** `{ "question": "who is on leave next week" }`

**Response:**
```json
{
  "answerText": "17 WIC agents on annual leave (2026-07-28 → 2026-08-03).",
  "dateRangeChecked": "2026-07-28 → 2026-08-03",
  "table": [...],
  "error": null,
  "hint": "WIC agents only. For all employees: \"show all employee vacation\""
}
```

**`hint` field rules:**
- Present only when `WicLeaveHandler` answers a question that contained no explicit
  domain word ("wic", "all employ", "alle mitarbeiter"). Tells the user the answer
  is WIC-scoped and how to ask for all employees.
- Absent (not serialized) when null — `[JsonIgnore(Condition = WhenWritingNull)]`.
- The legacy `/api/wic-assistant/ask` endpoint never returns `hint` (different DTO).

**Encoding:** `Content-Type: application/json; charset=utf-8`. The `→` separator
in `dateRangeChecked` is U+2192 (UTF-8: `E2 86 92`). German umlaut input is
normalized server-side (ä→ae etc.) before parsing, so `"März"` and `"ä"` in
questions are handled correctly.

---

### POST /api/assistant/score *(debug only)*

Returns the raw score each domain handler assigned to a question. Use when a
question routes to the wrong domain.

**Request:** `{ "question": "wic coverage this week" }`

**Response:**
```json
[
  { "domain": "wic-forecast", "label": "WIC coverage forecast", "score": 95 },
  { "domain": "wic-leave",    "label": "WIC annual leave",       "score": 90 },
  { "domain": "vacations",    "label": "all-employee vacation",  "score": 25 },
  ...
]
```

**Diagnosis workflow:**
1. POST the misbehaving question to `/api/assistant/score`.
2. Winner is always `scores[0]` (highest score). A tie (two equal top scores)
   returns a clarifying question instead of answering.
3. Fix by editing the losing handler's `Score()` method so the expected winner
   is ≥1 point higher than its nearest competitor.

Scores are also logged automatically at INFO level:
```
AssistantRouter [wic coverage this week] scores: wic-forecast:95 | wic-leave:90 | ...
```

**Domain keys:** `wic-leave`, `sick-leave`, `vacations`, `al-balance`,
`dashboard`, `wic-forecast`, `wic-coverage`, `pipeline`, `training`, `employees`

---

### POST /api/wic-assistant/ask *(legacy)*

Original WIC-only endpoint. Routes directly to `WicAssistantService`, bypassing
the scoring router. Response JSON does not include `hint`. Kept for backward
compatibility — new code should use `/api/assistant/ask`.
