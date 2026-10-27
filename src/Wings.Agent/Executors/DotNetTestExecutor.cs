using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Wings.Agent.Contracts;

namespace Wings.Agent.Executors;

/// <summary>
/// Runs .NET test projects (xUnit, NUnit, MSTest, FlaUI, Playwright for .NET) with "dotnet test", selecting the requested
/// tests with a FullyQualifiedName filter and reading outcomes from the TRX report. Compilation happens here, on the agent.
/// </summary>
public sealed class DotNetTestExecutor : ITestExecutor
{
    public IReadOnlyList<string> Frameworks { get; } = ["XUNIT", "NUNIT", "MSTEST", "FLAUI", "PLAYWRIGHT_CS"];

    private const int MaxFilterLength = 24_000; // Windows command lines are limited to ~32K characters

    public async Task<ExecutorResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var project = FindProject(ctx.PackageDirectory) ?? throw new PreparationException("No test project (.csproj referencing Microsoft.NET.Test.Sdk) was found in the package.");
        var ids = ctx.Job.TestIdentifiers;
        var filter = string.Join('|', ids.Select(i => "FullyQualifiedName=" + Escape(i)));
        var args = new List<string> { "test", project, "--nologo", "--logger", "trx;LogFileName=results.trx", "--results-directory", ctx.OutputDirectory };
        if (filter.Length <= MaxFilterLength) { args.Add("--filter"); args.Add(filter); }
        else ctx.Log("warn", $"{ids.Count} tests exceed the filter length limit; running the whole project and keeping only the requested results.");

        var env = new Dictionary<string, string>(ctx.Environment) { ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1" };
        if (ctx.Options.PackageCacheDirectory is { } cache) env["NUGET_PACKAGES"] = Path.Combine(cache, "nuget");
        var run = await ProcessRunner.RunAsync(ctx.Options.DotnetCommand, args, ctx.PackageDirectory, env, ctx.Log, ct);

        var trx = Directory.EnumerateFiles(ctx.OutputDirectory, "*.trx", SearchOption.AllDirectories).FirstOrDefault();
        if (trx is null)
        {
            // No report means restore/build failed: the code could not be compiled on this agent, so nothing was executed.
            var blocked = ids.Select(i => new TestResultReport { Identifier = i, Outcome = TestOutcomes.Blocked, ErrorMessage = "The test project failed to restore or build (see job log)." }).ToList();
            return new ExecutorResult(blocked, [], run.ExitCode, $"dotnet test exited with {run.ExitCode} and produced no TRX report (restore or build failure).");
        }

        var (results, resultFiles) = ParseTrx(trx, ids);
        var artifacts = new List<ArtifactFile> { new(trx, ArtifactKinds.Report, "application/xml", null) };
        artifacts.AddRange(resultFiles);
        artifacts.AddRange(ArtifactTypes.FromDirectory(ctx.ArtifactsDirectory, ids, MethodName));
        return new ExecutorResult(results, artifacts, run.ExitCode);
    }

    private static string? FindProject(string root)
    {
        var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).ToList();
        return projects.FirstOrDefault(p => File.ReadAllText(p).Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase) || File.ReadAllText(p).Contains("MSTest.Sdk"))
               ?? (projects.Count == 1 ? projects[0] : null);
    }

    /// <summary>Escapes characters that have meaning in the VSTest filter grammar.</summary>
    private static string Escape(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value) { if (c is '\\' or '(' or ')' or '&' or '|' or '=' or '!' or '~') sb.Append('\\'); sb.Append(c); }
        return sb.ToString();
    }

    private static string MethodName(string identifier) => identifier[(identifier.LastIndexOf('.') + 1)..];

    internal static (List<TestResultReport> Results, List<ArtifactFile> Files) ParseTrx(string path, IReadOnlyList<string> requested)
    {
        var doc = XDocument.Load(path);
        XNamespace ns = doc.Root!.Name.Namespace;
        var definitions = doc.Descendants(ns + "UnitTest").ToDictionary(
            u => (string)u.Attribute("id")!,
            u =>
            {
                var m = u.Element(ns + "TestMethod")!;
                var cls = ((string?)m.Attribute("className") ?? "").Replace('+', '.');
                cls = Regex.Replace(cls, @",.*$", ""); // MSTest may append the assembly name
                var name = Regex.Replace((string?)m.Attribute("name") ?? "", @"\(.*\)$", "");
                return $"{cls}.{name}";
            });

        var wanted = requested.ToHashSet(StringComparer.Ordinal);
        var grouped = new Dictionary<string, List<XElement>>();
        foreach (var r in doc.Descendants(ns + "UnitTestResult"))
        {
            if (!definitions.TryGetValue((string)r.Attribute("testId")!, out var id) || !wanted.Contains(id)) continue;
            if (!grouped.TryGetValue(id, out var list)) grouped[id] = list = [];
            list.Add(r);
        }

        var files = new List<ArtifactFile>();
        var dir = Path.GetDirectoryName(path)!;
        var results = grouped.Select(g =>
        {
            // Theories / data rows produce several results for one test: any failure fails the test.
            var outcomes = g.Value.Select(r => (string?)r.Attribute("outcome") ?? "").ToList();
            var outcome = outcomes.Any(o => o is "Failed" or "Error" or "Timeout" or "Aborted") ? TestOutcomes.Failed
                : outcomes.All(o => o is "NotExecuted" or "Inconclusive" or "Pending") ? TestOutcomes.Skipped : TestOutcomes.Passed;
            var failed = g.Value.FirstOrDefault(r => (string?)r.Attribute("outcome") is "Failed" or "Error" or "Timeout" or "Aborted") ?? g.Value[0];
            var output = failed.Element(ns + "Output");
            foreach (var r in g.Value)
                foreach (var f in r.Descendants(ns + "ResultFile"))
                {
                    var rel = (string?)f.Attribute("path");
                    var candidate = rel is null ? null : Directory.EnumerateFiles(dir, Path.GetFileName(rel), SearchOption.AllDirectories).FirstOrDefault();
                    if (candidate is not null) { var (k, t) = ArtifactTypes.For(candidate); files.Add(new ArtifactFile(candidate, k, t, g.Key)); }
                }
            return new TestResultReport
            {
                Identifier = g.Key, Outcome = outcome,
                DurationMs = (long)g.Value.Sum(r => TimeSpan.TryParse((string?)r.Attribute("duration"), CultureInfo.InvariantCulture, out var d) ? d.TotalMilliseconds : 0),
                ErrorMessage = output?.Element(ns + "ErrorInfo")?.Element(ns + "Message")?.Value,
                StackTrace = output?.Element(ns + "ErrorInfo")?.Element(ns + "StackTrace")?.Value,
                Output = output?.Element(ns + "StdOut")?.Value,
                StartedAt = DateTimeOffset.TryParse((string?)g.Value[0].Attribute("startTime"), CultureInfo.InvariantCulture, out var s) ? s : null,
                CompletedAt = DateTimeOffset.TryParse((string?)g.Value[^1].Attribute("endTime"), CultureInfo.InvariantCulture, out var e) ? e : null,
            };
        }).ToList();
        return (results, files);
    }
}
