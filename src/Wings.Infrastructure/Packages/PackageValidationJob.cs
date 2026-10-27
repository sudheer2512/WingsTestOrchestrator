using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wings.Application.Abstractions;
using Wings.Application.Testing;
using Wings.Domain.Testing;
using Wings.Infrastructure.Jobs;
using Wings.Infrastructure.Persistence;

namespace Wings.Infrastructure.Packages;

/// <summary>
/// Validates an uploaded package version entirely by static inspection. Nothing uploaded is built or executed in this
/// process: compilation and test runs belong to isolated build/execution agents.
/// </summary>
public sealed class PackageValidationJob(WingsDbContext db, IFileStore files, IEnumerable<IContentScanner> scanners, IOptions<PackageOptions> options,
    TimeProvider clock, ILogger<PackageValidationJob> log, IChangeNotifier notifier, Application.Quality.INotificationPublisher notifications) : IFailureAwareJobHandler
{
    public string JobType => PackageService.ValidateJob;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Folders that hold build output, dependencies or VCS metadata. Skipped rather than stored.</summary>
    private static readonly string[] IgnoredFolders = ["bin", "obj", "node_modules", ".git", ".vs", ".idea", "packages", "TestResults", "playwright-report", "test-results", "__pycache__", ".venv", "venv"];

    private sealed record Payload(Guid VersionId);
    private sealed record StoredFile(string Path, string Key, string Sha256, long Size);

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<Payload>(payloadJson, Json)!;
        var version = await db.TestPackageVersions.FirstOrDefaultAsync(v => v.Id == payload.VersionId, ct);
        if (version is null || version.Status is not (PackageStatus.Uploaded or PackageStatus.Validating)) return; // already processed: idempotent

        version.Status = PackageStatus.Validating;
        await db.SaveChangesAsync(ct);

        var opts = options.Value;
        var report = new ValidationReport();
        var stored = new List<StoredFile>();

        // 1. File type and archive safety
        var intake = new ValidationStage("File type and archive safety", StageStatus.Passed, "");
        report.Stages.Add(intake);
        var isZip = version.OriginalFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        if (isZip) await ExtractAsync(version.ArchiveStorageKey!, opts, intake, stored, ct);
        else
        {
            var path = ArchivePath.Normalize(version.OriginalFileName) ?? "upload";
            stored.Add(new StoredFile(path, version.ArchiveStorageKey!, version.Sha256, version.SizeBytes));
        }
        Finish(intake, $"{stored.Count} file(s) accepted" + (isZip ? " from archive" : ""));

        // 2–3. Content scanning and static analysis, one file at a time to bound memory.
        var scan = new ValidationStage("Malware and unsafe content scan", StageStatus.Passed, "");
        var syntax = new ValidationStage("Syntax validation", StageStatus.Passed, "");
        var project = new ValidationStage("Project files and dependencies", StageStatus.Passed, "");
        report.Stages.AddRange([scan, syntax, project]);
        var analyzer = new PackageAnalyzer();
        var fileRows = new List<TestPackageFile>();
        var activeScanners = scanners.Where(s => s.IsEnabled).ToList();
        foreach (var f in stored)
        {
            byte[] bytes;
            await using (var s = await files.OpenReadAsync(f.Key, ct))
            using (var ms = new MemoryStream())
            {
                await s.CopyToAsync(ms, ct);
                bytes = ms.ToArray();
            }
            foreach (var scanner in activeScanners)
            {
                try { scan.Findings.AddRange(await scanner.ScanAsync(f.Path, bytes, ct)); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    scan.Findings.Add(new("error", $"{scanner.Name} could not scan this file: {ex.Message}", f.Path));
                }
            }
            var isText = FileKinds.IsText(bytes);
            if (isText) analyzer.Analyze(f.Path, FileKinds.Decode(bytes));
            fileRows.Add(new TestPackageFile
            {
                VersionId = version.Id, Path = f.Path, SizeBytes = f.Size, Sha256 = f.Sha256, StorageKey = f.Key, IsText = isText,
                Language = isText ? FileKinds.LanguageFor(f.Path) : null,
            });
        }
        if (activeScanners.All(s => s is BuiltInContentScanner))
            scan.Findings.Add(new("info", "Only the built-in signature and file-type checks ran. Configure ClamAV (Scanning:ClamAv:Host) for antivirus scanning."));
        Finish(scan, $"Scanned {stored.Count} file(s) with {string.Join(", ", activeScanners.Select(s => s.Name))}");

        syntax.Findings.AddRange(analyzer.SyntaxFindings);
        Finish(syntax, analyzer.SyntaxFindings.Count == 0 ? "No syntax errors found" : $"{analyzer.SyntaxFindings.Count} syntax error(s)");

        project.Findings.AddRange(analyzer.ProjectFindings);
        if (analyzer.HasCSharp && !analyzer.HasCsproj && isZip)
            project.Findings.Add(new("warning", "C# files were uploaded without a .csproj; the build agent will not be able to compile them."));
        if (analyzer.HasJsTests && !analyzer.HasPackageJson && isZip)
            project.Findings.Add(new("warning", "Playwright tests were uploaded without a package.json; dependencies cannot be restored on the agent."));
        Finish(project, project.Findings.Count == 0 ? "Project files look complete" : $"{project.Findings.Count} item(s) to review");

        // 4. Framework detection and test discovery
        var (framework, language, platform) = analyzer.Summarize();
        report.Framework = framework;
        report.Language = language;
        report.Platform = platform;
        var discovery = new ValidationStage("Test discovery", analyzer.Tests.Count > 0 ? StageStatus.Passed : StageStatus.Warning,
            analyzer.Tests.Count > 0 ? $"{analyzer.Tests.Count} test(s) found ({string.Join(", ", analyzer.Frameworks.OrderBy(x => x))})" : "No tests were discovered");
        var dupes = analyzer.Tests.GroupBy(t => t.Identifier).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        discovery.Findings.AddRange(dupes.Select(d => new ValidationFinding("error", $"Duplicate test identifier '{d}'.")));
        if (dupes.Count > 0) discovery.Status = StageStatus.Failed;
        report.Stages.Add(discovery);

        // 5. Build: deliberately not performed here.
        report.Stages.Add(new ValidationStage("Compilation", StageStatus.Pending,
            "Not run on the server. Uploaded code is compiled only on an isolated build agent, which is part of the execution infrastructure phase."));

        // Persist: replace any rows from an earlier attempt so retries are idempotent.
        await db.TestPackageTests.Where(t => t.VersionId == version.Id).ExecuteDeleteAsync(ct);
        await db.TestPackageFiles.Where(t => t.VersionId == version.Id).ExecuteDeleteAsync(ct);
        db.TestPackageFiles.AddRange(fileRows);
        db.TestPackageTests.AddRange(await MapTestsAsync(version, analyzer.Tests.DistinctBy(t => t.Identifier).ToList(), ct));

        version.FileCount = fileRows.Count;
        version.DetectedFramework = framework;
        version.DetectedLanguage = language;
        version.Platform = platform;
        version.ValidatedAt = clock.GetUtcNow();
        version.ValidationReportJson = JsonSerializer.Serialize(report, Json);
        version.Status = report.Passed ? PackageStatus.Validated : PackageStatus.ValidationFailed;
        db.PendingAuditAction = report.Passed ? "Validated" : "ValidationFailed";
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("packageVersionChanged", new { versionId = version.Id, packageId = version.PackageId, status = version.Status.ToString() }, ct);
        if (report.Passed)
        {
            var package = await db.TestPackages.AsNoTracking().FirstAsync(p => p.Id == version.PackageId, ct);
            await notifications.PublishAsync(new Application.Quality.NotificationMessage(Application.Quality.NotificationEvents.ApprovalRequired, Application.Quality.NotificationSeverity.Info,
                $"Test code awaiting approval: {package.Name} v{version.VersionNumber}", $"Validated with {analyzer.Tests.Count} discovered test(s); uploaded by {version.CreatedBy}.",
                $"/test-code/versions/{version.Id}", package.ProductId), ct);
        }
        log.LogInformation("Package version {VersionId} validated: {Status}, {Tests} tests", version.Id, version.Status, analyzer.Tests.Count);
    }

    public async Task OnFinalFailureAsync(string payloadJson, Exception error, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<Payload>(payloadJson, Json)!;
        db.ChangeTracker.Clear();
        var version = await db.TestPackageVersions.FirstOrDefaultAsync(v => v.Id == payload.VersionId, ct);
        if (version is null) return;
        var report = new ValidationReport();
        report.Stages.Add(new ValidationStage("Validation", StageStatus.Failed, "Validation could not complete.") { Findings = [new("error", error.Message)] });
        version.ValidationReportJson = JsonSerializer.Serialize(report, Json);
        version.Status = PackageStatus.ValidationFailed;
        await db.SaveChangesAsync(ct);
    }

    private async Task ExtractAsync(string archiveKey, PackageOptions opts, ValidationStage stage, List<StoredFile> stored, CancellationToken ct)
    {
        await using var archiveStream = await files.OpenReadAsync(archiveKey, ct);
        ZipArchive zip;
        try { zip = new ZipArchive(archiveStream, ZipArchiveMode.Read); }
        catch (InvalidDataException) { stage.Findings.Add(new("error", "The file is not a valid ZIP archive.")); return; }

        using (zip)
        {
            if (zip.Entries.Count > opts.MaxEntries) { stage.Findings.Add(new("error", $"The archive has {zip.Entries.Count} entries; the limit is {opts.MaxEntries}.")); return; }
            long total = 0;
            var ignored = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // A single top-level folder (e.g. "MyTests/") is stripped so paths are relative to the project root.
            var names = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).Select(e => e.FullName.Replace('\\', '/')).ToList();
            var prefix = names.Count > 0 && names.All(n => n.Contains('/')) && names.Select(n => n[..n.IndexOf('/')]).Distinct().Count() == 1
                ? names[0][..(names[0].IndexOf('/') + 1)] : "";

            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // directory
                var raw = entry.FullName.Replace('\\', '/');
                var path = ArchivePath.Normalize(raw.StartsWith(prefix, StringComparison.Ordinal) ? raw[prefix.Length..] : raw);
                if (path is null) { stage.Findings.Add(new("error", $"Unsafe path in archive: '{entry.FullName}' (absolute or contains '..').")); continue; }
                if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) { stage.Findings.Add(new("error", "Symbolic links are not allowed.", path)); continue; }
                if (path.Split('/').SkipLast(1).Any(seg => IgnoredFolders.Contains(seg, StringComparer.OrdinalIgnoreCase))) { ignored++; continue; }
                if (!seen.Add(path)) { stage.Findings.Add(new("error", "Duplicate path in archive.", path)); continue; }
                if (entry.Length > opts.MaxFileBytes) { stage.Findings.Add(new("error", $"File exceeds the {opts.MaxFileBytes / 1024 / 1024} MB per-file limit.", path)); continue; }
                if (entry.CompressedLength > 0 && entry.Length / (double)entry.CompressedLength > opts.MaxCompressionRatio)
                {
                    stage.Findings.Add(new("error", "Suspicious compression ratio (possible zip bomb).", path));
                    continue;
                }

                // Declared sizes can lie, so the real byte count is enforced while reading.
                var remaining = Math.Min(opts.MaxFileBytes, opts.MaxUncompressedBytes - total);
                try
                {
                    await using var es = await entry.OpenAsync(ct);
                    var blob = await files.PutAsync(new LimitedReadStream(es, remaining), ct);
                    total += blob.SizeBytes;
                    stored.Add(new StoredFile(path, blob.Key, blob.Sha256, blob.SizeBytes));
                }
                catch (LimitExceededException)
                {
                    stage.Findings.Add(new("error", "Archive content exceeds the size limits while extracting (possible zip bomb).", path));
                    return;
                }
                catch (InvalidDataException ex) { stage.Findings.Add(new("error", $"Corrupt archive entry: {ex.Message}", path)); }
            }
            if (ignored > 0) stage.Findings.Add(new("info", $"Skipped {ignored} file(s) in build-output, dependency or VCS folders (bin, obj, node_modules, .git, ...)."));
            if (stored.Count == 0 && stage.Findings.All(f => f.Severity != "error")) stage.Findings.Add(new("error", "The archive contains no files."));
        }
    }

    /// <summary>Carries mappings forward from the parent/previous version by identifier, then applies naming conventions for new tests.</summary>
    private async Task<List<TestPackageTest>> MapTestsAsync(TestPackageVersion version, List<DiscoveredTest> tests, CancellationToken ct)
    {
        var previousId = version.ParentVersionId ?? await db.TestPackageVersions
            .Where(v => v.PackageId == version.PackageId && v.VersionNumber < version.VersionNumber).OrderByDescending(v => v.VersionNumber)
            .Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct);
        var previous = previousId is null ? [] : await db.TestPackageTests.AsNoTracking().Where(t => t.VersionId == previousId)
            .ToDictionaryAsync(t => t.Identifier, t => (t.TestCaseId, t.FeatureId), ct);

        var productId = await db.TestPackages.Where(p => p.Id == version.PackageId).Select(p => p.ProductId).FirstAsync(ct);
        var scopeId = productId is { } pid ? await db.Products.Where(p => p.Id == pid).Select(p => (Guid?)p.SequenceScopeId).FirstOrDefaultAsync(ct) : null;

        var rows = new List<TestPackageTest>();
        foreach (var t in tests)
        {
            var row = new TestPackageTest
            {
                VersionId = version.Id, Identifier = t.Identifier.Length > 800 ? t.Identifier[..800] : t.Identifier, DisplayName = t.DisplayName,
                FilePath = t.FilePath, Line = t.Line, Framework = t.Framework,
                Traits = t.Traits.Count == 0 ? null : JsonSerializer.Serialize(t.Traits),
            };
            if (previous.TryGetValue(t.Identifier, out var prev)) { row.TestCaseId = prev.TestCaseId; row.FeatureId = prev.FeatureId; }
            if (row.FeatureId is null && t.FeatureSequenceHint is { } seq && scopeId is { } sid)
                row.FeatureId = await db.Features.Where(f => f.SequenceScopeId == sid && f.SequenceNumber == seq).Select(f => (Guid?)f.Id).FirstOrDefaultAsync(ct);
            if (row.TestCaseId is null && t.TestCaseNumberHint is { } tcn)
                row.TestCaseId = await db.TestCases.Where(c => c.Number == tcn).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            rows.Add(row);
        }
        return rows;
    }

    private static void Finish(ValidationStage stage, string summary)
    {
        var status = stage.Findings.Any(f => f.Severity == "error") ? StageStatus.Failed
            : stage.Findings.Any(f => f.Severity == "warning") ? StageStatus.Warning : StageStatus.Passed;
        stage.Status = status;
        stage.Summary = summary;
    }
}
