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
        // ── Bucket C: post-decode alias forms ───────────────────────────────────
        // These keys fire after RepairDoubleEncoding() converts corrupt input to correct Unicode.
        // sharp-s (U+00DF) key: explicit alias needed because IgnoreNonSpace strips
        // combining diacritics only; it cannot equate sharp-s to the "ss" in
        // the existing "Berlin - Gaussstr" key.
        { "Berlin - Gaußstr.",     "DE_Berlin_Gauss"    },
        // "Berlin - Bruckenstrasse" (ASCII key): IgnoreNonSpace resolves the U+00FC
        // (u-umlaut) form that RepairDoubleEncoding produces from the corrupt DB value.
        { "Berlin - Bruckenstrasse",    "DE_Berlin_Kopenick" },
        // Underscore forms (PS1 scripts):
        { "Essen_BP1",               "DE_Essen_BP1"       },
        { "Essen_TK1",               "DE_Essen_TK1"       },
        { "Demmin_Wold",             "DE_Demmin_Wold"     },
        { "Demmin_Hanse",            "DE_Demmin_Hanse"    },
    };

    // Repairs Windows-1252 mis-decodes of UTF-8 two-byte sequences stored in the DB.
    // Each two-byte UTF-8 sequence 0xC3 0xNN was decoded as Windows-1252 characters
    // Atilde (Ã) + the W1252 glyph for byte 0xNN.
    //
    // Guard: Ã (Atilde, U+00C3) never appears in any legitimate German location
    // name, so clean values ("Munchen", "Saarbrucken", ASCII-only) return immediately
    // without any string allocation.  Values already correctly encoded ("Saarbrucken"
    // with real umlaut) also pass the guard unchanged because real umlauts (U+00FC etc.)
    // are not U+00C3.
    private static string RepairDoubleEncoding(string s)
    {
        if (!s.Contains('Ã')) return s;
        // Corrupt pair -> correct char.
        // Lowercase umlauts (covers all 8 affected WicShiftEntry values in Check 7):
        //   Ã¼  Atilde + 1/4        -> ü  u-umlaut  (0xC3 0xBC)
        //   Ã¶  Atilde + pilcrow     -> ö  o-umlaut  (0xC3 0xB6)
        //   Ã¤  Atilde + currency    -> ä  a-umlaut  (0xC3 0xA4)
        //   ÃŸ  Atilde + Y-diaeresis -> ß  sharp-s   (0xC3 0x9F; W1252 0x9F=U+0178)
        // Uppercase umlauts (completeness; W1252 extended range):
        //   Ã„  Atilde + low-9-quote -> Ä  A-umlaut  (0xC3 0x84; W1252 0x84=U+201E)
        //   Ã–  Atilde + en-dash     -> Ö  O-umlaut  (0xC3 0x96; W1252 0x96=U+2013)
        //   Ãœ  Atilde + oe-ligature -> Ü  U-umlaut  (0xC3 0x9C; W1252 0x9C=U+0153)
        //   Ã©  Atilde + copyright   -> é  e-acute   (0xC3 0xA9)
        return s
            .Replace("Ã¼", "ü")
            .Replace("Ã¶", "ö")
            .Replace("Ã¤", "ä")
            .Replace("ÃŸ", "ß")
            .Replace("Ã„", "Ä")
            .Replace("Ã–", "Ö")
            .Replace("Ãœ", "Ü")
            .Replace("Ã©", "é");
    }

    /// <summary>
    /// Matches WicShiftEntry.SupportLocation against a WicLocation.
    /// Checks DisplayName, City, and the alias map (against both LocationCode and LocationCodeLegacy).
    /// Applies RepairDoubleEncoding() first so Windows-1252 mis-decoded UTF-8 values
    /// (e.g. corrupt "SaarbrXcken" sequences) resolve to their correct Unicode form before lookup.
    /// </summary>
    public static bool MatchesSupportLocation(string? sl, WicLocation loc)
    {
        if (sl == null) return false;
        sl = RepairDoubleEncoding(sl);
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
