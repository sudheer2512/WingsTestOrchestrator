using Wings.Domain.Common;

namespace Wings.Domain.Quality;

public enum DefectStatus { Open = 0, Assigned = 1, InProgress = 2, Fixed = 3, ReadyForRetest = 4, Verified = 5, Closed = 6, Reopened = 7, Duplicate = 8, Rejected = 9, Deferred = 10 }
public enum DefectSeverity { Critical = 0, Major = 1, Minor = 2, Trivial = 3 }

public class Defect : Entity, IAudited, ISoftDeletable
{
    public long Number { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? FeatureId { get; set; }
    public Guid? TestCaseId { get; set; }
    /// <summary>The test execution that revealed the defect.</summary>
    public Guid? FailingTestExecutionId { get; set; }
    public Guid? FailingRunId { get; set; }
    public string? BuildVersion { get; set; }
    public Guid? EnvironmentId { get; set; }
    public DefectSeverity Severity { get; set; } = DefectSeverity.Major;
    public string Priority { get; set; } = "P2";
    public string? ReproductionSteps { get; set; }
    public string? ExpectedResult { get; set; }
    public string? ActualResult { get; set; }
    public string? AssignedTo { get; set; }
    public DefectStatus Status { get; set; }
    public string? RootCause { get; set; }
    public Guid? FixVersionId { get; set; }
    public string? VerificationResult { get; set; }
    public Guid? DuplicateOfId { get; set; }
    /// <summary>Normalised failure signature; repeated failures with the same fingerprint attach to this defect instead of creating new ones.</summary>
    public string? Fingerprint { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public DateTimeOffset? LastSeenAt { get; set; }
    public string? ExternalSystem { get; set; }
    public string? ExternalKey { get; set; }
    public string? ExternalUrl { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>Status changes and comments (complements the audit log with a readable timeline).</summary>
public class DefectEvent
{
    public long Id { get; set; }
    public Guid DefectId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string UserName { get; set; } = "";
    public string Kind { get; set; } = "Comment";
    public DefectStatus? FromStatus { get; set; }
    public DefectStatus? ToStatus { get; set; }
    public string? Text { get; set; }
}

/// <summary>Every failing execution linked to a defect (first occurrence and recurrences).</summary>
public class DefectOccurrence
{
    public Guid DefectId { get; set; }
    public Guid TestExecutionId { get; set; }
    public Guid RunId { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}

public enum GateType
{
    /// <summary>Every test case in a suite has passed at its latest execution in the release window.</summary>
    SuitePassed = 0,
    /// <summary>No unresolved defects at or above a severity for the product.</summary>
    NoOpenDefects = 1,
    /// <summary>Every feature added or modified in the release has at least one passing test in the window.</summary>
    ChangedFeaturesTested = 2,
    /// <summary>Share of the product's test cases that are automated is at least a threshold.</summary>
    MinAutomationCoverage = 3,
    /// <summary>All test cases with a tag (e.g. "accounting") passed at their latest execution.</summary>
    TaggedTestsPassed = 4,
    /// <summary>Pass rate of executed tests in the window is at least a threshold.</summary>
    MinPassRate = 5,
}

/// <summary>A release-readiness criterion for a product (applies to all its releases) or a single release.</summary>
public class QualityGate : Entity, IAudited
{
    public Guid ProductId { get; set; }
    public Guid? ReleaseId { get; set; }
    public string Name { get; set; } = "";
    public GateType Type { get; set; }
    /// <summary>Suite id, tag, severity name or numeric threshold, depending on <see cref="Type"/>.</summary>
    public string? Parameter { get; set; }
    /// <summary>Blocking gates must pass (or be waived) before sign-off; non-blocking gates only warn.</summary>
    public bool IsBlocking { get; set; } = true;
    public int Order { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>An authorised exception to a failing gate for one release, with a recorded justification.</summary>
public class GateWaiver : Entity, IAudited
{
    public Guid GateId { get; set; }
    public Guid ReleaseId { get; set; }
    public string Justification { get; set; } = "";
    public string ApprovedBy { get; set; } = "";
    public DateTimeOffset ApprovedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public enum ReleaseDecision { Approved = 0, Rejected = 1 }

public enum ReleaseStatus { Planning = 0, Testing = 1, SignedOff = 2, Released = 3 }
public enum FeatureChangeKind { Added = 0, Modified = 1, Deprecated = 2 }
public enum FeatureChangeSource { Declared = 0, Build = 1 }

/// <summary>
/// A feature in a release's scope. Features introduced in the release and features deprecated during its window are derived
/// automatically; this table holds declared changes and modifications detected from builds, with the change's risk.
/// </summary>
public class ReleaseFeatureChange : Entity, IAudited
{
    public Guid ReleaseId { get; set; }
    public Guid FeatureId { get; set; }
    public FeatureChangeKind Kind { get; set; }
    public FeatureChangeSource Source { get; set; }
    public Orchestration.RiskLevel Risk { get; set; } = Orchestration.RiskLevel.Medium;
    public string? Notes { get; set; }
}

/// <summary>Release testing sign-off. The report is frozen: its SHA-256 identifies exactly what was approved.</summary>
public class ReleaseApproval : Entity, IAudited
{
    public Guid ReleaseId { get; set; }
    public ReleaseDecision Decision { get; set; }
    public string Comment { get; set; } = "";
    public string ApprovedBy { get; set; } = "";
    public DateTimeOffset ApprovedAt { get; set; }
    public string ReportStorageKey { get; set; } = "";
    public string ReportSha256 { get; set; } = "";
    public string GateSummaryJson { get; set; } = "[]";
}

// ---- Notifications ----

public enum ChannelType { Email = 0, Teams = 1, Webhook = 2 }

/// <summary>An outbound destination configured by administrators. The URL (Teams/webhook) is encrypted at rest.</summary>
public class NotificationChannel : Entity, IAudited
{
    public string Name { get; set; } = "";
    public ChannelType Type { get; set; }
    /// <summary>Email: comma-separated recipients. Teams/Webhook: unused (the URL is secret).</summary>
    public string? Recipients { get; set; }
    public string? ProtectedUrl { get; set; }
    /// <summary>Webhook only: HMAC-SHA256 signing secret (encrypted), sent as X-Wings-Signature.</summary>
    public string? ProtectedSigningSecret { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Who hears about what: either a user (in-app inbox, and email to their address) or a channel, for a set of event types,
/// optionally limited to one product and a minimum severity.
/// </summary>
public class NotificationRule : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string EventTypes { get; set; } = "";
    public Guid? ProductId { get; set; }
    public string MinSeverity { get; set; } = "Info";
    public Guid? UserId { get; set; }
    public bool EmailUser { get; set; }
    public Guid? ChannelId { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>Unacknowledged notifications of these event types are re-sent to an escalation channel after a delay.</summary>
public class EscalationPolicy : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string EventTypes { get; set; } = "";
    public int AfterMinutes { get; set; } = 30;
    public Guid ChannelId { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class NotificationTemplate
{
    public string EventType { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
}

/// <summary>One occurrence of an event; the in-app inbox shows it to every user whose rule matched.</summary>
public class NotificationEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "Info";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string? Link { get; set; }
    public Guid? ProductId { get; set; }
    public string? DataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }
}

public class UserNotification
{
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public enum DeliveryStatus { Pending = 0, Sent = 1, Failed = 2 }

public class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EventId { get; set; }
    public Guid? ChannelId { get; set; }
    public string? EmailTo { get; set; }
    public DeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public bool IsEscalation { get; set; }
    /// <summary>Optional file attached to an email (scheduled report deliveries).</summary>
    public string? AttachmentKey { get; set; }
    public string? AttachmentName { get; set; }
}

// ---- Source and builds ----

public enum VcsType { Git = 0, Tfvc = 1 }

public class SourceRepository : Entity, IAudited
{
    public string Name { get; set; } = "";
    public VcsType Type { get; set; }
    public string? Url { get; set; }
    public Guid? ProductId { get; set; }
    public string? DefaultBranch { get; set; }
}

/// <summary>A build reported by CI, with the files and commits it changed.</summary>
public class ApplicationBuild : Entity
{
    public Guid ProductId { get; set; }
    public Guid? RepositoryId { get; set; }
    public Guid? ReleaseId { get; set; }
    public string BuildNumber { get; set; } = "";
    public string? Branch { get; set; }
    public string? CommitId { get; set; }
    public string? BuildUrl { get; set; }
    public string? ChangedFilesJson { get; set; }
    public string? CommitsJson { get; set; }
    public int ImpactedFeatureCount { get; set; }
    public Guid? TriggeredRunId { get; set; }
    public string? GateStatus { get; set; }
}

/// <summary>A commit (Git) or changeset (TFVC) received with a build.</summary>
public class SourceCommit
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BuildId { get; set; }
    public Guid? RepositoryId { get; set; }
    public string CommitId { get; set; } = "";
    public string? Author { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset? CommittedAt { get; set; }
    public string? FilesJson { get; set; }
}

/// <summary>Feature a commit touched: through an approved source mapping or a feature reference in its message.</summary>
public class CommitFeatureLink
{
    public Guid CommitId { get; set; }
    public Guid FeatureId { get; set; }
    public ImpactReason Reason { get; set; }
}

public enum ImpactReason { SourceMapping = 0, CommitMessage = 1, Dependency = 2 }

public class BuildFeatureImpact
{
    public Guid BuildId { get; set; }
    public Guid FeatureId { get; set; }
    public ImpactReason Reason { get; set; }
    public string Evidence { get; set; } = "";
}

/// <summary>Credential for CI/CD and issue-tracker integrations calling the API (builds, gate checks).</summary>
public class IntegrationKey : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>Issue tracker connection used to push defects (GitHub, Jira, Azure DevOps).</summary>
public class IssueTrackerConnection : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "GitHub";
    /// <summary>GitHub: owner/repo; Jira: base URL + project key ("https://x.atlassian.net|AUTO"); Azure DevOps: org URL + project.</summary>
    public string Target { get; set; } = "";
    /// <summary>API root; defaults per kind (https://api.github.com). Set for GitHub Enterprise, Jira and Azure DevOps Server.</summary>
    public string? BaseUrl { get; set; }
    public string? Username { get; set; }
    public string? ProtectedToken { get; set; }
    public Guid? ProductId { get; set; }
    public bool IsEnabled { get; set; } = true;
}

// ---- Reports ----

public class SavedReport : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string ReportKey { get; set; } = "";
    public string ParametersJson { get; set; } = "{}";
    public string? Columns { get; set; }
    public string Owner { get; set; } = "";
    public bool IsShared { get; set; }
    /// <summary>Optional delivery: cron (UTC) and an email channel.</summary>
    public string? ScheduleCron { get; set; }
    public Guid? DeliveryChannelId { get; set; }
    public string Format { get; set; } = "xlsx";
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
}
