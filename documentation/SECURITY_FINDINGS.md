# Security Findings - EON GSD Dashboard

Date: 2026-07-06  
Scope: C:\GSDDashboard (ASP.NET Core 8 + React 19, internal network only)

---

## Finding 1 — Unauthenticated Write Endpoints (HIGH)

**File:** `Backend/EmployeeService.cs` (POST/PATCH/DELETE `/api/employees`),  
and all other POST/PATCH/DELETE endpoints across the API.

**Description:**  
All write endpoints (create/update/delete for employees, shifts, sick leave, vacations, breaks, training, pipeline, WIC assignments) are accessible without any authentication or authorization. There is no middleware, policy, or attribute gate in front of any endpoint.

**Evidence:**  
`Program.cs` line 67-71: CORS allows `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`.  
No `[Authorize]`, no `RequireAuthorization()`, no JWT/cookie middleware registered.

**Risk:**  
Any user on the same network (or beyond, depending on firewall) can create, modify, or soft-delete employee records, shift entries, and WIC assignments without credentials.

**Recommendation:**  
Add Windows Authentication or at minimum a shared API key header check. For read-only viewers, restrict write endpoints via policy.

---

## Finding 2 — Swagger UI Exposed (MEDIUM)

**File:** `Backend/Program.cs` lines 75-80

**Description:**  
Swagger UI is served at `/swagger` unconditionally — no environment check, no auth gate.

**Evidence:**
```csharp
app.UseSwagger();
app.UseSwaggerUI(c => { ... c.RoutePrefix = "swagger"; });
```
No `if (app.Environment.IsDevelopment())` guard.

**Risk:**  
Full API schema (all routes, request/response shapes) is visible to anyone who can reach the server. Lowers the effort needed to probe write endpoints.

**Recommendation:**  
Wrap in `if (app.Environment.IsDevelopment())` or add authentication to the Swagger route.

---

## Finding 3 — Soft-Delete Contract (LOW / Informational)

**File:** `Backend/EmployeeService.cs` line 96

**Description:**  
`DeleteAsync` sets `IsActive = false` (soft delete) — it does NOT remove the row. All service-layer queries now correctly filter on `IsActive = true`. However, the PATCH endpoint (`UpdateAsync`, line 85) also accepts `IsActive` as a body field, meaning a caller can reactivate a previously soft-deleted employee by PATCH with `{ "isActive": true }`.

**Evidence:**  
`UpdateEmployeeDto` includes `bool? IsActive`. `UpdateAsync` applies it unconditionally.

**Risk:**  
Low. Unintended reactivation of a soft-deleted employee without going through any approval flow.

**Recommendation:**  
If reactivation requires a deliberate action, consider removing `IsActive` from `UpdateEmployeeDto` and exposing a separate `/reactivate` endpoint with explicit intent.

---

## Finding 4 — Missing FK: ShiftEntries → Employees (LOW / Informational)

**File:** `Backend/schema.sql` line 48-74

**Description:**  
`ShiftEntries.EmployeeId` (NVARCHAR) has no FOREIGN KEY constraint to `Employees.EmployeeId`. Same for `WicShiftEntries.EmployeeId`.

**Evidence:**  
`schema.sql` contains no `REFERENCES Employees(EmployeeId)` clause on either table.

**Risk:**  
Orphan rows are possible (employee deleted or ID typo'd without a cascade/reject at DB level). The application performs soft deletes and the EF layer should prevent most cases, but direct SQL inserts bypass this.

**Recommendation:**  
Add `FOREIGN KEY (EmployeeId) REFERENCES Employees(EmployeeId)` constraints if the DB is accessed only through the application. If bulk SQL imports are used, enforce referential integrity at the application layer before import.

---

## Finding 5 — CORS Open Policy (MEDIUM)

**File:** `Backend/Program.cs` lines 67-71

**Description:**  
CORS is configured with `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`. This allows cross-origin requests from any domain.

**Evidence:**
```csharp
policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
```

**Risk:**  
While this is an internal tool, the open CORS policy means any web page a user visits can make authenticated cross-origin requests to this API using the user's browser session (if cookies are ever added). For an intranet tool with no auth, the immediate risk is low but the policy is overly permissive.

**Recommendation:**  
Restrict CORS to known origins (the dashboard's own domain/localhost).
