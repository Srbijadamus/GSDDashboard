using GSDDashboard.API.Data.Models;

namespace GSDDashboard.API.Services;

/// <summary>
/// Single source of truth for selecting one row when the same employee+date has
/// multiple ShiftEntry or WicShiftEntry rows from different import sources.
///
/// WHY TWO RULES?
/// WicShiftEntry: multiple import modules (WIC location planner, GSD shift planner,
/// WIC on-site recorder) each write their own row for the same employee+date — all
/// legitimate, recording different aspects of the day. Semantic fields (IsOnSite,
/// SupportLocation) indicate which source is most authoritative for display, so
/// priority ordering is field-based.
///
/// ShiftEntry: duplicates arise when an AL_IMPORT correction arrives after the original
/// EXCEL import. ShiftEntry has no semantic priority fields between sources — the only
/// reliable signal is recency. Id is auto-increment, so higher Id = later import =
/// most current correction. Concrete example: employee 9114618 on 2026-07-31 has
/// Id=28143 (WORKING, EXCEL2) and Id=31260 (AL, AL_IMPORT). MaxBy picks Id=31260 → AL.
/// </summary>
public static class ShiftDuplicateResolver
{
    // WicShiftEntries: on-site WIC assignment (IsOnSite + SupportLocation) wins —
    // most display-relevant. Then explicit shift time, then off-day marker, then
    // highest Id as a final tie-break.
    public static WicShiftEntry BestWicEntry(IEnumerable<WicShiftEntry> entries) =>
        entries
            .OrderByDescending(e => e.IsOnSite && e.SupportLocation != null ? 3 :
                                    e.WorkingShift != null                   ? 2 :
                                    e.IsOffDay                               ? 1 : 0)
            .ThenByDescending(e => e.Id)
            .First();

    // ShiftEntries: highest Id = latest import = most current correction.
    public static ShiftEntry BestShiftEntry(IEnumerable<ShiftEntry> entries) =>
        entries.MaxBy(s => s.Id)!;
}
