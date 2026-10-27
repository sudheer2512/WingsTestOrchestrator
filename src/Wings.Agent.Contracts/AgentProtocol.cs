namespace Wings.Agent.Contracts;

/// <summary>
/// Wire contract between the orchestrator and execution agents. Agents pull work over HTTPS (so they work behind
/// firewalls and NAT), authenticate with an agent key in the <see cref="AgentHeaders.Key"/> header, and hold each job
/// under a lease they renew with heartbeats. A job whose lease expires is treated as lost by the server.
/// </summary>
public static class AgentHeaders
{
    public const string Key = "X-Wings-Agent-Key";
}

public static class AgentKinds
{
    /// <summary>Interactive Windows desktop session for WinForms/FlaUI automation. Runs one UI job at a time.</summary>
    public const string Desktop = "Desktop";
    /// <summary>Headless or headed browser automation (Playwright).</summary>
    public const string Browser = "Browser";
    public const string Api = "Api";
    public const string Database = "Database";
}

public sealed record AgentRegistration
{
    public string MachineName { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string AgentVersion { get; init; } = "";
    public string Kind { get; init; } = AgentKinds.Browser;
    public int Slots { get; init; } = 1;
    /// <summary>Framework codes this agent can execute (FLAUI, XUNIT, NUNIT, MSTEST, PLAYWRIGHT_TS, PLAYWRIGHT_CS, PYTEST...).</summary>
    public IReadOnlyList<string> Frameworks { get; init; } = [];
    public bool InteractiveSession { get; init; }
    public int? ProcessSessionId { get; init; }
    public string? ScreenResolution { get; init; }
    public int? DpiScalePercent { get; init; }
    public int ProcessorCount { get; init; }
    public long TotalMemoryMb { get; init; }
    /// <summary>Tool and runtime versions discovered on the machine, e.g. {"dotnet":"10.0.100","node":"22.11.0","playwright":"1.56.1"}.</summary>
    public IReadOnlyDictionary<string, string> Tools { get; init; } = new Dictionary<string, string>();
    /// <summary>Applications under test installed on this machine (name → version).</summary>
    public IReadOnlyDictionary<string, string> Applications { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Browsers { get; init; } = [];
}

public sealed record AgentRegistrationResponse(Guid AgentId, string Name, int HeartbeatSeconds, int LeaseSeconds, bool Enabled);

public sealed record HeartbeatRequest(IReadOnlyList<Guid> ActiveJobIds, int FreeSlots);

/// <summary>Jobs the server wants stopped (user cancellation or lost lease) and whether this agent is still allowed to work.</summary>
public sealed record HeartbeatResponse(IReadOnlyList<Guid> CancelJobIds, bool Enabled, DateTimeOffset LeaseExpiresAt);

public sealed record ClaimRequest(int FreeSlots);

public sealed record JobAssignment
{
    public Guid JobId { get; init; }
    public Guid RunId { get; init; }
    public int Attempt { get; init; }
    public Guid PackageVersionId { get; init; }
    public string PackageName { get; init; } = "";
    public int PackageVersionNumber { get; init; }
    public string PackageSha256 { get; init; } = "";
    public string PackageFileName { get; init; } = "";
    public string Framework { get; init; } = "";
    /// <summary>Test identifiers exactly as discovered by the orchestrator; the agent runs only these.</summary>
    public IReadOnlyList<string> TestIdentifiers { get; init; } = [];
    /// <summary>Variables for the tests (base URLs, application paths...). Secrets must come from the agent's own configuration.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
    public int TimeoutMinutes { get; init; } = 30;
    public bool CaptureTrace { get; init; } = true;
    /// <summary>Names of variables whose values are secret; the agent masks them in logs and evidence it uploads.</summary>
    public IReadOnlyList<string> SecretNames { get; init; } = [];
    /// <summary>JSON written to the file named by WINGS_CONTEXT_FILE: run info, reserved test data and per-test parameter sets.</summary>
    public string ContextJson { get; init; } = "{}";
    public DateTimeOffset LeaseExpiresAt { get; init; }
}

public sealed record ClaimResponse(IReadOnlyList<JobAssignment> Jobs);

public sealed record LogLine(DateTimeOffset Timestamp, string Level, string Message);

public sealed record LogBatch(IReadOnlyList<LogLine> Lines);

public static class TestOutcomes
{
    public const string Passed = "Passed";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";
    public const string Blocked = "Blocked";
    public const string NotRun = "NotRun";
    public const string InfrastructureError = "InfrastructureError";
}

public sealed record TestResultReport
{
    public string Identifier { get; init; } = "";
    public string Outcome { get; init; } = TestOutcomes.Passed;
    public long DurationMs { get; init; }
    public string? ErrorMessage { get; init; }
    public string? StackTrace { get; init; }
    public string? Output { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record ResultBatch(IReadOnlyList<TestResultReport> Results);

public static class JobCompletions
{
    /// <summary>The test process ran to completion; individual outcomes are in the reported results.</summary>
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string TimedOut = "TimedOut";
    /// <summary>Preparation or tooling failed (download, restore, build, missing browser, no desktop session). Not a product defect.</summary>
    public const string InfrastructureError = "InfrastructureError";
}

public sealed record JobCompletion(string Status, string? Error, int? ExitCode);

public static class ArtifactKinds
{
    public const string Log = "Log";
    public const string Screenshot = "Screenshot";
    public const string Trace = "Trace";
    public const string Video = "Video";
    public const string Report = "Report";
    public const string Other = "Other";
}
