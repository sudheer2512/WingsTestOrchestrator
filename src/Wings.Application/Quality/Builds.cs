using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Domain.Features;
using Wings.Domain.Quality;
using Wings.Domain.Testing;

namespace Wings.Application.Quality;

/// <summary>Normalises paths and commit identities from a version-control system into repository-relative form.</summary>
public interface ISourceAdapter
{
    VcsType Type { get; }
    string NormalizePath(string path, SourceRepository? repository);
}

public sealed class GitSourceAdapter : ISourceAdapter
{
    public VcsType Type => VcsType.Git;
    public string NormalizePath(string path, SourceRepository? repository) => path.Replace('\\', '/').TrimStart('.', '/').Trim();
}

/// <summary>
/// TFVC reports server paths ("$/WingsAuto/Main/src/Sales/InvoiceService.cs"). The repository's URL is its server path root
/// ("$/WingsAuto/Main"); without one, the team project and branch (first two segments) are removed.
/// </summary>
public sealed class TfvcSourceAdapter : ISourceAdapter
{
    public VcsType Type => VcsType.Tfvc;

    public string NormalizePath(string path, SourceRepository? repository)
    {
        var p = path.Replace('\\', '/').Trim();
        if (repository?.Url is { } root && root.StartsWith("$/") && p.StartsWith(root.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
            return p[(root.TrimEnd('/').Length + 1)..];
        if (p.StartsWith("$/"))
        {
            var parts = p[2..].Split('/', 3);
            return parts.Length == 3 ? parts[2] : parts[^1];
        }
        return p.TrimStart('/');
    }
}

public sealed record CommitReport(string Id, string? Author, string Message, DateTimeOffset? Timestamp = null, IReadOnlyList<string>? Files = null);

/// <summary>A build reported by CI (or a what-if analysis when not recorded).</summary>
public sealed record BuildReport
{
    public string Product { get; init; } = "";
    public string BuildNumber { get; init; } = "";
    public string? Repository { get; init; }
    public string? Vcs { get; init; }
    public string? Branch { get; init; }
    public string? CommitId { get; init; }
    public string? BuildUrl { get; init; }
    /// <summary>Release (version label) this build is a candidate for.</summary>
    public string? Release { get; init; }
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public IReadOnlyList<CommitReport> Commits { get; init; } = [];
    /// <summary>"affected" runs the automated tests of impacted features; "none" only records the build.</summary>
    public string Trigger { get; init; } = "none";
    public string? EnvironmentCode { get; init; }
    /// <summary>Also treat features that depend on changed features as impacted (up to three levels).</summary>
    public bool IncludeDependents { get; init; } = true;
}

public sealed record ImpactedFeatureDto(Guid FeatureId, long SequenceNumber, string Name, ImpactReason Reason, string Evidence);
public sealed record AffectedTestDto(Guid TestCaseId, string NumberDisplay, string Name, string Priority, bool IsAutomated, IReadOnlyList<long> ViaFeatures, string? LatestOutcome);
public sealed record SuggestedSuiteDto(Guid SuiteId, string NumberDisplay, string Name, int Matching, int Total);

public sealed record BuildSummaryDto(Guid Id, Guid ProductId, string ProductName, string BuildNumber, string? Branch, string? CommitId, string? BuildUrl, Guid? ReleaseId,
    string? ReleaseLabel, string? RepositoryName, int ChangedFileCount, int CommitCount, int ImpactedFeatureCount, Guid? TriggeredRunId, string? TriggeredRunNumber,
    string? GateStatus, DateTimeOffset CreatedAt);

public sealed record BuildImpactDto(BuildSummaryDto? Build, IReadOnlyList<string> ChangedFiles, IReadOnlyList<CommitReport> Commits, IReadOnlyList<ImpactedFeatureDto> Features,
    IReadOnlyList<AffectedTestDto> Tests, IReadOnlyList<SuggestedSuiteDto> Suites, IReadOnlyList<string> UnmappedFiles, IReadOnlyList<string> Warnings, RunSummaryDto? TriggeredRun);

public sealed record FeatureChangeDto(Guid BuildId, string BuildNumber, DateTimeOffset At, string CommitId, string? Author, string Message, ImpactReason Reason);

public sealed record RepositoryDto(Guid Id, string Name, VcsType Type, string? Url, Guid? ProductId, string? DefaultBranch, long Version);
public sealed record RepositoryUpsert(string Name, VcsType Type, string? Url, Guid? ProductId, string? DefaultBranch, long? Version = null);

public sealed record IntegrationKeyDto(Guid Id, string Name, string KeyPrefix, bool IsEnabled, DateTimeOffset? LastUsedAt, DateTimeOffset CreatedAt, string CreatedBy);
public sealed record IntegrationKeyResult(IntegrationKeyDto Key, string Secret);

public interface IBuildService
{
    Task<BuildImpactDto> RecordAsync(BuildReport report, string reportedBy, CancellationToken ct = default);
    Task<BuildImpactDto> AnalyzeAsync(BuildReport report, CancellationToken ct = default);
    Task<IReadOnlyList<BuildSummaryDto>> ListAsync(Guid? productId, int take, CancellationToken ct = default);
    Task<BuildImpactDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FeatureChangeDto>> FeatureChangesAsync(Guid featureId, CancellationToken ct = default);
    /// <summary>Called when a build-triggered run finishes: records the gate status for the build's release.</summary>
    Task OnRunFinishedAsync(Guid runId, CancellationToken ct = default);

    Task<IReadOnlyList<RepositoryDto>> RepositoriesAsync(CancellationToken ct = default);
    Task<RepositoryDto> SaveRepositoryAsync(Guid? id, RepositoryUpsert request, CancellationToken ct = default);
    Task DeleteRepositoryAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<IntegrationKeyDto>> KeysAsync(CancellationToken ct = default);
    Task<IntegrationKeyResult> CreateKeyAsync(string name, CancellationToken ct = default);
    Task SetKeyEnabledAsync(Guid id, bool enabled, CancellationToken ct = default);
    Task DeleteKeyAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns the key's name when the secret is valid and enabled.</summary>
    Task<string?> AuthenticateKeyAsync(string secret, CancellationToken ct = default);
}

public sealed partial class BuildService(IWingsDbContext db, IEnumerable<ISourceAdapter> adapters, IExecutionService runs, IReleaseService releases,
    INotificationPublisher notifications, IProductAccess access, IChangeNotifier notifier, TimeProvider clock) : IBuildService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- impact analysis ----

    private sealed record Analysis(Product Product, SourceRepository? Repository, List<string> Files, List<CommitReport> Commits, List<ImpactedFeatureDto> Features,
        Dictionary<string, List<Guid>> CommitFeatures, List<AffectedTestDto> Tests, List<SuggestedSuiteDto> Suites, List<string> Unmapped, List<string> Warnings);

    private async Task<Analysis> AnalyzeCoreAsync(BuildReport r, CancellationToken ct)
    {
        new Validator().Required(r.Product, "product").Required(r.BuildNumber, "buildNumber", 100)
            .Must(r.ChangedFiles.Count + r.Commits.Count > 0 || r.Trigger == "none", "changedFiles", "Report changed files or commits to analyse impact.")
            .Must(r.Trigger is "none" or "affected", "trigger", "Trigger must be \"none\" or \"affected\".")
            .Must(r.ChangedFiles.Count <= 20_000 && r.Commits.Count <= 2_000, "changedFiles", "Too many changes in one report.")
            .ThrowIfInvalid();
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Code == r.Product, ct) ?? throw new ValidationException("product", $"Product '{r.Product}' does not exist.");
        var repo = r.Repository.Clean() is { } rn ? await db.SourceRepositories.AsNoTracking().FirstOrDefaultAsync(x => x.Name == rn, ct) : null;
        var warnings = new List<string>();
        if (r.Repository.Clean() is { } name && repo is null) warnings.Add($"Repository '{name}' is not registered; paths are used as reported.");
        var vcs = repo?.Type ?? (string.Equals(r.Vcs, "tfvc", StringComparison.OrdinalIgnoreCase) ? VcsType.Tfvc : VcsType.Git);
        var adapter = adapters.First(a => a.Type == vcs);

        var commits = r.Commits.Select(c => c with { Files = c.Files?.Select(f => adapter.NormalizePath(f, repo)).ToList() }).ToList();
        var files = r.ChangedFiles.Select(f => adapter.NormalizePath(f, repo)).Concat(commits.SelectMany(c => c.Files ?? [])).Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var features = await db.Features.AsNoTracking().Where(f => f.ProductId == product.Id).ToListAsync(ct);
        var byId = features.ToDictionary(f => f.Id);
        var mappings = await db.FeatureSourceMappings.AsNoTracking().Where(m => m.IsApproved && features.Select(f => f.Id).Contains(m.FeatureId)).ToListAsync(ct);
        if (repo is not null) mappings = mappings.Where(m => m.Repository.Clean() is null || string.Equals(m.Repository, repo.Name, StringComparison.OrdinalIgnoreCase)).ToList();

        // Rules: approved source mappings, plus the technical identifiers recorded on each feature.
        var rules = mappings.Select(m => (m.FeatureId, m.Kind, m.Value, Source: "mapping")).ToList();
        foreach (var f in features)
        {
            if (f.AssemblyName.Clean() is { } a) rules.Add((f.Id, SourceMappingKind.Assembly, a, "feature technical info"));
            if (f.ClassName.Clean() is { } c) rules.Add((f.Id, SourceMappingKind.Class, c, "feature technical info"));
            if (f.FormOrRoute.Clean() is { } fr && !fr.Contains('/')) rules.Add((f.Id, SourceMappingKind.Form, fr, "feature technical info"));
            foreach (var o in (f.DatabaseObjects ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                rules.Add((f.Id, SourceMappingKind.DatabaseTable, o, "feature technical info"));
        }

        var impacted = new Dictionary<Guid, ImpactedFeatureDto>();
        var unmapped = new List<string>();
        var fileFeatures = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var hits = rules.Where(rule => Matches(file, rule.Kind, rule.Value)).ToList();
            if (hits.Count == 0) { unmapped.Add(file); continue; }
            fileFeatures[file] = hits.Select(h => h.FeatureId).Distinct().ToList();
            foreach (var h in hits)
                if (!impacted.ContainsKey(h.FeatureId))
                    impacted[h.FeatureId] = new ImpactedFeatureDto(h.FeatureId, byId[h.FeatureId].SequenceNumber, byId[h.FeatureId].Name, ImpactReason.SourceMapping,
                        $"{file} matches {h.Kind.ToString().ToLowerInvariant()} \"{h.Value}\" ({h.Source})");
        }

        // Commit messages may name features: "feature 27", "[F27]" or "<PRODUCT>-27".
        var commitFeatures = new Dictionary<string, List<Guid>>();
        foreach (var c in commits)
        {
            var linked = (c.Files ?? []).Where(fileFeatures.ContainsKey).SelectMany(f => fileFeatures[f]).ToHashSet();
            foreach (var seq in FeatureRefs(c.Message, product.Code))
                if (features.FirstOrDefault(f => f.SequenceNumber == seq) is { } f)
                {
                    linked.Add(f.Id);
                    if (!impacted.ContainsKey(f.Id))
                        impacted[f.Id] = new ImpactedFeatureDto(f.Id, f.SequenceNumber, f.Name, ImpactReason.CommitMessage, $"Commit {Short(c.Id)}: \"{FirstLine(c.Message)}\"");
                }
            if (linked.Count > 0) commitFeatures[c.Id] = linked.ToList();
        }

        if (r.IncludeDependents && impacted.Count > 0)
        {
            var edges = await db.FeatureDependencies.AsNoTracking().Where(d => d.Type != DependencyType.Related && features.Select(f => f.Id).Contains(d.FeatureId))
                .Select(d => new { d.FeatureId, d.DependsOnFeatureId }).ToListAsync(ct);
            var frontier = impacted.Keys.ToList();
            for (var depth = 0; depth < 3 && frontier.Count > 0; depth++)
            {
                var next = new List<Guid>();
                foreach (var e in edges.Where(e => frontier.Contains(e.DependsOnFeatureId) && !impacted.ContainsKey(e.FeatureId)))
                {
                    if (!byId.TryGetValue(e.FeatureId, out var f) || impacted.ContainsKey(f.Id)) continue;
                    var on = byId[e.DependsOnFeatureId];
                    impacted[f.Id] = new ImpactedFeatureDto(f.Id, f.SequenceNumber, f.Name, ImpactReason.Dependency, $"Depends on [{on.SequenceNumber}] {on.Name}");
                    next.Add(f.Id);
                }
                frontier = next;
            }
        }

        // ---- affected tests and suites ----
        var impactedIds = impacted.Keys.ToList();
        var mapped = await db.TestCaseFeatureMappings.AsNoTracking().Where(m => impactedIds.Contains(m.FeatureId)).Select(m => new { m.TestCaseId, m.FeatureId }).ToListAsync(ct);
        var caseIds = mapped.Select(m => m.TestCaseId).Distinct().ToList();
        var cases = await db.TestCases.AsNoTracking().Where(t => caseIds.Contains(t.Id) && !t.IsSharedSteps && !t.IsTemplate && t.ReviewStatus != ReviewStatus.Obsolete)
            .Select(t => new { t.Id, t.Number, t.Name, t.Priority, t.AutomationStatus }).ToListAsync(ct);
        var latest = await db.TestExecutions.AsNoTracking().Where(t => t.TestCaseId != null && caseIds.Contains(t.TestCaseId.Value) && !t.IsSuperseded && t.Outcome != "Pending")
            .GroupBy(t => t.TestCaseId!.Value).Select(g => new { g.Key, Outcome = g.OrderByDescending(t => t.CompletedAt).Select(t => t.Outcome).First() }).ToDictionaryAsync(x => x.Key, x => x.Outcome, ct);
        var tests = cases.Select(t => new AffectedTestDto(t.Id, SequenceFormat.Format(t.Number, 5, "TC-"), t.Name, t.Priority, t.AutomationStatus == AutomationStatus.Automated,
                mapped.Where(m => m.TestCaseId == t.Id).Select(m => impacted[m.FeatureId].SequenceNumber).Distinct().Order().ToList(), latest.GetValueOrDefault(t.Id)))
            .OrderBy(t => t.Priority).ThenBy(t => t.NumberDisplay).ToList();
        var testIds = tests.Select(t => t.TestCaseId).ToList();
        var suites = await db.TestSuites.AsNoTracking().Where(s => s.Items.Any(i => testIds.Contains(i.TestCaseId)))
            .Select(s => new { s.Id, s.Number, s.Name, Matching = s.Items.Count(i => testIds.Contains(i.TestCaseId)), Total = s.Items.Count }).ToListAsync(ct);

        if (impacted.Count > 0 && tests.Count == 0) warnings.Add("Impacted features have no test cases: add tests before release.");
        if (files.Count > 0 && unmapped.Count == files.Count) warnings.Add("No changed file matches an approved source mapping. Declare mappings on the features' Technical tab.");
        return new Analysis(product, repo, files, commits, impacted.Values.OrderBy(f => f.Reason).ThenBy(f => f.SequenceNumber).ToList(), commitFeatures, tests,
            suites.OrderByDescending(s => s.Matching).ThenBy(s => s.Total).Select(s => new SuggestedSuiteDto(s.Id, SequenceFormat.Format(s.Number, 5, "TS-"), s.Name, s.Matching, s.Total)).ToList(),
            unmapped, warnings);
    }

    /// <summary>Glob-or-suffix match for files; name-based matches for assemblies, classes, forms and database objects.</summary>
    public static bool Matches(string file, SourceMappingKind kind, string value)
    {
        var v = value.Replace('\\', '/').Trim().TrimStart('/');
        if (v.Length == 0) return false;
        var name = file.Split('/')[^1];
        var stem = name.Split('.')[0];
        var segments = file.Split('/');
        switch (kind)
        {
            case SourceMappingKind.File:
                if (v.Contains('*')) return Glob(v).IsMatch(file);
                return file.Equals(v, StringComparison.OrdinalIgnoreCase) || file.EndsWith("/" + v, StringComparison.OrdinalIgnoreCase)
                       || (v.EndsWith('/') && (file.StartsWith(v, StringComparison.OrdinalIgnoreCase) || file.Contains("/" + v, StringComparison.OrdinalIgnoreCase)));
            case SourceMappingKind.Assembly:
                return segments[..^1].Any(s => s.Equals(v, StringComparison.OrdinalIgnoreCase)) || name.Equals(v + ".csproj", StringComparison.OrdinalIgnoreCase);
            case SourceMappingKind.Class:
                var cls = v.Split('.')[^1];
                return stem.Equals(cls, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                       || (v.Contains('.') && file.Replace('/', '.').Contains(v, StringComparison.OrdinalIgnoreCase));
            case SourceMappingKind.Form:
                return stem.Equals(v, StringComparison.OrdinalIgnoreCase);
            case SourceMappingKind.DatabaseTable or SourceMappingKind.DatabaseView or SourceMappingKind.StoredProcedure:
                var obj = v.Split('.')[^1].Trim('[', ']');
                return name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                       && name[..^4].Split('.').Any(p => p.Trim('[', ']').Equals(obj, StringComparison.OrdinalIgnoreCase));
            default:
                return false;
        }
    }

    private static Regex Glob(string pattern)
        => new("^" + Regex.Escape(pattern).Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$", RegexOptions.IgnoreCase);

    public static IEnumerable<long> FeatureRefs(string message, string productCode)
    {
        foreach (Match m in FeatureRefPattern().Matches(message)) yield return long.Parse(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(message, $@"\b{Regex.Escape(productCode)}-(\d{{1,9}})\b", RegexOptions.IgnoreCase)) yield return long.Parse(m.Groups[1].Value);
    }

    [GeneratedRegex(@"(?:\bfeature[\s:#-]*|\[F)(\d{1,9})\]?", RegexOptions.IgnoreCase)]
    private static partial Regex FeatureRefPattern();

    private static string Short(string id) => id.Length > 10 ? id[..10] : id;
    private static string FirstLine(string s) => s.Split('\n')[0].Trim() is var l && l.Length > 120 ? l[..119] + "…" : s.Split('\n')[0].Trim();

    public async Task<BuildImpactDto> AnalyzeAsync(BuildReport report, CancellationToken ct = default)
    {
        var a = await AnalyzeCoreAsync(report with { Trigger = "none" }, ct);
        await access.EnsureCanViewAsync(a.Product.Id, ct);
        return new BuildImpactDto(null, a.Files, a.Commits, a.Features, a.Tests, a.Suites, a.Unmapped, a.Warnings, null);
    }

    public async Task<BuildImpactDto> RecordAsync(BuildReport r, string reportedBy, CancellationToken ct = default)
    {
        var a = await AnalyzeCoreAsync(r, ct);
        Guid? releaseId = null;
        if (r.Release.Clean() is { } label)
        {
            releaseId = await db.ProductVersions.Where(v => v.ProductId == a.Product.Id && v.VersionLabel == label).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct);
            if (releaseId is null) a.Warnings.Add($"Release '{label}' does not exist for {a.Product.Name}; the build is recorded without it.");
        }
        var build = new ApplicationBuild
        {
            ProductId = a.Product.Id, RepositoryId = a.Repository?.Id, ReleaseId = releaseId, BuildNumber = r.BuildNumber.Trim(), Branch = r.Branch.Clean(), CommitId = r.CommitId.Clean(),
            BuildUrl = r.BuildUrl.Clean(), ChangedFilesJson = JsonSerializer.Serialize(a.Files, Json), CommitsJson = JsonSerializer.Serialize(a.Commits, Json),
            ImpactedFeatureCount = a.Features.Count, CreatedBy = reportedBy,
        };
        db.ApplicationBuilds.Add(build);
        foreach (var f in a.Features) db.BuildFeatureImpacts.Add(new BuildFeatureImpact { BuildId = build.Id, FeatureId = f.FeatureId, Reason = f.Reason, Evidence = Trim(f.Evidence, 1000) });
        foreach (var c in a.Commits)
        {
            var commit = new SourceCommit
            {
                BuildId = build.Id, RepositoryId = a.Repository?.Id, CommitId = Trim(c.Id, 100), Author = c.Author.Clean() is { } au ? Trim(au, 256) : null, Message = Trim(c.Message, 4000),
                CommittedAt = c.Timestamp, FilesJson = c.Files is { Count: > 0 } ? JsonSerializer.Serialize(c.Files, Json) : null,
            };
            db.SourceCommits.Add(commit);
            foreach (var fid in a.CommitFeatures.GetValueOrDefault(c.Id) ?? [])
                db.CommitFeatureLinks.Add(new CommitFeatureLink { CommitId = commit.Id, FeatureId = fid, Reason = a.Features.First(x => x.FeatureId == fid).Reason });
        }
        // Directly changed features become "modified" in the candidate release's scope.
        if (releaseId is { } rid)
        {
            var existing = await db.ReleaseFeatureChanges.Where(c => c.ReleaseId == rid).Select(c => new { c.FeatureId, c.Kind }).ToListAsync(ct);
            foreach (var f in a.Features.Where(f => f.Reason != ImpactReason.Dependency && !existing.Any(e => e.FeatureId == f.FeatureId && e.Kind == FeatureChangeKind.Modified)))
                db.ReleaseFeatureChanges.Add(new ReleaseFeatureChange { ReleaseId = rid, FeatureId = f.FeatureId, Kind = FeatureChangeKind.Modified, Source = FeatureChangeSource.Build, Notes = $"Build {build.BuildNumber}" });
        }
        await db.SaveChangesAsync(ct);

        RunSummaryDto? run = null;
        if (r.Trigger == "affected")
        {
            var automated = a.Tests.Where(t => t.IsAutomated).Select(t => t.TestCaseId).ToList();
            if (automated.Count == 0) a.Warnings.Add("No automated tests cover the impacted features; nothing was started.");
            else
            {
                Guid? envId = null;
                if (r.EnvironmentCode.Clean() is { } code)
                    envId = await db.TestEnvironments.Where(e => e.Code == code).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct) ?? throw new ValidationException("environmentCode", $"Environment '{code}' does not exist.");
                try
                {
                    run = await runs.StartAsync(new StartRunRequest
                    {
                        Name = $"Build {build.BuildNumber}: tests affected by {a.Features.Count} changed feature(s)", TestCaseIds = automated, EnvironmentId = envId, BuildVersion = build.BuildNumber,
                    }, new RunOrigin($"build:{build.BuildNumber} ({reportedBy})", CheckPermissions: false), ct);
                    await db.ExecutionRuns.Where(x => x.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReleaseId, releaseId), ct);
                    build.TriggeredRunId = run.Id;
                    build.GateStatus = releaseId is null ? null : "Pending";
                }
                catch (AppException ex) when (ex is ValidationException or ConflictException)
                {
                    a.Warnings.Add($"Affected tests were not started: {ex.Message}");
                }
            }
        }
        if (releaseId is { } relId && build.TriggeredRunId is null) build.GateStatus = await releases.EvaluateAndNotifyAsync(relId, $"build {build.BuildNumber}", ct);
        await db.SaveChangesAsync(ct);

        await notifications.PublishAsync(new NotificationMessage(NotificationEvents.BuildReceived, NotificationSeverity.Info,
            $"{a.Product.Name} build {build.BuildNumber}: {a.Features.Count} impacted feature(s), {a.Tests.Count} affected test(s)",
            string.Join("\n", a.Features.Take(15).Select(f => $"- [{f.SequenceNumber}] {f.Name} ({f.Reason})")) + (run is null ? "" : $"\nStarted {run.NumberDisplay}."),
            $"/impact/builds/{build.Id}", a.Product.Id, new Dictionary<string, string> { ["build"] = build.BuildNumber }), ct);
        await notifier.NotifyAsync("buildsChanged", new { buildId = build.Id }, ct);
        var dto = await GetAsync(build.Id, ct);
        return dto with { Warnings = a.Warnings, UnmappedFiles = a.Unmapped, TriggeredRun = run ?? dto.TriggeredRun };
    }

    public async Task OnRunFinishedAsync(Guid runId, CancellationToken ct = default)
    {
        var build = await db.ApplicationBuilds.FirstOrDefaultAsync(b => b.TriggeredRunId == runId, ct);
        if (build?.ReleaseId is not { } rid) return;
        build.GateStatus = await releases.EvaluateAndNotifyAsync(rid, $"build {build.BuildNumber} tests", ct);
        await db.SaveChangesAsync(ct);
    }

    // ---- queries ----

    public async Task<IReadOnlyList<BuildSummaryDto>> ListAsync(Guid? productId, int take, CancellationToken ct = default)
    {
        var q = db.ApplicationBuilds.AsNoTracking();
        if (productId is { } p) q = q.Where(b => b.ProductId == p);
        if (await access.VisibleProductIdsAsync(ct) is { } visible) q = q.Where(b => visible.Contains(b.ProductId));
        return await Summaries(q.OrderByDescending(b => b.CreatedAt).Take(Math.Clamp(take, 1, 500)), ct);
    }

    private async Task<List<BuildSummaryDto>> Summaries(IQueryable<ApplicationBuild> q, CancellationToken ct)
    {
        var rows = await q.Select(b => new
        {
            b,
            Product = db.Products.Where(p => p.Id == b.ProductId).Select(p => p.Name).FirstOrDefault(),
            Release = db.ProductVersions.Where(v => v.Id == b.ReleaseId).Select(v => v.VersionLabel).FirstOrDefault(),
            Repo = db.SourceRepositories.Where(r => r.Id == b.RepositoryId).Select(r => r.Name).FirstOrDefault(),
            Run = db.ExecutionRuns.Where(r => r.Id == b.TriggeredRunId).Select(r => (long?)r.Number).FirstOrDefault(),
            Commits = db.SourceCommits.Count(c => c.BuildId == b.Id),
        }).ToListAsync(ct);
        return rows.Select(x => new BuildSummaryDto(x.b.Id, x.b.ProductId, x.Product ?? "", x.b.BuildNumber, x.b.Branch, x.b.CommitId, x.b.BuildUrl, x.b.ReleaseId, x.Release, x.Repo,
            JsonSerializer.Deserialize<List<string>>(x.b.ChangedFilesJson ?? "[]", Json)!.Count, x.Commits, x.b.ImpactedFeatureCount, x.b.TriggeredRunId,
            x.Run is { } n ? ExecutionService.Format(n) : null, x.b.GateStatus, x.b.CreatedAt)).ToList();
    }

    public async Task<BuildImpactDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var b = await db.ApplicationBuilds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Build", id);
        await access.EnsureCanViewAsync(b.ProductId, ct);
        var summary = (await Summaries(db.ApplicationBuilds.Where(x => x.Id == id), ct)).Single();
        var impacts = await (from i in db.BuildFeatureImpacts.Where(i => i.BuildId == id)
                             join f in db.Features on i.FeatureId equals f.Id
                             select new ImpactedFeatureDto(f.Id, f.SequenceNumber, f.Name, i.Reason, i.Evidence)).ToListAsync(ct);
        var files = JsonSerializer.Deserialize<List<string>>(b.ChangedFilesJson ?? "[]", Json)!;
        var commits = JsonSerializer.Deserialize<List<CommitReport>>(b.CommitsJson ?? "[]", Json)!;

        // Affected tests reflect current mappings and latest results.
        var ids = impacts.Select(i => i.FeatureId).ToList();
        var mapped = await db.TestCaseFeatureMappings.AsNoTracking().Where(m => ids.Contains(m.FeatureId)).Select(m => new { m.TestCaseId, m.FeatureId }).ToListAsync(ct);
        var caseIds = mapped.Select(m => m.TestCaseId).Distinct().ToList();
        var cases = await db.TestCases.AsNoTracking().Where(t => caseIds.Contains(t.Id) && !t.IsSharedSteps && !t.IsTemplate).Select(t => new { t.Id, t.Number, t.Name, t.Priority, t.AutomationStatus }).ToListAsync(ct);
        var inRun = b.TriggeredRunId is { } rid
            ? await db.TestExecutions.AsNoTracking().Where(t => t.RunId == rid && !t.IsSuperseded && t.TestCaseId != null).GroupBy(t => t.TestCaseId!.Value).Select(g => new { g.Key, Outcome = g.Select(t => t.Outcome).First() }).ToDictionaryAsync(x => x.Key, x => x.Outcome, ct)
            : [];
        var seq = impacts.ToDictionary(i => i.FeatureId, i => i.SequenceNumber);
        var tests = cases.Select(t => new AffectedTestDto(t.Id, SequenceFormat.Format(t.Number, 5, "TC-"), t.Name, t.Priority, t.AutomationStatus == AutomationStatus.Automated,
            mapped.Where(m => m.TestCaseId == t.Id).Select(m => seq[m.FeatureId]).Distinct().Order().ToList(), inRun.GetValueOrDefault(t.Id))).OrderBy(t => t.Priority).ThenBy(t => t.NumberDisplay).ToList();
        var testIds = tests.Select(t => t.TestCaseId).ToList();
        var suites = await db.TestSuites.AsNoTracking().Where(s => s.Items.Any(i => testIds.Contains(i.TestCaseId)))
            .Select(s => new SuggestedSuiteDto(s.Id, "", s.Name, s.Items.Count(i => testIds.Contains(i.TestCaseId)), s.Items.Count)).ToListAsync(ct);
        var suiteNumbers = await db.TestSuites.AsNoTracking().Where(s => suites.Select(x => x.SuiteId).Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Number, ct);
        RunSummaryDto? run = b.TriggeredRunId is { } r ? (await runs.GetAsync(r, ct)).Run : null;
        return new BuildImpactDto(summary, files, commits, impacts.OrderBy(f => f.Reason).ThenBy(f => f.SequenceNumber).ToList(), tests,
            suites.Select(s => s with { NumberDisplay = SequenceFormat.Format(suiteNumbers[s.SuiteId], 5, "TS-") }).OrderByDescending(s => s.Matching).ToList(), [], [], run);
    }

    public async Task<IReadOnlyList<FeatureChangeDto>> FeatureChangesAsync(Guid featureId, CancellationToken ct = default)
        => await (from l in db.CommitFeatureLinks.Where(l => l.FeatureId == featureId)
                  join c in db.SourceCommits on l.CommitId equals c.Id
                  join b in db.ApplicationBuilds on c.BuildId equals b.Id
                  orderby b.CreatedAt descending
                  select new FeatureChangeDto(b.Id, b.BuildNumber, b.CreatedAt, c.CommitId, c.Author, c.Message, l.Reason)).Take(100).ToListAsync(ct);

    // ---- repositories ----

    public async Task<IReadOnlyList<RepositoryDto>> RepositoriesAsync(CancellationToken ct = default)
        => await db.SourceRepositories.AsNoTracking().OrderBy(r => r.Name).Select(r => new RepositoryDto(r.Id, r.Name, r.Type, r.Url, r.ProductId, r.DefaultBranch, r.Version)).ToListAsync(ct);

    public async Task<RepositoryDto> SaveRepositoryAsync(Guid? id, RepositoryUpsert r, CancellationToken ct = default)
    {
        new Validator().Required(r.Name, "name", 200).MaxLength(r.Url, "url", 800)
            .Must(r.Type != VcsType.Tfvc || r.Url.Clean() is null || r.Url!.StartsWith("$/"), "url", "For TFVC enter the server path root, e.g. $/WingsAuto/Main.")
            .ThrowIfInvalid();
        if (await db.SourceRepositories.AnyAsync(x => x.Name == r.Name.Trim() && x.Id != id, ct)) throw new ConflictException($"A repository named '{r.Name}' already exists.");
        SourceRepository repo;
        if (id is { } existing)
        {
            repo = await db.SourceRepositories.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Repository", existing);
            if (r.Version is { } v) db.SetOriginalVersion(repo, v);
        }
        else db.SourceRepositories.Add(repo = new SourceRepository());
        repo.Name = r.Name.Trim();
        repo.Type = r.Type;
        repo.Url = r.Url.Clean();
        repo.ProductId = r.ProductId;
        repo.DefaultBranch = r.DefaultBranch.Clean();
        await db.SaveChangesAsync(ct);
        return new RepositoryDto(repo.Id, repo.Name, repo.Type, repo.Url, repo.ProductId, repo.DefaultBranch, repo.Version);
    }

    public async Task DeleteRepositoryAsync(Guid id, CancellationToken ct = default)
    {
        var repo = await db.SourceRepositories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Repository", id);
        db.SourceRepositories.Remove(repo);
        await db.SaveChangesAsync(ct);
    }

    // ---- integration keys ----

    public async Task<IReadOnlyList<IntegrationKeyDto>> KeysAsync(CancellationToken ct = default)
        => await db.IntegrationKeys.AsNoTracking().OrderBy(k => k.Name).Select(k => new IntegrationKeyDto(k.Id, k.Name, k.KeyPrefix, k.IsEnabled, k.LastUsedAt, k.CreatedAt, k.CreatedBy)).ToListAsync(ct);

    public async Task<IntegrationKeyResult> CreateKeyAsync(string name, CancellationToken ct = default)
    {
        new Validator().Required(name, "name", 100).ThrowIfInvalid();
        var secret = "wik_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var key = new IntegrationKey { Name = name.Trim(), KeyHash = HashKey(secret), KeyPrefix = secret[..12] };
        db.IntegrationKeys.Add(key);
        await db.SaveChangesAsync(ct);
        return new IntegrationKeyResult(new IntegrationKeyDto(key.Id, key.Name, key.KeyPrefix, key.IsEnabled, null, key.CreatedAt, key.CreatedBy), secret);
    }

    public async Task SetKeyEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var key = await db.IntegrationKeys.FirstOrDefaultAsync(k => k.Id == id, ct) ?? throw new NotFoundException("Integration key", id);
        key.IsEnabled = enabled;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteKeyAsync(Guid id, CancellationToken ct = default)
    {
        var key = await db.IntegrationKeys.FirstOrDefaultAsync(k => k.Id == id, ct) ?? throw new NotFoundException("Integration key", id);
        db.IntegrationKeys.Remove(key);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> AuthenticateKeyAsync(string secret, CancellationToken ct = default)
    {
        if (!secret.StartsWith("wik_")) return null;
        var hash = HashKey(secret);
        var key = await db.IntegrationKeys.AsNoTracking().FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsEnabled, ct);
        if (key is null) return null;
        await db.IntegrationKeys.Where(k => k.Id == key.Id).ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, clock.GetUtcNow()), ct);
        return key.Name;
    }

    private static string HashKey(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
