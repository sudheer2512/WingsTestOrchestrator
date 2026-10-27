using System.Text.Json;
using System.Text.RegularExpressions;
using Wings.Agent.Contracts;

namespace Wings.Agent.Executors;

/// <summary>
/// Runs Playwright Test (TypeScript/JavaScript) packages: installs dependencies, runs the requested test files with the
/// JSON reporter and traces enabled, then maps results, screenshots, videos and traces back to the discovered identifiers
/// ("path/to/file.spec.ts > describe > test title").
/// </summary>
public sealed class PlaywrightExecutor : ITestExecutor
{
    public IReadOnlyList<string> Frameworks { get; } = ["PLAYWRIGHT_TS"];

    public async Task<ExecutorResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var root = FindRoot(ctx.PackageDirectory) ?? throw new PreparationException("No package.json was found in the package.");
        var env = new Dictionary<string, string>(ctx.Environment) { ["CI"] = "1", ["PLAYWRIGHT_JSON_OUTPUT_NAME"] = Path.Combine(ctx.OutputDirectory, "results.json") };
        if (ctx.Options.PackageCacheDirectory is { } cache) env["npm_config_cache"] = Path.Combine(cache, "npm");

        var install = File.Exists(Path.Combine(root, "package-lock.json"))
            ? new[] { "ci", "--no-audit", "--no-fund" }
            : new[] { "install", "--no-audit", "--no-fund", "--no-package-lock" };
        var npm = await ProcessRunner.RunAsync(ctx.Options.NpmCommand, install, root, env, ctx.Log, ct);
        if (npm.ExitCode != 0) throw new PreparationException($"npm {install[0]} failed with exit code {npm.ExitCode}.");
        if (ctx.Options.InstallPlaywrightBrowsers)
        {
            var browsers = await ProcessRunner.RunAsync(ctx.Options.NpxCommand, ["playwright", "install"], root, env, ctx.Log, ct);
            if (browsers.ExitCode != 0) throw new PreparationException("Installing Playwright browsers failed.");
        }

        var parsed = ctx.Job.TestIdentifiers.Select(Parse).ToList();
        var files = parsed.Select(p => RelativeTo(root, ctx.PackageDirectory, p.File)).Distinct().ToList();
        var args = new List<string> { "playwright", "test", "--reporter=json,line", "--output", Path.Combine(ctx.OutputDirectory, "test-results") };
        if (ctx.Job.CaptureTrace) args.Add("--trace=retain-on-failure");
        if (ctx.Job.Variables.TryGetValue("PLAYWRIGHT_PROJECT", out var project)) args.Add($"--project={project}");
        args.Add("--grep");
        args.Add(string.Join('|', parsed.Select(p => Regex.Escape(p.Titles[^1]))));
        args.AddRange(files);
        var run = await ProcessRunner.RunAsync(ctx.Options.NpxCommand, args, root, env, ctx.Log, ct);

        var reportPath = env["PLAYWRIGHT_JSON_OUTPUT_NAME"];
        if (!File.Exists(reportPath))
            throw new PreparationException($"Playwright exited with {run.ExitCode} without writing a JSON report (configuration or browser installation problem; see log).");

        var (results, artifacts) = ParseReport(reportPath, parsed, ctx.Job.TestIdentifiers);
        artifacts.Add(new ArtifactFile(reportPath, ArtifactKinds.Report, "application/json", null));
        artifacts.AddRange(ArtifactTypes.FromDirectory(ctx.ArtifactsDirectory, ctx.Job.TestIdentifiers, i => Parse(i).Titles[^1]));
        return new ExecutorResult(results, artifacts, run.ExitCode);
    }

    private sealed record ParsedId(string Identifier, string File, string[] Titles);

    private static ParsedId Parse(string identifier)
    {
        var parts = identifier.Split(" > ");
        return new ParsedId(identifier, parts[0], parts[1..]);
    }

    private static string? FindRoot(string dir)
        => Directory.EnumerateFiles(dir, "package.json", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"))
            .OrderBy(p => p.Length).Select(Path.GetDirectoryName).FirstOrDefault();

    private static string RelativeTo(string root, string packageDir, string packagePath)
        => Path.GetRelativePath(root, Path.Combine(packageDir, packagePath)).Replace('\\', '/');

    private static (List<TestResultReport>, List<ArtifactFile>) ParseReport(string path, List<ParsedId> wanted, IReadOnlyList<string> identifiers)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var specs = new List<(string File, string[] Titles, JsonElement Spec)>();
        void Walk(JsonElement suite, string[] titles)
        {
            var file = suite.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "";
            if (suite.TryGetProperty("specs", out var ss))
                foreach (var spec in ss.EnumerateArray())
                    specs.Add((spec.TryGetProperty("file", out var sf) ? sf.GetString() ?? file : file, [.. titles, spec.GetProperty("title").GetString()!], spec));
            if (suite.TryGetProperty("suites", out var children))
                foreach (var child in children.EnumerateArray())
                    Walk(child, child.TryGetProperty("specs", out _) && IsFileSuite(child) ? titles : [.. titles, child.GetProperty("title").GetString()!]);
        }
        foreach (var top in doc.RootElement.GetProperty("suites").EnumerateArray()) Walk(top, []); // top-level suites are files

        var results = new List<TestResultReport>();
        var artifacts = new List<ArtifactFile>();
        foreach (var w in wanted)
        {
            var match = specs.FirstOrDefault(s => s.Titles.SequenceEqual(w.Titles) && (w.File.Replace('\\', '/').EndsWith(s.File.Replace('\\', '/')) || s.File.EndsWith(Path.GetFileName(w.File))));
            if (match.File is null) continue;
            var runs = match.Spec.GetProperty("tests").EnumerateArray().SelectMany(t => t.GetProperty("results").EnumerateArray()).ToList();
            if (runs.Count == 0) continue;
            var statuses = runs.Select(r => r.GetProperty("status").GetString()).ToList();
            var tests = match.Spec.GetProperty("tests").EnumerateArray().ToList();
            // Each browser project is a separate test; a test that passed on retry ("flaky") counts as passed.
            var outcome = tests.All(t => t.GetProperty("status").GetString() == "skipped") ? TestOutcomes.Skipped
                : tests.Any(t => t.GetProperty("status").GetString() == "unexpected") ? TestOutcomes.Failed : TestOutcomes.Passed;
            var error = runs.Select(r => r.TryGetProperty("error", out var e) ? e : (JsonElement?)null).FirstOrDefault(e => e is not null);
            results.Add(new TestResultReport
            {
                Identifier = w.Identifier, Outcome = outcome,
                DurationMs = runs.Sum(r => r.GetProperty("duration").GetInt64()),
                ErrorMessage = error is { } er && er.TryGetProperty("message", out var m) ? StripAnsi(m.GetString()) : null,
                StackTrace = error is { } es && es.TryGetProperty("stack", out var st) ? StripAnsi(st.GetString()) : null,
                StartedAt = runs[0].TryGetProperty("startTime", out var start) && DateTimeOffset.TryParse(start.GetString(), out var s) ? s : null,
                Output = string.Join('\n', runs.SelectMany(r => r.TryGetProperty("stdout", out var o) ? o.EnumerateArray().Select(x => x.TryGetProperty("text", out var t) ? t.GetString() : null) : [])
                    .Where(x => x is not null)),
            });
            foreach (var r in runs)
                if (r.TryGetProperty("attachments", out var atts))
                    foreach (var a in atts.EnumerateArray())
                        if (a.TryGetProperty("path", out var p) && p.GetString() is { } file && File.Exists(file))
                        {
                            var (kind, type) = ArtifactTypes.For(file);
                            artifacts.Add(new ArtifactFile(file, kind, a.TryGetProperty("contentType", out var ctp) ? ctp.GetString() ?? type : type, w.Identifier));
                        }
        }
        return (results, artifacts);
    }

    private static bool IsFileSuite(JsonElement suite) => suite.TryGetProperty("file", out var f) && suite.TryGetProperty("title", out var t) && f.GetString() == t.GetString();

    private static string? StripAnsi(string? s) => s is null ? null : Regex.Replace(s, @"\x1B\[[0-9;]*[A-Za-z]", "");
}
