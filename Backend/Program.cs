using GSDDashboard.API.Data;
using GSDDashboard.API.Modules.ALCalendar;
using GSDDashboard.API.Modules.Overview;
using GSDDashboard.API.Modules.Overview;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.Dashboard;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Modules.SickLeave;
using GSDDashboard.API.Modules.Vacations;
using GSDDashboard.API.Modules.PublicHolidays;
using GSDDashboard.API.Modules.Pipeline;
using GSDDashboard.API.Modules.WicSchedule;
using GSDDashboard.API.Modules.Training;
using GSDDashboard.API.Services;
using GSDDashboard.API.Services;
using GSDDashboard.API.Modules.Employees;
using GSDDashboard.API.Modules.ALBalance;
using GSDDashboard.API.Modules.Attendance;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GSDContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(3)
    ));

builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ShiftService>();
builder.Services.AddScoped<WicShiftService>();
builder.Services.AddScoped<WicCardsService>();
builder.Services.AddScoped<SickLeaveService>();
builder.Services.AddScoped<VacationService>();
builder.Services.AddScoped<ShiftSyncService>();
builder.Services.AddScoped<PublicHolidayService>();
builder.Services.AddScoped<ShiftValidationService>();
builder.Services.AddScoped<PipelineService>();
builder.Services.AddScoped<WicScheduleService>();
builder.Services.AddScoped<TrainingService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<ALBalanceService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<GSDDashboard.API.Modules.ALCalendar.ALCalendarService>();
builder.Services.AddScoped<GSDDashboard.API.Modules.Overview.OverviewService>();
builder.Services.AddScoped<GSDDashboard.API.Modules.Overview.OverviewService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "EON GSD Dashboard API", Version = "v1" });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "GSD Dashboard API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapDashboardEndpoints();
app.MapShiftEndpoints();
app.MapWicEndpoints();
app.MapWicCardsEndpoints();
app.MapAttendanceEndpoints();
app.MapSickLeaveEndpoints();
app.MapVacationEndpoints();
app.MapPublicHolidayEndpoints();
app.MapPipelineEndpoints();
app.MapWicScheduleEndpoints();
app.MapTrainingEndpoints();
app.MapALBalanceEndpoints();
app.MapEmployeeEndpoints();
app.MapShiftReorderEndpoints();
app.MapALCalendarEndpoints();
app.MapOverviewEndpoints();
app.MapOverviewEndpoints();


app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));
app.MapFallbackToFile("index.html");
app.Run();












