using Wings.Domain.Common;

namespace Wings.Domain.Orchestration;

public enum EnvironmentKind { Development = 0, QA = 1, Staging = 2, Preproduction = 3, Automation = 4, Production = 5 }

public enum EnvironmentHealth { Unknown = 0, Available = 1, Degraded = 2, Unavailable = 3 }

/// <summary>A target environment for test runs: endpoints, build, database, agent pools and variables.</summary>
public class TestEnvironment : Entity, IAudited, ISoftDeletable
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public EnvironmentKind Kind { get; set; }
    public string? Description { get; set; }
    /// <summary>Production environments get the destructive-test guard and need a separate permission to target.</summary>
    public bool IsProduction { get; set; }
    /// <summary>Exact test cases (ids, comma separated) explicitly authorised to run destructive operations here. Empty = none.</summary>
    public string? AllowedDestructiveTestCaseIds { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApplicationBuildVersion { get; set; }
    public string? DatabaseReference { get; set; }
    public string? TenantCompany { get; set; }
    public string? AuthenticationNotes { get; set; }
    public string? Dependencies { get; set; }
    /// <summary>Agent pools allowed to run this environment's jobs (comma separated). Empty = any pool.</summary>
    public string? AgentPools { get; set; }
    public string? HealthCheckUrl { get; set; }
    public EnvironmentHealth Health { get; set; }
    public string? HealthMessage { get; set; }
    public DateTimeOffset? LastHealthCheckAt { get; set; }
    public bool InMaintenance { get; set; }
    public string? ResetProcedure { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public List<EnvironmentVariable> Variables { get; set; } = [];

    public IReadOnlyList<string> Pools => (AgentPools ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Plain values are stored as-is; secret values only encrypted (<see cref="ProtectedValue"/>) and never returned to users.</summary>
public class EnvironmentVariable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EnvironmentId { get; set; }
    public string Name { get; set; } = "";
    public string? Value { get; set; }
    public string? ProtectedValue { get; set; }
    public bool IsSecret { get; set; }
    public string? Description { get; set; }
}

/// <summary>A versioned set of business records (customers, products, ledgers...) that tests reserve exclusively while running.</summary>
public class TestDataSet : Entity, IAudited, ISoftDeletable
{
    /// <summary>UPPER_SNAKE code used by test cases ("CUSTOMERS:2") and in the run context file.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Category { get; set; }
    /// <summary>When set, records are valid only for runs against this environment.</summary>
    public Guid? EnvironmentId { get; set; }
    public int DataVersion { get; set; } = 1;
    public string? SetupNotes { get; set; }
    public string? CleanupNotes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

public class TestDataRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DataSetId { get; set; }
    public string Key { get; set; } = "";
    /// <summary>JSON object of field values.</summary>
    public string ValuesJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A record held exclusively by one job; released when the job ends so parallel runs never share data.</summary>
public class TestDataReservation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RecordId { get; set; }
    public Guid DataSetId { get; set; }
    public Guid RunId { get; set; }
    public Guid JobId { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    /// <summary>1 while held; null once released (lets a unique index allow only one active reservation per record).</summary>
    public int? Active { get; set; } = 1;
}

/// <summary>Exclusive lock on a shared resource (database, company, user account, desktop...) held by a running job.</summary>
public class ResourceLock
{
    public string Key { get; set; } = "";
    public Guid JobId { get; set; }
    public Guid RunId { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
}

public enum PlanStatus { Draft = 0, Active = 1, Completed = 2, Approved = 3 }
public enum RiskLevel { Low = 0, Medium = 1, High = 2, Critical = 3 }

/// <summary>A testing objective for a release or environment, built from suites.</summary>
public class TestPlan : Entity, IAudited, ISoftDeletable
{
    public long Number { get; set; }
    public string Name { get; set; } = "";
    public string? Objective { get; set; }
    public string? Scope { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? TargetReleaseId { get; set; }
    public string? BuildVersion { get; set; }
    public Guid? EnvironmentId { get; set; }
    public string? Owners { get; set; }
    public string? Testers { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? EntryCriteria { get; set; }
    public string? ExitCriteria { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.Medium;
    public string? DefectAcceptanceRules { get; set; }
    public PlanStatus Status { get; set; }
    public bool IsTemplate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovalComment { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public List<TestPlanSuite> Suites { get; set; } = [];
}

public class TestPlanSuite
{
    public Guid PlanId { get; set; }
    public Guid SuiteId { get; set; }
    public int Order { get; set; }
}

public enum CampaignStatus { Planned = 0, Running = 1, Completed = 2, Cancelled = 3 }

/// <summary>An execution grouping of plans across environments and a build.</summary>
public class TestCampaign : Entity, IAudited, ISoftDeletable
{
    public long Number { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? BuildVersion { get; set; }
    public CampaignStatus Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public List<TestCampaignPlan> Plans { get; set; } = [];
    public List<TestCampaignEnvironment> Environments { get; set; } = [];
}

public class TestCampaignPlan { public Guid CampaignId { get; set; } public Guid PlanId { get; set; } }
public class TestCampaignEnvironment { public Guid CampaignId { get; set; } public Guid EnvironmentId { get; set; } }

public enum TriggerType { Once = 0, Hourly = 1, Daily = 2, Weekly = 3, Monthly = 4, Cron = 5, Webhook = 6 }
public enum ScheduleTarget { Suite = 0, Plan = 1, Campaign = 2, Feature = 3, Package = 4 }

/// <summary>Starts runs on a calendar (cron in a time zone) or when an external system calls its webhook (build, deployment).</summary>
public class TestSchedule : Entity, IAudited
{
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public TriggerType Trigger { get; set; }
    /// <summary>Five-field cron expression; for simple triggers it is generated from the chosen time and days.</summary>
    public string? CronExpression { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public DateTimeOffset? RunOnceAt { get; set; }
    public ScheduleTarget TargetType { get; set; }
    /// <summary>Suite, plan, campaign, feature or package id (package = its latest published version at trigger time).</summary>
    public Guid TargetId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public string? VariablesJson { get; set; }
    public int Priority { get; set; } = 50;
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastTriggeredAt { get; set; }
    public Guid? LastRunId { get; set; }
    public string? LastError { get; set; }
    public string? WebhookTokenHash { get; set; }
    public string? WebhookTokenPrefix { get; set; }
}
