using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Domain.Features;
using Wings.Domain.Testing;

namespace Wings.Application.Testing;

public interface IPackageService
{
    Task<IReadOnlyList<PackageSummaryDto>> ListAsync(Guid? productId, string? text, CancellationToken ct = default);
    Task<(PackageSummaryDto Package, IReadOnlyList<PackageVersionSummaryDto> Versions)> GetAsync(Guid packageId, CancellationToken ct = default);
    Task<PackageVersionSummaryDto> UploadAsync(UploadPackageRequest request, string fileName, Stream content, CancellationToken ct = default);
    Task<PackageVersionDetailDto> GetVersionAsync(Guid versionId, CancellationToken ct = default);
    Task<PackageFileContentDto> GetFileAsync(Guid versionId, string path, CancellationToken ct = default);
    Task<PackageVersionSummaryDto> EditFilesAsync(Guid packageId, EditPackageFilesRequest request, CancellationToken ct = default);
    Task<DiscoveredTestDto> MapTestAsync(Guid versionId, Guid testId, MapTestRequest request, CancellationToken ct = default);
    Task<PackageVersionDetailDto> ApproveAsync(Guid versionId, ReviewDecision decision, CancellationToken ct = default);
    Task<PackageVersionDetailDto> RejectAsync(Guid versionId, ReviewDecision decision, CancellationToken ct = default);
    Task<PackageVersionDetailDto> PublishAsync(Guid versionId, CancellationToken ct = default);
    Task<PackageVersionDetailDto> DeprecateAsync(Guid versionId, CancellationToken ct = default);
    Task<PackageVersionDetailDto> RevalidateAsync(Guid versionId, CancellationToken ct = default);
    Task<(Stream Content, string FileName)> DownloadAsync(Guid versionId, CancellationToken ct = default);
    /// <summary>Automated tests mapped to a feature, from the latest version of each package.</summary>
    Task<IReadOnlyList<FeatureAutomationDto>> ForFeatureAsync(Guid featureId, CancellationToken ct = default);
}

public sealed record FeatureAutomationDto(Guid PackageId, string PackageName, Guid VersionId, int VersionNumber, PackageStatus Status, DiscoveredTestDto Test);

public sealed class PackageOptions
{
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;
    public long MaxUncompressedBytes { get; set; } = 500L * 1024 * 1024;
    public long MaxFileBytes { get; set; } = 50L * 1024 * 1024;
    public int MaxEntries { get; set; } = 5000;
    public double MaxCompressionRatio { get; set; } = 200;
    public long MaxEditableFileBytes { get; set; } = 2L * 1024 * 1024;

    public static readonly string[] UploadExtensions =
        [".zip", ".cs", ".csproj", ".sln", ".slnx", ".ts", ".js", ".mjs", ".cjs", ".py", ".json", ".yaml", ".yml", ".runsettings", ".config", ".xml", ".props", ".targets"];
}

public sealed class PackageService(IWingsDbContext db, ISequenceAllocator sequences, IFileStore files, IJobQueue jobs, ICurrentUser user,
    TimeProvider clock, Microsoft.Extensions.Options.IOptions<PackageOptions> options, ITestCaseService testCases) : IPackageService
{
    public const string ScopeKey = "TESTPACKAGE";
    public const string ValidateJob = "package.validate";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public async Task<IReadOnlyList<PackageSummaryDto>> ListAsync(Guid? productId, string? text, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(ct);
        var q = db.TestPackages.AsNoTracking();
        if (productId is { } pid) q = q.Where(p => p.ProductId == pid);
        if (text.Clean() is { } t) q = q.Where(p => EF.Functions.Like(p.Name, Search.Like(t), Search.Escape));
        var rows = await q.OrderByDescending(p => p.CreatedAt).Select(p => new
        {
            p, Latest = p.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new { v.Status, v.DetectedFramework, Tests = v.Tests.Count }).FirstOrDefault(),
        }).ToListAsync(ct);
        return rows.Select(r => new PackageSummaryDto(r.p.Id, r.p.Number, SequenceFormat.Format(r.p.Number, scope.DisplayWidth, scope.Prefix), r.p.Name, r.p.Description,
            r.p.ProductId, r.p.LatestVersionNumber, r.Latest?.Status, r.Latest?.DetectedFramework, r.Latest?.Tests ?? 0, r.p.CreatedAt, r.p.CreatedBy)).ToList();
    }

    public async Task<(PackageSummaryDto Package, IReadOnlyList<PackageVersionSummaryDto> Versions)> GetAsync(Guid packageId, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(ct);
        var p = await db.TestPackages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == packageId, ct) ?? throw new NotFoundException("Test package", packageId);
        var versions = await VersionSummaries(db.TestPackageVersions.Where(v => v.PackageId == packageId).OrderByDescending(v => v.VersionNumber)).ToListAsync(ct);
        var latest = versions.FirstOrDefault();
        return (new PackageSummaryDto(p.Id, p.Number, SequenceFormat.Format(p.Number, scope.DisplayWidth, scope.Prefix), p.Name, p.Description, p.ProductId,
            p.LatestVersionNumber, latest?.Status, latest?.DetectedFramework, latest?.TestCount ?? 0, p.CreatedAt, p.CreatedBy), versions);
    }

    public async Task<PackageVersionSummaryDto> UploadAsync(UploadPackageRequest request, string fileName, Stream content, CancellationToken ct = default)
    {
        var name = Path.GetFileName(fileName ?? "");
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var v = new Validator()
            .Required(name, "file", 255)
            .Must(PackageOptions.UploadExtensions.Contains(ext), "file", $"'{ext}' files are not accepted. Upload a .zip of the test project or one of: {string.Join(", ", PackageOptions.UploadExtensions)}.")
            .MaxLength(request.SourceCommit, "sourceCommit", 100).MaxLength(request.SourceRepository, "sourceRepository", 400);
        if (request.PackageId is null) v.Required(request.Name, "name", 200);
        v.ThrowIfInvalid();
        if (request.ProductId is { } pid && !await db.Products.AnyAsync(p => p.Id == pid, ct)) throw new ValidationException("productId", "Unknown product.");

        // Store first (outside the transaction): the blob store is content-addressed, so an aborted request leaves at most an orphan blob.
        await using var limited = new LimitedReadStream(content, options.Value.MaxUploadBytes);
        StoredBlob blob;
        try { blob = await files.PutAsync(limited, ct); }
        catch (LimitExceededException) { throw new ValidationException("file", $"Uploads are limited to {options.Value.MaxUploadBytes / 1024 / 1024} MB."); }
        if (blob.SizeBytes == 0) throw new ValidationException("file", "The uploaded file is empty.");

        var versionId = await db.InTransactionAsync(async () =>
        {
            TestPackage package;
            if (request.PackageId is { } existingId)
                package = await db.TestPackages.FirstOrDefaultAsync(p => p.Id == existingId, ct) ?? throw new NotFoundException("Test package", existingId);
            else
            {
                package = new TestPackage
                {
                    Number = await sequences.NextAsync((await ScopeAsync(ct)).Id, ct), Name = request.Name!.Trim(),
                    Description = request.Description.Clean(), ProductId = request.ProductId,
                };
                db.TestPackages.Add(package);
            }
            var version = new TestPackageVersion
            {
                PackageId = package.Id, VersionNumber = ++package.LatestVersionNumber, Status = PackageStatus.Uploaded, OriginalFileName = name,
                ArchiveStorageKey = blob.Key, Sha256 = blob.Sha256, SizeBytes = blob.SizeBytes, SourceRepository = request.SourceRepository.Clean(),
                SourceCommit = request.SourceCommit.Clean(), ChangeNote = request.ChangeNote.Clean(),
            };
            db.TestPackageVersions.Add(version);
            await jobs.EnqueueAsync(ValidateJob, new { versionId = version.Id }, ct);
            db.PendingAuditAction = "Uploaded";
            await db.SaveChangesAsync(ct);
            return version.Id;
        }, ct);
        return await VersionSummaries(db.TestPackageVersions.Where(x => x.Id == versionId)).SingleAsync(ct);
    }

    public async Task<PackageVersionDetailDto> GetVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var v = await db.TestPackageVersions.AsNoTracking().Include(x => x.Package).FirstOrDefaultAsync(x => x.Id == versionId, ct)
                ?? throw new NotFoundException("Package version", versionId);
        var summary = await VersionSummaries(db.TestPackageVersions.Where(x => x.Id == versionId)).SingleAsync(ct);
        var fileList = await db.TestPackageFiles.AsNoTracking().Where(f => f.VersionId == versionId).OrderBy(f => f.Path)
            .Select(f => new PackageFileDto(f.Id, f.Path, f.SizeBytes, f.Sha256, f.IsText, f.Language)).ToListAsync(ct);
        var tests = await DiscoveredTests(db.TestPackageTests.Where(t => t.VersionId == versionId), ct);
        var report = v.ValidationReportJson is null ? null : JsonSerializer.Deserialize<ValidationReport>(v.ValidationReportJson, Json);
        return new PackageVersionDetailDto(summary, v.PackageId, v.Package!.Name, v.SourceRepository, report, fileList, tests,
            v.ReviewedBy, v.ReviewedAt, v.ReviewComment, v.PublishedBy, v.PublishedAt);
    }

    public async Task<PackageFileContentDto> GetFileAsync(Guid versionId, string path, CancellationToken ct = default)
    {
        var f = await db.TestPackageFiles.AsNoTracking().FirstOrDefaultAsync(x => x.VersionId == versionId && x.Path == path, ct) ?? throw new NotFoundException("File", path);
        if (!f.IsText) throw new ValidationException("path", "Binary files cannot be displayed.");
        if (f.SizeBytes > options.Value.MaxEditableFileBytes) throw new ValidationException("path", "The file is too large to display in the editor.");
        await using var s = await files.OpenReadAsync(f.StorageKey, ct);
        using var reader = new StreamReader(s, Encoding.UTF8);
        return new PackageFileContentDto(f.Path, f.Language ?? "plaintext", await reader.ReadToEndAsync(ct), f.Sha256);
    }

    public async Task<PackageVersionSummaryDto> EditFilesAsync(Guid packageId, EditPackageFilesRequest request, CancellationToken ct = default)
    {
        new Validator().Required(request.ChangeNote, "changeNote", 2000).Must(request.Edits.Count > 0, "edits", "No changes to save.").ThrowIfInvalid();
        var baseVersion = await db.TestPackageVersions.AsNoTracking().Include(v => v.Files).FirstOrDefaultAsync(v => v.Id == request.BaseVersionId && v.PackageId == packageId, ct)
                          ?? throw new NotFoundException("Package version", request.BaseVersionId);
        if (baseVersion.Files.Count == 0) throw new ConflictException("The base version has not been validated yet, so its files are not available for editing.");

        var fileSet = baseVersion.Files.ToDictionary(f => f.Path, f => (f.StorageKey, f.Sha256, f.SizeBytes));
        foreach (var edit in request.Edits)
        {
            var path = ArchivePath.Normalize(edit.Path) ?? throw new ValidationException("edits", $"'{edit.Path}' is not a valid relative path.");
            if (edit.Delete) { fileSet.Remove(path); continue; }
            var bytes = Encoding.UTF8.GetBytes(edit.Content ?? "");
            if (bytes.Length > options.Value.MaxEditableFileBytes) throw new ValidationException("edits", $"'{path}' exceeds the editable file size limit.");
            var blob = await files.PutAsync(new MemoryStream(bytes), ct);
            fileSet[path] = (blob.Key, blob.Sha256, blob.SizeBytes);
        }

        // Package the edited file set as a canonical zip so the version is a single hashed, immutable artifact like an upload.
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, f) in fileSet.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                await using var es = await entry.OpenAsync(ct);
                await using var src = await files.OpenReadAsync(f.StorageKey, ct);
                await src.CopyToAsync(es, ct);
            }
        zip.Position = 0;
        var archiveBlob = await files.PutAsync(zip, ct);

        var versionId = await db.InTransactionAsync(async () =>
        {
            var package = await db.TestPackages.FirstAsync(p => p.Id == packageId, ct);
            var version = new TestPackageVersion
            {
                PackageId = packageId, VersionNumber = ++package.LatestVersionNumber, Status = PackageStatus.Uploaded, ParentVersionId = baseVersion.Id,
                OriginalFileName = $"edit-of-v{baseVersion.VersionNumber}.zip", ArchiveStorageKey = archiveBlob.Key, Sha256 = archiveBlob.Sha256,
                SizeBytes = archiveBlob.SizeBytes, SourceRepository = baseVersion.SourceRepository, SourceCommit = baseVersion.SourceCommit, ChangeNote = request.ChangeNote.Trim(),
            };
            db.TestPackageVersions.Add(version);
            await jobs.EnqueueAsync(ValidateJob, new { versionId = version.Id }, ct);
            db.PendingAuditAction = "Edited";
            db.PendingAuditSummary = $"New version v{version.VersionNumber} from v{baseVersion.VersionNumber}: {string.Join(", ", request.Edits.Select(e => e.Path))}";
            await db.SaveChangesAsync(ct);
            return version.Id;
        }, ct);
        return await VersionSummaries(db.TestPackageVersions.Where(x => x.Id == versionId)).SingleAsync(ct);
    }

    public async Task<DiscoveredTestDto> MapTestAsync(Guid versionId, Guid testId, MapTestRequest request, CancellationToken ct = default)
    {
        var test = await db.TestPackageTests.FirstOrDefaultAsync(t => t.Id == testId && t.VersionId == versionId, ct) ?? throw new NotFoundException("Discovered test", testId);
        var version = await db.TestPackageVersions.FirstAsync(v => v.Id == versionId, ct);
        if (request.FeatureId is { } fid && !await db.Features.AnyAsync(f => f.Id == fid, ct)) throw new ValidationException("featureId", "Unknown feature.");
        // Creating a test case keeps the feature found by convention (trait/tag) unless another one is chosen.
        test.FeatureId = request.FeatureId ?? (request.CreateTestCase ? test.FeatureId : null);

        if (request.CreateTestCase)
        {
            var created = await testCases.CreateAsync(new SaveTestCaseRequest(new TestCaseFields
            {
                Name = test.DisplayName, Objective = $"Automated by {test.Identifier}", Platform = version.Platform,
                FrameworkCode = await db.TestingFrameworks.AnyAsync(f => f.Code == test.Framework, ct) ? test.Framework : null,
                AutomationStatus = version.Status == PackageStatus.Published ? AutomationStatus.Automated : AutomationStatus.AutomationPlanned,
                FeatureIds = test.FeatureId is { } f2 ? [f2] : [], PrimaryFeatureId = test.FeatureId,
            }, ChangeSummary: $"Created from discovered test {test.Identifier}"), ct);
            test.TestCaseId = created.Id;
        }
        else
        {
            if (request.TestCaseId is { } tcid && !await db.TestCases.AnyAsync(t => t.Id == tcid, ct)) throw new ValidationException("testCaseId", "Unknown test case.");
            test.TestCaseId = request.TestCaseId;
        }
        db.Touch(version);
        db.PendingAuditAction = "TestMapped";
        db.PendingAuditSummary = $"{test.Identifier} → test case {test.TestCaseId?.ToString() ?? "none"}, feature {test.FeatureId?.ToString() ?? "none"}";
        await db.SaveChangesAsync(ct);
        return (await DiscoveredTests(db.TestPackageTests.Where(t => t.Id == testId), ct)).Single();
    }

    public async Task<PackageVersionDetailDto> ApproveAsync(Guid versionId, ReviewDecision decision, CancellationToken ct = default)
    {
        var v = await LoadVersionAsync(versionId, ct);
        if (v.Status != PackageStatus.Validated) throw new ConflictException($"Only validated versions can be approved (this one is {v.Status}).");
        if (string.Equals(v.CreatedBy, user.UserName, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Test code must be approved by someone other than the uploader.");
        v.Status = PackageStatus.Approved; v.ReviewedBy = user.UserName; v.ReviewedAt = clock.GetUtcNow(); v.ReviewComment = decision.Comment.Clean();
        db.PendingAuditAction = "Approved";
        await db.SaveChangesAsync(ct);
        return await GetVersionAsync(versionId, ct);
    }

    public async Task<PackageVersionDetailDto> RejectAsync(Guid versionId, ReviewDecision decision, CancellationToken ct = default)
    {
        new Validator().Required(decision.Comment, "comment", 4000).ThrowIfInvalid();
        var v = await LoadVersionAsync(versionId, ct);
        if (v.Status is not (PackageStatus.Validated or PackageStatus.ValidationFailed or PackageStatus.Approved))
            throw new ConflictException($"A {v.Status} version cannot be rejected.");
        v.Status = PackageStatus.Rejected; v.ReviewedBy = user.UserName; v.ReviewedAt = clock.GetUtcNow(); v.ReviewComment = decision.Comment!.Trim();
        db.PendingAuditAction = "Rejected";
        await db.SaveChangesAsync(ct);
        return await GetVersionAsync(versionId, ct);
    }

    public async Task<PackageVersionDetailDto> PublishAsync(Guid versionId, CancellationToken ct = default)
    {
        await db.InTransactionAsync(async () =>
        {
            var v = await LoadVersionAsync(versionId, ct);
            if (v.Status != PackageStatus.Approved) throw new ConflictException("Only approved versions can be published.");
            v.Status = PackageStatus.Published; v.PublishedBy = user.UserName; v.PublishedAt = clock.GetUtcNow();
            // The newest published version supersedes older ones; their artifacts stay immutable for reproducing past runs.
            foreach (var older in await db.TestPackageVersions.Where(x => x.PackageId == v.PackageId && x.Status == PackageStatus.Published && x.Id != v.Id).ToListAsync(ct))
                older.Status = PackageStatus.Deprecated;
            var mapped = await db.TestPackageTests.Where(t => t.VersionId == v.Id && t.TestCaseId != null).Select(t => t.TestCaseId!.Value).Distinct().ToListAsync(ct);
            foreach (var tc in await db.TestCases.Where(t => mapped.Contains(t.Id) && t.AutomationStatus != AutomationStatus.Automated).ToListAsync(ct))
                tc.AutomationStatus = AutomationStatus.Automated;
            db.PendingAuditAction = "Published";
            await db.SaveChangesAsync(ct);
        }, ct);
        return await GetVersionAsync(versionId, ct);
    }

    public async Task<PackageVersionDetailDto> DeprecateAsync(Guid versionId, CancellationToken ct = default)
    {
        var v = await LoadVersionAsync(versionId, ct);
        if (v.Status == PackageStatus.Deprecated) return await GetVersionAsync(versionId, ct);
        v.Status = PackageStatus.Deprecated;
        db.PendingAuditAction = "Deprecated";
        await db.SaveChangesAsync(ct);
        return await GetVersionAsync(versionId, ct);
    }

    public async Task<PackageVersionDetailDto> RevalidateAsync(Guid versionId, CancellationToken ct = default)
    {
        var v = await LoadVersionAsync(versionId, ct);
        if (v.Status is not (PackageStatus.Uploaded or PackageStatus.ValidationFailed or PackageStatus.Validated))
            throw new ConflictException($"A {v.Status} version cannot be revalidated.");
        v.Status = PackageStatus.Uploaded;
        await jobs.EnqueueAsync(ValidateJob, new { versionId }, ct);
        db.PendingAuditAction = "RevalidationRequested";
        await db.SaveChangesAsync(ct);
        return await GetVersionAsync(versionId, ct);
    }

    public async Task<(Stream Content, string FileName)> DownloadAsync(Guid versionId, CancellationToken ct = default)
    {
        var v = await db.TestPackageVersions.AsNoTracking().Include(x => x.Package).FirstOrDefaultAsync(x => x.Id == versionId, ct) ?? throw new NotFoundException("Package version", versionId);
        if (v.ArchiveStorageKey is null) throw new NotFoundException("Package archive", versionId);
        var safe = string.Concat(v.Package!.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        var ext = Path.GetExtension(v.OriginalFileName);
        return (await files.OpenReadAsync(v.ArchiveStorageKey, ct), $"{safe}-v{v.VersionNumber}{ext}");
    }

    public async Task<IReadOnlyList<FeatureAutomationDto>> ForFeatureAsync(Guid featureId, CancellationToken ct = default)
    {
        var latest = db.TestPackageTests.Where(t => t.FeatureId == featureId && db.TestPackageVersions.Any(v => v.Id == t.VersionId && v.VersionNumber == v.Package!.LatestVersionNumber));
        var tests = await DiscoveredTests(latest, ct);
        var versionIds = await latest.Select(t => new { t.Id, t.VersionId }).ToDictionaryAsync(t => t.Id, t => t.VersionId, ct);
        var versions = await db.TestPackageVersions.AsNoTracking().Where(v => versionIds.Values.Contains(v.Id))
            .Select(v => new { v.Id, v.PackageId, v.Package!.Name, v.VersionNumber, v.Status }).ToDictionaryAsync(v => v.Id, ct);
        return tests.Select(t =>
        {
            var v = versions[versionIds[t.Id]];
            return new FeatureAutomationDto(v.PackageId, v.Name, v.Id, v.VersionNumber, v.Status, t);
        }).ToList();
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private async Task<TestPackageVersion> LoadVersionAsync(Guid id, CancellationToken ct)
        => await db.TestPackageVersions.FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("Package version", id);

    private async Task<SequenceScope> ScopeAsync(CancellationToken ct)
        => await db.SequenceScopes.AsNoTracking().FirstAsync(s => s.Key == ScopeKey, ct);

    private static IQueryable<PackageVersionSummaryDto> VersionSummaries(IQueryable<TestPackageVersion> q) => q.Select(v => new PackageVersionSummaryDto(
        v.Id, v.VersionNumber, v.Status, v.OriginalFileName, v.Sha256, v.SizeBytes, v.FileCount, v.Tests.Count, v.DetectedFramework, v.DetectedLanguage,
        v.Platform, v.SourceCommit, v.ParentVersionId, v.ChangeNote, v.CreatedAt, v.CreatedBy));

    private async Task<List<DiscoveredTestDto>> DiscoveredTests(IQueryable<TestPackageTest> q, CancellationToken ct)
    {
        var tcScope = await db.SequenceScopes.AsNoTracking().FirstAsync(s => s.Key == TestCaseService.ScopeKey, ct);
        var rows = await q.AsNoTracking().OrderBy(t => t.FilePath).ThenBy(t => t.Line).Select(t => new
        {
            t, TcNumber = (long?)t.TestCase!.Number, TcName = t.TestCase.Name, FName = t.Feature!.Name, FSeq = (long?)t.Feature.SequenceNumber,
            FWidth = (int?)t.Feature.Product!.SequenceScope!.DisplayWidth, FPrefix = t.Feature.Product.SequenceScope.Prefix,
        }).ToListAsync(ct);
        return rows.Select(r => new DiscoveredTestDto(r.t.Id, r.t.Identifier, r.t.DisplayName, r.t.FilePath, r.t.Line, r.t.Framework, r.t.Traits,
            r.t.TestCaseId, r.TcNumber is { } n ? SequenceFormat.Format(n, tcScope.DisplayWidth, tcScope.Prefix) : null, r.TcName,
            r.t.FeatureId, r.FName, r.FSeq is { } s ? SequenceFormat.Format(s, r.FWidth ?? 5, r.FPrefix) : null)).ToList();
    }
}

/// <summary>Normalizes archive/user-supplied paths and rejects traversal, absolute and drive-qualified paths.</summary>
public static class ArchivePath
{
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var p = raw.Replace('\\', '/').Trim();
        if (p.StartsWith('/') || p.Contains(':') || p.Contains('\0')) return null;
        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(s => s is "." or ".." || s.Trim() != s)) return null;
        var normalized = string.Join('/', parts);
        return normalized.Length > 800 ? null : normalized;
    }
}

public sealed class LimitExceededException() : IOException("Stream exceeded the configured size limit.");

/// <summary>Counts bytes actually read and throws once the limit is passed (does not trust declared lengths).</summary>
public sealed class LimitedReadStream(Stream inner, long limit) : Stream
{
    private long _read;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => Count(await inner.ReadAsync(buffer, ct));
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    private int Count(int n) { _read += n; return _read > limit ? throw new LimitExceededException() : n; }
}
