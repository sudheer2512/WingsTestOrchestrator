using Wings.Domain.Features;

namespace Wings.Application.Features;

public sealed record ProductDto(Guid Id, string Code, string Name, string? Description, int DisplayOrder, bool IsActive,
    Guid SequenceScopeId, string SequenceScopeKey, int FeatureCount, long Version);

public sealed record ProductUpsert(string Code, string Name, string? Description, bool IsActive = true, int? DisplayOrder = null,
    // Existing scope key to share numbering with other products; null creates a dedicated scope.
    string? SequenceScopeKey = null, long? Version = null);

public sealed record ProductVersionDto(Guid Id, Guid ProductId, string VersionLabel, DateOnly? ReleaseDate, bool IsReleased, string? Notes, long Version);
public sealed record ProductVersionUpsert(string VersionLabel, DateOnly? ReleaseDate, bool IsReleased, string? Notes, long? Version = null);

public sealed record SequenceScopeDto(Guid Id, string Key, string Name, long NextValue, int DisplayWidth, string? Prefix);

public sealed record FeatureTreeNode(
    Guid Id, Guid ProductId, Guid? ParentId, string Name, long SequenceNumber, string SequenceDisplay, string FeatureTypeCode,
    string StatusCode, ApplicationType ApplicationType, int DisplayOrder, bool HasChildren, bool IsDeprecated,
    int TestCaseCount, int AutomatedTestCount, string CoverageStatus, DateTimeOffset CreatedAt);

/// <summary>Flat search/filter hit with its ancestor chain so the UI can show context and expand the tree to it.</summary>
public sealed record FeatureSearchHit(FeatureTreeNode Node, string ProductName, IReadOnlyList<FeatureBreadcrumb> Ancestors);

public sealed record FeatureBreadcrumb(Guid Id, string Name, string SequenceDisplay);

public sealed record FeatureFilter
{
    public Guid? ProductId { get; init; }
    public string? Text { get; init; }
    public string? StatusCode { get; init; }
    public string? FeatureTypeCode { get; init; }
    public ApplicationType? ApplicationType { get; init; }
    public long? SequenceFrom { get; init; }
    public long? SequenceTo { get; init; }
    public DateTimeOffset? CreatedAfter { get; init; }
    public Guid? IntroducedInVersionId { get; init; }
    /// <summary>Features created after the most recent released ProductVersion of the product.</summary>
    public bool SinceLastRelease { get; init; }
    public bool Untested { get; init; }
    public bool MissingAutomation { get; init; }
    public int? RecentDays { get; init; }
    public bool IncludeDeprecated { get; init; } = true;
    /// <summary>Restrict to the subtree under this node.</summary>
    public Guid? UnderFeatureId { get; init; }
    /// <summary>"tree" (display order), "sequence", "name" or "created".</summary>
    public string SortBy { get; init; } = "sequence";
    public bool Descending { get; init; }
}

public sealed record FeatureDetailDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public Guid? ParentId { get; init; }
    public string? ParentName { get; init; }
    public IReadOnlyList<FeatureBreadcrumb> Ancestors { get; init; } = [];
    public long SequenceNumber { get; init; }
    public string SequenceDisplay { get; init; } = "";
    public string SequenceScopeKey { get; init; } = "";
    public bool IsLegacySequence { get; init; }
    public int DisplayOrder { get; init; }
    public long Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset? ModifiedAt { get; init; }
    public string? ModifiedBy { get; init; }
    public string? IntroducedInVersionLabel { get; init; }
    public FeatureFields Fields { get; init; } = new();
    public FeatureTestingSummary Testing { get; init; } = new();
    public IReadOnlyList<RequirementDto> Requirements { get; init; } = [];
    public IReadOnlyList<SourceMappingDto> SourceMappings { get; init; } = [];
    public IReadOnlyList<AttachmentDto> Attachments { get; init; } = [];
    public IReadOnlyList<DependencyDto> Dependencies { get; init; } = [];
    public IReadOnlyList<DependencyDto> Dependents { get; init; } = [];
    public string? ReplacedByName { get; init; }
}

/// <summary>Editable feature fields, shared by create and update.</summary>
public sealed record FeatureFields
{
    public string Name { get; init; } = "";
    public string FeatureTypeCode { get; init; } = Domain.Features.FeatureTypes.Feature;
    public string StatusCode { get; init; } = FeatureStatuses.Planned;
    public ApplicationType ApplicationType { get; init; }
    public string? Description { get; init; }
    public string? BusinessObjective { get; init; }
    public string? FunctionalRequirements { get; init; }
    public string? CurrentVersion { get; init; }
    public Guid? IntroducedInVersionId { get; init; }
    public string? AssemblyName { get; init; }
    public string? Namespace { get; init; }
    public string? ClassName { get; init; }
    public string? FormOrRoute { get; init; }
    public string? RelatedServices { get; init; }
    public string? ApiEndpoints { get; init; }
    public string? DatabaseObjects { get; init; }
    public string? SourceRepository { get; init; }
    public string? BuildPackage { get; init; }
    public string? EntryPoint { get; init; }
    public string? RequiredPermissions { get; init; }
    public string? Preconditions { get; init; }
    public string? InputParameters { get; init; }
    public string? ExpectedOutcomes { get; init; }
    public string? BusinessRules { get; init; }
    public string? ValidationRules { get; init; }
    public string? ErrorConditions { get; init; }
    public string? UserRoles { get; init; }
    public string? KnownLimitations { get; init; }
    public bool IsDeprecated { get; init; }
    public Guid? ReplacedByFeatureId { get; init; }
    public string? DeprecationNote { get; init; }
}

public sealed record FeatureTestingSummary
{
    public int TestCaseCount { get; init; }
    public int AutomatedCount { get; init; }
    public int ManualCount { get; init; }
    public int ApprovedCount { get; init; }
    public int DraftCount { get; init; }
    public int PackagedTestCount { get; init; }
    public string CoverageStatus { get; init; } = "NONE";
    /// <summary>Latest run that executed this feature's automated tests, with its outcome counts for this feature.</summary>
    public DateTimeOffset? LastExecutedAt { get; init; }
    public string? LastResult { get; init; }
    public Guid? LastRunId { get; init; }
}

public sealed record CreateFeatureRequest(Guid ProductId, Guid? ParentId, FeatureFields Fields,
    // Only for imports: preserve a legacy sequence number instead of allocating one.
    long? LegacySequenceNumber = null);

public sealed record UpdateFeatureRequest(FeatureFields Fields, long Version);

public sealed record MoveFeatureRequest(Guid? NewParentId, Guid? NewProductId, int? Position, long Version);

public sealed record ReorderRequest(Guid ProductId, Guid? ParentId, IReadOnlyList<Guid> OrderedIds);

public sealed record DuplicateFeatureRequest(string? NewName, bool IncludeRequirements = true, bool IncludeTestMappings = false);

public sealed record RequirementDto(Guid Id, string Code, string Title, string? Description, string Priority, string? ExternalLink, long Version);
public sealed record RequirementUpsert(string Code, string Title, string? Description, string Priority = "MEDIUM", string? ExternalLink = null, long? Version = null);

public sealed record SourceMappingDto(Guid Id, SourceMappingKind Kind, string Value, string? Repository, string? Notes, bool IsApproved, string CreatedBy);
public sealed record SourceMappingUpsert(SourceMappingKind Kind, string Value, string? Repository, string? Notes);

public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, string? ExternalUrl, string? Caption, DateTimeOffset CreatedAt, string CreatedBy);

public sealed record DependencyDto(Guid Id, Guid FeatureId, string FeatureName, string FeatureSequence, DependencyType Type, string? Notes);
public sealed record DependencyUpsert(Guid DependsOnFeatureId, DependencyType Type, string? Notes);

public sealed record FeatureImportRow(string Path, string Name, long? SequenceNumber, string? FeatureTypeCode, string? StatusCode,
    ApplicationType? ApplicationType, string? Description, DateTimeOffset? CreatedAt, string? CreatedBy, string? OriginatingRelease);

public sealed record FeatureImportResult(int Created, int Skipped, IReadOnlyList<string> Errors);

public sealed record AuditEntryDto(long Id, DateTimeOffset Timestamp, string UserName, string Action, string EntityType, Guid EntityId, string? ChangesJson, string? Summary);

public sealed record AuditQuery(string? EntityType = null, Guid? EntityId = null, string? UserName = null, string? Action = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 50);

public sealed record LookupItem(string Code, string Name, int SortOrder);
