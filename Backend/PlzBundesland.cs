namespace GSDDashboard.API.Data;

/// <summary>
/// Static PLZ → Bundesland lookup for WIC locations.
/// Sources: schema.sql seed (40 locations) + WicCardsService alias map extras.
/// NL locations: null (no Bundesland concept).
/// </summary>
public static class PlzBundesland
{
    // PostalCode (5-digit string) → Bundesland (German official name)
    // Verified against: schema.sql PostalCode column, official PLZ ranges.
    // Sanity checks: Augsburg (86150) → Bayern ✓, Berlin (10589/12355) → Berlin ✓, Hamburg (20537) → Hamburg ✓
    private static readonly Dictionary<string, string> _byPlz =
        new(StringComparer.Ordinal)
    {
        // Bayern
        { "86150", "Bayern" },   // Augsburg (Schaezlerstr.)
        { "96052", "Bayern" },   // Bamberg
        { "84051", "Bayern" },   // Essenbach
        { "97506", "Bayern" },   // Grafenrheinfeld
        { "84036", "Bayern" },   // Landshut
        { "80634", "Bayern" },   // München (Arnulfstr.)
        { "85276", "Bayern" },   // Pfaffenhofen a.d. Ilm (PFAFFENHOFEN alias)
        { "93049", "Bayern" },   // Regensburg (Lilienthalstr.)
        { "93059", "Bayern" },   // Regensburg (DE_Regensburg alias)

        // Berlin
        { "10179", "Berlin" },   // Berlin (Brückenstr.)
        { "10589", "Berlin" },   // Berlin (Gaußstr.)
        { "12355", "Berlin" },   // Berlin (Köpenicker Str.)

        // Brandenburg
        { "15517", "Brandenburg" }, // Fürstenwalde
        { "14467", "Brandenburg" }, // Potsdam

        // Hamburg
        { "20537", "Hamburg" },  // Hamburg

        // Hessen
        { "63263", "Hessen" },   // Neu-Isenburg

        // Mecklenburg-Vorpommern
        { "17109", "Mecklenburg-Vorpommern" }, // Demmin (both Hanseufer + Woldeforster)

        // Niedersachsen
        { "31860", "Niedersachsen" }, // Emmerthal
        { "30459", "Niedersachsen" }, // Hannover
        { "38350", "Niedersachsen" }, // Helmstedt
        { "49074", "Niedersachsen" }, // Osnabrück
        { "38226", "Niedersachsen" }, // Salzgitter
        { "21683", "Niedersachsen" }, // Stade
        { "26935", "Niedersachsen" }, // Stadland

        // NRW
        { "59821", "NRW" }, // Arnsberg
        { "44139", "NRW" }, // Dortmund
        { "45131", "NRW" }, // Essen (Brüsseler Platz)
        { "45143", "NRW" }, // Essen (ThyssenKrupp)
        { "45476", "NRW" }, // Mülheim
        { "48163", "NRW" }, // Münster
        { "41460", "NRW" }, // Neuss
        { "45661", "NRW" }, // Recklinghausen
        { "57072", "NRW" }, // Siegen
        { "46483", "NRW" }, // Wesel

        // Rheinland-Pfalz
        { "56648", "Rheinland-Pfalz" }, // Saffig
        { "54294", "Rheinland-Pfalz" }, // Trier

        // Saarland
        { "66121", "Saarland" }, // Saarbrücken

        // Sachsen
        { "04416", "Sachsen" },  // Markkleeberg

        // Sachsen-Anhalt
        { "06112", "Sachsen-Anhalt" }, // Halle (Saale)

        // Schleswig-Holstein
        { "25576", "Schleswig-Holstein" }, // Brokdorf
        { "25451", "Schleswig-Holstein" }, // Quickborn
        { "24768", "Schleswig-Holstein" }, // Rendsburg (RENDSBURG alias)
    };

    // LocationCode → Bundesland for old-style codes that have no PostalCode in DB (NL = null omitted)
    private static readonly Dictionary<string, string> _byLocationCode =
        new(StringComparer.OrdinalIgnoreCase)
    {
        { "RENDSBURG",    "Schleswig-Holstein" },
        { "PFAFFENHOFEN", "Bayern"             },
    };

    /// <summary>
    /// Returns the Bundesland for a WIC location.
    /// Lookup order: (1) PostalCode column, (2) LocationCode fallback.
    /// Returns null for NL locations or unknown PLZs.
    /// </summary>
    public static string? Get(string locationCode, string? postalCode, string? country)
    {
        // NL locations have no Bundesland
        if (string.Equals(country, "NL", StringComparison.OrdinalIgnoreCase))
            return null;

        // Try PostalCode first (most reliable)
        if (!string.IsNullOrWhiteSpace(postalCode) && _byPlz.TryGetValue(postalCode.Trim(), out var bl))
            return bl;

        // Try extracting PLZ from new-format LocationCode: DE~PLZ~City~Address
        if (locationCode.Contains('~'))
        {
            var parts = locationCode.Split('~');
            if (parts.Length >= 2 && _byPlz.TryGetValue(parts[1].Trim(), out var bl2))
                return bl2;
        }

        // Fallback: LocationCode direct map (old-style codes like RENDSBURG)
        if (_byLocationCode.TryGetValue(locationCode, out var bl3))
            return bl3;

        return null; // unknown — do not fabricate
    }

    /// <summary>
    /// Returns all PLZ entries in the dictionary (for testing/logging).
    /// </summary>
    public static IReadOnlyDictionary<string, string> AllPlzEntries => _byPlz;
}
