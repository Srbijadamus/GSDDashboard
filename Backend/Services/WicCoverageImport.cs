using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

public static class WicCoverageImport
{
    // ─── Excluded agents ──────────────────────────────────────────────────────
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase);

    // ─── City normalization ───────────────────────────────────────────────────
    private static readonly HashSet<string> JunkCities = new(StringComparer.OrdinalIgnoreCase)
        { "offboading", "backup superman", "" };

    private static readonly Dictionary<string, string> CityAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Standland"]   = "Stadland",
        ["Munich"]      = "München",
        ["Essen (RWE)"] = "Essen",
        ["Denbosch"]    = "s-Hertogenbosch",
    };

    private static string? NormalizeCity(string city)
    {
        var t = city.Trim();
        if (string.IsNullOrEmpty(t) || JunkCities.Contains(t)) return null;
        return CityAliases.TryGetValue(t, out var alias) ? alias : t;
    }

    // ─── WIC display-name aliases ─────────────────────────────────────────────
    private static readonly Dictionary<string, string> WicNameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Essen BP1"] = "Essen - BP1",
        ["Essen TK1"] = "Essen - TK",
        ["Denbosch"]  = "s-Hertogenbosch",
    };

    private static string NormalizeWicName(string name)
    {
        var t = name.Trim();
        return WicNameAliases.TryGetValue(t, out var alias) ? alias : t;
    }

    // ─── Agent name aliases (original CSV names → DB FullName) ───────────────
    private static readonly Dictionary<string, string> AgentNameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Aman Kedo"]       = "Amani Kedo",
        ["Jolanda Coppers"] = "Yolanda Coppers",
        ["Yun Hee Ho"]      = "Yun Hee Oh",
    };

    private static string NormalizeAgentName(string name)
    {
        var t = name.Trim();
        return AgentNameAliases.TryGetValue(t, out var alias) ? alias : t;
    }

    // ─── Seed records ─────────────────────────────────────────────────────────

    private sealed record AgentSeed(
        string Name, string PrimaryKid, string SecondaryKid,
        string InfosysEmail, string EonEmail, string[] Cities);

    private sealed record WicSeed(
        string City, string OpeningDay, string? Comment,
        string[] Main, string[] Backup, string[] Regional);

    // ─── 70 agents ────────────────────────────────────────────────────────────

    private static readonly AgentSeed[] AgentSeeds =
    [
        new("Aakash Som",                  "A81657",  "A81768",  "aakash.som@infosys.com",            "Aakash.Som.external@eon.com",                         ["Salzgitter"]),
        new("Abdulrahman Aldera",          "A73442",  "A73462",  "abdulrahman.aldera@infosys.com",    "Abdulrahman.Aldera.External@eon.com",                  ["Stade", "Standland"]),
        new("Adam Szilvagyi",              "A80925",  "A80957",  "adam.szilvagyi@infosys.com",        "Adam.Szilvagyi.external@eon.com",                      ["Landshut", "Essenbach"]),
        new("Ahmad Dabbas",                "A81394",  "A81449",  "ahmad.dabbas@infosys.com",          "Ahmad.Dabbas.external@eon.com",                        ["Salzgitter"]),
        new("Amani Kedo",                  "A79697",  "A79906",  "aman.kedo@infosys.com",             "amani.kedo.external@eon.com",                          ["Mülheim", "Recklinghausen", "Arnsberg", "Essen"]),
        new("Amir Nassri",                 "A79647",  "A79744",  "amir.nassri@infosys.com",           "amir.nassri.external1@eon.com",                        ["Quickborn"]),
        new("Angelika Weber",              "A65019",  "A65416",  "angelika.weber@infosys.com",        "Angelika.Weber.external@eon.com",                      ["Arnsberg", "Wesel", "Dortmund", "Essen"]),
        new("Anisha Nellikka Panikkan",    "A80245",  "A80351",  "anisha.panikkan@infosys.com",       "Anisha.Nellikka.Panikkan.external@eon.com",            ["München", "Landshut"]),
        new("Binod Dutta",                 "B29884",  "B29903",  "binod.dutta@infosys.com",           "Binod.Dutta.external@eon.com",                         ["Pfaffenhofen"]),
        new("Bishal Maharjan",             "B29243",  "B29265",  "bishal.maharjan@infosys.com",       "bishal.maharjan.external@eon.com",                     ["Hamburg"]),
        new("Burak Kurtulmaz",             "B25462",  "B25481",  "burak.kurtulmaz@infosys.com",       "burak.kurtulmaz.external@eon.com",                     ["Saffig", "Neuss"]),
        new("Christian Martino",           "C43776",  "C43836",  "christian.martino@infosys.com",     "Christian.Martino.external@eon.com",                   ["Essen", "Dortmund"]),
        new("Christoph Ulatowski",         "C40658",  "C40728",  "christoph.ulatowski@infosys.com",  "christoph.ulatowski.external@eon.com",                 ["Neuss"]),
        new("Christos Kyrillidis",         "C44238",  "C44289",  "christos.kyrillidis@infosys.com",  "Christos.Kyrillidis.external@eon.com",                 ["Pfaffenhofen"]),
        new("Dennis Markus",               "D43156",  "D43759",  "dennis.markus@infosys.com",         "dennis.markus.external@eon.com",                       ["Potsdam"]),
        new("Dennis Obazee",               "D44372",  "D44405",  "dennis.obazee@infosys.com",         "Dennis.Obazee.external@eon.com",                       ["offboading"]),
        new("Dmytro Shelikhov",            "D45199",  "D45250",  "dmytro.shelikhov@infosys.com",     "Dmytro.Shelikhov.external@eon.com",                    ["Regensburg"]),
        new("Elaheh Ramzi",                "E26548",  "E26561",  "elaheh.ramzi@infosys.com",          "Elaheh.Ramzi.external@eon.com",                        ["Neu-Isenburg"]),
        new("Elias Erdem",                 "E26615",  "E26618",  "elias.erdem@infosys.com",           "Elias.Erdem.external@eon.com",                         ["Hamburg"]),
        new("Erdal Coskun",                "E21423",  "E23989",  "erdal.coskun@infosys.com",          "Erdal.Coskun.External@eon.com",                        ["Essen"]),
        new("Erik Goecks",                 "E26074",  "E26100",  "erik.siebecke@infosys.com",         "erik.goecks.external@eon.com",                         ["Berlin"]),
        new("Eyup Akyurek",                "E22918",  "E22920",  "eyup.akyurek@infosys.com",          "Eyuep.Akyuerek.external@eon.com",                      ["Munich", "München"]),
        new("Felix Spindler",              "F23636",  "F23650",  "felix.spindler@infosys.com",        "Felix.Spindler.external@eon.com",                      ["Halle", "Markkleeberg"]),
        new("Francois Sicot",              "F23403",  "F23405",  "francois.sicot@infosys.com",        "Francois.Sicot.external@eon.com",                      ["Bamberg"]),
        new("Hamyaz Pathan",               "H34111",  "H34131",  "hamyaz.pathan@infosys.com",         "Hamyaz.Pathan.external@eon.com",                       ["Potsdam", "Fürstenwalde", "Berlin", "Demmin"]),
        new("Hamza Forrousso",             "H34487",  "H34512",  "hamza.forrousso@infosys.com",       "Hamza.Forrousso.external@eon.com",                     ["Rendsburg"]),
        new("Hesham Montasser",            "H33776",  "H33826",  "hesham.montasser@infosys.com",      "hesham.montasser.external@eon.com",                    ["Saarbrücken"]),
        new("Holger Kuhlmann",             "H29193",  "H29204",  "holger.kuhlmann@infosys.com",       "holger.kuhlmann1.external@eon.com",                    ["Essen", "Dortmund"]),
        new("Holger Petzholdt",            "H29101",  "H29105",  "holger.petzholdt01@infosys.com",   "Holger.Petzholdt.external@eon.com",                    ["Emmerthal"]),
        new("Ion Bodnariuc",               "I18521",  "I18532",  "ion.bodnariuc@infosys.com",         "Ion.Bodnariuc.external@eon.com",                       ["Halle", "Markkleeberg"]),
        new("Jannik Borner",               "J51104",  "J51116",  "jannik.borner@infosys.com",         "jannik.borner.external@eon.com",                       ["Brokdorf"]),
        new("Joel Broring",                "J59326",  "J59337",  "joel.broring@infosys.com",          "Joel.Broering.external@eon.com",                       ["Stade", "Standland"]),
        new("John Daniel Wendland",        "J57815",  "J57971",  "johndaniel.wendland@infosys.com",  "John-Daniel.Wendland.External@eon.com",                ["Essenbach"]),
        new("Kaan Arslan",                 "K41008",  "K41098",  "kaan.arslan@infosys.com",           "kaan.arslan.external@eon.com",                         ["Essen"]),
        new("Kamil Filipowicz",            "K40479",  "K40557",  "kamil.filipowicz@infosys.com",     "kamil.filipowicz.external@eon.com",                    ["Augsburg"]),
        new("Kavinraj Pathmanathan",       "K37144",  "K38953",  "kavinraj.p@infosys.com",            "Kavin.Pathmanathan.external@eon.com",                  ["Essen", "Wesel"]),
        new("Kevin Heynen",                "K35422",  "K35458",  "kevin.heynen@infosys.com",          "kevin.heynen.external@eon.com",                        ["Essen", "Mülheim"]),
        new("Khaled Alali",                "K41452",  "K41469",  "khaled.alali@infosys.com",          "Khaled.Alali.external@eon.com",                        ["Munich", "Landshut", "München"]),
        new("Klaus Friedrich",             "K40212",  "K40639",  "klaus.friedrich01@infosys.com",    "klaus.friedrich.external@eon.com",                     ["Fürstenwalde"]),
        new("Krishnendu Das",              "K41065",  "K41083",  "krishnendu.das01@infosys.com",     "krishnendu.das.external@eon.com",                      ["Berlin", "Brandenburg", "Fürstenwalde", "Potsdam"]),
        new("Lukas Schiefele",             "L26183",  "L26198",  "lukas.scheifele@infosys.com",      "Lukas.Scheifele.external@eon.com",                     ["Hannover", "Emmerthal", "Grafenrheinfeld", "Stade", "Stadland"]),
        new("Mahboubeh Abdighara",         "M98548",  "M98587",  "mahboubeh.abdighara@infosys.com",  "boubeh.Abdighara.external@eon.com",                    ["Münster", "Osnabrück", "Recklinghausen"]),
        new("Marcus Rusch",                "M100548", "M100649", "marcus.rusch@infosys.com",          "Marcus.Rusch.external@eon.com",                        ["Regensburg"]),
        new("Mariusz Kozinski",            "M99927",  "M99934",  "mariusz.kozinski@infosys.com",     "Mariusz.Kozinski.external@eon.com",                    ["Bamberg"]),
        new("Mark Bachmann",               "M73873",  "M73911",  "mark.bachmann@infosys.com",         "Mark.Bachmann1.external@eon.com",                      ["Essen"]),
        new("Merlin Voss",                 "M99198",  "M99378",  "merlin.voss@infosys.com",           "Merlin.Voss.external@eon.com",                         ["Helmstedt"]),
        new("Mohamad Nasir Amany",         "M100791", "M100845", "mohamadnasir.amany@infosys.com",   "Mohamad.Nasir.Amany.external@eon.com",                 ["Augsburg", "Pfaffenhofen"]),
        new("Mohamed Khaled Mahmoud",      "M99031",  "M99241",  "mohamedkhaled.m@infosys.com",      "Mohamed.Khaled.Mahmoud.external@eon.com",              ["offboading"]),
        new("Negin Bazmi",                 "N23935",  "N23947",  "negin.bazmi@infosys.com",           "Negin.Bazmi.external@eon.com",                         ["Siegen", "Saffig"]),
        new("Olaf Wittenberg",             "O6319",   "O6321",   "olaf.wittenberg@infosys.com",      "Olaf.Wittenberg.external@eon.com",                     ["Hannover", "Emmerthal"]),
        new("Önder Arslan",                "O9045",   "-",       "onder.arslan@infosys.com",          "-",                                                    ["Hamburg"]),
        new("Patrick Henschel",            "P37233",  "P37250",  "patrick.henschel@infosys.com",     "Patrick.Henschel.external@eon.com",                    ["Essen (RWE)", "Wesel"]),
        new("Rene Altmeyer",               "R44968",  "R45047",  "rene.altmeyer@infosys.com",         "rene.altmeyer.external@eon.com",                       ["Demmin"]),
        new("Sebastian Höck",              "S60574",  "S60838",  "sebastian.hock@infosys.com",        "sebastian.kersten.external@eon.com",                   ["Dortmund"]),
        new("Sebastian Lewandowski",       "S75427",  "S75452",  "sebastian.l@infosys.com",           "Sebastian.Lewandowski.external@eon.com",               ["Demmin"]),
        new("Senthuran Shanmugalingam",    "S74973",  "S75024",  "senthuran.s@infosys.com",           "Senthuran.Shanmugalingam.external@eon.com",            ["Helmstedt"]),
        new("Sina Sidharthan",             "S74279",  "S74419",  "sina.sidharthan@infosys.com",       "Sina.Sidharthan.external@eon.com",                     ["Augsburg", "Landshut"]),
        new("Suhrab Sadieqy",              "S73217",  "S73256",  "suhrab.sadieqy@infosys.com",        "suhrab.sadieqy.external@eon.com",                      ["Saarbrücken", "Trier"]),
        new("Tim Boger",                   "T33358",  "T33369",  "tim.boger@infosys.com",             "Tim.Boeger.External@eon.com",                          ["Grafenrheinfeld"]),
        new("Viktor Winter",               "V18959",  "-",       "viktor.winter@infosys.com",         "-",                                                    ["Rendsburg", "Brokdorf", "Demmin"]),
        new("Yun Hee Oh",                  "Y6505",   "Y6508",   "yunhee.oh@infosys.com",             "Yun-Hee.Oh.external@eon.com",                          ["Neuss"]),
        // NL agents — no KIDs in source
        new("Ayten Karatas",               "",        "",        "",                                  "",                                                     ["Denbosch"]),
        new("Ivan Leurs",                  "",        "",        "",                                  "",                                                     ["Denbosch", "Zwolle"]),
        new("Yolanda Coppers",             "",        "",        "",                                  "",                                                     ["Denbosch"]),
        new("Mehmet Tigli",                "",        "",        "",                                  "",                                                     ["Pfaffenhofen"]),
        new("Mohammad Al Masalma",         "M101365", "",        "",                                  "Mohammad.Al.Masalma.external@eon.com",                  ["Neu-Isenburg"]),
        new("Kai Eric Kumlehn",            "",        "",        "",                                  "",                                                     ["Münster", "Osnabrück", "Recklinghausen"]),
        new("Elliot van Staveren Kuster",  "",        "",        "",                                  "",                                                     ["Zwolle"]),
        new("Michael Holz",                "",        "",        "",                                  "",                                                     []),
        new("Stojnic Nebojsa",             "",        "",        "",                                  "",                                                     ["backup superman"]),
    ];

    // ─── 44 WICs ──────────────────────────────────────────────────────────────

    private static readonly WicSeed[] WicSeeds =
    [
        new("Hamburg",                "daily",                                                     null,
            ["Bishal Maharjan"], ["Önder Arslan", "Elias Erdem"], []),
        new("Hannover",               "(Di. Mi. Do. ) volltag + (Mo. Fr.) Vormittag",              null,
            ["Olaf Wittenberg"], [], ["Lukas Schiefele"]),
        new("Emmerthal",              "daily",                                                     "check with Hannover together, the backup can't be used, need 1 more",
            ["Holger Petzholdt"], ["Olaf Wittenberg"], ["Lukas Schiefele"]),
        new("München",                "(Mo. Di. Mi. Do. ) volltag + Fr. Vormittag",                "combine with Landshut, maybe need one more backup",
            ["Eyup Akyurek"], ["Khaled Alali", "Anisha Nellikka Panikkan"], []),
        new("Landshut",               "Do.",                                                       "combine with München, maybe need one more backup",
            ["Khaled Alali"], ["Eyup Akyurek", "Adam Szilvagyi", "Sina Sidharthan", "Anisha Nellikka Panikkan"], []),
        new("Essenbach",              "(Mo. Di. Mi. Do. ) volltag + Fr. Vormittag",                "no backup",
            ["John Daniel Wendland", "Adam Szilvagyi"], ["TBD"], []),
        new("Augsburg",               "daily +Fri 07:30 - 14:00",                                  null,
            ["Kamil Filipowicz"], ["Mohamad Nasir Amany", "Sina Sidharthan"], []),
        new("Demmin - Woldeforster Str", "(Di.Do ) Nachmittag",                                   "Demmin 2 WICs can be combined together",
            ["Rene Altmeyer"], ["Sebastian Lewandowski", "Hamyaz Pathan", "Viktor Winter"], []),
        new("Demmin - Am Hanseufer",  "(Mo. Di. Mi. Do. ) Vormittag",                             null,
            ["Rene Altmeyer"], ["Sebastian Lewandowski", "Hamyaz Pathan", "Viktor Winter"], []),
        new("Rendsburg",              "Mo.",                                                       null,
            ["Hamza Forrousso"], ["Viktor Winter"], []),
        new("Helmstedt",              "(Mo. Di. Mi. Do. ) volltag + Fr. Vormittag",               null,
            ["Merlin Voss"], ["Senthuran Shanmugalingam"], []),
        new("Essen BP1",              "daily *3",                                                  null,
            ["Holger Kuhlmann", "Mark Bachmann", "Erdal Coskun"],
            ["Kavinraj Pathmanathan", "Angelika Weber", "Kevin Heynen"], ["Patrick Henschel"]),
        new("Essen TK1",              "daily",                                                     null,
            ["Kaan Arslan"],
            ["Kavinraj Pathmanathan", "Angelika Weber", "Kevin Heynen"], []),
        new("Mülheim",                "Do.",                                                       null,
            ["Aman Kedo"], ["TBD"], ["Kevin Heynen"]),
        new("Salzgitter",             "daily",                                                     null,
            ["Ahmad Dabbas", "Aakash Som"], [], []),
        new("Fürstenwalde",           "(Mo. Mi.)volltag + (Di. Do.) Vormittag",                   "Hamyaz backup for several WIC, maybe need 1 more backup",
            ["Klaus Friedrich"], ["Hamyaz Pathan"], []),
        new("Potsdam",                "(Mo. Mi.)volltag + (Di. Do.) Vormittag",                   "Hamyaz backup for several WIC, maybe need 1 more backup",
            ["Dennis Markus"], ["Hamyaz Pathan"], []),
        new("Quickborn",              "(Di. Mi.Do.)volltag",                                       "no backup now",
            ["Amir Nassri"], ["TBD"], []),
        new("Arnsberg",               "Di",                                                        "Aman Kedo supported as backup before",
            ["Angelika Weber"], ["Aman Kedo"], []),
        new("Dortmund",               "daily",                                                     null,
            ["Christian Martino"], ["Holger Kuhlmann", "Angelika Weber", "Sebastian Höck"], []),
        new("Münster",                "Di.",                                                       null,
            ["Mahboubeh Abdighara"], ["Kai Eric Kumlehn"], []),
        new("Osnabrück",              "Mo.",                                                       null,
            ["Mahboubeh Abdighara"], ["Kai Eric Kumlehn"], []),
        new("Recklinghausen",         "Mi.",                                                       null,
            ["Mahboubeh Abdighara"], ["Kai Eric Kumlehn", "Aman Kedo"], []),
        new("Wesel",                  "Do.",                                                       null,
            ["Angelika Weber"], ["Kavinraj Pathmanathan"], ["Patrick Henschel"]),
        new("Neuss",                  "Mi.",                                                       null,
            ["Yun Hee Ho"], ["Burak Kurtulmaz"], ["Christoph Ulatowski"]),
        new("Neu-Isenburg",           "daily",                                                     null,
            ["Elaheh Ramzi", "Mohammad Al Masalma"], [], []),
        new("Brokdorf",               "daily",                                                     "Viktor supported 1 time before, but maybe need a new backup.",
            ["Jannik Borner"], ["Viktor Winter"], []),
        new("Stade",                  "Mo. Do.",                                                   "no backup available, because of the demands from Stadland",
            ["Abdulrahman Aldera", "Joel Broring"], ["Abdulrahman Aldera", "Joel Broring"], ["Lukas Schiefele"]),
        new("Stadland",               "(Mo. Di. Mi. Do.)volltag + Fr. Vormittag",                 "no backup available on Mo.and  Do.",
            ["Abdulrahman Aldera", "Joel Broring"], ["Abdulrahman Aldera", "Joel Broring"], ["Lukas Schiefele"]),
        new("Grafenrheinfeld",        "daily",                                                     "no backup now",
            ["Tim Boger"], ["TBD"], ["Lukas Schiefele"]),
        new("Berlin - Gaußstr.",      "Mo",                                                        "Berlin 2 WICs can be combined together",
            ["Erik Goecks"], ["Krishnendu Das"], []),
        new("Berlin - Brückenstrasse","Di",                                                        null,
            ["Erik Goecks"], ["Krishnendu Das"], []),
        new("Saffig",                 "Di.",                                                       null,
            ["Burak Kurtulmaz"], ["Negin Bazmi"], []),
        new("Siegen",                 "Mi.",                                                       "no backup now",
            ["Negin Bazmi"], ["TBD"], []),
        new("Saarbrücken",            "daily",                                                     "no backup on Do.",
            ["Hesham Montasser"], ["Suhrab Sadieqy"], []),
        new("Trier",                  "Do.",                                                       "no backup now",
            ["Suhrab Sadieqy"], ["TBD"], []),
        new("Halle",                  "Mo. Di. Mi. Do.",                                           null,
            ["Ion Bodnariuc"], ["Felix Spindler"], []),
        new("Markkleeberg",           "Mi. Do.",                                                   null,
            ["Ion Bodnariuc"], ["Felix Spindler"], []),
        new("Bamberg",                "daily +Fri 1/2",                                            null,
            ["Mariusz Kozinski"], ["Francois Sicot"], []),
        new("s-Hertogenbosch",        "#NV",                                                       "do we have this WIC?",
            ["Ayten Karatas"], ["Jolanda Coppers"], []),
        new("Zwolle",                 "Mo. Mi.",                                                   null,
            ["Ivan Leurs"], ["Elliot van Staveren Kuster"], []),
        new("Pfaffenhofen",           "(Mo. Di. Mi. Do. ) volltag + Fr. Vormittag",                null,
            ["Binod Dutta"], ["Christos Kyrillidis", "Mohamad Nasir Amany", "Mehmet Tigli"], []),
        new("Regensburg",             "(Mo. Di. Mi. Do. ) volltag + Fr. Vormittag",                null,
            ["Marcus Rusch"], ["Dmytro Shelikhov"], []),
        new("Denbosch",               "daily",                                                     null,
            ["Ayten Karatas", "Ivan Leurs"], ["Yolanda Coppers"], []),
    ];

    // ─── Main import logic ────────────────────────────────────────────────────

    public static async Task RunAsync(GSDContext db)
    {
        if (await db.AgentReachableCities.AnyAsync()) return;

        var employees = await db.Employees.ToListAsync();
        var wicLocations = await db.WicLocations.ToListAsync();
        var existingAssignments = await db.WicAgentAssignments.ToListAsync();

        var empByName = employees
            .Where(e => e.FullName != null)
            .GroupBy(e => e.FullName!.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var wicByDisplayName = wicLocations
            .GroupBy(w => w.DisplayName.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var unmatchedAgents = new List<string>();
        var unmatchedWics   = new List<string>();

        // ── Agents ──────────────────────────────────────────────────────────

        foreach (var seed in AgentSeeds)
        {
            empByName.TryGetValue(seed.Name.ToLowerInvariant(), out var emp);
            if (emp == null) unmatchedAgents.Add(seed.Name);

            if (emp != null)
            {
                if (!string.IsNullOrWhiteSpace(seed.PrimaryKid) && seed.PrimaryKid != "-")
                    emp.PrimaryKid = seed.PrimaryKid;
                if (!string.IsNullOrWhiteSpace(seed.SecondaryKid) && seed.SecondaryKid != "-")
                    emp.SecondaryKid = seed.SecondaryKid;
                if (!string.IsNullOrWhiteSpace(seed.InfosysEmail) && seed.InfosysEmail != "-")
                    emp.InfosysEmail = seed.InfosysEmail;
                if (!string.IsNullOrWhiteSpace(seed.EonEmail) && seed.EonEmail != "-")
                    emp.EonEmail = seed.EonEmail;
            }

            var seenCities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var city in seed.Cities)
            {
                var normalized = NormalizeCity(city);
                if (normalized == null || !seenCities.Add(normalized)) continue;

                db.AgentReachableCities.Add(new AgentReachableCity
                {
                    EmployeeId   = emp?.EmployeeId,
                    EmployeeName = seed.Name,
                    City         = normalized,
                    Source       = "seed",
                });
            }
        }

        // ── WICs ────────────────────────────────────────────────────────────

        foreach (var seed in WicSeeds)
        {
            var displayName = NormalizeWicName(seed.City);
            wicByDisplayName.TryGetValue(displayName.ToLowerInvariant(), out var wicLoc);

            if (wicLoc == null)
            {
                unmatchedWics.Add(seed.City);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(seed.OpeningDay))
                wicLoc.OpeningDay = seed.OpeningDay;
            if (!string.IsNullOrWhiteSpace(seed.Comment))
                wicLoc.Comment = seed.Comment;

            void AddAssignment(string rawName, string type)
            {
                var name = NormalizeAgentName(rawName);
                if (string.Equals(name, "TBD", StringComparison.OrdinalIgnoreCase)) return;
                if (Excluded.Contains(name)) return;

                var locCode = wicLoc.LocationCode;
                bool exists = existingAssignments.Any(a =>
                    string.Equals(a.LocationCode,   locCode, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.EmployeeName,   name,    StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.AssignmentType, type,    StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    db.WicAgentAssignments.Add(new WicAgentAssignment
                    {
                        LocationCode   = locCode,
                        EmployeeName   = name,
                        AssignmentType = type,
                        IsActive       = true,
                    });
                }
            }

            foreach (var n in seed.Main)     AddAssignment(n, "MAIN");
            foreach (var n in seed.Backup)   AddAssignment(n, "BACKUP");
            foreach (var n in seed.Regional) AddAssignment(n, "REGIONAL");
        }

        await db.SaveChangesAsync();

        Console.WriteLine($"[WicCoverage] Import done. Unmatched agents ({unmatchedAgents.Count}): {string.Join(", ", unmatchedAgents)}");
        Console.WriteLine($"[WicCoverage] Unmatched WICs ({unmatchedWics.Count}): {string.Join(", ", unmatchedWics)}");
    }
}
