using GSDDashboard.API.Data.Models;
using System.Globalization;

namespace GSDDashboard.API.Services;

/// <summary>
/// Single source of truth for location matching — shared by all services.
/// Replacing the per-file _aliases/_locAliases/LocationAliases copies.
/// </summary>
public static class WicLocationMatcher
{
    // WicShiftEntry.SupportLocation to old-style assignment code.
    // Checked against both LocationCode (new tilde format) and LocationCodeLegacy (old format).
    private static readonly Dictionary<string, string> _aliases =
        new(StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace))
    {
        { "Essen BP1",             "DE_Essen_BP1"       },
        { "Essen TK1",             "DE_Essen_TK1"       },
        { "Halle",                 "DE_Halle"           },
        { "Berlin - Gaussstr",     "DE_Berlin_Gauss"    },
        { "Furstenwalde",          "DE_Furstenwalde"    },
        { "Munchen",               "DE_Munchen"         },
        { "Osnabruck",             "DE_Osnabruck"       },
        { "Saarbrucken",           "DE_Saarbrucken"     },
        { "Demmin - Am Hanseufer", "DE_Demmin_Hanse"    },
        { "Denbosch",              "NL_Denbosch"        },
        { "Augsburg",              "DE_Augsburg"        },
        { "Bamberg",               "DE_Bamberg"         },
        { "Brokdorf",              "DE_Brokdorf"        },
        { "Dortmund",              "DE_Dortmund"        },
        { "Emmerthal",             "DE_Emmerthal"       },
        { "Essenbach",             "DE_Essenbach"       },
        { "Grafenrheinfeld",       "DE_Grafenrheinfeld" },
        { "Hamburg",               "DE_Hamburg"         },
        { "Hannover",              "DE_Hannover"        },
        { "Helmstedt",             "DE_Helmstedt"       },
        { "Neu-Isenburg",          "DE_NeuIsenburg"     },
        { "Pfaffenhofen",          "PFAFFENHOFEN"       },
        { "Potsdam",               "DE_Potsdam"         },
        { "Quickborn",             "DE_Quickborn"       },
        { "Regensburg",            "DE_Regensburg"      },
        { "Rendsburg",             "RENDSBURG"          },
        { "Salzgitter",            "DE_Salzgitter"      },
        { "Stade",                 "DE_Stade"           },
        { "Stadland",              "DE_Stadland"        },
        { "Zwolle",                "NL_Zwolle"          },
        // ── Bucket B: legacy / variant SupportLocation forms ────────────────────
        // Two ASCII keys per umlaut location: ue/oe-romanized (matches those exact DB values)
        // and u/o-stripped (IgnoreNonSpace comparer maps ü→u and ö→o at compare time,
        // so the actual-umlaut DB forms resolve without any umlaut literals in source).
        { "Essen (Bruesseler Pl.)",  "DE_Essen_BP1"       }, // ue form in DB
        { "Essen (Brusseler Pl.)",   "DE_Essen_BP1"       }, // u-stripped; resolves ü form via IgnoreNonSpace
        { "Essen (ThyssenKrupp)",    "DE_Essen_TK1"       },
        { "Demmin (Am Hanseufer)",   "DE_Demmin_Hanse"    },
        { "Berlin (Koepenicker)",    "DE_Berlin_Kopenick" }, // oe form in DB
        { "Berlin (Kopenicker)",     "DE_Berlin_Kopenick" }, // o-stripped; resolves ö form via IgnoreNonSpace
        { "Berlin - Kopenicker",     "DE_Berlin_Kopenick" }, // o-stripped dash; resolves ö dash form
        // Underscore forms (PS1 scripts):
        { "Essen_BP1",               "DE_Essen_BP1"       },
        { "Essen_TK1",               "DE_Essen_TK1"       },
        { "Demmin_Wold",             "DE_Demmin_Wold"     },
        { "Demmin_Hanse",            "DE_Demmin_Hanse"    },
    };

    /// <summary>
    /// Matches WicShiftEntry.SupportLocation against a WicLocation.
    /// Checks DisplayName, City, and the alias map (against both LocationCode and LocationCodeLegacy).
    /// </summary>
    public static bool MatchesSupportLocation(string? sl, WicLocation loc)
    {
        if (sl == null) return false;
        if (sl == loc.DisplayName) return true;
        if (loc.City != null && sl == loc.City) return true;
        if (_aliases.TryGetValue(sl, out var code))
        {
            if (string.Equals(code, loc.LocationCode, StringComparison.OrdinalIgnoreCase)) return true;
            if (loc.LocationCodeLegacy != null &&
                string.Equals(code, loc.LocationCodeLegacy, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Matches WicAgentAssignment.LocationCode against a WicLocation.
    /// Checks both new tilde-format LocationCode and the legacy bridge code.
    /// </summary>
    public static bool MatchesAssignmentCode(string assignmentCode, WicLocation loc) =>
        string.Equals(assignmentCode, loc.LocationCode, StringComparison.OrdinalIgnoreCase) ||
        (loc.LocationCodeLegacy != null &&
         string.Equals(assignmentCode, loc.LocationCodeLegacy, StringComparison.OrdinalIgnoreCase));
}
