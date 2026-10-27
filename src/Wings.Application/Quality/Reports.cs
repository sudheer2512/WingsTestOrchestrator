using System.Text.Json;
using Cronos;
using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Application.Security;
using Wings.Domain.Execution;
using Wings.Domain.Quality;
using Wings.Domain.Testing;

namespace Wings.Application.Quality;

/// <summary>Parameters a report may use; each report declares which ones apply.</summary>
public sealed record ReportParameters
{
    public Guid? ProductId { get; init; }
    public Guid? FeatureId { get; init; }
    public Guid? ReleaseId { get; init; }
    public Guid? CompareReleaseId { get; init; }
    public long? FromSequence { get; init; }
    public int Days { get; init; } = 30;
}

public sealed record ReportColumn(string Key, string Label);
public sealed record ReportDefinitionDto(string Key, string Title, string Category, string Description, string Definition, IReadOnlyList<string> Parameters,
    IReadOnlyList<ReportColumn> Columns, string? RequiredPermission);
public sealed record ReportResultDto(string Key, string Title, string Description, string Definition, IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows, IReadOnlyList<(string Label, string Value)> Summary, DateTimeOffset GeneratedAt, string ParameterText);

public sealed record SavedReportDto(Guid Id, string Name, string ReportKey, ReportParameters Parameters, IReadOnlyList<string>? Columns, string Owner, bool IsShared,
    string? ScheduleCron, Guid? DeliveryChannelId, string Format, DateTimeOffset? NextRunAt, DateTimeOffset? LastRunAt, bool IsMine, long Version);
public sealed record SavedReportUpsert(string Name, string ReportKey, ReportParameters Parameters, IReadOnlyList<string>? Columns, bool IsShared, string? ScheduleCron,
    Guid? DeliveryChannelId, string Format = "xlsx", long? Version = null);

public sealed record ReportFile(byte[] Content, string ContentType, string FileName);

public interface IReportService
{
    Task<IReadOnlyList<ReportDefinitionDto>> CatalogAsync(CancellationToken ct = default);
    Task<ReportResultDto> RunAsync(string key, ReportParameters parameters, IReadOnlyList<string>? columns, CancellationToken ct = default);
    Task<ReportFile> ExportAsync(string key, ReportParameters parameters, IReadOnlyList<string>? columns, string format, CancellationToken ct = default);

    Task<IReadOnlyList<SavedReportDto>> SavedAsync(CancellationToken ct = default);
    Task<SavedReportDto> SaveAsync(Guid? id, SavedReportUpsert request, CancellationToken ct = default);
    Task DeleteSavedAsync(Guid id, CancellationToken ct = default);
    /// <summary>Renders due scheduled reports and emails them through their delivery channel.</summary>
    Task<int> DeliverDueAsync(CancellationToken ct = default);
}

public sealed class ReportService(IWingsDbContext db, ICurrentUser user, IPermissionService permissions, TimeProvider clock, IEnumerable<ITabularCodec> codecs,
    IPdfRenderer pdf, IFileStore files, IJobQueue jobs, IReleaseService releases, IDefectService defects, IProductAccess access) : IReportService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Def(string Key, string Title, string Category, string Description, string Definition, string[] Parameters, ReportColumn[] Columns, string? Permission = null);

    private static ReportColumn[] Cols(params string[] spec) => spec.Select(s => s.Split('|') is var p ? new ReportColumn(p[0], p.Length > 1 ? p[1] : p[0]) : null!).ToArray();

    private static readonly Def[] Defs =
    [
        new("feature-inventory", "Feature inventory", "Features", "Every feature of a product with its type, status, release and test coverage.", Coverage.Definitions["featureCoverage"],
            ["productId"], Cols("seq|Seq", "path|Path", "name|Feature", "type|Type", "status|Status", "app|Application", "release|Introduced in", "tests|Test cases", "automated|Automated", "coverage|Coverage", "latest|Latest result")),
        new("features-by-sequence", "Features by creation sequence", "Features", "Features in the order they were created; optionally from a sequence number onwards (new-feature regression scope).",
            "Creation sequence numbers are immutable and never reused, so the order reflects when each feature was created.",
            ["productId", "fromSequence"], Cols("seq|Seq", "name|Feature", "parent|Parent", "type|Type", "status|Status", "created|Created", "createdBy|Created by", "tests|Test cases")),
        new("features-between-releases", "Features added between releases", "Features", "Features introduced in releases after one release up to and including another.",
            "A feature belongs to the release recorded as \"introduced in\" when it was created.",
            ["productId", "releaseId", "compareReleaseId"], Cols("release|Release", "seq|Seq", "name|Feature", "status|Status", "tests|Test cases", "automated|Automated", "latest|Latest result")),
        new("untested-features", "Untested features", "Coverage", "Features with no test cases, and features whose test cases have never been executed.", Coverage.Definitions["features"],
            ["productId"], Cols("seq|Seq", "path|Path", "name|Feature", "status|Status", "reason|Reason", "tests|Test cases")),
        new("automation-coverage", "Automation coverage", "Coverage", "Per module: test cases by automation status, with feature, requirement and automation coverage.",
            string.Join(" ", Coverage.Definitions["featureCoverage"], Coverage.Definitions["requirementCoverage"], Coverage.Definitions["automation"], Coverage.Definitions["codeCoverage"]),
            ["productId"], Cols("module|Module", "features|Features", "withTests|With tests", "featureCoverage|Feature coverage %", "requirements|Requirements", "requirementsCovered|Covered", "requirementCoverage|Requirement coverage %", "tests|Test cases", "automated|Automated", "planned|Planned", "manual|Manual", "notAutomatable|Not automatable", "automation|Automation %")),
        new("execution-summary", "Test execution summary", "Execution", "Runs in the period with their outcomes and duration.", Coverage.Definitions["passRate"],
            ["days"], Cols("run|Run", "name|Name", "status|Status", "environment|Environment", "build|Build", "total|Tests", "passed|Passed", "failed|Failed", "blocked|Blocked", "infra|Infra errors", "passRate|Pass rate %", "duration|Duration (min)", "triggeredBy|Triggered by", "completed|Completed")),
        new("feature-history", "Test history by feature", "Execution", "Every execution of the tests linked to a feature (and its sub-features) in the period.", Coverage.Definitions["latest"],
            ["featureId", "days"], Cols("completed|Completed", "run|Run", "test|Test", "testCase|Test case", "feature|Feature", "outcome|Outcome", "duration|Duration (s)", "agent|Agent", "error|Error")),
        new("failed-tests", "Failed tests", "Execution", "Failed executions in the period with their error and linked defect.", "Failed = the product failed the test's assertion. Infrastructure errors are listed separately in the execution summary.",
            ["productId", "days"], Cols("completed|Completed", "run|Run", "test|Test", "testCase|Test case", "feature|Feature", "error|Error", "defect|Defect")),
        new("flaky-tests", "Flaky tests", "Execution", "Tests that both passed and failed in the period, ordered by how often their outcome flipped.",
            "Flips = number of consecutive executions with a different outcome (Passed ↔ Failed). Fail rate = failed ÷ (passed + failed).",
            ["productId", "days"], Cols("test|Test", "testCase|Test case", "executions|Executions", "failures|Failures", "failRate|Fail rate %", "flips|Flips", "lastFailed|Last failed", "defect|Open defect")),
        new("defects-by-module", "Defects by product and module", "Defects", "Defects per module by severity and status.", "Unresolved = Open, Assigned, In progress, Fixed, Ready for retest or Reopened.",
            ["productId"], Cols("product|Product", "module|Module", "total|Total", "unresolved|Unresolved", "critical|Critical open", "major|Major open", "minor|Minor open", "trivial|Trivial open", "closed|Closed/verified", "rejected|Rejected/duplicate", "deferred|Deferred")),
        new("release-comparison", "Release comparison", "Releases", "Two releases side by side: scope, testing completion, results, defects and gates.", "Figures use each release's own testing window; see the release workspace for gate definitions.",
            ["releaseId", "compareReleaseId"], Cols("metric|Metric", "a|Release A", "b|Release B")),
        new("tester-productivity", "Tester productivity", "People", "Per person in the period: manual results recorded, runs started, test cases created, defects raised and verified.",
            "Counts come from execution records and the audit trail.", ["days"],
            Cols("user|User", "manualResults|Manual results", "runsStarted|Runs started", "testCasesCreated|Test cases created", "testCasesApproved|Test cases approved", "defectsRaised|Defects raised", "defectsVerified|Defects verified"), Permissions.AuditView),
        new("agent-utilization", "Agent utilization", "Execution", "Per agent in the period: jobs, busy hours and utilization of its slots.", "Utilization = busy job hours ÷ (slots × hours in the period).",
            ["days"], Cols("agent|Agent", "kind|Kind", "pool|Pool", "slots|Slots", "jobs|Jobs", "busyHours|Busy hours", "utilization|Utilization %", "infraErrors|Infrastructure errors", "avgJob|Avg job (s)")),
        new("execution-duration", "Average execution duration", "Execution", "Average and maximum duration per test in the period.", "Duration of a test = reported test duration of its completed executions.",
            ["productId", "days"], Cols("test|Test", "testCase|Test case", "framework|Framework", "executions|Executions", "avg|Average (s)", "max|Max (s)", "total|Total (min)")),
        new("quality-trends", "Historical quality trends", "Trends", "Weekly executed tests, pass rate and defects opened/closed.", Coverage.Definitions["passRate"],
            ["productId", "days"], Cols("week|Week starting", "executed|Executed", "passed|Passed", "failed|Failed", "blocked|Blocked", "passRate|Pass rate %", "defectsOpened|Defects opened", "defectsClosed|Defects closed/verified")),
        new("critical-feature-coverage", "Business-critical feature coverage", "Coverage", "Features with high-priority requirements or P1 test cases, with requirement coverage and latest results.",
            "Business-critical = the feature has a requirement of priority HIGH or CRITICAL, or a P1 test case. " + Coverage.Definitions["requirementCoverage"],
            ["productId"], Cols("seq|Seq", "name|Feature", "criticalRequirements|High/critical requirements", "requirementsCovered|Covered by tests", "tests|Test cases", "p1|P1 tests", "passed|Latest passed", "failed|Latest failed", "notRun|Not executed", "status|Status")),
        new("changed-code-coverage", "Changed-code testing coverage", "Coverage", "Features impacted by builds in the period and whether they were tested after the change.",
            "Tested after change = at least one linked test case passed after the build was recorded. Coverage % = tested ÷ impacted features (code coverage itself is not measured).",
            ["productId", "days"], Cols("build|Build", "date|Date", "seq|Seq", "feature|Feature", "reason|Reason", "tests|Test cases", "testedAfter|Passed after build", "status|Status")),
    ];

    private async Task<IReadOnlySet<string>> PermsAsync(CancellationToken ct) => user.UserId is { } id ? await permissions.GetPermissionsAsync(id, ct) : new HashSet<string>();

    public async Task<IReadOnlyList<ReportDefinitionDto>> CatalogAsync(CancellationToken ct = default)
    {
        var perms = await PermsAsync(ct);
        return Defs.Where(d => d.Permission is null || perms.Contains(d.Permission))
            .Select(d => new ReportDefinitionDto(d.Key, d.Title, d.Category, d.Description, d.Definition, d.Parameters, d.Columns, d.Permission)).ToList();
    }

    private static readonly HashSet<string> NeedsProduct = ["feature-inventory", "features-by-sequence", "features-between-releases", "untested-features", "automation-coverage", "critical-feature-coverage"];

    public Task<ReportResultDto> RunAsync(string key, ReportParameters p, IReadOnlyList<string>? columns, CancellationToken ct = default) => RunCoreAsync(key, p, columns, true, ct);

    /// <summary>Scheduled deliveries skip the permission check: it was made against the owner when the schedule was saved.</summary>
    private async Task<ReportResultDto> RunCoreAsync(string key, ReportParameters p, IReadOnlyList<string>? columns, bool checkPermission, CancellationToken ct)
    {
        var def = Defs.FirstOrDefault(d => d.Key == key) ?? throw new NotFoundException("Report", key);
        if (checkPermission && def.Permission is { } perm && !(await PermsAsync(ct)).Contains(perm)) throw new ForbiddenException($"This report needs the {perm} permission.");
        new Validator()
            .Must(p.Days is >= 1 and <= 730, "days", "Use a period of 1–730 days.")
            .Must(!NeedsProduct.Contains(key) || p.ProductId is not null, "productId", "Choose a product.")
            .Must(key != "feature-history" || p.FeatureId is not null, "featureId", "Choose a feature.")
            .Must(key is not ("features-between-releases" or "release-comparison") || p.ReleaseId is not null, "releaseId", "Choose a release.")
            .Must(key != "release-comparison" || p.CompareReleaseId is not null, "compareReleaseId", "Choose the release to compare with.")
            .ThrowIfInvalid();
        if (p.ProductId is { } pid) await access.EnsureCanViewAsync(pid, ct);

        var since = clock.GetUtcNow().AddDays(-p.Days);
        var summary = new List<(string, string)>();
        var rows = key switch
        {
            "feature-inventory" => await FeatureInventory(p, ct),
            "features-by-sequence" => await FeaturesBySequence(p, ct),
            "features-between-releases" => await FeaturesBetween(p, ct),
            "untested-features" => await Untested(p, summary, ct),
            "automation-coverage" => await AutomationCoverage(p, summary, ct),
            "execution-summary" => await ExecutionSummary(since, summary, ct),
            "feature-history" => await FeatureHistory(p, since, ct),
            "failed-tests" => await FailedTests(p, since, ct),
            "flaky-tests" => await Flaky(p, ct),
            "defects-by-module" => await DefectsByModule(p, ct),
            "release-comparison" => await ReleaseComparison(p, ct),
            "tester-productivity" => await TesterProductivity(since, ct),
            "agent-utilization" => await AgentUtilization(p, since, ct),
            "execution-duration" => await Durations(p, since, ct),
            "quality-trends" => await Trends(p, since, ct),
            "critical-feature-coverage" => await CriticalCoverage(p, summary, ct),
            "changed-code-coverage" => await ChangedCode(p, since, summary, ct),
            _ => throw new NotFoundException("Report", key),
        };
        summary.Insert(0, ("Rows", rows.Count.ToString()));

        var selected = columns is { Count: > 0 } ? def.Columns.Where(c => columns.Contains(c.Key)).ToList() : def.Columns.ToList();
        if (selected.Count == 0) selected = def.Columns.ToList();
        var index = selected.Select(c => Array.FindIndex(def.Columns, x => x.Key == c.Key)).ToArray();
        var projected = rows.Select(r => (IReadOnlyList<string?>)index.Select(i => i < r.Length ? r[i] : null).ToList()).ToList();
        return new ReportResultDto(def.Key, def.Title, def.Description, def.Definition, selected, projected, summary, clock.GetUtcNow(), await DescribeAsync(def, p, ct));
    }

    public Task<ReportFile> ExportAsync(string key, ReportParameters p, IReadOnlyList<string>? columns, string format, CancellationToken ct = default)
        => ExportCoreAsync(key, p, columns, format, true, ct);

    private async Task<ReportFile> ExportCoreAsync(string key, ReportParameters p, IReadOnlyList<string>? columns, string format, bool checkPermission, CancellationToken ct)
    {
        var result = await RunCoreAsync(key, p, columns, checkPermission, ct);
        var stamp = result.GeneratedAt.ToString("yyyyMMdd-HHmm");
        if (format == "pdf")
        {
            var doc = new PdfDocumentModel { Title = result.Title, Subtitle = $"{result.ParameterText} · generated {result.GeneratedAt:yyyy-MM-dd HH:mm} UTC by {user.UserName}", Footer = "Wings Test Orchestrator", Landscape = result.Columns.Count > 6 };
            doc.Fields(result.Summary.Select(s => (s.Label, (string?)s.Value))).Table(result.Columns.Select(c => c.Label).ToList(), result.Rows).Heading("Definition").Paragraph(result.Definition);
            return new ReportFile(pdf.Render(doc), "application/pdf", $"{key}-{stamp}.pdf");
        }
        var codec = codecs.FirstOrDefault(c => c.Format == format) ?? throw new ValidationException("format", "Use csv, xlsx or pdf.");
        return new ReportFile(codec.Write(result.Columns.Select(c => c.Label).ToList(), result.Rows, result.Title.Length > 31 ? result.Title[..31] : result.Title), codec.ContentType, $"{key}-{stamp}.{format}");
    }

    private async Task<string> DescribeAsync(Def def, ReportParameters p, CancellationToken ct)
    {
        var parts = new List<string>();
        if (def.Parameters.Contains("productId")) parts.Add(p.ProductId is { } pid ? await db.Products.Where(x => x.Id == pid).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "" : "All products");
        if (def.Parameters.Contains("featureId") && p.FeatureId is { } fid) parts.Add(await db.Features.Where(x => x.Id == fid).Select(x => $"[{x.SequenceNumber}] {x.Name}").FirstOrDefaultAsync(ct) ?? "");
        if (def.Parameters.Contains("releaseId") && p.ReleaseId is { } rid) parts.Add(await db.ProductVersions.Where(x => x.Id == rid).Select(x => x.VersionLabel).FirstOrDefaultAsync(ct) ?? "");
        if (def.Parameters.Contains("compareReleaseId") && p.CompareReleaseId is { } cid) parts.Add("vs " + await db.ProductVersions.Where(x => x.Id == cid).Select(x => x.VersionLabel).FirstOrDefaultAsync(ct));
        if (def.Parameters.Contains("fromSequence") && p.FromSequence is { } s) parts.Add($"from sequence {s}");
        if (def.Parameters.Contains("days")) parts.Add($"last {p.Days} days");
        return string.Join(" · ", parts.Where(x => x.Length > 0));
    }

    // ---- report builders (each row matches its definition's columns) ----

    private static string N(int v) => v.ToString();
    private static string? Pct(double? v) => v?.ToString("0.#");
    private static string? D(DateTimeOffset? v) => v?.ToString("yyyy-MM-dd HH:mm");
    private static string Tc(long n) => SequenceFormat.Format(n, 5, "TC-");

    private async Task<(List<Coverage.FeatureRow> All, List<Coverage.CaseRow> Cases, Dictionary<Guid, Coverage.ResultRow> Latest, ILookup<Guid, Coverage.CaseRow> ByFeature)> ProductDataAsync(Guid? productId, CancellationToken ct)
    {
        var all = await Coverage.FeaturesAsync(db, productId, ct);
        var cases = await Coverage.CasesAsync(db, productId, ct);
        var latest = await Coverage.LatestAsync(db, cases.Select(c => c.Id).ToHashSet(), null, ct);
        return (all, cases, latest, cases.SelectMany(c => c.FeatureIds.Select(f => (f, c))).ToLookup(x => x.f, x => x.c));
    }

    private static string? LatestOf(IEnumerable<Coverage.CaseRow> cases, Dictionary<Guid, Coverage.ResultRow> latest)
    {
        var results = cases.Select(c => latest.GetValueOrDefault(c.Id)?.Outcome).Where(o => o is not null).ToList();
        return results.Count == 0 ? null : results.Any(o => o is "Failed" or "Blocked") ? "Failed" : results.Any(o => o == "Passed") ? "Passed" : results[0];
    }

    private static string PathOf(Coverage.FeatureRow f, Dictionary<Guid, Coverage.FeatureRow> byId)
    {
        var names = new List<string>();
        for (var cur = f.ParentId; cur is { } id && byId.TryGetValue(id, out var parent); cur = parent.ParentId) names.Insert(0, parent.Name);
        return string.Join(" › ", names);
    }

    private async Task<List<string?[]>> FeatureInventory(ReportParameters p, CancellationToken ct)
    {
        var (all, _, latest, byFeature) = await ProductDataAsync(p.ProductId, ct);
        var byId = all.ToDictionary(f => f.Id);
        var releasesById = await db.ProductVersions.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.VersionLabel, ct);
        var apps = await db.Features.AsNoTracking().Where(f => f.ProductId == p.ProductId).ToDictionaryAsync(f => f.Id, f => f.ApplicationType.ToString(), ct);
        return all.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f =>
        {
            var c = byFeature[f.Id].DistinctBy(x => x.Id).ToList();
            var auto = c.Count(x => x.Automation == AutomationStatus.Automated);
            return new string?[] { f.SequenceNumber.ToString(), PathOf(f, byId), f.Name, f.Type, f.Status, apps.GetValueOrDefault(f.Id), f.IntroducedInVersionId is { } r ? releasesById.GetValueOrDefault(r) : null,
                N(c.Count), N(auto), c.Count == 0 ? "NONE" : auto == 0 ? "MANUAL" : auto < c.Count ? "PARTIAL" : "AUTOMATED", LatestOf(c, latest) };
        }).ToList();
    }

    private async Task<List<string?[]>> FeaturesBySequence(ReportParameters p, CancellationToken ct)
    {
        var rows = await db.Features.AsNoTracking().Where(f => f.ProductId == p.ProductId && (p.FromSequence == null || f.SequenceNumber >= p.FromSequence))
            .OrderBy(f => f.SequenceNumber).Select(f => new { f.Id, f.SequenceNumber, f.Name, Parent = f.Parent!.Name, f.FeatureTypeCode, f.StatusCode, f.CreatedAt, f.CreatedBy,
                Tests = db.TestCaseFeatureMappings.Count(m => m.FeatureId == f.Id && db.TestCases.Any(t => t.Id == m.TestCaseId)) }).ToListAsync(ct);
        return rows.Select(f => new string?[] { f.SequenceNumber.ToString(), f.Name, f.Parent, f.FeatureTypeCode, f.StatusCode, D(f.CreatedAt), f.CreatedBy, N(f.Tests) }).ToList();
    }

    private async Task<List<string?[]>> FeaturesBetween(ReportParameters p, CancellationToken ct)
    {
        var versions = await db.ProductVersions.AsNoTracking().Where(v => v.ProductId == p.ProductId).ToListAsync(ct);
        var ordered = versions.OrderBy(v => v.ReleaseDate ?? DateOnly.MaxValue).ThenBy(v => v.CreatedAt).ToList();
        var from = ordered.FindIndex(v => v.Id == p.ReleaseId);
        var to = p.CompareReleaseId is { } c ? ordered.FindIndex(v => v.Id == c) : ordered.Count - 1;
        if (from < 0 || to < 0) throw new ValidationException("releaseId", "Both releases must belong to the product.");
        if (from > to) (from, to) = (to, from);
        var inRange = ordered.Skip(from + 1).Take(to - from).ToDictionary(v => v.Id, v => v.VersionLabel);
        var (all, _, latest, byFeature) = await ProductDataAsync(p.ProductId, ct);
        return all.Where(f => f.IntroducedInVersionId is { } r && inRange.ContainsKey(r)).OrderBy(f => f.SequenceNumber).Select(f =>
        {
            var cs = byFeature[f.Id].DistinctBy(x => x.Id).ToList();
            return new string?[] { inRange[f.IntroducedInVersionId!.Value], f.SequenceNumber.ToString(), f.Name, f.Status, N(cs.Count), N(cs.Count(x => x.Automation == AutomationStatus.Automated)), LatestOf(cs, latest) };
        }).ToList();
    }

    private async Task<List<string?[]>> Untested(ReportParameters p, List<(string, string)> summary, CancellationToken ct)
    {
        var (all, _, latest, byFeature) = await ProductDataAsync(p.ProductId, ct);
        var byId = all.ToDictionary(f => f.Id);
        var features = all.Where(f => !f.IsContainer && !f.IsDeprecated).ToList();
        var rows = new List<string?[]>();
        foreach (var f in features.OrderBy(f => f.SequenceNumber))
        {
            var cs = byFeature[f.Id].DistinctBy(x => x.Id).ToList();
            var reason = cs.Count == 0 ? "No test cases" : cs.All(c => !latest.ContainsKey(c.Id)) ? "Test cases never executed" : null;
            if (reason is not null) rows.Add([f.SequenceNumber.ToString(), PathOf(f, byId), f.Name, f.Status, reason, N(cs.Count)]);
        }
        summary.Add(("Features", N(features.Count)));
        summary.Add(("Without test cases", N(rows.Count(r => r[4] == "No test cases"))));
        summary.Add(("Never executed", N(rows.Count(r => r[4] != "No test cases"))));
        return rows;
    }

    private async Task<List<string?[]>> AutomationCoverage(ReportParameters p, List<(string, string)> summary, CancellationToken ct)
    {
        var (all, cases, _, byFeature) = await ProductDataAsync(p.ProductId, ct);
        var reqs = await db.FeatureRequirements.AsNoTracking().Where(r => db.Features.Any(f => f.Id == r.FeatureId && f.ProductId == p.ProductId)).Select(r => new { r.Id, r.FeatureId }).ToListAsync(ct);
        var covered = (await db.TestCaseRequirementLinks.AsNoTracking().Select(l => l.RequirementId).Distinct().ToListAsync(ct)).ToHashSet();
        var rows = new List<string?[]>();
        string?[] Row(string label, HashSet<Guid> ids)
        {
            var features = all.Where(f => ids.Contains(f.Id) && !f.IsContainer && !f.IsDeprecated).ToList();
            var withTests = features.Count(f => byFeature[f.Id].Any());
            var cs = cases.Where(c => c.FeatureIds.Any(ids.Contains)).ToList();
            var rq = reqs.Where(r => ids.Contains(r.FeatureId)).ToList();
            var rqCovered = rq.Count(r => covered.Contains(r.Id));
            var auto = cs.Count(c => c.Automation == AutomationStatus.Automated);
            return [label, N(features.Count), N(withTests), Pct(Coverage.Percent(withTests, features.Count)), N(rq.Count), N(rqCovered), Pct(Coverage.Percent(rqCovered, rq.Count)), N(cs.Count), N(auto),
                N(cs.Count(c => c.Automation == AutomationStatus.AutomationPlanned)), N(cs.Count(c => c.Automation == AutomationStatus.Manual)), N(cs.Count(c => c.Automation == AutomationStatus.NotAutomatable)),
                Pct(Coverage.Percent(auto, cs.Count))];
        }
        foreach (var m in all.Where(f => f.ParentId is null).OrderBy(f => f.SequenceNumber))
            rows.Add(Row($"[{m.SequenceNumber}] {m.Name}", all.Where(f => f.Path.StartsWith(m.Path)).Select(f => f.Id).ToHashSet()));
        var total = Row("Total", all.Select(f => f.Id).ToHashSet());
        rows.Add(total);
        summary.Add(("Feature coverage", $"{total[3] ?? "n/a"}%"));
        summary.Add(("Requirement coverage", $"{total[6] ?? "n/a"}%"));
        summary.Add(("Test-case automation", $"{total[12] ?? "n/a"}%"));
        summary.Add(("Code coverage", "not measured by the orchestrator"));
        return rows;
    }

    private async Task<List<string?[]>> ExecutionSummary(DateTimeOffset since, List<(string, string)> summary, CancellationToken ct)
    {
        var rows = await db.ExecutionRuns.AsNoTracking().Where(r => r.CreatedAt >= since).OrderByDescending(r => r.Number)
            .Select(r => new { r, Env = db.TestEnvironments.Where(e => e.Id == r.EnvironmentId).Select(e => e.Name).FirstOrDefault() }).Take(5000).ToListAsync(ct);
        summary.Add(("Runs", N(rows.Count)));
        summary.Add(("Tests executed", N(rows.Sum(x => x.r.Passed + x.r.Failed + x.r.Blocked))));
        summary.Add(("Overall pass rate", $"{Pct(Coverage.Percent(rows.Sum(x => x.r.Passed), rows.Sum(x => x.r.Passed + x.r.Failed + x.r.Blocked))) ?? "n/a"}%"));
        return rows.Select(x =>
        {
            var r = x.r;
            return new string?[] { ExecutionService.Format(r.Number), r.Name, r.Status.ToString(), x.Env, r.BuildVersion, N(r.TotalTests), N(r.Passed), N(r.Failed), N(r.Blocked), N(r.InfrastructureErrors),
                Pct(Coverage.Percent(r.Passed, r.Passed + r.Failed + r.Blocked)), r.StartedAt is { } s && r.CompletedAt is { } c ? (c - s).TotalMinutes.ToString("0.#") : null, r.TriggeredBy ?? r.CreatedBy, D(r.CompletedAt) };
        }).ToList();
    }

    private async Task<List<string?[]>> ExecutionRows(IQueryable<TestExecution> q, CancellationToken ct, bool withDefects)
    {
        var rows = await (from t in q
                          join r in db.ExecutionRuns on t.RunId equals r.Id
                          orderby t.CompletedAt descending
                          select new
                          {
                              t.Id, t.CompletedAt, r.Number, t.DisplayName, t.Outcome, t.DurationMs, Error = t.ErrorMessage ?? t.Reason,
                              Tc = db.TestCases.IgnoreQueryFilters().Where(c => c.Id == t.TestCaseId).Select(c => (long?)c.Number).FirstOrDefault(),
                              Feature = db.Features.Where(f => f.Id == t.FeatureId).Select(f => f.Name).FirstOrDefault(),
                              Agent = db.ExecutionAgents.Where(a => a.Id == t.AgentId).Select(a => a.Name).FirstOrDefault(),
                          }).Take(5000).ToListAsync(ct);
        var defectsBy = new Dictionary<Guid, string>();
        if (withDefects)
        {
            var ids = rows.Select(r => r.Id).ToList();
            foreach (var o in await (from o in db.DefectOccurrences.Where(o => ids.Contains(o.TestExecutionId)) join d in db.Defects on o.DefectId equals d.Id select new { o.TestExecutionId, d.Number }).ToListAsync(ct))
                defectsBy[o.TestExecutionId] = DefectService.Format(o.Number);
        }
        return rows.Select(x => withDefects
            ? new string?[] { D(x.CompletedAt), ExecutionService.Format(x.Number), x.DisplayName, x.Tc is { } n ? Tc(n) : null, x.Feature, First(x.Error), defectsBy.GetValueOrDefault(x.Id) }
            : [D(x.CompletedAt), ExecutionService.Format(x.Number), x.DisplayName, x.Tc is { } m ? Tc(m) : null, x.Feature, x.Outcome, x.DurationMs is { } ms ? (ms / 1000.0).ToString("0.#") : null, x.Agent, First(x.Error)]).ToList();
    }

    private static string? First(string? s) => s?.Replace("\r", "").Split('\n')[0] is { } l && l.Length > 300 ? l[..299] + "…" : s?.Replace("\r", "").Split('\n')[0];

    private async Task<List<string?[]>> FeatureHistory(ReportParameters p, DateTimeOffset since, CancellationToken ct)
    {
        var root = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == p.FeatureId, ct) ?? throw new NotFoundException("Feature", p.FeatureId!);
        await access.EnsureCanViewAsync(root.ProductId, ct);
        var ids = db.Features.Where(f => f.SequenceScopeId == root.SequenceScopeId && f.Path.StartsWith(root.Path)).Select(f => f.Id);
        var cases = db.TestCaseFeatureMappings.Where(m => ids.Contains(m.FeatureId)).Select(m => m.TestCaseId);
        return await ExecutionRows(db.TestExecutions.AsNoTracking().Where(t => !t.IsSuperseded && t.CompletedAt >= since && t.Outcome != "Pending"
            && ((t.FeatureId != null && ids.Contains(t.FeatureId.Value)) || (t.TestCaseId != null && cases.Contains(t.TestCaseId.Value)))), ct, false);
    }

    private IQueryable<TestExecution> ProductExecutions(Guid? productId, DateTimeOffset since)
    {
        var q = db.TestExecutions.AsNoTracking().Where(t => !t.IsSuperseded && t.CompletedAt >= since);
        if (productId is { } pid)
        {
            var productCases = db.TestCaseFeatureMappings.Where(m => m.Feature!.ProductId == pid).Select(m => m.TestCaseId);
            q = q.Where(t => (t.FeatureId != null && db.Features.Any(f => f.Id == t.FeatureId && f.ProductId == pid)) || (t.TestCaseId != null && productCases.Contains(t.TestCaseId.Value)));
        }
        return q;
    }

    private Task<List<string?[]>> FailedTests(ReportParameters p, DateTimeOffset since, CancellationToken ct)
        => ExecutionRows(ProductExecutions(p.ProductId, since).Where(t => t.Outcome == "Failed"), ct, true);

    private async Task<List<string?[]>> Flaky(ReportParameters p, CancellationToken ct)
        => (await defects.RecurringAsync(p.ProductId, p.Days, 1, ct)).Where(r => r.Flips > 0 && r.Failures < r.Executions)
            .OrderByDescending(r => r.Flips).ThenByDescending(r => r.Failures)
            .Select(r => new string?[] { r.DisplayName, r.TestCaseNumber, N(r.Executions), N(r.Failures), Pct(Coverage.Percent(r.Failures, r.Executions)), N(r.Flips), D(r.LastFailedAt), r.DefectNumber }).ToList();

    private async Task<List<string?[]>> DefectsByModule(ReportParameters p, CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().Where(x => p.ProductId == null || x.Id == p.ProductId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var all = (await Coverage.FeaturesAsync(db, p.ProductId, ct)).Where(f => products.ContainsKey(f.ProductId)).ToList();
        var list = await db.Defects.AsNoTracking().Where(d => p.ProductId == null || d.ProductId == p.ProductId).Select(d => new { d.ProductId, d.FeatureId, d.Severity, d.Status }).ToListAsync(ct);
        var rows = new List<string?[]>();
        var modules = all.Where(f => f.ParentId is null).OrderBy(f => f.ProductId).ThenBy(f => f.SequenceNumber).ToList();
        foreach (var m in modules)
        {
            var ids = all.Where(f => f.ProductId == m.ProductId && f.Path.StartsWith(m.Path)).Select(f => f.Id).ToHashSet();
            var ds = list.Where(d => d.FeatureId is { } fid && ids.Contains(fid)).ToList();
            if (ds.Count == 0) continue;
            rows.Add(Bucket(products[m.ProductId], $"[{m.SequenceNumber}] {m.Name}", ds.Select(d => (d.Severity, d.Status)).ToList()));
        }
        foreach (var g in list.Where(d => d.FeatureId is null).GroupBy(d => d.ProductId))
            rows.Add(Bucket(g.Key is { } pid ? products.GetValueOrDefault(pid, "(other)") : "(no product)", "(no feature)", g.Select(d => (d.Severity, d.Status)).ToList()));
        return rows;

        static string?[] Bucket(string product, string module, List<(DefectSeverity Severity, DefectStatus Status)> ds)
        {
            var open = ds.Where(d => DefectWorkflow.Unresolved.Contains(d.Status)).ToList();
            return [product, module, N(ds.Count), N(open.Count), N(open.Count(d => d.Severity == DefectSeverity.Critical)), N(open.Count(d => d.Severity == DefectSeverity.Major)),
                N(open.Count(d => d.Severity == DefectSeverity.Minor)), N(open.Count(d => d.Severity == DefectSeverity.Trivial)),
                N(ds.Count(d => d.Status is DefectStatus.Closed or DefectStatus.Verified)), N(ds.Count(d => d.Status is DefectStatus.Rejected or DefectStatus.Duplicate)), N(ds.Count(d => d.Status == DefectStatus.Deferred))];
        }
    }

    private async Task<List<string?[]>> ReleaseComparison(ReportParameters p, CancellationToken ct)
    {
        var a = await releases.GetAsync(p.ReleaseId!.Value, ct);
        var b = await releases.GetAsync(p.CompareReleaseId!.Value, ct);
        string?[] Row(string label, Func<ReleaseWorkspaceDto, string?> value) => [label, value(a), value(b)];
        return
        [
            Row("Release", w => $"{w.Release.ProductName} {w.Release.VersionLabel}"), Row("Status", w => w.Release.Status.ToString()), Row("Readiness", w => w.Release.Readiness),
            Row("Testing window", w => $"{w.WindowStart:yyyy-MM-dd} – {w.WindowEnd:yyyy-MM-dd}"),
            Row("Features added", w => N(w.Metrics.FeaturesAdded)), Row("Features modified", w => N(w.Metrics.FeaturesModified)), Row("Features deprecated", w => N(w.Metrics.FeaturesDeprecated)),
            Row("Required tests", w => N(w.Metrics.TestsRequired)), Row("Completed", w => N(w.Metrics.TestsCompleted)), Row("Passed", w => N(w.Metrics.TestsPassed)),
            Row("Failed/blocked", w => N(w.Metrics.TestsFailed)), Row("Pass rate %", w => Pct(w.Metrics.PassRate)), Row("Failed critical tests", w => N(w.Metrics.FailedCritical)),
            Row("Unresolved critical defects", w => N(w.Metrics.OpenCriticalDefects)), Row("Regression completion", w => $"{w.Metrics.RegressionCompleted}/{w.Metrics.RegressionTotal}"),
            Row("Gates passed", w => $"{w.Release.GatesPassed}/{w.Release.GatesTotal}"), Row("Environments", w => string.Join(", ", w.Environments.Select(e => e.Name))),
            Row("Signed off", w => w.Approvals.FirstOrDefault(x => x.Decision == ReleaseDecision.Approved) is { } x ? $"{x.ApprovedBy} {x.ApprovedAt:yyyy-MM-dd}" : "No"),
        ];
    }

    private async Task<List<string?[]>> TesterProductivity(DateTimeOffset since, CancellationToken ct)
    {
        var manual = await db.TestExecutions.AsNoTracking().Where(t => t.ExecutedBy != null && t.CompletedAt >= since).GroupBy(t => t.ExecutedBy!).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var started = await db.ExecutionRuns.AsNoTracking().Where(r => r.CreatedAt >= since).GroupBy(r => r.CreatedBy).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Timestamp >= since && ((a.EntityType == nameof(TestCase) && (a.Action == "Created" || a.Action == "Approved")) || (a.EntityType == nameof(Defect) && a.Action == "Created")))
            .GroupBy(a => new { a.UserName, a.EntityType, a.Action }).Select(g => new { g.Key.UserName, g.Key.EntityType, g.Key.Action, Count = g.Count() }).ToListAsync(ct);
        var verified = await db.DefectEvents.AsNoTracking().Where(e => e.Timestamp >= since && e.ToStatus == DefectStatus.Verified).GroupBy(e => e.UserName).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var people = manual.Select(x => x.Key).Concat(started.Select(x => x.Key)).Concat(audits.Select(x => x.UserName)).Concat(verified.Select(x => x.Key))
            .Where(u => !string.IsNullOrEmpty(u) && u != "system" && !u.Contains(':')).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        int A(string u, string type, string action) => audits.Where(a => string.Equals(a.UserName, u, StringComparison.OrdinalIgnoreCase) && a.EntityType == type && a.Action == action).Sum(a => a.Count);
        return people.Select(u => new string?[] { u, N(manual.Where(x => x.Key == u).Sum(x => x.Count)), N(started.Where(x => x.Key == u).Sum(x => x.Count)),
            N(A(u, nameof(TestCase), "Created")), N(A(u, nameof(TestCase), "Approved")), N(A(u, nameof(Defect), "Created")), N(verified.Where(x => x.Key == u).Sum(x => x.Count)) }).ToList();
    }

    private async Task<List<string?[]>> AgentUtilization(ReportParameters p, DateTimeOffset since, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var jobs = await db.ExecutionJobs.AsNoTracking().Where(j => j.AgentId != null && (j.CompletedAt == null || j.CompletedAt >= since) && j.LeasedAt != null)
            .Select(j => new { j.AgentId, j.Status, j.LeasedAt, j.StartedAt, j.CompletedAt }).ToListAsync(ct);
        var agents = await db.ExecutionAgents.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);
        return agents.Select(a =>
        {
            var mine = jobs.Where(j => j.AgentId == a.Id).ToList();
            var busy = mine.Sum(j => { var s = (j.StartedAt ?? j.LeasedAt)!.Value; if (s < since) s = since; var e = j.CompletedAt ?? now; return e > s ? (e - s).TotalHours : 0; });
            var done = mine.Where(j => j.StartedAt is not null && j.CompletedAt is not null).ToList();
            return new string?[] { a.Name, a.Kind, a.Pool, N(a.Slots), N(mine.Count), busy.ToString("0.##"), Pct(100.0 * busy / (Math.Max(1, a.Slots) * p.Days * 24.0)),
                N(mine.Count(j => j.Status == JobStatus.InfrastructureError)), done.Count == 0 ? null : done.Average(j => (j.CompletedAt!.Value - j.StartedAt!.Value).TotalSeconds).ToString("0") };
        }).ToList();
    }

    private async Task<List<string?[]>> Durations(ReportParameters p, DateTimeOffset since, CancellationToken ct)
    {
        var rows = await (from t in ProductExecutions(p.ProductId, since).Where(t => t.DurationMs != null && t.Outcome != "Pending")
                          join j in db.ExecutionJobs on t.JobId equals j.Id
                          select new { t.Identifier, t.DisplayName, t.TestCaseId, j.Framework, t.DurationMs }).ToListAsync(ct);
        var numbers = await db.TestCases.IgnoreQueryFilters().AsNoTracking().Where(c => rows.Select(r => r.TestCaseId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, ct);
        return rows.GroupBy(r => r.Identifier).Select(g => new { g, Avg = g.Average(r => r.DurationMs!.Value) }).OrderByDescending(x => x.Avg).Take(1000)
            .Select(x => new string?[] { x.g.First().DisplayName, x.g.First().TestCaseId is { } id && numbers.TryGetValue(id, out var n) ? Tc(n) : null, x.g.First().Framework, N(x.g.Count()),
                (x.Avg / 1000).ToString("0.#"), (x.g.Max(r => r.DurationMs!.Value) / 1000.0).ToString("0.#"), (x.g.Sum(r => r.DurationMs!.Value) / 60000.0).ToString("0.#") }).ToList();
    }

    private async Task<List<string?[]>> Trends(ReportParameters p, DateTimeOffset since, CancellationToken ct)
    {
        var tests = await ProductExecutions(p.ProductId, since).Where(t => t.Outcome != "Pending").Select(t => new { t.CompletedAt, t.Outcome }).ToListAsync(ct);
        var opened = await db.Defects.AsNoTracking().Where(d => d.CreatedAt >= since && (p.ProductId == null || d.ProductId == p.ProductId)).Select(d => d.CreatedAt).ToListAsync(ct);
        var closed = await db.DefectEvents.AsNoTracking().Where(e => e.Timestamp >= since && (e.ToStatus == DefectStatus.Closed || e.ToStatus == DefectStatus.Verified)
            && db.Defects.Any(d => d.Id == e.DefectId && (p.ProductId == null || d.ProductId == p.ProductId))).Select(e => e.Timestamp).ToListAsync(ct);
        static DateOnly Week(DateTimeOffset t) { var d = DateOnly.FromDateTime(t.UtcDateTime); return d.AddDays(-(((int)d.DayOfWeek + 6) % 7)); }
        var weeks = Enumerable.Range(0, (int)Math.Ceiling(p.Days / 7.0) + 1).Select(i => Week(since.AddDays(7 * i))).Where(w => w <= Week(clock.GetUtcNow())).Distinct().ToList();
        return weeks.Select(w =>
        {
            var t = tests.Where(x => x.CompletedAt is { } c && Week(c) == w).ToList();
            var passed = t.Count(x => x.Outcome == "Passed");
            var failed = t.Count(x => x.Outcome == "Failed");
            var blocked = t.Count(x => x.Outcome == "Blocked");
            return new string?[] { w.ToString("yyyy-MM-dd"), N(passed + failed + blocked), N(passed), N(failed), N(blocked), Pct(Coverage.Percent(passed, passed + failed + blocked)),
                N(opened.Count(o => Week(o) == w)), N(closed.Count(c => Week(c) == w)) };
        }).ToList();
    }

    private async Task<List<string?[]>> CriticalCoverage(ReportParameters p, List<(string, string)> summary, CancellationToken ct)
    {
        var (all, _, latest, byFeature) = await ProductDataAsync(p.ProductId, ct);
        var reqs = await db.FeatureRequirements.AsNoTracking().Where(r => db.Features.Any(f => f.Id == r.FeatureId && f.ProductId == p.ProductId) && (r.Priority == "HIGH" || r.Priority == "CRITICAL"))
            .Select(r => new { r.Id, r.FeatureId }).ToListAsync(ct);
        var covered = (await db.TestCaseRequirementLinks.AsNoTracking().Select(l => l.RequirementId).Distinct().ToListAsync(ct)).ToHashSet();
        var rows = new List<string?[]>();
        foreach (var f in all.Where(f => !f.IsDeprecated).OrderBy(f => f.SequenceNumber))
        {
            var cs = byFeature[f.Id].DistinctBy(c => c.Id).ToList();
            var fr = reqs.Where(r => r.FeatureId == f.Id).ToList();
            var p1 = cs.Count(c => c.Priority == "P1");
            if (fr.Count == 0 && p1 == 0) continue;
            var outcomes = cs.Select(c => latest.GetValueOrDefault(c.Id)?.Outcome).ToList();
            var passed = outcomes.Count(o => o == "Passed");
            var failed = outcomes.Count(o => o is "Failed" or "Blocked");
            var reqCovered = fr.Count(r => covered.Contains(r.Id));
            var status = cs.Count == 0 ? "No tests" : failed > 0 ? "Failing" : reqCovered < fr.Count ? "Requirements not covered" : passed == cs.Count ? "Covered and passing" : "Not fully executed";
            rows.Add([f.SequenceNumber.ToString(), f.Name, N(fr.Count), N(reqCovered), N(cs.Count), N(p1), N(passed), N(failed), N(outcomes.Count(o => o is null)), status]);
        }
        summary.Add(("Business-critical features", N(rows.Count)));
        summary.Add(("Covered and passing", N(rows.Count(r => r[9] == "Covered and passing"))));
        return rows;
    }

    private async Task<List<string?[]>> ChangedCode(ReportParameters p, DateTimeOffset since, List<(string, string)> summary, CancellationToken ct)
    {
        var impacts = await (from i in db.BuildFeatureImpacts
                             join b in db.ApplicationBuilds on i.BuildId equals b.Id
                             join f in db.Features on i.FeatureId equals f.Id
                             where b.CreatedAt >= since && (p.ProductId == null || b.ProductId == p.ProductId)
                             orderby b.CreatedAt descending, f.SequenceNumber
                             select new { b.BuildNumber, b.CreatedAt, f.Id, f.SequenceNumber, f.Name, i.Reason }).Take(5000).ToListAsync(ct);
        var featureIds = impacts.Select(i => i.Id).Distinct().ToList();
        var mappings = await db.TestCaseFeatureMappings.AsNoTracking().Where(m => featureIds.Contains(m.FeatureId)).Select(m => new { m.FeatureId, m.TestCaseId }).ToListAsync(ct);
        var caseIds = mappings.Select(m => m.TestCaseId).Distinct().ToList();
        var passes = await db.TestExecutions.AsNoTracking().Where(t => t.TestCaseId != null && caseIds.Contains(t.TestCaseId.Value) && t.Outcome == "Passed" && !t.IsSuperseded && t.CompletedAt >= since)
            .Select(t => new { TestCaseId = t.TestCaseId!.Value, t.CompletedAt }).ToListAsync(ct);
        var rows = impacts.Select(i =>
        {
            var cases = mappings.Where(m => m.FeatureId == i.Id).Select(m => m.TestCaseId).ToHashSet();
            var after = passes.Where(x => cases.Contains(x.TestCaseId) && x.CompletedAt >= i.CreatedAt).Select(x => x.TestCaseId).Distinct().Count();
            return new string?[] { i.BuildNumber, D(i.CreatedAt), i.SequenceNumber.ToString(), i.Name, i.Reason.ToString(), N(cases.Count), N(after), cases.Count == 0 ? "No tests" : after > 0 ? "Tested" : "Not tested" };
        }).ToList();
        summary.Add(("Impacted feature changes", N(rows.Count)));
        summary.Add(("Tested after change", $"{Pct(Coverage.Percent(rows.Count(r => r[7] == "Tested"), rows.Count)) ?? "n/a"}%"));
        return rows;
    }

    // ---- saved and scheduled reports ----

    public async Task<IReadOnlyList<SavedReportDto>> SavedAsync(CancellationToken ct = default)
    {
        var me = user.UserName;
        return (await db.SavedReports.AsNoTracking().Where(r => r.Owner == me || r.IsShared).OrderBy(r => r.Name).ToListAsync(ct)).Select(ToDto).ToList();
    }

    private SavedReportDto ToDto(SavedReport r) => new(r.Id, r.Name, r.ReportKey, JsonSerializer.Deserialize<ReportParameters>(r.ParametersJson, Json) ?? new(),
        r.Columns?.Split(',', StringSplitOptions.RemoveEmptyEntries), r.Owner, r.IsShared, r.ScheduleCron, r.DeliveryChannelId, r.Format, r.NextRunAt, r.LastRunAt,
        string.Equals(r.Owner, user.UserName, StringComparison.OrdinalIgnoreCase), r.Version);

    public async Task<SavedReportDto> SaveAsync(Guid? id, SavedReportUpsert r, CancellationToken ct = default)
    {
        CronExpression? cron = null;
        var v = new Validator().Required(r.Name, "name", 200)
            .Must((await CatalogAsync(ct)).Any(d => d.Key == r.ReportKey), "reportKey", "Unknown report, or you may not view it.")
            .Must(r.Format is "csv" or "xlsx" or "pdf", "format", "Use csv, xlsx or pdf.");
        if (r.ScheduleCron.Clean() is { } expr)
        {
            try { cron = CronExpression.Parse(expr); } catch (CronFormatException) { v.Add("scheduleCron", "Not a valid five-field cron expression."); }
            v.Must(r.DeliveryChannelId is not null, "deliveryChannelId", "Choose the email channel that receives the scheduled report.");
            if (r.DeliveryChannelId is { } cid)
                v.Must(await db.NotificationChannels.AnyAsync(c => c.Id == cid && c.Type == ChannelType.Email, ct), "deliveryChannelId", "Scheduled reports are delivered through an email channel.");
        }
        v.ThrowIfInvalid();
        SavedReport s;
        if (id is { } existing)
        {
            s = await db.SavedReports.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Saved report", existing);
            if (!string.Equals(s.Owner, user.UserName, StringComparison.OrdinalIgnoreCase)) throw new ForbiddenException("Only the owner can change a saved report.");
            if (r.Version is { } ver) db.SetOriginalVersion(s, ver);
        }
        else db.SavedReports.Add(s = new SavedReport { Owner = user.UserName });
        s.Name = r.Name.Trim();
        s.ReportKey = r.ReportKey;
        s.ParametersJson = JsonSerializer.Serialize(r.Parameters, Json);
        s.Columns = r.Columns is { Count: > 0 } ? string.Join(',', r.Columns) : null;
        s.IsShared = r.IsShared;
        s.ScheduleCron = cron is null ? null : r.ScheduleCron!.Trim();
        s.DeliveryChannelId = cron is null ? null : r.DeliveryChannelId;
        s.Format = r.Format;
        s.NextRunAt = cron?.GetNextOccurrence(clock.GetUtcNow().UtcDateTime, TimeZoneInfo.Utc) is { } next ? new DateTimeOffset(next, TimeSpan.Zero) : null;
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    public async Task DeleteSavedAsync(Guid id, CancellationToken ct = default)
    {
        var s = await db.SavedReports.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Saved report", id);
        if (!string.Equals(s.Owner, user.UserName, StringComparison.OrdinalIgnoreCase) && !(await PermsAsync(ct)).Contains(Permissions.NotificationsManage))
            throw new ForbiddenException("Only the owner can delete a saved report.");
        db.SavedReports.Remove(s);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> DeliverDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var due = await db.SavedReports.Where(r => r.ScheduleCron != null && r.DeliveryChannelId != null && r.NextRunAt <= now).Take(20).ToListAsync(ct);
        foreach (var s in due)
        {
            // Claim first so another node (or a failure) cannot send it twice for the same occurrence.
            var next = CronExpression.Parse(s.ScheduleCron!).GetNextOccurrence(now.UtcDateTime, TimeZoneInfo.Utc) is { } n ? new DateTimeOffset(n, TimeSpan.Zero) : (DateTimeOffset?)null;
            var claimed = await db.SavedReports.Where(r => r.Id == s.Id && r.NextRunAt == s.NextRunAt).ExecuteUpdateAsync(x => x.SetProperty(r => r.NextRunAt, next).SetProperty(r => r.LastRunAt, now), ct);
            if (claimed != 1) continue;
            var ev = new NotificationEvent { Type = NotificationEvents.ReportDelivered, Severity = NotificationSeverity.Info, Title = $"Scheduled report: {s.Name}", Link = "/reports", CreatedAt = now, AcknowledgedAt = now, AcknowledgedBy = "system" };
            try
            {
                var file = await ExportCoreAsync(s.ReportKey, JsonSerializer.Deserialize<ReportParameters>(s.ParametersJson, Json) ?? new(), s.Columns?.Split(','), s.Format, false, ct);
                var blob = await files.PutAsync(new MemoryStream(file.Content), ct);
                ev.Body = $"The attached {s.Format.ToUpperInvariant()} was generated {now:yyyy-MM-dd HH:mm} UTC for {s.Owner}.";
                db.NotificationEvents.Add(ev);
                var d = new NotificationDelivery { EventId = ev.Id, ChannelId = s.DeliveryChannelId, CreatedAt = now, AttachmentKey = blob.Key, AttachmentName = file.FileName };
                db.NotificationDeliveries.Add(d);
                await jobs.EnqueueAsync(NotificationPublisher.DeliverJob, new { deliveryId = d.Id }, ct);
            }
            catch (AppException ex)
            {
                ev.Severity = NotificationSeverity.Warning;
                ev.Body = $"The report could not be generated: {ex.Message}";
                db.NotificationEvents.Add(ev);
            }
            await db.SaveChangesAsync(ct);
        }
        return due.Count;
    }
}
