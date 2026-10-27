using Wings.Domain.Features;
using Wings.Domain.Testing;

namespace Wings.Application.Testing;

public sealed record TestCaseSummaryDto(
    Guid Id, long Number, string NumberDisplay, string Name, string TestTypeCode, ApplicationType Platform, string? FrameworkCode,
    string Priority, string FailureSeverity, AutomationStatus AutomationStatus, ReviewStatus ReviewStatus, int Revision, string? OwnerUserName,
    int FeatureCount, IReadOnlyList<string> Tags, Guid? FolderId, bool IsTemplate, bool IsSharedSteps, DateTimeOffset CreatedAt, DateTimeOffset? ModifiedAt);

public sealed record TestStepDto(int StepNumber, string Action, string? ExpectedResult, string? TestData, Guid? SharedStepsTestCaseId, string? SharedStepsName = null);

public sealed record LinkedFeatureDto(Guid FeatureId, string Name, string SequenceDisplay, string ProductName, bool IsPrimary);
public sealed record LinkedRequirementDto(Guid RequirementId, string Code, string Title, Guid FeatureId, string FeatureName);
public sealed record TestCaseDependencyDto(Guid Id, Guid DependsOnTestCaseId, string NumberDisplay, string Name, DependencyType Type, string? Notes);
public sealed record LinkedAutomationDto(Guid PackageId, string PackageName, int VersionNumber, PackageStatus Status, string Identifier, string FilePath, int Line);

public sealed record TestCaseFields
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? Objective { get; init; }
    public string TestTypeCode { get; init; } = TestTypes.Functional;
    public ApplicationType Platform { get; init; }
    public string? FrameworkCode { get; init; }
    public string Priority { get; init; } = "P2";
    public string FailureSeverity { get; init; } = "MAJOR";
    public string? Preconditions { get; init; }
    public string? TestDataRequirements { get; init; }
    public string? ExpectedResults { get; init; }
    public string? CleanupSteps { get; init; }
    public int? EstimatedDurationMinutes { get; init; }
    public AutomationStatus AutomationStatus { get; init; }
    public string? OwnerUserName { get; init; }
    public Guid? FolderId { get; init; }
    public bool IsTemplate { get; init; }
    public bool IsSharedSteps { get; init; }
    public string? ParametersJson { get; init; }
    public bool IsDestructive { get; init; }
    public string? ResourceKeys { get; init; }
    public string? DataRequirements { get; init; }
    public IReadOnlyList<TestStepDto> Steps { get; init; } = [];
    public IReadOnlyList<Guid> FeatureIds { get; init; } = [];
    public Guid? PrimaryFeatureId { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<Guid> RequirementIds { get; init; } = [];
}

public sealed record TestCaseDetailDto
{
    public Guid Id { get; init; }
    public long Number { get; init; }
    public string NumberDisplay { get; init; } = "";
    public int Revision { get; init; }
    public ReviewStatus ReviewStatus { get; init; }
    public string? ReviewComment { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public long Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset? ModifiedAt { get; init; }
    public string? ModifiedBy { get; init; }
    public TestCaseFields Fields { get; init; } = new();
    public IReadOnlyList<LinkedFeatureDto> Features { get; init; } = [];
    public IReadOnlyList<LinkedRequirementDto> Requirements { get; init; } = [];
    public IReadOnlyList<TestCaseDependencyDto> Dependencies { get; init; } = [];
    public IReadOnlyList<LinkedAutomationDto> Automation { get; init; } = [];
    public IReadOnlyList<SuiteRefDto> Suites { get; init; } = [];
}

public sealed record SuiteRefDto(Guid Id, string NumberDisplay, string Name);

public sealed record SaveTestCaseRequest(TestCaseFields Fields, long? Version = null, string? ChangeSummary = null);

public sealed record TestCaseFilter
{
    public string? Text { get; init; }
    public Guid? FeatureId { get; init; }
    public bool IncludeSubfeatures { get; init; }
    public Guid? FolderId { get; init; }
    public string? TestTypeCode { get; init; }
    public AutomationStatus? AutomationStatus { get; init; }
    public ReviewStatus? ReviewStatus { get; init; }
    public string? Priority { get; init; }
    public string? Tag { get; init; }
    public ApplicationType? Platform { get; init; }
    public bool? IsTemplate { get; init; }
    public bool? IsSharedSteps { get; init; }
    public string? OwnerUserName { get; init; }
    public Guid? SuiteId { get; init; }
    public string SortBy { get; init; } = "number";
    public bool Descending { get; init; }
}

public sealed record BulkUpdateRequest(
    IReadOnlyList<Guid> Ids, string? Priority = null, string? FailureSeverity = null, string? OwnerUserName = null, AutomationStatus? AutomationStatus = null,
    string? TestTypeCode = null, Guid? FolderId = null, bool ClearFolder = false, IReadOnlyList<string>? AddTags = null, IReadOnlyList<string>? RemoveTags = null,
    IReadOnlyList<Guid>? AddFeatureIds = null);

public sealed record BulkResult(int Updated, IReadOnlyList<string> Errors);

public sealed record TestCaseVersionDto(int Revision, string? ChangeSummary, DateTimeOffset CreatedAt, string CreatedBy, string SnapshotJson);

public sealed record ReviewDecision(string? Comment);

public sealed record CloneRequest(string? NewName, bool AsTemplate = false, bool KeepFeatureLinks = true);

public sealed record TestCaseDependencyUpsert(Guid DependsOnTestCaseId, DependencyType Type = DependencyType.Prerequisite, string? Notes = null);

public sealed record ImportResult(int Created, IReadOnlyList<string> Errors);

public sealed record FolderDto(Guid Id, string Name, Guid? ParentId, int DisplayOrder, int TestCaseCount);
public sealed record FolderUpsert(string Name, Guid? ParentId);

public sealed record SuiteSummaryDto(Guid Id, long Number, string NumberDisplay, string Name, string? Description, Guid? ProductId, string? Tags, string? OwnerUserName, int ItemCount, int AutomatedCount, long Version);
public sealed record SuiteItemDto(Guid ItemId, int Order, TestCaseSummaryDto TestCase);
public sealed record SuiteDetailDto(SuiteSummaryDto Suite, IReadOnlyList<SuiteItemDto> Items);
public sealed record SuiteUpsert(string Name, string? Description, Guid? ProductId, string? Tags, string? OwnerUserName, long? Version = null);
public sealed record SuiteItemsRequest(IReadOnlyList<Guid> TestCaseIds);

// ---- Test packages ------------------------------------------------------------------------------------------------

public sealed record PackageSummaryDto(Guid Id, long Number, string NumberDisplay, string Name, string? Description, Guid? ProductId,
    int LatestVersionNumber, PackageStatus? LatestStatus, string? Framework, int TestCount, DateTimeOffset CreatedAt, string CreatedBy);

public sealed record PackageVersionSummaryDto(Guid Id, int VersionNumber, PackageStatus Status, string OriginalFileName, string Sha256, long SizeBytes,
    int FileCount, int TestCount, string? DetectedFramework, string? DetectedLanguage, ApplicationType Platform, string? SourceCommit,
    Guid? ParentVersionId, string? ChangeNote, DateTimeOffset CreatedAt, string CreatedBy);

public sealed record PackageVersionDetailDto(PackageVersionSummaryDto Summary, Guid PackageId, string PackageName, string? SourceRepository,
    ValidationReport? Report, IReadOnlyList<PackageFileDto> Files, IReadOnlyList<DiscoveredTestDto> Tests,
    string? ReviewedBy, DateTimeOffset? ReviewedAt, string? ReviewComment, string? PublishedBy, DateTimeOffset? PublishedAt);

public sealed record PackageFileDto(Guid Id, string Path, long SizeBytes, string Sha256, bool IsText, string? Language);
public sealed record PackageFileContentDto(string Path, string Language, string Content, string Sha256);

public sealed record DiscoveredTestDto(Guid Id, string Identifier, string DisplayName, string FilePath, int Line, string Framework, string? Traits,
    Guid? TestCaseId, string? TestCaseNumber, string? TestCaseName, Guid? FeatureId, string? FeatureName, string? FeatureSequence);

public sealed record UploadPackageRequest(Guid? PackageId, string? Name, string? Description, Guid? ProductId, string? SourceRepository, string? SourceCommit, string? ChangeNote);

public sealed record EditPackageFilesRequest(Guid BaseVersionId, IReadOnlyList<FileEdit> Edits, string ChangeNote);
public sealed record FileEdit(string Path, string? Content, bool Delete = false);

public sealed record MapTestRequest(Guid? TestCaseId, Guid? FeatureId, bool CreateTestCase = false);

public sealed record ValidationReport
{
    public List<ValidationStage> Stages { get; init; } = [];
    public string? Framework { get; set; }
    public string? Language { get; set; }
    public ApplicationType Platform { get; set; }
    public bool Passed => Stages.All(s => s.Status != StageStatus.Failed);
}

public enum StageStatus { Passed, Warning, Failed, Skipped, Pending }

public sealed class ValidationStage
{
    public ValidationStage() { }
    public ValidationStage(string name, StageStatus status, string summary) { Name = name; Status = status; Summary = summary; }
    public string Name { get; set; } = "";
    public StageStatus Status { get; set; }
    public string Summary { get; set; } = "";
    public List<ValidationFinding> Findings { get; init; } = [];
}

public sealed record ValidationFinding(string Severity, string Message, string? File = null, int? Line = null);
