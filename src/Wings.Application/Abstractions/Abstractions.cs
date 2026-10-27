using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Wings.Domain.Administration;
using Wings.Domain.Features;
using Wings.Domain.Testing;

namespace Wings.Application.Abstractions;

public interface IWingsDbContext
{
    DbSet<Product> Products { get; }
    DbSet<ProductVersion> ProductVersions { get; }
    DbSet<SequenceScope> SequenceScopes { get; }
    DbSet<Feature> Features { get; }
    DbSet<FeatureType> FeatureTypes { get; }
    DbSet<FeatureStatus> FeatureStatuses { get; }
    DbSet<FeatureRequirement> FeatureRequirements { get; }
    DbSet<FeatureSourceMapping> FeatureSourceMappings { get; }
    DbSet<FeatureDependency> FeatureDependencies { get; }
    DbSet<FeatureAttachment> FeatureAttachments { get; }

    DbSet<TestCase> TestCases { get; }
    DbSet<TestStep> TestSteps { get; }
    DbSet<TestCaseFeatureMapping> TestCaseFeatureMappings { get; }
    DbSet<TestCaseTag> TestCaseTags { get; }
    DbSet<TestCaseRequirementLink> TestCaseRequirementLinks { get; }
    DbSet<TestCaseVersion> TestCaseVersions { get; }
    DbSet<TestCaseDependency> TestCaseDependencies { get; }
    DbSet<TestFolder> TestFolders { get; }
    DbSet<TestSuite> TestSuites { get; }
    DbSet<TestSuiteItem> TestSuiteItems { get; }
    DbSet<TestType> TestTypes { get; }
    DbSet<TestingFramework> TestingFrameworks { get; }
    DbSet<TestPackage> TestPackages { get; }
    DbSet<TestPackageVersion> TestPackageVersions { get; }
    DbSet<TestPackageFile> TestPackageFiles { get; }
    DbSet<TestPackageTest> TestPackageTests { get; }

    DbSet<Domain.Execution.ExecutionAgent> ExecutionAgents { get; }
    DbSet<Domain.Execution.ExecutionRun> ExecutionRuns { get; }
    DbSet<Domain.Execution.ExecutionJob> ExecutionJobs { get; }
    DbSet<Domain.Execution.TestExecution> TestExecutions { get; }
    DbSet<Domain.Execution.ExecutionArtifact> ExecutionArtifacts { get; }
    DbSet<Domain.Execution.ExecutionLog> ExecutionLogs { get; }

    DbSet<Domain.Execution.ExecutionJobDependency> ExecutionJobDependencies { get; }
    DbSet<Domain.Orchestration.TestEnvironment> TestEnvironments { get; }
    DbSet<Domain.Orchestration.EnvironmentVariable> EnvironmentVariables { get; }
    DbSet<Domain.Orchestration.TestDataSet> TestDataSets { get; }
    DbSet<Domain.Orchestration.TestDataRecord> TestDataRecords { get; }
    DbSet<Domain.Orchestration.TestDataReservation> TestDataReservations { get; }
    DbSet<Domain.Orchestration.ResourceLock> ResourceLocks { get; }
    DbSet<Domain.Orchestration.TestPlan> TestPlans { get; }
    DbSet<Domain.Orchestration.TestPlanSuite> TestPlanSuites { get; }
    DbSet<Domain.Orchestration.TestCampaign> TestCampaigns { get; }
    DbSet<Domain.Orchestration.TestCampaignPlan> TestCampaignPlans { get; }
    DbSet<Domain.Orchestration.TestCampaignEnvironment> TestCampaignEnvironments { get; }
    DbSet<Domain.Orchestration.TestSchedule> TestSchedules { get; }

    DbSet<Domain.Quality.Defect> Defects { get; }
    DbSet<Domain.Quality.DefectEvent> DefectEvents { get; }
    DbSet<Domain.Quality.DefectOccurrence> DefectOccurrences { get; }
    DbSet<Domain.Quality.QualityGate> QualityGates { get; }
    DbSet<Domain.Quality.GateWaiver> GateWaivers { get; }
    DbSet<Domain.Quality.ReleaseApproval> ReleaseApprovals { get; }
    DbSet<Domain.Quality.ReleaseFeatureChange> ReleaseFeatureChanges { get; }
    DbSet<Domain.Quality.NotificationChannel> NotificationChannels { get; }
    DbSet<Domain.Quality.NotificationRule> NotificationRules { get; }
    DbSet<Domain.Quality.EscalationPolicy> EscalationPolicies { get; }
    DbSet<Domain.Quality.NotificationTemplate> NotificationTemplates { get; }
    DbSet<Domain.Quality.NotificationEvent> NotificationEvents { get; }
    DbSet<Domain.Quality.UserNotification> UserNotifications { get; }
    DbSet<Domain.Quality.NotificationDelivery> NotificationDeliveries { get; }
    DbSet<Domain.Quality.SourceRepository> SourceRepositories { get; }
    DbSet<Domain.Quality.ApplicationBuild> ApplicationBuilds { get; }
    DbSet<Domain.Quality.SourceCommit> SourceCommits { get; }
    DbSet<Domain.Quality.CommitFeatureLink> CommitFeatureLinks { get; }
    DbSet<Domain.Quality.BuildFeatureImpact> BuildFeatureImpacts { get; }
    DbSet<Domain.Quality.IntegrationKey> IntegrationKeys { get; }
    DbSet<Domain.Quality.IssueTrackerConnection> IssueTrackerConnections { get; }
    DbSet<Domain.Quality.SavedReport> SavedReports { get; }

    DbSet<Domain.Ai.AiInteraction> AiInteractions { get; }
    DbSet<Domain.Ai.AiPolicy> AiPolicies { get; }

    DbSet<AuditLog> AuditLogs { get; }
    DbSet<BackgroundJob> BackgroundJobs { get; }
    DbSet<UserProductAccess> UserProductAccess { get; }

    DatabaseFacade Database { get; }

    /// <summary>Overrides the generic "Updated" audit action for the next SaveChanges (e.g. "Moved", "Approved").</summary>
    string? PendingAuditAction { get; set; }
    /// <summary>Free-text summary written with the next SaveChanges' audit entries.</summary>
    string? PendingAuditSummary { get; set; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    void SetOriginalVersion(object entity, long version);
    /// <summary>Marks an aggregate root as modified so a change to its non-audited children is versioned and audited.</summary>
    void Touch(object entity);
    /// <summary>Discards pending tracked changes (after a rolled-back attempt).</summary>
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    string UserName { get; }
    bool IsAuthenticated { get; }
}

/// <summary>Allocates creation-sequence values. Must be called inside the caller's transaction.</summary>
public interface ISequenceAllocator
{
    Task<long> NextAsync(Guid scopeId, CancellationToken ct = default);
    /// <summary>Records a legacy value so the counter never hands it (or anything below it) out again.</summary>
    Task ReserveAsync(Guid scopeId, long value, CancellationToken ct = default);
}

public interface IFileStore
{
    /// <summary>Stores content under a content-addressed key derived from its SHA-256; returns (key, sha256, size).</summary>
    Task<StoredBlob> PutAsync(Stream content, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}

public sealed record StoredBlob(string Key, string Sha256, long SizeBytes);

public interface IJobQueue
{
    Task EnqueueAsync(string type, object payload, CancellationToken ct = default);
}

public interface IJobHandler
{
    string JobType { get; }
    Task HandleAsync(string payloadJson, CancellationToken ct);
}

public interface IPermissionService
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken ct = default);
    void Invalidate(Guid userId);
}

/// <summary>Resolves which products the current user may see or change.</summary>
public interface IProductAccess
{
    /// <summary>Null means unrestricted.</summary>
    Task<IReadOnlySet<Guid>?> VisibleProductIdsAsync(CancellationToken ct = default);
    Task EnsureCanViewAsync(Guid productId, CancellationToken ct = default);
    Task EnsureCanEditAsync(Guid productId, CancellationToken ct = default);
}

/// <summary>Reads/writes tabular files (CSV, XLSX) for bulk import and export.</summary>
public interface ITabularCodec
{
    string Format { get; }
    string ContentType { get; }
    byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows, string sheetName);
    IReadOnlyList<Dictionary<string, string>> Read(Stream input);
}

/// <summary>Best-effort push of change events to connected clients (SignalR in the API host).</summary>
public interface IChangeNotifier
{
    Task NotifyAsync(string topic, object payload, CancellationToken ct = default);
}

public sealed class NullChangeNotifier : IChangeNotifier
{
    public Task NotifyAsync(string topic, object payload, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Encrypts secret values at rest (ASP.NET Core Data Protection in the API host).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}

public sealed record UserContact(Guid Id, string UserName, string DisplayName, string? Email, bool IsActive);

/// <summary>Read-only lookup of user accounts (assignees, notification recipients).</summary>
public interface IUserDirectory
{
    Task<IReadOnlyList<UserContact>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UserContact>> FindAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<UserContact?> FindByUserNameAsync(string userName, CancellationToken ct = default);
}

/// <summary>A simple paginated document: headings, paragraphs, key/value blocks and tables.</summary>
public sealed class PdfDocumentModel
{
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public string? Footer { get; init; }
    public bool Landscape { get; init; }
    public List<PdfBlock> Blocks { get; } = [];
    public PdfDocumentModel Heading(string text) { Blocks.Add(new PdfBlock.Heading(text)); return this; }
    public PdfDocumentModel Paragraph(string text) { Blocks.Add(new PdfBlock.Paragraph(text)); return this; }
    public PdfDocumentModel Fields(IEnumerable<(string Label, string? Value)> rows) { Blocks.Add(new PdfBlock.KeyValues(rows.ToList())); return this; }
    public PdfDocumentModel Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows, IReadOnlyList<double>? weights = null)
    { Blocks.Add(new PdfBlock.Table(headers, rows.ToList(), weights)); return this; }
}

public abstract record PdfBlock
{
    public sealed record Heading(string Text) : PdfBlock;
    public sealed record Paragraph(string Text) : PdfBlock;
    public sealed record KeyValues(IReadOnlyList<(string Label, string? Value)> Rows) : PdfBlock;
    public sealed record Table(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string?>> Rows, IReadOnlyList<double>? Weights) : PdfBlock;
}

public interface IPdfRenderer
{
    byte[] Render(PdfDocumentModel document);
}
