using System.Globalization;
using System.Xml.Linq;
using Wings.Agent.Contracts;

namespace Wings.Agent.Executors;

/// <summary>Runs pytest node ids (the discovered identifiers are pytest node ids) and reads the JUnit XML report.</summary>
public sealed class PytestExecutor : ITestExecutor
{
    public IReadOnlyList<string> Frameworks { get; } = ["PYTEST", "PLAYWRIGHT_PY"];

    public async Task<ExecutorResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var env = new Dictionary<string, string>(ctx.Environment);
        var requirements = Path.Combine(ctx.PackageDirectory, "requirements.txt");
        if (File.Exists(requirements))
        {
            var pip = await ProcessRunner.RunAsync(ctx.Options.PythonCommand, ["-m", "pip", "install", "--disable-pip-version-check", "-r", requirements], ctx.PackageDirectory, env, ctx.Log, ct);
            if (pip.ExitCode != 0) throw new PreparationException($"pip install failed with exit code {pip.ExitCode}.");
        }
        var report = Path.Combine(ctx.OutputDirectory, "junit.xml");
        var run = await ProcessRunner.RunAsync(ctx.Options.PythonCommand, ["-m", "pytest", .. ctx.Job.TestIdentifiers, $"--junitxml={report}", "-o", "junit_family=xunit2"],
            ctx.PackageDirectory, env, ctx.Log, ct);
        if (!File.Exists(report)) throw new PreparationException($"pytest exited with {run.ExitCode} without a JUnit report.");

        var results = new List<TestResultReport>();
        foreach (var tc in XDocument.Load(report).Descendants("testcase"))
        {
            var name = (string?)tc.Attribute("name") ?? "";
            var cls = (string?)tc.Attribute("classname") ?? "";
            var className = cls.Contains('.') ? cls[(cls.LastIndexOf('.') + 1)..] : "";
            var id = ctx.Job.TestIdentifiers.FirstOrDefault(i => i.EndsWith($"::{className}::{name}") || (i.EndsWith($"::{name}") && i.Split("::").Length == 2));
            if (id is null) continue;
            var failure = tc.Element("failure") ?? tc.Element("error");
            results.Add(new TestResultReport
            {
                Identifier = id,
                Outcome = failure is not null ? TestOutcomes.Failed : tc.Element("skipped") is not null ? TestOutcomes.Skipped : TestOutcomes.Passed,
                DurationMs = (long)(double.TryParse((string?)tc.Attribute("time"), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t * 1000 : 0),
                ErrorMessage = (string?)failure?.Attribute("message"), StackTrace = failure?.Value, Output = tc.Element("system-out")?.Value,
            });
        }
        var artifacts = new List<ArtifactFile> { new(report, ArtifactKinds.Report, "application/xml", null) };
        artifacts.AddRange(ArtifactTypes.FromDirectory(ctx.ArtifactsDirectory, ctx.Job.TestIdentifiers, i => i[(i.LastIndexOf("::") + 2)..]));
        return new ExecutorResult(results, artifacts, run.ExitCode);
    }
}
