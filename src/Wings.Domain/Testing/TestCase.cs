using Wings.Domain.Common;
using Wings.Domain.Features;

namespace Wings.Domain.Testing;

public enum AutomationStatus { Manual = 0, AutomationPlanned = 1, Automated = 2, NotAutomatable = 3 }

public enum ReviewStatus { Draft = 0, InReview = 1, Approved = 2, Rejected = 3, Obsolete = 4 }

public class TestCase : Entity, IAudited, ISoftDeletable
{
    /// <summary>Human-readable number ("TC-00042"), allocated from the test-case sequence scope.</summary>
    public long Number { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Objective { get; set; }
    public string TestTypeCode { get; set; } = TestTypes.Functional;
    public ApplicationType Platform { get; set; }
    public string? FrameworkCode { get; set; }
    public string Priority { get; set; } = "P2";
    public string FailureSeverity { get; set; } = "MAJOR";
    public string? Preconditions { get; set; }
    public string? TestDataRequirements { get; set; }
    public string? ExpectedResults { get; set; }
    public string? CleanupSteps { get; set; }
    public int? EstimatedDurationMinutes { get; set; }
    public AutomationStatus AutomationStatus { get; set; }
    public string? OwnerUserName { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    /// <summary>Content revision; every saved change creates an immutable <see cref="TestCaseVersion"/> snapshot.</summary>
    public int Revision { get; set; } = 1;
    public Guid? FolderId { get; set; }
    public TestFolder? Folder { get; set; }
    public bool IsTemplate { get; set; }
    /// <summary>A reusable block of steps that other test cases can call.</summary>
    public bool IsSharedSteps { get; set; }
    /// <summary>JSON array of parameter sets: [{"name":"GST 18%","values":{"rate":"18"}}].</summary>
    public string? ParametersJson { get; set; }
    /// <summary>Modifies or deletes business data; blocked on production environments unless explicitly authorised there.</summary>
    public bool IsDestructive { get; set; }
    /// <summary>Shared resources the test needs exclusively while running, comma separated (e.g. "db:AUTO-QA,user:sales01").</summary>
    public string? ResourceKeys { get; set; }
    /// <summary>Test data to reserve, comma separated DATASET[:count] (e.g. "CUSTOMERS:2,VEHICLES").</summary>
    public string? DataRequirements { get; set; }
    public string? ReviewComment { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public List<TestStep> Steps { get; set; } = [];
    public List<TestCaseFeatureMapping> FeatureMappings { get; set; } = [];
    public List<TestCaseTag> Tags { get; set; } = [];
    public List<TestCaseRequirementLink> RequirementLinks { get; set; } = [];
}

public class TestStep
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TestCaseId { get; set; }
    public int StepNumber { get; set; }
    public string Action { get; set; } = "";
    public string? ExpectedResult { get; set; }
    public string? TestData { get; set; }
    /// <summary>When set, this step invokes the steps of a shared-steps test case.</summary>
    public Guid? SharedStepsTestCaseId { get; set; }
}

public class TestCaseFeatureMapping
{
    public Guid TestCaseId { get; set; }
    public Guid FeatureId { get; set; }
    public Feature? Feature { get; set; }
    public bool IsPrimary { get; set; }
}

public class TestCaseTag
{
    public Guid TestCaseId { get; set; }
    public string Tag { get; set; } = "";
}

public class TestCaseRequirementLink
{
    public Guid TestCaseId { get; set; }
    public Guid RequirementId { get; set; }
    public FeatureRequirement? Requirement { get; set; }
}

/// <summary>Immutable snapshot of a test case at a given revision.</summary>
public class TestCaseVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TestCaseId { get; set; }
    public int Revision { get; set; }
    public string SnapshotJson { get; set; } = "";
    public string? ChangeSummary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public class TestCaseDependency : Entity, IAudited
{
    public Guid TestCaseId { get; set; }
    public Guid DependsOnTestCaseId { get; set; }
    public TestCase? DependsOnTestCase { get; set; }
    public DependencyType Type { get; set; }
    public string? Notes { get; set; }
}

public class TestFolder : Entity, IAudited
{
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public string Path { get; set; } = "";
    public int DisplayOrder { get; set; }
}

public class TestSuite : Entity, IAudited, ISoftDeletable
{
    public long Number { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ProductId { get; set; }
    public string? Tags { get; set; }
    public string? OwnerUserName { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public List<TestSuiteItem> Items { get; set; } = [];
}

public class TestSuiteItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SuiteId { get; set; }
    public Guid TestCaseId { get; set; }
    public TestCase? TestCase { get; set; }
    public int Order { get; set; }
}

public static class TestTypes
{
    public const string Functional = "FUNCTIONAL";
}

/// <summary>Master list of test types; custom types can be added by administrators.</summary>
public class TestType
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsCustom { get; set; }
}

/// <summary>Master list of automation frameworks the platform knows about.</summary>
public class TestingFramework
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Language { get; set; } = "";
    public ApplicationType Platform { get; set; }
}
