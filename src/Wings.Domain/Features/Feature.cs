using Wings.Domain.Common;

namespace Wings.Domain.Features;

public enum ApplicationType { None = 0, WinForms = 1, Browser = 2, Api = 3, Service = 4, Database = 5, Mixed = 6 }

/// <summary>
/// Any node in the product hierarchy below the product itself (module, submodule, feature, subfeature, operation...).
/// The hierarchy is unlimited; <see cref="FeatureTypeCode"/> describes what a node represents.
/// </summary>
public class Feature : Entity, IAudited, ISoftDeletable
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid? ParentId { get; set; }
    public Feature? Parent { get; set; }

    /// <summary>
    /// Materialized path of ancestor sequence numbers including this node: "/125/131/146/". Sequence numbers are
    /// immutable and unique within the scope, so the path is stable and compact (about 100 levels fit the indexed column).
    /// </summary>
    public string Path { get; set; } = "";
    public int Depth { get; set; }

    /// <summary>Order among siblings. Changes on drag-and-drop; never affects <see cref="SequenceNumber"/>.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Creation sequence, unique within <see cref="SequenceScopeId"/>. Immutable after creation.</summary>
    public long SequenceNumber { get; set; }
    public Guid SequenceScopeId { get; set; }
    public bool IsLegacySequence { get; set; }

    public string Name { get; set; } = "";
    public string FeatureTypeCode { get; set; } = FeatureTypes.Feature;
    public string StatusCode { get; set; } = FeatureStatuses.Planned;
    public ApplicationType ApplicationType { get; set; }

    // Identification
    public string? Description { get; set; }
    public string? BusinessObjective { get; set; }
    public string? FunctionalRequirements { get; set; }
    public string? CurrentVersion { get; set; }
    public Guid? IntroducedInVersionId { get; set; }
    public ProductVersion? IntroducedInVersion { get; set; }
    /// <summary>Release label captured at creation; kept even if the ProductVersion record is later renamed.</summary>
    public string? OriginatingRelease { get; set; }
    public bool IsDeprecated { get; set; }
    public Guid? ReplacedByFeatureId { get; set; }
    public string? DeprecationNote { get; set; }

    // Technical information
    public string? AssemblyName { get; set; }
    public string? Namespace { get; set; }
    public string? ClassName { get; set; }
    public string? FormOrRoute { get; set; }
    public string? RelatedServices { get; set; }
    public string? ApiEndpoints { get; set; }
    public string? DatabaseObjects { get; set; }
    public string? SourceRepository { get; set; }
    public string? BuildPackage { get; set; }
    public string? EntryPoint { get; set; }
    public string? RequiredPermissions { get; set; }

    // Business behaviour (markdown)
    public string? Preconditions { get; set; }
    public string? InputParameters { get; set; }
    public string? ExpectedOutcomes { get; set; }
    public string? BusinessRules { get; set; }
    public string? ValidationRules { get; set; }
    public string? ErrorConditions { get; set; }
    public string? UserRoles { get; set; }
    public string? KnownLimitations { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public List<FeatureRequirement> Requirements { get; set; } = [];
    public List<FeatureSourceMapping> SourceMappings { get; set; } = [];
    public List<FeatureAttachment> Attachments { get; set; } = [];
}

public static class FeatureTypes
{
    public const string Module = "MODULE";
    public const string Submodule = "SUBMODULE";
    public const string Feature = "FEATURE";
    public const string Subfeature = "SUBFEATURE";
    public const string Operation = "OPERATION";
}

public static class FeatureStatuses
{
    public const string Planned = "PLANNED";
    public const string InDevelopment = "IN_DEVELOPMENT";
    public const string Active = "ACTIVE";
    public const string Deprecated = "DEPRECATED";
    public const string Retired = "RETIRED";
}

/// <summary>Master list of node types. Administrators can add types without code changes.</summary>
public class FeatureType
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsContainer { get; set; }
    public string? Icon { get; set; }
}

public class FeatureStatus
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsTerminal { get; set; }
}

public class FeatureRequirement : Entity, IAudited
{
    public Guid FeatureId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Priority { get; set; } = "MEDIUM";
    public string? ExternalLink { get; set; }
}

public enum SourceMappingKind { File = 0, Assembly = 1, Class = 2, Form = 3, ApiEndpoint = 4, DatabaseTable = 5, DatabaseView = 6, StoredProcedure = 7, Route = 8 }

public class FeatureSourceMapping : Entity, IAudited
{
    public Guid FeatureId { get; set; }
    public SourceMappingKind Kind { get; set; }
    public string Value { get; set; } = "";
    public string? Repository { get; set; }
    public string? Notes { get; set; }
    /// <summary>Mappings that could not be inferred automatically must be declared and approved by a developer.</summary>
    public bool IsApproved { get; set; }
}

public enum DependencyType { Prerequisite = 0, SetupDependency = 1, SharedResource = 2, DataDependency = 3, Related = 4 }

public class FeatureDependency : Entity, IAudited
{
    public Guid FeatureId { get; set; }
    public Feature? Feature { get; set; }
    public Guid DependsOnFeatureId { get; set; }
    public Feature? DependsOnFeature { get; set; }
    public DependencyType Type { get; set; }
    public string? Notes { get; set; }
}

public class FeatureAttachment : Entity, IAudited
{
    public Guid FeatureId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    /// <summary>Key inside the configured file store. Null for link-only attachments.</summary>
    public string? StorageKey { get; set; }
    public string? ExternalUrl { get; set; }
    public string? Caption { get; set; }
}
