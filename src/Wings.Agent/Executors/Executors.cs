using Wings.Agent.Contracts;

namespace Wings.Agent.Executors;

public sealed class JobContext
{
    public required JobAssignment Job { get; init; }
    public required AgentOptions Options { get; init; }
    /// <summary>Extracted package root.</summary>
    public required string PackageDirectory { get; init; }
    /// <summary>Runner output (TRX, JSON report...).</summary>
    public required string OutputDirectory { get; init; }
    /// <summary>Exposed to tests as WINGS_ARTIFACTS_DIR; screenshots and logs saved there are uploaded as evidence.</summary>
    public required string ArtifactsDirectory { get; init; }
    public required IReadOnlyDictionary<string, string> Environment { get; init; }
    public required Action<string, string> Log { get; init; }
}

public sealed record ArtifactFile(string Path, string Kind, string ContentType, string? TestIdentifier);

public sealed record ExecutorResult(IReadOnlyList<TestResultReport> Results, IReadOnlyList<ArtifactFile> Artifacts, int? ExitCode, string? Error = null);

public interface ITestExecutor
{
    /// <summary>Framework codes handled by this executor.</summary>
    IReadOnlyList<string> Frameworks { get; }
    Task<ExecutorResult> RunAsync(JobContext ctx, CancellationToken ct);
}

public static class ArtifactTypes
{
    public static (string Kind, string ContentType) For(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => (ArtifactKinds.Screenshot, "image/png"),
        ".jpg" or ".jpeg" => (ArtifactKinds.Screenshot, "image/jpeg"),
        ".webm" => (ArtifactKinds.Video, "video/webm"),
        ".mp4" => (ArtifactKinds.Video, "video/mp4"),
        ".zip" when path.Contains("trace", StringComparison.OrdinalIgnoreCase) => (ArtifactKinds.Trace, "application/zip"),
        ".trx" or ".xml" or ".json" or ".html" => (ArtifactKinds.Report, "application/octet-stream"),
        ".log" or ".txt" => (ArtifactKinds.Log, "text/plain"),
        _ => (ArtifactKinds.Other, "application/octet-stream"),
    };

    /// <summary>Files tests saved into WINGS_ARTIFACTS_DIR; a file whose name starts with a test's method/title is attached to that test.</summary>
    public static IEnumerable<ArtifactFile> FromDirectory(string dir, IReadOnlyList<string> identifiers, Func<string, string> shortName)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var name = System.IO.Path.GetFileName(file);
            var owner = identifiers.Where(i => name.StartsWith(shortName(i), StringComparison.OrdinalIgnoreCase)).OrderByDescending(i => shortName(i).Length).FirstOrDefault();
            var (kind, type) = For(file);
            yield return new ArtifactFile(file, kind, type, owner);
        }
    }
}
