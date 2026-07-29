# GSDDashboard — AI Agent Improvement & Stabilization Prompt

> **IMPORTANT — Read before starting any work:**
> See **[CLAUDE.md](CLAUDE.md)** for verification rules (never claim success without proof).
> See **[DEPLOYMENT_AND_VERIFICATION.md](DEPLOYMENT_AND_VERIFICATION.md)** for the
> definition of done for frontend and backend changes.
> **Frontend changes must be built into `Backend/wwwroot` and verified on the
> tunnel (port 5000), never only on localhost:5173.**

> **Purpose:** Hand this document to an AI coding agent (or a developer) to systematically
> improve the stability, correctness, and maintainability of GSDDashboard.
> Every issue below was found by static analysis of the actual source tree — all
> file paths and line numbers are real.

---

## Project Overview

| Layer | Stack |
|---|---|
| Backend | ASP.NET Core (.NET 8), EF Core, SQL Server, Minimal API |
| Frontend | React 18 + TypeScript, Vite, i18next |
| Auth | None (internal network only) |
| Deployment | Windows Server, devtunnel, IIS / direct `dotnet run` |

**Entry points:**
- `Backend/Program.cs` — DI registration, middleware, route mapping
- `Frontend/src/App.tsx` — router, layout
- `Frontend/src/api/client.ts` — centralized HTTP client (partially used)

---

## Phase 1 — Error Handling (Priority: Critical)

### 1.1 Backend: unguarded `DateOnly.Parse` crashes

**Files:** `Backend/SickLeaveService.cs` lines 150-151, 181-182

`DateOnly.Parse(req.StartDate)` is called unconditionally on user-supplied strings with no null-check or `TryParse` guard. Any malformed date returns an unhandled 500.

**Fix:**
```csharp
if (!DateOnly.TryParse(req.StartDate, out var startDate))
    return Results.BadRequest("Invalid StartDate format. Expected yyyy-MM-dd.");
```
Apply the same pattern to `EndDate` in both `CreateAsync` and `PatchAsync`.

---

### 1.2 Backend: SubstituteAccept has no try/catch around SaveChangesAsync

**File:** `Backend/Modules/SubstitutionModule.cs` line 121

A DB constraint violation (e.g. duplicate key) produces an unhandled 500 with no meaningful error body.

**Fix:** Wrap the entire accept handler body in:
```csharp
try { ... }
catch (DbUpdateException ex)
{
    logger.LogError(ex, "SubstituteAccept failed");
    return Results.Conflict("Accept failed — a concurrent change may have occurred. Please refresh and retry.");
}
```

---

### 1.3 Backend: SubstitutionService silently swallows exceptions

**File:** `Backend/Services/SubstitutionService.cs` lines 154-156

The `catch` block loads `SubstitutionHistory` with an empty dict fallback. Nothing is logged. A timeout or connection error is invisible.

**Fix:**
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to load SubstitutionHistory; returning empty");
    // then return empty dict as before
}
```

---

### 1.4 Frontend: empty catch blocks hide failures from users

**Files:**
- `Frontend/src/pages/SickLeave.tsx` line 35 — `CommentCell.save()` has `catch { }` (empty)
- `Frontend/src/pages/Overview.tsx` line 252 — `handleAccept` has no `catch` at all

**Fix:** Both should display an error toast on failure:
```ts
catch (err) {
    console.error(err);
    toast.error("Failed to save — please try again.");
}
```

---

### 1.5 Backend: add centralized exception middleware

**File:** `Backend/Program.cs`

No global exception handler exists. Unhandled exceptions leak stack traces in 500 responses.

**Fix:** Add before route registration:
```csharp
app.UseExceptionHandler(errApp => errApp.Run(async ctx => {
    ctx.Response.StatusCode = 500;
    ctx.Response.ContentType = "application/json";
    var feature = ctx.Features.Get<IExceptionHandlerFeature>();
    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogError(feature?.Error, "Unhandled exception");
    await ctx.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." });
}));
```

---

## Phase 2 — Input Validation (Priority: High)

### 2.1 Backend: SickLeaveRequest fields are unvalidated strings

**File:** `Backend/SickLeaveService.cs` lines 147-152

`CreateSickLeaveRequest.StartDate` / `EndDate` are `string?` with no `[Required]`. A null date passes silently into `DateOnly.Parse`.

**Fix:** Add data annotations to the request record:
```csharp
public record CreateSickLeaveRequest(
    [Required] string EmployeeId,
    [Required][RegularExpression(@"\d{4}-\d{2}-\d{2}")] string StartDate,
    [Required][RegularExpression(@"\d{4}-\d{2}-\d{2}")] string EndDate,
    string? Note
);
```
Register `app.UseRouting()` + call `Results.ValidationProblem(ctx.Request.GetValidationErrors())` or use a validation filter.

---

### 2.2 Backend: ShiftType is not validated against an allowed set

**File:** `Backend/ShiftService.cs` lines 138, 179

Any string can be persisted as `ShiftType`. Add an enum or a whitelist constant and reject unknown values with `400`.

---

### 2.3 Backend: EmployeeService.CreateAsync accepts empty EmployeeId

**File:** `Backend/EmployeeService.cs` lines 53-74

`CreateEmployeeDto.EmployeeId` has no `[Required]` or non-empty guard. An empty string passes the duplicate check and creates a broken row.

**Fix:**
```csharp
if (string.IsNullOrWhiteSpace(dto.EmployeeId))
    return Results.BadRequest("EmployeeId is required.");
```

---

## Phase 3 — Centralize Frontend API Calls (Priority: High)

### 3.1 Migrate bare `fetch()` calls to `apiFetch` / `api.*`

The following files bypass `Frontend/src/api/client.ts` and use raw `fetch()`, which skips the centralized error-throwing guard:

| File | Lines | Issue |
|---|---|---|
| `WicAttendance.tsx` | 225, 236, 247, 342 | raw `fetch('/api/wic/...')` |
| `WicAttendance.tsx` | 259 | hardcoded devtunnel URL (see §5.1) |
| `SickLeave.tsx` | 29, 207, 423 | raw `fetch(BASE + '/api/...')` |
| `Vacations.tsx` | 30 | raw `fetch('/api/employees/...')` |

**Fix:** Replace each with the equivalent `apiFetch(...)` call from `client.ts`. The `apiFetch` wrapper already throws on non-OK responses, which means the `catch` blocks in §1.4 will fire correctly.

Note: the raw `fetch` calls inside `client.ts` itself at lines 63, 98-103, 110 return `Response` objects directly (intentional). Callers of those methods must check `.ok` — verify each call site does so.

---

## Phase 4 — Race Conditions & Data Integrity (Priority: High)

### 4.1 SubstituteAccept: wrap in a transaction

**File:** `Backend/Modules/SubstitutionModule.cs` lines 54-121

The entire accept flow (read → upsert two rows → insert history → SaveChanges) has no `BeginTransactionAsync`. Two concurrent accepts for the same slot can both pass the null-checks and produce duplicate rows or a constraint error.

**Fix:**
```csharp
await using var tx = await db.Database.BeginTransactionAsync();
try {
    // ... existing logic ...
    await db.SaveChangesAsync();
    await tx.CommitAsync();
} catch {
    await tx.RollbackAsync();
    throw;
}
```

---

### 4.2 VacationService.CreateAsync: non-atomic AL balance update

**File:** `Backend/VacationService.cs` lines 127-139

`SaveChangesAsync` is called twice (line 125, line 138). Two concurrent vacation creates for the same employee can read the same `PlannedTakenAL` and produce an incorrect final balance.

**Fix:** Merge into a single `SaveChangesAsync` call inside a transaction, or use a `ROWVERSION` / optimistic concurrency token on `ALBalance`.

---

### 4.3 EmployeeService.CreateAsync and ShiftService.AssignShiftAsync: check-then-act without transaction

**Files:**
- `Backend/EmployeeService.cs` lines 53-74
- `Backend/ShiftService.cs` lines 171-183

Both read for existence then insert without a transaction. Two concurrent requests can both read `null` and both try to insert.

**Fix:** Add a unique DB constraint on `EmployeeId` (migration) and catch `DbUpdateException` at the service level, returning `409 Conflict` instead of a 500.

---

## Phase 5 — Performance (Priority: Medium)

### 5.1 No pagination on any list endpoint

**Files:** `ShiftService.cs:57-78`, `VacationService.cs:35-47`, `SickLeaveService.cs:67-78`, `EmployeeService.cs:35-47`

All list endpoints return every matching row. At scale (100+ employees × 30+ days) this is thousands of rows per request.

**Fix:** Add `?page=1&pageSize=200` parameters with `.Skip((page-1)*pageSize).Take(pageSize)`. Frontend doesn't need immediate pagination UI — just cap at a safe default (e.g. 500 rows) to prevent memory blowouts.

---

### 5.2 Full-table loads into memory

**Files:**
- `Backend/WicScheduleService.cs` line 47 — loads ALL active assignments, then filters in memory
- `Backend/Services/SubstitutionService.cs` lines 114-115 — loads entire `WicOpeningHours` table on every substitution call with no date/location filter

**Fix:** Push the filter to SQL:
```csharp
// WicScheduleService.cs
await _db.WicAgentAssignments
    .Where(a => a.IsActive && wicEmployeeIds.Contains(a.EmployeeId))
    .ToListAsync();

// SubstitutionService.cs
await _db.WicOpeningHours
    .Where(h => h.LocationId == locationId && h.Date >= startDate)
    .ToListAsync();
```

---

## Phase 6 — Configuration & Security (Priority: Medium)

### 6.1 Hardcoded devtunnel URL

**File:** `Frontend/src/pages/WicAttendance.tsx` line 259

```ts
fetch("https://ssr7tm2l-8000.euw.devtunnels.ms/api/attendance")
```

The devtunnel subdomain changes. This URL is now committed in source control and will silently break when the tunnel is recreated.

**Fix:** Move to `Frontend/.env`:
```
VITE_KIOSK_API_URL=https://ssr7tm2l-8000.euw.devtunnels.ms
```
Then in code:
```ts
fetch(`${import.meta.env.VITE_KIOSK_API_URL}/api/attendance`)
```
Add `.env.local` to `.gitignore` and document the variable in a `.env.example`.

---

### 6.2 Overly permissive CORS policy

**File:** `Backend/Program.cs` lines 71-73

`AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` is appropriate only for local dev. If the API is ever reachable from the internet, this allows cross-site data exfiltration.

**Fix:** Lock to known origins via config:
```csharp
policy.WithOrigins(builder.Configuration["AllowedOrigins"]!.Split(','))
      .AllowAnyMethod()
      .AllowAnyHeader();
```
Set `AllowedOrigins` in `appsettings.json` / environment variables.

---

## Phase 7 — Dead Code Cleanup (Priority: Low)

| Item | Location | Action |
|---|---|---|
| `WicShifts_old.tsx` | `Frontend/src/pages/` | Delete the file |
| Commented-out `READONLY_TYPES` | `Shifts.tsx` line 11 | Remove the comment |
| Non-functional context menu items | `Shifts.tsx` lines 667-669 | Implement or remove ("Remove from plan", "Delete agent", "Edit agent") |
| `const BASE = ""` | `SickLeave.tsx` line 8 | Remove; use `apiFetch` directly |
| `CoverageCalculatorTests.cs` | `Backend/` | Move to a separate `Backend.Tests` project or delete |

---

## Phase 8 — Structured Logging (Priority: Medium)

**File:** `Backend/Program.cs`

Add Serilog (already likely available via NuGet) with a file sink for production diagnostics:

```csharp
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console()
       .WriteTo.File("logs/gsddashboard-.log", rollingInterval: RollingInterval.Day));
```

Replace any `Console.WriteLine` / bare `_logger.LogInformation` with structured events:
```csharp
_logger.LogInformation("SubstituteAccept {ShiftId} accepted by {EmployeeId}", shiftId, empId);
```

---

## Definition of Done

- [ ] All `DateOnly.Parse` calls replaced with `TryParse` + `400` response
- [ ] SubstituteAccept wrapped in a DB transaction
- [ ] VacationService AL balance update is atomic
- [ ] All bare `fetch()` calls in WicAttendance and SickLeave migrated to `apiFetch`
- [ ] Hardcoded devtunnel URL extracted to `VITE_KIOSK_API_URL` env variable
- [ ] Global exception handler added to `Program.cs`
- [ ] `CoverageCalculatorTests.cs` removed from backend project
- [ ] `WicShifts_old.tsx` deleted
- [ ] `WicOpeningHours` query in SubstitutionService filters by location/date before `.ToListAsync()`

---

## Out of Scope for This Pass

- Authentication / authorization (the app is internal-only)
- Automated test suite (no test infrastructure exists; add as a separate initiative)
- Database migrations for new constraints (coordinate with ops before running against prod)
- Replacing the devtunnel setup with a proper reverse proxy

---

## Agent Instructions

Work phase by phase. Within a phase, fix the highest-severity item first (marked **Critical** > **High** > **Medium** > **Low**).

After each file change, verify the project still builds:
```
dotnet build Backend/Backend.csproj
cd Frontend && npm run build
```

Do not introduce new abstractions beyond what is described above. Do not refactor working code that is not listed here. Keep diffs minimal and targeted.
