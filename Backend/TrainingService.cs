using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
namespace GSDDashboard.API.Modules.Training;

public record TrainingTopicDto(int Id, string Name, int DurationHours, int MinGroupSize, int MaxGroupSize, bool IsMandatory, string? Notes);
public record TrainingSessionDto(int Id, int TopicId, string TopicName, string ScheduledDate, string StartTime, string EndTime, List<string> AgentIds, string? SuggestedBy, string? ConfirmedBy, string Status, string? Notes);
public record SuggestRequest(int TopicId, string DateFrom, string DateTo, List<string>? SelectedAgentIds = null, int MaxResults = 12);
public record AttendeeDto(string EmployeeId, string FullName, string ShiftStart, string ShiftEnd);
public record SlotResult(string Date, string DayName, string StartTime, string EndTime, int SelectedAvailable, int TotalSelected, int CoveragePct, int AvailableCount, int TotalOnDuty, int ImpactPct, double Score, List<AttendeeDto> SelectedAttendees, List<string> MissingSelected);
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

    public async Task<object> SuggestSlotsAsync(SuggestRequest req)
    {
        var topic = await _db.TrainingTopics.FindAsync(req.TopicId);
        if (topic == null) return new { warning = "Topic not found.", slots = new List<object>() };
        if (!DateOnly.TryParse(req.DateFrom, out var fromDate)) fromDate = DateOnly.FromDateTime(DateTime.Today);
        if (!DateOnly.TryParse(req.DateTo,   out var toDate))   toDate   = fromDate.AddDays(14);
        if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);

        int durationMins = topic.DurationHours * 60;
        if (durationMins <= 0) durationMins = 60;
        int minGroup = topic.MinGroupSize > 0 ? topic.MinGroupSize : 1;

        var workingTypes = new[] { "WORKING", "WIC", "WIC_DUTY", "TRAINING" };
        var offTypes     = new[] { "AL", "SL", "OFF", "OFF_WEEKEND", "PH", "LPH", "CD", "UL", "OL" };

        var allEmployees = await _db.Employees.Where(e => e.IsActive).ToListAsync();
        var empById = allEmployees.ToDictionary(e => e.EmployeeId);
        var allIds = allEmployees.Select(e => e.EmployeeId).ToList();

        var shifts = await _db.ShiftEntries
            .Where(s => allIds.Contains(s.EmployeeId)
                     && s.ShiftDate >= fromDate && s.ShiftDate <= toDate
                     && workingTypes.Contains(s.ShiftType))
            .ToListAsync();

        if (shifts.Count == 0)
            return new { warning = $"No working-shift data found for {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}. Upload the shift plan for this period first.", slots = new List<object>() };

        var offSet = (await _db.ShiftEntries
            .Where(s => allIds.Contains(s.EmployeeId)
                     && s.ShiftDate >= fromDate && s.ShiftDate <= toDate
                     && offTypes.Contains(s.ShiftType))
            .Select(s => new { s.EmployeeId, s.ShiftDate })
            .ToListAsync())
            .Select(x => (x.EmployeeId, x.ShiftDate)).ToHashSet();

        var byDate = shifts.GroupBy(s => s.ShiftDate).ToDictionary(g => g.Key, g => g.ToList());

        // Full shift map (all types) for diagnosing WHY a selected agent can't attend.
        var allShiftMap = (await _db.ShiftEntries
            .Where(s => allIds.Contains(s.EmployeeId) && s.ShiftDate >= fromDate && s.ShiftDate <= toDate)
            .ToListAsync())
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g));

        var selected = (req.SelectedAgentIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
        int totalSelected = selected.Count;

        var slots = new List<SlotResult>();
        var windowStart = new TimeSpan(7, 0, 0);
        var windowEndLimit = new TimeSpan(18, 0, 0);
        var step = TimeSpan.FromMinutes(30);

        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
            if (!byDate.TryGetValue(d, out var dayShifts)) continue;
            int totalOnDuty = dayShifts.Count;
            if (totalOnDuty < minGroup) continue;

            for (var ts = windowStart; ts + TimeSpan.FromMinutes(durationMins) <= windowEndLimit; ts += step)
            {
                var trainStart = ts;
                var trainEnd = ts + TimeSpan.FromMinutes(durationMins);
                var available = new List<AgentAvailability>();

                foreach (var s in dayShifts)
                {
                    if (!empById.TryGetValue(s.EmployeeId, out var e)) continue;
                    if (s.ShiftType == "RESIGNED") continue;
                    if (!TimeSpan.TryParse(s.ShiftStart, out var sStart) || !TimeSpan.TryParse(s.ShiftEnd, out var sEnd)) continue;
                    if (sEnd <= sStart) continue; // skip night/cross-midnight
                    if (!(sStart <= trainStart && sEnd >= trainEnd)) continue;
                    if (offSet.Contains((e.EmployeeId, d))) continue;
                    bool isStudent = string.Equals(e.Engagement, "Student", StringComparison.OrdinalIgnoreCase);
                    available.Add(new AgentAvailability(e.EmployeeId, e.FullName ?? e.EmployeeId, e.Engagement ?? "", s.ShiftStart!, s.ShiftEnd!, isStudent));
                }

                int selectedAvailable = totalSelected == 0 ? available.Count : available.Count(a => selected.Contains(a.EmployeeId));
                if (totalSelected > 0 && selectedAvailable == 0) continue;
                int effectiveCount = totalSelected > 0 ? selectedAvailable : available.Count;
                if (effectiveCount < minGroup) continue;

                double coverage = totalSelected == 0 ? 1.0 : (double)selectedAvailable / totalSelected;
                // Impact = how much of the day's floor is pulled by taking the TRAINING attendees out.
                // We only pull the selected attendees (or, if none selected, everyone available).
                int pulled = totalSelected > 0 ? selectedAvailable : available.Count;
                double impactPct = totalOnDuty > 0 ? (double)pulled / totalOnDuty : 0.0;
                // Score: coverage dominates; low impact strongly rewarded; small tie-breaker for raw attendees.
                double score = (coverage * 100.0) - (impactPct * 40.0) + (selectedAvailable * 0.5);

                var attendees = available.Where(a => totalSelected == 0 || selected.Contains(a.EmployeeId))
                    .Select(a => new AttendeeDto(a.EmployeeId, a.FullName, a.ShiftStart, a.ShiftEnd)).ToList();
                var missing = selected.Where(id => !available.Any(a => a.EmployeeId == id))
                    .Select(id => {
                        string nm = empById.TryGetValue(id, out var e) ? (e.FullName ?? id) : id;
                        string reason = "no shift";
                        if (allShiftMap.TryGetValue((id, d), out var se))
                        {
                            var st = se.ShiftType ?? "";
                            if (st == "AL" || st == "SL" || st == "OFF" || st == "OFF_WEEKEND" || st == "PH" || st == "RESIGNED")
                                reason = st == "OFF_WEEKEND" ? "OFF" : st;
                            else if (!TimeSpan.TryParse(se.ShiftStart, out var ss) || !TimeSpan.TryParse(se.ShiftEnd, out var ee))
                                reason = "no times";
                            else if (ee <= ss)
                                reason = "NIGHT";
                            else
                                reason = $"{se.ShiftStart}-{se.ShiftEnd}";
                        }
                        return $"{nm} ({reason})";
                    }).ToList();

                slots.Add(new SlotResult(
                    d.ToString("yyyy-MM-dd"), d.DayOfWeek.ToString(),
                    $"{trainStart:hh\\:mm}", $"{trainEnd:hh\\:mm}",
                    selectedAvailable, totalSelected, (int)Math.Round(coverage * 100),
                    available.Count, totalOnDuty, (int)Math.Round(impactPct * 100),
                    Math.Round(score, 2), attendees, missing));
            }
        }

        var ordered = slots.OrderByDescending(s => s.Score).Take(req.MaxResults > 0 ? req.MaxResults : 12).ToList();
        return new { warning = (string?)null, slots = ordered };
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
