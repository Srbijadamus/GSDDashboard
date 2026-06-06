using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;
namespace GSDDashboard.API.Modules.Training;

public record TrainingTopicDto(int Id, string Name, int DurationHours, int MinGroupSize, int MaxGroupSize, bool IsMandatory, string? Notes);
public record TrainingSessionDto(int Id, int TopicId, string TopicName, string ScheduledDate, string StartTime, string EndTime, List<string> AgentIds, string? SuggestedBy, string? ConfirmedBy, string Status, string? Notes);
public record SuggestRequest(int TopicId, string DateFrom, string DateTo, List<string> AgentIds);
public record TrainingSuggestion(string Date, string StartTime, string EndTime, List<AgentAvailability> AvailableAgents, List<string> Conflicts, int SurplusAgents);
public record AgentAvailability(string EmployeeId, string FullName, string Engagement, string ShiftStart, string ShiftEnd, bool IsStudent);
public record ConfirmRequest(int TopicId, string Date, string StartTime, string EndTime, List<string> AgentIds, string? Notes);

public class TrainingService
{
    private readonly GSDContext _db;
    public TrainingService(GSDContext db) => _db = db;

    public async Task<List<TrainingTopicDto>> GetTopicsAsync()
    {
        var topics = await _db.TrainingTopics.OrderBy(t => t.Name).ToListAsync();
        return topics.Select(t => new TrainingTopicDto(t.Id, t.Name, t.DurationHours, t.MinGroupSize, t.MaxGroupSize, t.IsMandatory, t.Notes)).ToList();
    }

    public async Task<TrainingTopicDto> CreateTopicAsync(TrainingTopicDto req)
    {
        var t = new TrainingTopic { Name = req.Name, DurationHours = req.DurationHours, MinGroupSize = req.MinGroupSize, MaxGroupSize = req.MaxGroupSize, IsMandatory = req.IsMandatory, Notes = req.Notes };
        _db.TrainingTopics.Add(t);
        await _db.SaveChangesAsync();
        return new TrainingTopicDto(t.Id, t.Name, t.DurationHours, t.MinGroupSize, t.MaxGroupSize, t.IsMandatory, t.Notes);
    }

    public async Task<List<TrainingSessionDto>> GetSessionsAsync(string? from, string? to)
    {
        var fromDate = from != null && DateOnly.TryParse(from, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today);
        var toDate   = to   != null && DateOnly.TryParse(to,   out var td) ? td : fromDate.AddDays(30);
        var sessions = await _db.TrainingSessions.Where(s => s.ScheduledDate >= fromDate && s.ScheduledDate <= toDate).OrderBy(s => s.ScheduledDate).ToListAsync();
        var topicIds = sessions.Select(s => s.TopicId).Distinct().ToList();
        var topics = await _db.TrainingTopics.Where(t => topicIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);
        return sessions.Select(s => new TrainingSessionDto(s.Id, s.TopicId, topics.ContainsKey(s.TopicId) ? topics[s.TopicId] : "Unknown", s.ScheduledDate.ToString("yyyy-MM-dd"), s.StartTime, s.EndTime, string.IsNullOrEmpty(s.AgentIds) ? new() : s.AgentIds.Split(',').ToList(), s.SuggestedBy, s.ConfirmedBy, s.Status, s.Notes)).ToList();
    }

    public async Task<List<TrainingSuggestion>> SuggestSlotsAsync(SuggestRequest req)
    {
        var topic = await _db.TrainingTopics.FindAsync(req.TopicId);
        if (topic == null) return new();
        if (!DateOnly.TryParse(req.DateFrom, out var fromDate)) fromDate = DateOnly.FromDateTime(DateTime.Today);
        if (!DateOnly.TryParse(req.DateTo,   out var toDate))   toDate   = fromDate.AddDays(14);

        var shifts = await _db.ShiftEntries
            .Where(s => req.AgentIds.Contains(s.EmployeeId) && s.ShiftDate >= fromDate && s.ShiftDate <= toDate && (s.ShiftType == "WORKING" || s.ShiftType == "WIC_DUTY"))
            .Join(_db.Employees, s => s.EmployeeId, e => e.EmployeeId, (s, e) => new { Shift = s, Employee = e })
            .ToListAsync();

        var offShifts = await _db.ShiftEntries
            .Where(s => req.AgentIds.Contains(s.EmployeeId) && s.ShiftDate >= fromDate && s.ShiftDate <= toDate && (s.ShiftType == "AL" || s.ShiftType == "SL" || s.ShiftType == "OFF" || s.ShiftType == "OFF_WEEKEND" || s.ShiftType == "PH"))
            .Select(s => new { s.EmployeeId, s.ShiftDate })
            .ToListAsync();

        var suggestions = new List<TrainingSuggestion>();

        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
            var dayShifts = shifts.Where(x => x.Shift.ShiftDate == d).ToList();
            if (dayShifts.Count < topic.MinGroupSize) continue;

            for (int startH = 7; startH <= 18 - topic.DurationHours; startH++)
            {
                var trainingStart = new TimeSpan(startH, 0, 0);
                var trainingEnd   = new TimeSpan(startH + topic.DurationHours, 0, 0);
                var available = new List<AgentAvailability>();
                var conflicts = new List<string>();

                foreach (var x in dayShifts)
                {
                    var s = x.Shift; var e = x.Employee;
                    if (s.ShiftStart == null || s.ShiftEnd == null) continue;
                    if (!TimeSpan.TryParse(s.ShiftStart, out var sStart) || !TimeSpan.TryParse(s.ShiftEnd, out var sEnd)) continue;
                    if (sEnd < sStart) sEnd = sEnd.Add(TimeSpan.FromHours(24));
                    bool coversTraining = sStart <= trainingStart && sEnd >= trainingEnd;
                    if (!coversTraining) { conflicts.Add($"{e.FullName}: shift {s.ShiftStart}-{s.ShiftEnd} doesn't cover training window"); continue; }
                    bool onLeave = offShifts.Any(o => o.EmployeeId == e.EmployeeId && o.ShiftDate == d);
                    if (onLeave) { conflicts.Add($"{e.FullName}: on leave"); continue; }
                    bool isStudent = e.Engagement == "Student";
                    if (isStudent) conflicts.Add($"{e.FullName}: student — verify weekly hours");
                    available.Add(new AgentAvailability(e.EmployeeId, e.FullName ?? e.EmployeeId, e.Engagement ?? "", s.ShiftStart, s.ShiftEnd, isStudent));
                }

                if (available.Count < topic.MinGroupSize) continue;
                suggestions.Add(new TrainingSuggestion(d.ToString("yyyy-MM-dd"), $"{startH:D2}:00", $"{startH + topic.DurationHours:D2}:00", available, conflicts, available.Count - topic.MinGroupSize));
                if (suggestions.Count >= 3) break;
            }
            if (suggestions.Count >= 3) break;
        }
        return suggestions;
    }

    public async Task<TrainingSessionDto> ConfirmAsync(ConfirmRequest req)
    {
        var topic = await _db.TrainingTopics.FindAsync(req.TopicId);
        if (!DateOnly.TryParse(req.Date, out var date)) date = DateOnly.FromDateTime(DateTime.Today);

        foreach (var agentId in req.AgentIds)
        {
            var existing = await _db.ShiftEntries.FirstOrDefaultAsync(s => s.EmployeeId == agentId && s.ShiftDate == date);
            if (existing != null) { existing.AgentTask = "TRAINING"; }
            else { _db.ShiftEntries.Add(new ShiftEntry { EmployeeId = agentId, ShiftDate = date, ShiftType = "TRAINING", ShiftStart = req.StartTime, ShiftEnd = req.EndTime, AgentTask = "TRAINING", AutoGenerated = true, SourceModule = "Training" }); }
        }

        var session = new TrainingSession { TopicId = req.TopicId, ScheduledDate = date, StartTime = req.StartTime, EndTime = req.EndTime, AgentIds = string.Join(",", req.AgentIds), Status = "CONFIRMED", Notes = req.Notes };
        _db.TrainingSessions.Add(session);
        await _db.SaveChangesAsync();
        return new TrainingSessionDto(session.Id, session.TopicId, topic?.Name ?? "Unknown", session.ScheduledDate.ToString("yyyy-MM-dd"), session.StartTime, session.EndTime, req.AgentIds, null, null, session.Status, session.Notes);
    }

    public async Task<bool> DeleteSessionAsync(int id)
    {
        var s = await _db.TrainingSessions.FindAsync(id);
        if (s == null) return false;
        _db.TrainingSessions.Remove(s);
        await _db.SaveChangesAsync();
        return true;
    }
}

public static class TrainingEndpointMapper
{
    public static void MapTrainingEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/training").WithTags("Training");
        grp.MapGet("/topics",   async (TrainingService svc) => Results.Ok(await svc.GetTopicsAsync()));
        grp.MapPost("/topics",  async (TrainingTopicDto req, TrainingService svc) => Results.Ok(await svc.CreateTopicAsync(req)));
        grp.MapGet("/sessions", async (string? from, string? to, TrainingService svc) => Results.Ok(await svc.GetSessionsAsync(from, to)));
        grp.MapPost("/suggest", async (SuggestRequest req, TrainingService svc) => Results.Ok(await svc.SuggestSlotsAsync(req)));
        grp.MapPost("/confirm", async (ConfirmRequest req, TrainingService svc) => Results.Ok(await svc.ConfirmAsync(req)));
        grp.MapDelete("/sessions/{id:int}", async (int id, TrainingService svc) =>
        {
            var ok = await svc.DeleteSessionAsync(id);
            return ok ? Results.Ok(new { deleted = true }) : Results.NotFound();
        });
    }
}
