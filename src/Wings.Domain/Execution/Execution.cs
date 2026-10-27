using Wings.Domain.Common;

namespace Wings.Domain.Execution;

public enum AgentStatus { Offline = 0, Online = 1, Maintenance = 2, Disabled = 3 }

/// <summary>A registered execution machine. It authenticates with a key whose SHA-256 hash is stored here.</summary>
public class ExecutionAgent : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Browser";
    public string KeyHash { get; set; } = "";
    /// <summary>First characters of the key, shown so administrators can tell keys apart.</summary>
    public string KeyPrefix { get; set; } = "";
    public string? Pool { get; set; }
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool InMaintenance { get; set; }
    public string? MachineName { get; set; }
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }
    public int Slots { get; set; } = 1;
    public string? FrameworksCsv { get; set; }
    public bool InteractiveSession { get; set; }
    public string? ScreenResolution { get; set; }
    public int? DpiScalePercent { get; set; }
    public int ProcessorCount { get; set; }
    public long TotalMemoryMb { get; set; }
    /// <summary>Tools, applications and browsers reported at registration, as JSON.</summary>
    public string? CapabilitiesJson { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset? RegisteredAt { get; set; }
    /// <summary>Set when an "agent offline" notification was sent for the current outage.</summary>
    public DateTimeOffset? OfflineNotifiedAt { get; set; }

    public IReadOnlyList<string> Frameworks => (FrameworksCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
}

public enum RunStatus { Queued = 0, Running = 1, Completed = 2, Cancelled = 3 }

/// <summary>One execution request (a batch). Infrastructure status lives on jobs; product outcomes live on test executions.</summary>
public class ExecutionRun : Entity
{
    public long Number { get; set; }
    public string Name { get; set; } = "";
    public RunStatus Status { get; set; }
    /// <summary>Human-readable description of what was selected ("Feature [00003] Create Enquiry incl. sub-features").</summary>
    public string ScopeDescription { get; set; } = "";
    public string? ScopeJson { get; set; }
    public int Priority { get; set; } = 50;
    /// <summary>When true, queued jobs of this run are not handed to agents (running jobs continue).</summary>
    public bool SchedulingPaused { get; set; }
    public bool CancelRequested { get; set; }
    public string? VariablesJson { get; set; }
    public int MaxInfrastructureRetries { get; set; } = 1;
    public int TimeoutMinutes { get; set; } = 30;
    public Guid? SourceRunId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? EnvironmentId { get; set; }
    public Guid? PlanId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? ScheduleId { get; set; }
    public string? BuildVersion { get; set; }
    /// <summary>Release whose readiness this run was started for (gates are re-evaluated when it finishes).</summary>
    public Guid? ReleaseId { get; set; }
    /// <summary>Defect whose fix this run retests.</summary>
    public Guid? DefectId { get; set; }
    /// <summary>Who or what started the run: a user name, "schedule:&lt;name&gt;" or "webhook:&lt;name&gt;".</summary>
    public string? TriggeredBy { get; set; }
    /// <summary>Manual test cases stay pending until a tester records their result; the run completes after that.</summary>
    public bool IncludeManual { get; set; }
    public int TotalTests { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int Blocked { get; set; }
    public int NotRun { get; set; }
    public int InfrastructureErrors { get; set; }
    public List<ExecutionJob> Jobs { get; set; } = [];
}

/// <summary>Orchestration/infrastructure lifecycle of one unit of work on one agent.</summary>
public enum JobStatus { Queued = 0, Leased = 1, Running = 2, Completed = 3, Cancelled = 4, TimedOut = 5, InfrastructureError = 6 }

/// <summary>The tests of one package version executed together on one agent.</summary>
public class ExecutionJob : Entity
{
    public Guid RunId { get; set; }
    public ExecutionRun? Run { get; set; }
    public Guid PackageVersionId { get; set; }
    public string Framework { get; set; } = "";
    public bool RequiresDesktop { get; set; }
    public JobStatus Status { get; set; }
    public int Attempt { get; set; } = 1;
    public Guid? AgentId { get; set; }
    public ExecutionAgent? Agent { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? LeasedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool CancelRequested { get; set; }
    public string TestIdentifiersJson { get; set; } = "[]";
    public string? Error { get; set; }
    public int? ExitCode { get; set; }
    /// <summary>Job this one retries after an infrastructure failure.</summary>
    public Guid? RetryOfJobId { get; set; }
    public int LogLineCount { get; set; }
    /// <summary>Dependency level: a job starts only after every job it depends on has finished.</summary>
    public int Wave { get; set; }
    public Guid? EnvironmentId { get; set; }
    public string? AgentPools { get; set; }
    public string? ResourceKeys { get; set; }
    /// <summary>JSON {"DATASET": count} reserved exclusively for this job when it is dispatched.</summary>
    public string? DataRequirementsJson { get; set; }

    public bool IsActive => Status is JobStatus.Leased or JobStatus.Running;
    public bool IsTerminal => Status is JobStatus.Completed or JobStatus.Cancelled or JobStatus.TimedOut or JobStatus.InfrastructureError;
}

/// <summary>The outcome of one test in one run attempt. Kept permanently with the exact code, agent and timing used.</summary>
public class TestExecution
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RunId { get; set; }
    public Guid? JobId { get; set; }
    public int Attempt { get; set; } = 1;
    public Guid? PackageVersionId { get; set; }
    public Guid? PackageTestId { get; set; }
    public Guid? TestCaseId { get; set; }
    public Guid? FeatureId { get; set; }
    public string Identifier { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Pending until reported; then Passed, Failed, Skipped, Blocked, NotRun or InfrastructureError.</summary>
    public string Outcome { get; set; } = "Pending";
    public string? Reason { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public string? Output { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? AgentId { get; set; }
    /// <summary>True once an infrastructure retry replaced this attempt; run totals count only the latest attempt.</summary>
    public bool IsSuperseded { get; set; }
    /// <summary>User who recorded a manual result.</summary>
    public string? ExecutedBy { get; set; }
    /// <summary>Ids of tests in the same run that must pass first (JSON array).</summary>
    public string? PrerequisitesJson { get; set; }
}

public class ExecutionJobDependency
{
    public Guid JobId { get; set; }
    public Guid DependsOnJobId { get; set; }
}

public class ExecutionArtifact
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RunId { get; set; }
    public Guid JobId { get; set; }
    public Guid? TestExecutionId { get; set; }
    public string Kind { get; set; } = "Other";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class ExecutionLog
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = "info";
    public string Message { get; set; } = "";
}
