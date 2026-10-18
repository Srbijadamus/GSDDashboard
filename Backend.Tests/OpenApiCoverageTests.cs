using System.Text.RegularExpressions;

namespace GSDDashboard.Tests;

/// <summary>
/// Fails when a route registered by the backend is missing from Backend/openapi.yaml.
/// Routes are discovered by scanning the Backend source for the Minimal-API
/// MapGroup + Map{Get,Post,Put,Delete,Patch} registrations (the same ones
/// Program.cs invokes), then compared against the committed spec. No database or
/// service container is required.
/// </summary>
public class OpenApiCoverageTests
{
    [Fact]
    public void EveryRegisteredRoute_IsPresentInOpenApiSpec()
    {
        var repoRoot = FindRepoRoot();
        var backendDir = Path.Combine(repoRoot, "Backend");
        var specPath = Path.Combine(backendDir, "openapi.yaml");

        var actual = ScanRoutes(backendDir);
        var spec = ParseOpenApiRoutes(specPath);

        // Guard: the API surface is well over 100 routes.
        Assert.True(actual.Count >= 100, $"Only {actual.Count} routes discovered — source scan failed.");

        var missing = actual.Where(r => !spec.Contains(r)).OrderBy(r => r).ToList();
        Assert.True(
            missing.Count == 0,
            "Routes registered in code but missing from Backend/openapi.yaml:\n  " +
            string.Join("\n  ", missing));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found (no CLAUDE.md).");
    }

    private static HashSet<string> ScanRoutes(string backendDir)
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupRe = new Regex(@"\.MapGroup\(\s*""([^""]+)""");
        var mapRe = new Regex(@"\.Map(Get|Post|Put|Delete|Patch)\(\s*""([^""]+)""");

        foreach (var file in Directory.EnumerateFiles(backendDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var group = groupRe.Match(text).Groups[1].Value.TrimEnd('/');

            foreach (Match m in mapRe.Matches(text))
            {
                var method = m.Groups[1].Value.ToUpperInvariant();
                var path = m.Groups[2].Value;

                // Full-path registrations (Program.cs and a few services) start with
                // /api/ or /health; everything else is relative to the file's MapGroup.
                var route = path.StartsWith("/api/", StringComparison.Ordinal) ||
                            path.StartsWith("/health", StringComparison.Ordinal)
                    ? path
                    : group + path;

                routes.Add($"{method} {NormalizeRoute(route.TrimEnd('/'))}");
            }
        }
        return routes;
    }

    private static string NormalizeRoute(string pattern) =>
        // Strip ASP.NET route constraints ({id:int}, {dow:min(1)}, ...) so they
        // match the OpenAPI placeholder form ({id}).
        Regex.Replace(pattern, @"\{([^}:]+)(?::[^}]+)?\}", "{$1}");

    private static HashSet<string> ParseOpenApiRoutes(string path)
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathRe = new Regex(@"^  (/[^:]+):\s*$");
        var methodRe = new Regex(@"^    (get|post|put|patch|delete):\s*$");

        string? current = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var pm = pathRe.Match(raw);
            if (pm.Success)
            {
                current = pm.Groups[1].Value;
                continue;
            }
            var mm = methodRe.Match(raw);
            if (mm.Success && current is not null)
                routes.Add($"{mm.Groups[1].Value.ToUpperInvariant()} {current}");
        }
        return routes;
    }
}
