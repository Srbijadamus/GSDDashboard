using GSDDashboard.API.Data;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Shifts;

public record ReorderDto(List<string> OrderedEmployeeIds);

public static class ShiftReorderEndpointMapper
{
    public static void MapShiftReorderEndpoints(this WebApplication app)
    {
        app.MapPatch("/api/shiftplan/reorder", async (ReorderDto dto, GSDContext db) =>
        {
            // Store order in a simple way — update a display order field if it exists
            // For now just return success (order is managed frontend-side)
            return Results.Ok(new { success = true, count = dto.OrderedEmployeeIds.Count });
        }).WithTags("Shifts");
    }
}
