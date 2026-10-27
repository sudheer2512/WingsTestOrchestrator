using Wings.Domain.Execution;

namespace Wings.Application.Execution;

/// <summary>What to execute. Combine any of the selectors; the union is resolved to published automated tests.</summary>
public sealed record StartRunRequest
{
    public string? Name { get; init; }
    public IReadOnlyList<Guid> TestCaseIds { get; init; } = [];
    public Guid? FeatureId { get; init; }
    public bool IncludeSubfeatures { get; init; } = true;
    public Guid? SuiteId { get; init; }
    public IReadOnlyList<Guid> SuiteIds { get; init; } = [];
    /// <summary>Target environment: supplies variables (incl. decrypted secrets at dispatch), agent pools and the production guard.</summary>
    public Guid? EnvironmentId { get; init; }
    public string? BuildVersion { get; init; }
    /// <summary>Keep manual test cases pending for testers to record, instead of marking them not run.</summary>
    public bool IncludeManual { get; init; }
    /// <summary>Start even if the environment is in maintenance or failed its last health check.</summary>
    public bool IgnoreEnvironmentHealth { get; init; }
    /// <summary>Run every discovered test of one package version (Published, or Approved for a pre-publication check).</summary>
    public Guid? PackageVersionId { get; init; }
    /// <summary>With <see cref="SequenceFrom"/>: features of this product created at or after that sequence number.</summary>
    public Guid? ProductId { get; init; }
    public long? SequenceFrom { get; init; }
    /// <summary>Re-run tests from an earlier run on the exact same package versions.</summary>
    public Guid? RerunOfRunId { get; init; }
    public bool OnlyFailed { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
    public int Priority { get; init; } = 50;
    public int MaxInfrastructureRetries { get; init; } = 1;
    public int TimeoutMinutes { get; init; } = 30;
}

public sealed record RunSummaryDto(
    Guid Id, long Number, string NumberDisplay, string Name, RunStatus Status, string ScopeDescription, int Priority, bool SchedulingPaused,
    bool CancelRequested, int TotalTests, int Passed, int Failed, int Skipped, int Blocked, int NotRun, int InfrastructureErrors, int Pending,
    int JobCount, DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, Guid? SourceRunId,
    Guid? EnvironmentId, string? EnvironmentName, string? BuildVersion, string? TriggeredBy, Guid? PlanId, Guid? CampaignId, Guid? ScheduleId, bool IncludeManual);

/// <summary>Who started a run and from which plan, campaign or schedule; internal callers skip user permission checks.</summary>
public sealed record RunOrigin(string TriggeredBy, Guid? PlanId = null, Guid? CampaignId = null, Guid? ScheduleId = null, bool CheckPermissions = true);

public sealed record ManualResultRequest(string Outcome, string? Comment);

public sealed record JobDto(
    Guid Id, Guid PackageVersionId, string PackageName, int PackageVersionNumber, string Framework, bool RequiresDesktop, JobStatus Status,
    int Attempt, Guid? AgentId, string? AgentName, DateTimeOffset? LeaseExpiresAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    bool CancelRequested, string? Error, int? ExitCode, int TestCount, int LogLineCount, Guid? RetryOfJobId, int Wave, int WaitingOn, string? ResourceKeys, string? DataRequirements);

public sealed record TestExecutionDto(
    Guid Id, Guid RunId, Guid? JobId, int Attempt, string Identifier, string DisplayName, string Outcome, string? Reason, long? DurationMs,
    string? ErrorMessage, string? StackTrace, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, Guid? TestCaseId, string? TestCaseNumber,
    Guid? FeatureId, string? FeatureName, string? AgentName, bool IsSuperseded, IReadOnlyList<ArtifactDto> Artifacts, string? ExecutedBy, bool IsManual, int PrerequisiteCount);

public sealed record ArtifactDto(Guid Id, Guid JobId, Guid? TestExecutionId, string Kind, string FileName, string ContentType, long SizeBytes, string Sha256, DateTimeOffset CreatedAt);

public sealed record RunDetailDto(RunSummaryDto Run, IReadOnlyDictionary<string, string> Variables, IReadOnlyList<JobDto> Jobs,
    IReadOnlyList<TestExecutionDto> Tests, IReadOnlyList<ArtifactDto> JobArtifacts, int MaxInfrastructureRetries, int TimeoutMinutes);

public sealed record LogLineDto(int Sequence, DateTimeOffset Timestamp, string Level, string Message);

public sealed record RunFilter(RunStatus? Status = null, string? Text = null, int Page = 1, int PageSize = 50, Guid? PlanId = null, Guid? CampaignId = null, Guid? EnvironmentId = null, Guid? ScheduleId = null);

/// <summary>Recent outcomes of a feature's or test case's tests across runs.</summary>
public sealed record ExecutionHistoryDto(Guid RunId, string RunNumber, string RunName, Guid TestExecutionId, string Identifier, string DisplayName,
    string Outcome, string? ErrorMessage, long? DurationMs, DateTimeOffset? CompletedAt, string? AgentName, int PackageVersionNumber, string? PackageName);

public sealed record AgentDto(
    Guid Id, string Name, string Kind, string? Pool, string? Description, bool IsEnabled, bool InMaintenance, bool IsOnline, string? MachineName,
    string? OperatingSystem, string? AgentVersion, int Slots, IReadOnlyList<string> Frameworks, bool InteractiveSession, string? ScreenResolution,
    int? DpiScalePercent, int ProcessorCount, long TotalMemoryMb, string? CapabilitiesJson, DateTimeOffset? LastHeartbeatAt, DateTimeOffset? RegisteredAt,
    string KeyPrefix, int ActiveJobs, long Version);

public sealed record AgentUpsert(string Name, string Kind, string? Pool, string? Description, bool IsEnabled = true, bool InMaintenance = false, long? Version = null);

/// <summary>Returned once when an agent is created or its key is rotated; the key is never retrievable again.</summary>
public sealed record AgentKeyResult(AgentDto Agent, string Key);
public sealed record ResourceLockDto(string Key, Guid RunId, string RunNumber, string RunName, Guid JobId, string? AgentName, DateTimeOffset AcquiredAt);
