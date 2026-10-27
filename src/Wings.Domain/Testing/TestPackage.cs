using Wings.Domain.Common;
using Wings.Domain.Features;

namespace Wings.Domain.Testing;

/// <summary>Lifecycle: Uploaded → Validating → Validated | ValidationFailed → Approved → Published → Deprecated. Rejected ends a review.</summary>
public enum PackageStatus { Draft = 0, Uploaded = 1, Validating = 2, ValidationFailed = 3, Validated = 4, Approved = 5, Published = 6, Deprecated = 7, Rejected = 8 }

/// <summary>A logical automation package (e.g. "Wings Auto – Invoicing UI tests"). Content lives in immutable versions.</summary>
public class TestPackage : Entity, IAudited, ISoftDeletable
{
    public long Number { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ProductId { get; set; }
    public int LatestVersionNumber { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public List<TestPackageVersion> Versions { get; set; } = [];
}

/// <summary>An immutable, hashed set of files. Editing creates a new version derived from <see cref="ParentVersionId"/>.</summary>
public class TestPackageVersion : Entity, IAudited
{
    public Guid PackageId { get; set; }
    public TestPackage? Package { get; set; }
    public int VersionNumber { get; set; }
    public PackageStatus Status { get; set; }
    public Guid? ParentVersionId { get; set; }
    public string? ChangeNote { get; set; }
    public string OriginalFileName { get; set; } = "";
    /// <summary>Blob key of the uploaded (or, for edited versions, generated) archive or single file.</summary>
    public string? ArchiveStorageKey { get; set; }
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public string? SourceRepository { get; set; }
    public string? SourceCommit { get; set; }
    public string? DetectedFramework { get; set; }
    public string? DetectedLanguage { get; set; }
    public ApplicationType Platform { get; set; }
    /// <summary>Structured validation report (stages, findings, diagnostics) as JSON.</summary>
    public string? ValidationReportJson { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public List<TestPackageFile> Files { get; set; } = [];
    public List<TestPackageTest> Tests { get; set; } = [];
}

public class TestPackageFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid VersionId { get; set; }
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    /// <summary>Content-addressed key in the file store (identical content is stored once).</summary>
    public string StorageKey { get; set; } = "";
    public bool IsText { get; set; }
    public string? Language { get; set; }
}

/// <summary>A test method/identifier discovered by static analysis of a package version.</summary>
public class TestPackageTest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid VersionId { get; set; }
    public string Identifier { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public string Framework { get; set; } = "";
    public string? Traits { get; set; }
    public Guid? TestCaseId { get; set; }
    public TestCase? TestCase { get; set; }
    public Guid? FeatureId { get; set; }
    public Feature? Feature { get; set; }
}
