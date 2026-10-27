using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Wings.Application.Abstractions;
using Wings.Domain.Administration;
using Wings.Domain.Common;
using Wings.Domain.Execution;
using Wings.Domain.Orchestration;
using Wings.Domain.Quality;
using Wings.Domain.Ai;
using Wings.Domain.Features;
using Wings.Domain.Testing;
using Wings.Infrastructure.Identity;

namespace Wings.Infrastructure.Persistence;

/// <summary>
/// Provider-neutral model. <see cref="SqliteWingsDbContext"/> and <see cref="SqlServerWingsDbContext"/> exist only so each
/// provider keeps its own migration history; the model is identical.
/// </summary>
public abstract class WingsDbContext(DbContextOptions options, ICurrentUser? currentUser = null, TimeProvider? clock = null)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IWingsDbContext
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVersion> ProductVersions => Set<ProductVersion>();
    public DbSet<SequenceScope> SequenceScopes => Set<SequenceScope>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<FeatureType> FeatureTypes => Set<FeatureType>();
    public DbSet<FeatureStatus> FeatureStatuses => Set<FeatureStatus>();
    public DbSet<FeatureRequirement> FeatureRequirements => Set<FeatureRequirement>();
    public DbSet<FeatureSourceMapping> FeatureSourceMappings => Set<FeatureSourceMapping>();
    public DbSet<FeatureDependency> FeatureDependencies => Set<FeatureDependency>();
    public DbSet<FeatureAttachment> FeatureAttachments => Set<FeatureAttachment>();
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<TestStep> TestSteps => Set<TestStep>();
    public DbSet<TestCaseFeatureMapping> TestCaseFeatureMappings => Set<TestCaseFeatureMapping>();
    public DbSet<TestCaseTag> TestCaseTags => Set<TestCaseTag>();
    public DbSet<TestCaseRequirementLink> TestCaseRequirementLinks => Set<TestCaseRequirementLink>();
    public DbSet<TestCaseVersion> TestCaseVersions => Set<TestCaseVersion>();
    public DbSet<TestCaseDependency> TestCaseDependencies => Set<TestCaseDependency>();
    public DbSet<TestFolder> TestFolders => Set<TestFolder>();
    public DbSet<TestSuite> TestSuites => Set<TestSuite>();
    public DbSet<TestSuiteItem> TestSuiteItems => Set<TestSuiteItem>();
    public DbSet<TestType> TestTypes => Set<TestType>();
    public DbSet<TestingFramework> TestingFrameworks => Set<TestingFramework>();
    public DbSet<TestPackage> TestPackages => Set<TestPackage>();
    public DbSet<TestPackageVersion> TestPackageVersions => Set<TestPackageVersion>();
    public DbSet<TestPackageFile> TestPackageFiles => Set<TestPackageFile>();
    public DbSet<TestPackageTest> TestPackageTests => Set<TestPackageTest>();
    public DbSet<ExecutionAgent> ExecutionAgents => Set<ExecutionAgent>();
    public DbSet<ExecutionRun> ExecutionRuns => Set<ExecutionRun>();
    public DbSet<ExecutionJob> ExecutionJobs => Set<ExecutionJob>();
    public DbSet<TestExecution> TestExecutions => Set<TestExecution>();
    public DbSet<ExecutionArtifact> ExecutionArtifacts => Set<ExecutionArtifact>();
    public DbSet<ExecutionLog> ExecutionLogs => Set<ExecutionLog>();
    public DbSet<ExecutionJobDependency> ExecutionJobDependencies => Set<ExecutionJobDependency>();
    public DbSet<TestEnvironment> TestEnvironments => Set<TestEnvironment>();
    public DbSet<EnvironmentVariable> EnvironmentVariables => Set<EnvironmentVariable>();
    public DbSet<TestDataSet> TestDataSets => Set<TestDataSet>();
    public DbSet<TestDataRecord> TestDataRecords => Set<TestDataRecord>();
    public DbSet<TestDataReservation> TestDataReservations => Set<TestDataReservation>();
    public DbSet<ResourceLock> ResourceLocks => Set<ResourceLock>();
    public DbSet<TestPlan> TestPlans => Set<TestPlan>();
    public DbSet<TestPlanSuite> TestPlanSuites => Set<TestPlanSuite>();
    public DbSet<TestCampaign> TestCampaigns => Set<TestCampaign>();
    public DbSet<TestCampaignPlan> TestCampaignPlans => Set<TestCampaignPlan>();
    public DbSet<TestCampaignEnvironment> TestCampaignEnvironments => Set<TestCampaignEnvironment>();
    public DbSet<TestSchedule> TestSchedules => Set<TestSchedule>();
    public DbSet<Defect> Defects => Set<Defect>();
    public DbSet<DefectEvent> DefectEvents => Set<DefectEvent>();
    public DbSet<DefectOccurrence> DefectOccurrences => Set<DefectOccurrence>();
    public DbSet<QualityGate> QualityGates => Set<QualityGate>();
    public DbSet<GateWaiver> GateWaivers => Set<GateWaiver>();
    public DbSet<ReleaseApproval> ReleaseApprovals => Set<ReleaseApproval>();
    public DbSet<ReleaseFeatureChange> ReleaseFeatureChanges => Set<ReleaseFeatureChange>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<NotificationRule> NotificationRules => Set<NotificationRule>();
    public DbSet<EscalationPolicy> EscalationPolicies => Set<EscalationPolicy>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<SourceRepository> SourceRepositories => Set<SourceRepository>();
    public DbSet<ApplicationBuild> ApplicationBuilds => Set<ApplicationBuild>();
    public DbSet<SourceCommit> SourceCommits => Set<SourceCommit>();
    public DbSet<CommitFeatureLink> CommitFeatureLinks => Set<CommitFeatureLink>();
    public DbSet<BuildFeatureImpact> BuildFeatureImpacts => Set<BuildFeatureImpact>();
    public DbSet<IntegrationKey> IntegrationKeys => Set<IntegrationKey>();
    public DbSet<IssueTrackerConnection> IssueTrackerConnections => Set<IssueTrackerConnection>();
    public DbSet<SavedReport> SavedReports => Set<SavedReport>();
    public DbSet<AiInteraction> AiInteractions => Set<AiInteraction>();
    public DbSet<AiPolicy> AiPolicies => Set<AiPolicy>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    public DbSet<UserProductAccess> UserProductAccess => Set<UserProductAccess>();

    public string? PendingAuditAction { get; set; }
    public string? PendingAuditSummary { get; set; }

    public void SetOriginalVersion(object entity, long version)
        => Entry(entity).Property(nameof(Entity.Version)).OriginalValue = version;

    public void Touch(object entity)
    {
        var entry = Entry(entity);
        if (entry.State == EntityState.Unchanged) entry.Property(nameof(Entity.Version)).IsModified = true;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<ApplicationType>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<AutomationStatus>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<ReviewStatus>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<PackageStatus>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<DependencyType>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<SourceMappingKind>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<BackgroundJobStatus>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<RunStatus>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<JobStatus>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<EnvironmentKind>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<EnvironmentHealth>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<PlanStatus>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<RiskLevel>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<CampaignStatus>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<TriggerType>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<ScheduleTarget>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<DefectStatus>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<DefectSeverity>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<GateType>().HaveConversion<string>().HaveMaxLength(40);
        b.Properties<ReleaseDecision>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<ReleaseStatus>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<FeatureChangeKind>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<FeatureChangeSource>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<ChannelType>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<DeliveryStatus>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<VcsType>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<ImpactReason>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<AiFeature>().HaveConversion<string>().HaveMaxLength(30);
        b.Properties<AiInteractionStatus>().HaveConversion<string>().HaveMaxLength(30);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<ApplicationUser>().ToTable("Users");
        b.Entity<ApplicationRole>().ToTable("Roles");
        b.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        b.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
            b.Entity(type.ClrType).Property(nameof(Entity.Version)).IsConcurrencyToken();

        // Ids are assigned in code (GUID v7), so EF must treat new children reached through navigations as inserts, not updates.
        foreach (var type in b.Model.GetEntityTypes())
            if (type.FindPrimaryKey()?.Properties is [{ Name: "Id", ClrType: var t }] && t == typeof(Guid) && !type.ClrType.Namespace!.StartsWith("Microsoft"))
                b.Entity(type.ClrType).Property("Id").ValueGeneratedNever();

        b.Entity<SequenceScope>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Prefix).HasMaxLength(20);
        });

        b.Entity<Product>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.HasOne(x => x.SequenceScope).WithMany().HasForeignKey(x => x.SequenceScopeId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        b.Entity<ProductVersion>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.VersionLabel }).IsUnique();
            e.Property(x => x.VersionLabel).HasMaxLength(50);
            e.HasOne(x => x.Product).WithMany(p => p.Versions).HasForeignKey(x => x.ProductId);
            e.Property(x => x.Notes).HasMaxLength(4000);
        });

        b.Entity<FeatureType>(e => { e.HasKey(x => x.Code); e.Property(x => x.Code).HasMaxLength(50); });
        b.Entity<FeatureStatus>(e => { e.HasKey(x => x.Code); e.Property(x => x.Code).HasMaxLength(50); });

        b.Entity<Feature>(e =>
        {
            e.HasIndex(x => new { x.SequenceScopeId, x.SequenceNumber }).IsUnique();
            e.HasIndex(x => new { x.ProductId, x.ParentId, x.DisplayOrder });
            e.HasIndex(x => x.Path);
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.StatusCode);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Path).HasMaxLength(850);
            e.Property(x => x.FeatureTypeCode).HasMaxLength(50);
            e.Property(x => x.StatusCode).HasMaxLength(50);
            e.HasOne<FeatureType>().WithMany().HasForeignKey(x => x.FeatureTypeCode).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FeatureStatus>().WithMany().HasForeignKey(x => x.StatusCode).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SequenceScope>().WithMany().HasForeignKey(x => x.SequenceScopeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.IntroducedInVersion).WithMany().HasForeignKey(x => x.IntroducedInVersionId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Feature>().WithMany().HasForeignKey(x => x.ReplacedByFeatureId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        b.Entity<FeatureRequirement>(e =>
        {
            e.HasIndex(x => new { x.FeatureId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.HasOne<Feature>().WithMany(f => f.Requirements).HasForeignKey(x => x.FeatureId);
        });
        b.Entity<FeatureSourceMapping>(e =>
        {
            e.HasIndex(x => new { x.Kind, x.Value });
            e.Property(x => x.Value).HasMaxLength(450);
            e.HasOne<Feature>().WithMany(f => f.SourceMappings).HasForeignKey(x => x.FeatureId);
        });
        b.Entity<FeatureAttachment>(e => e.HasOne<Feature>().WithMany(f => f.Attachments).HasForeignKey(x => x.FeatureId));
        b.Entity<FeatureDependency>(e =>
        {
            e.HasIndex(x => new { x.FeatureId, x.DependsOnFeatureId }).IsUnique();
            e.HasOne(x => x.Feature).WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DependsOnFeature).WithMany().HasForeignKey(x => x.DependsOnFeatureId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TestType>(e => { e.HasKey(x => x.Code); e.Property(x => x.Code).HasMaxLength(50); });
        b.Entity<TestingFramework>(e => { e.HasKey(x => x.Code); e.Property(x => x.Code).HasMaxLength(50); });

        b.Entity<TestCase>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.ReviewStatus);
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.TestTypeCode).HasMaxLength(50);
            e.Property(x => x.Priority).HasMaxLength(20);
            e.Property(x => x.FailureSeverity).HasMaxLength(20);
            e.HasOne<TestType>().WithMany().HasForeignKey(x => x.TestTypeCode).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TestingFramework>().WithMany().HasForeignKey(x => x.FrameworkCode).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Folder).WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.TestCaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.FeatureMappings).WithOne().HasForeignKey(m => m.TestCaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Tags).WithOne().HasForeignKey(t => t.TestCaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.RequirementLinks).WithOne().HasForeignKey(t => t.TestCaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        b.Entity<TestStep>(e =>
        {
            e.HasIndex(x => new { x.TestCaseId, x.StepNumber });
            e.HasOne<TestCase>().WithMany().HasForeignKey(x => x.SharedStepsTestCaseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TestCaseFeatureMapping>(e =>
        {
            e.HasKey(x => new { x.TestCaseId, x.FeatureId });
            e.HasIndex(x => x.FeatureId);
            e.HasOne(x => x.Feature).WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TestCaseTag>(e => { e.HasKey(x => new { x.TestCaseId, x.Tag }); e.HasIndex(x => x.Tag); e.Property(x => x.Tag).HasMaxLength(60); });
        b.Entity<TestCaseRequirementLink>(e =>
        {
            e.HasKey(x => new { x.TestCaseId, x.RequirementId });
            e.HasOne(x => x.Requirement).WithMany().HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TestCaseVersion>(e =>
        {
            e.HasIndex(x => new { x.TestCaseId, x.Revision }).IsUnique();
            e.HasOne<TestCase>().WithMany().HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TestCaseDependency>(e =>
        {
            e.HasIndex(x => new { x.TestCaseId, x.DependsOnTestCaseId }).IsUnique();
            e.HasOne<TestCase>().WithMany().HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DependsOnTestCase).WithMany().HasForeignKey(x => x.DependsOnTestCaseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TestFolder>(e =>
        {
            e.HasIndex(x => x.Path);
            e.Property(x => x.Path).HasMaxLength(850);
            e.HasOne<TestFolder>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TestSuite>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.SuiteId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        b.Entity<TestSuiteItem>(e =>
        {
            e.HasIndex(x => new { x.SuiteId, x.TestCaseId }).IsUnique();
            e.HasOne(x => x.TestCase).WithMany().HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TestPackage>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasMany(x => x.Versions).WithOne(v => v.Package).HasForeignKey(v => v.PackageId);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        b.Entity<TestPackageVersion>(e =>
        {
            e.HasIndex(x => new { x.PackageId, x.VersionNumber }).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasMany(x => x.Files).WithOne().HasForeignKey(f => f.VersionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Tests).WithOne().HasForeignKey(t => t.VersionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TestPackageFile>(e =>
        {
            e.HasIndex(x => new { x.VersionId, x.Path }).IsUnique();
            e.Property(x => x.Path).HasMaxLength(800);
            e.Property(x => x.Sha256).HasMaxLength(64);
        });
        b.Entity<TestPackageTest>(e =>
        {
            e.HasIndex(x => new { x.VersionId, x.Identifier }).IsUnique();
            e.HasIndex(x => x.TestCaseId);
            e.HasIndex(x => x.FeatureId);
            e.Property(x => x.Identifier).HasMaxLength(800);
            e.Property(x => x.FilePath).HasMaxLength(800);
            e.HasOne(x => x.TestCase).WithMany().HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Feature).WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ExecutionAgent>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.KeyHash).HasMaxLength(64);
            e.Property(x => x.KeyPrefix).HasMaxLength(16);
            e.Property(x => x.Pool).HasMaxLength(100);
            e.Property(x => x.FrameworksCsv).HasMaxLength(400);
            e.Ignore(x => x.Frameworks);
        });
        b.Entity<ExecutionRun>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.ScopeDescription).HasMaxLength(1000);
            e.HasMany(x => x.Jobs).WithOne(j => j.Run).HasForeignKey(j => j.RunId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ExecutionJob>(e =>
        {
            e.HasIndex(x => new { x.Status, x.Framework });
            e.HasIndex(x => new { x.AgentId, x.Status });
            e.Property(x => x.Framework).HasMaxLength(50);
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<TestPackageVersion>().WithMany().HasForeignKey(x => x.PackageVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TestExecution>(e =>
        {
            e.HasIndex(x => new { x.RunId, x.Outcome });
            e.HasIndex(x => new { x.JobId, x.Identifier });
            e.HasIndex(x => new { x.TestCaseId, x.CompletedAt });
            e.HasIndex(x => new { x.FeatureId, x.CompletedAt });
            e.Property(x => x.Identifier).HasMaxLength(800);
            e.Property(x => x.DisplayName).HasMaxLength(400);
            e.Property(x => x.Outcome).HasMaxLength(30);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasOne<ExecutionRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ExecutionArtifact>(e =>
        {
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => x.TestExecutionId);
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.FileName).HasMaxLength(400);
            e.Property(x => x.ContentType).HasMaxLength(200);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.StorageKey).HasMaxLength(200);
        });
        b.Entity<ExecutionLog>(e =>
        {
            e.HasIndex(x => new { x.JobId, x.Sequence });
            e.Property(x => x.Level).HasMaxLength(20);
        });

        b.Entity<ExecutionJobDependency>(e =>
        {
            e.HasKey(x => new { x.JobId, x.DependsOnJobId });
            e.HasIndex(x => x.DependsOnJobId);
            e.HasOne<ExecutionJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ExecutionJob>().WithMany().HasForeignKey(x => x.DependsOnJobId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TestEnvironment>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.AgentPools).HasMaxLength(400);
            e.Ignore(x => x.Pools);
            e.HasMany(x => x.Variables).WithOne().HasForeignKey(v => v.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        b.Entity<EnvironmentVariable>(e =>
        {
            e.HasIndex(x => new { x.EnvironmentId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(64);
        });
        b.Entity<TestDataSet>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne<TestEnvironment>().WithMany().HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        b.Entity<TestDataRecord>(e =>
        {
            e.HasIndex(x => new { x.DataSetId, x.Key }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(200);
            e.HasOne<TestDataSet>().WithMany().HasForeignKey(x => x.DataSetId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TestDataReservation>(e =>
        {
            // At most one active reservation per record (Active is null once released): concurrent claims cannot share a record.
            e.HasIndex(x => x.RecordId).IsUnique().HasFilter(Database.IsSqlServer() ? "[Active] IS NOT NULL" : "\"Active\" IS NOT NULL");
            e.HasIndex(x => x.JobId);
            e.HasOne<TestDataRecord>().WithMany().HasForeignKey(x => x.RecordId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ResourceLock>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(200);
            e.HasIndex(x => x.JobId);
        });
        b.Entity<TestPlan>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Name).HasMaxLength(300);
            e.HasMany(x => x.Suites).WithOne().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        b.Entity<TestPlanSuite>(e =>
        {
            e.HasKey(x => new { x.PlanId, x.SuiteId });
            e.HasOne<TestSuite>().WithMany().HasForeignKey(x => x.SuiteId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TestCampaign>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Name).HasMaxLength(300);
            e.HasMany(x => x.Plans).WithOne().HasForeignKey(p => p.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Environments).WithOne().HasForeignKey(p => p.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        b.Entity<TestCampaignPlan>(e => { e.HasKey(x => new { x.CampaignId, x.PlanId }); e.HasOne<TestPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<TestCampaignEnvironment>(e => { e.HasKey(x => new { x.CampaignId, x.EnvironmentId }); e.HasOne<TestEnvironment>().WithMany().HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<TestSchedule>(e =>
        {
            e.HasIndex(x => new { x.IsEnabled, x.NextRunAt });
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.CronExpression).HasMaxLength(100);
            e.Property(x => x.TimeZoneId).HasMaxLength(100);
            e.Property(x => x.WebhookTokenHash).HasMaxLength(64);
            e.Property(x => x.WebhookTokenPrefix).HasMaxLength(16);
        });
        b.Entity<ExecutionRun>(e =>
        {
            e.HasIndex(x => x.PlanId);
            e.HasIndex(x => x.CampaignId);
            e.HasIndex(x => x.ScheduleId);
        });

        b.Entity<Defect>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.ProductId, x.Status });
            e.HasIndex(x => x.Fingerprint);
            e.HasIndex(x => x.TestCaseId);
            e.HasIndex(x => x.FeatureId);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Priority).HasMaxLength(10);
            e.Property(x => x.AssignedTo).HasMaxLength(256);
            e.Property(x => x.BuildVersion).HasMaxLength(100);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.ExternalSystem).HasMaxLength(30);
            e.Property(x => x.ExternalKey).HasMaxLength(100);
            e.Property(x => x.ExternalUrl).HasMaxLength(800);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        b.Entity<DefectEvent>(e =>
        {
            e.HasIndex(x => new { x.DefectId, x.Id });
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.UserName).HasMaxLength(256);
            e.HasOne<Defect>().WithMany().HasForeignKey(x => x.DefectId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DefectOccurrence>(e =>
        {
            e.HasKey(x => new { x.DefectId, x.TestExecutionId });
            e.HasIndex(x => x.TestExecutionId);
            e.HasOne<Defect>().WithMany().HasForeignKey(x => x.DefectId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<QualityGate>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.ReleaseId });
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Parameter).HasMaxLength(200);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<GateWaiver>(e =>
        {
            e.HasIndex(x => new { x.ReleaseId, x.GateId });
            e.Property(x => x.ApprovedBy).HasMaxLength(256);
            e.HasOne<QualityGate>().WithMany().HasForeignKey(x => x.GateId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ReleaseApproval>(e =>
        {
            e.HasIndex(x => x.ReleaseId);
            e.Property(x => x.ApprovedBy).HasMaxLength(256);
            e.Property(x => x.ReportStorageKey).HasMaxLength(200);
            e.Property(x => x.ReportSha256).HasMaxLength(64);
            e.HasOne<ProductVersion>().WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ReleaseFeatureChange>(e =>
        {
            e.HasIndex(x => new { x.ReleaseId, x.FeatureId, x.Kind }).IsUnique();
            e.HasOne<ProductVersion>().WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Cascade);
            // Features are soft-deleted; no cascade, so SQL Server never sees two cascade paths from Products.
            e.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ProductVersion>(e => e.Property(x => x.BuildVersion).HasMaxLength(100));
        b.Entity<NotificationChannel>(e => e.Property(x => x.Name).HasMaxLength(200));
        b.Entity<NotificationRule>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.EventTypes).HasMaxLength(1000);
            e.Property(x => x.MinSeverity).HasMaxLength(20);
            e.HasOne<NotificationChannel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<EscalationPolicy>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.EventTypes).HasMaxLength(1000);
            e.HasOne<NotificationChannel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<NotificationTemplate>(e =>
        {
            e.HasKey(x => x.EventType);
            e.Property(x => x.EventType).HasMaxLength(60);
            e.Property(x => x.Subject).HasMaxLength(400);
        });
        b.Entity<NotificationEvent>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.Type, x.AcknowledgedAt });
            e.Property(x => x.Type).HasMaxLength(60);
            e.Property(x => x.Severity).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(400);
            e.Property(x => x.Link).HasMaxLength(400);
            e.Property(x => x.AcknowledgedBy).HasMaxLength(256);
        });
        b.Entity<UserNotification>(e =>
        {
            e.HasKey(x => new { x.UserId, x.EventId });
            e.HasIndex(x => x.EventId);
            e.HasOne<NotificationEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<NotificationDelivery>(e =>
        {
            e.HasIndex(x => x.EventId);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.EmailTo).HasMaxLength(400);
            e.Property(x => x.AttachmentKey).HasMaxLength(200);
            e.Property(x => x.AttachmentName).HasMaxLength(200);
            e.HasOne<NotificationEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SourceRepository>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Url).HasMaxLength(800);
            e.Property(x => x.DefaultBranch).HasMaxLength(200);
        });
        b.Entity<ApplicationBuild>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.CreatedAt });
            e.HasIndex(x => x.ReleaseId);
            e.Property(x => x.BuildNumber).HasMaxLength(100);
            e.Property(x => x.Branch).HasMaxLength(200);
            e.Property(x => x.CommitId).HasMaxLength(100);
            e.Property(x => x.BuildUrl).HasMaxLength(800);
            e.Property(x => x.GateStatus).HasMaxLength(30);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SourceCommit>(e =>
        {
            e.HasIndex(x => x.BuildId);
            e.HasIndex(x => x.CommitId);
            e.Property(x => x.CommitId).HasMaxLength(100);
            e.Property(x => x.Author).HasMaxLength(256);
            e.HasOne<ApplicationBuild>().WithMany().HasForeignKey(x => x.BuildId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CommitFeatureLink>(e =>
        {
            e.HasKey(x => new { x.CommitId, x.FeatureId });
            e.HasIndex(x => x.FeatureId);
            e.HasOne<SourceCommit>().WithMany().HasForeignKey(x => x.CommitId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<BuildFeatureImpact>(e =>
        {
            e.HasKey(x => new { x.BuildId, x.FeatureId });
            e.HasIndex(x => x.FeatureId);
            e.Property(x => x.Evidence).HasMaxLength(1000);
            e.HasOne<ApplicationBuild>().WithMany().HasForeignKey(x => x.BuildId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<IntegrationKey>(e =>
        {
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.KeyHash).HasMaxLength(64);
            e.Property(x => x.KeyPrefix).HasMaxLength(16);
        });
        b.Entity<IssueTrackerConnection>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.Target).HasMaxLength(400);
            e.Property(x => x.BaseUrl).HasMaxLength(400);
            e.Property(x => x.Username).HasMaxLength(256);
        });
        b.Entity<SavedReport>(e =>
        {
            e.HasIndex(x => x.Owner);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ReportKey).HasMaxLength(60);
            e.Property(x => x.Owner).HasMaxLength(256);
            e.Property(x => x.ScheduleCron).HasMaxLength(100);
            e.Property(x => x.Format).HasMaxLength(10);
            e.Property(x => x.Columns).HasMaxLength(2000);
        });
        b.Entity<ExecutionRun>(e => { e.HasIndex(x => x.ReleaseId); e.HasIndex(x => x.DefectId); });
        b.Entity<AiInteraction>(e =>
        {
            e.HasIndex(x => new { x.UserName, x.CreatedAt });
            e.HasIndex(x => new { x.SubjectType, x.SubjectId });
            e.Property(x => x.UserName).HasMaxLength(256);
            e.Property(x => x.Provider).HasMaxLength(50);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.SubjectType).HasMaxLength(50);
            e.Property(x => x.SubjectLabel).HasMaxLength(400);
            e.Property(x => x.InputSha256).HasMaxLength(64);
            e.Property(x => x.Outcome).HasMaxLength(30);
            e.Property(x => x.OutcomeBy).HasMaxLength(256);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => new { x.RootEntityType, x.RootEntityId });
            e.HasIndex(x => x.Timestamp);
            e.Property(x => x.Summary).HasMaxLength(2000);
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.RootEntityType).HasMaxLength(100);
            e.Property(x => x.UserName).HasMaxLength(256);
            e.Property(x => x.Action).HasMaxLength(50);
        });
        b.Entity<BackgroundJob>(e =>
        {
            e.HasIndex(x => new { x.Status, x.AvailableAt });
            e.Property(x => x.Type).HasMaxLength(100);
        });
        b.Entity<UserProductAccess>(e =>
        {
            e.HasKey(x => new { x.UserId, x.ProductId });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var user = currentUser?.UserName is { Length: > 0 } u ? u : "system";
        var audits = new List<(EntityEntry Entry, AuditLog Log)>();

        foreach (var entry in ChangeTracker.Entries<Entity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                    if (string.IsNullOrEmpty(entry.Entity.CreatedBy)) entry.Entity.CreatedBy = user;
                    entry.Entity.Version = 1;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedBy = user;
                    entry.Entity.Version = (long)entry.Property(nameof(Entity.Version)).OriginalValue! + 1;
                    if (entry.Entity is ISoftDeletable { IsDeleted: true } sd && entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified)
                    {
                        sd.DeletedAt = now;
                        sd.DeletedBy = user;
                    }
                    break;
            }
            if (entry.Entity is IAudited && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                audits.Add((entry, BuildAudit(entry, now, user)));
        }

        foreach (var (_, log) in audits) AuditLogs.Add(log);
        PendingAuditAction = null;
        PendingAuditSummary = null;

        try
        {
            return await base.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new Application.Common.ConflictException(
                $"The {ex.Entries.FirstOrDefault()?.Metadata.ClrType.Name ?? "record"} was changed by someone else. Reload and try again.");
        }
    }

    private AuditLog BuildAudit(EntityEntry entry, DateTimeOffset now, string user)
    {
        var entity = (Entity)entry.Entity;
        var changes = new Dictionary<string, object?>();
        var skip = new HashSet<string> { nameof(Entity.Version), nameof(Entity.ModifiedAt), nameof(Entity.ModifiedBy), nameof(Entity.CreatedAt), nameof(Entity.CreatedBy) };
        foreach (var p in entry.Properties.Where(p => !skip.Contains(p.Metadata.Name)))
        {
            if (entry.State == EntityState.Added && p.CurrentValue is not null)
                changes[p.Metadata.Name] = new { @new = p.CurrentValue };
            else if (entry.State == EntityState.Modified && p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
                changes[p.Metadata.Name] = new { old = p.OriginalValue, @new = p.CurrentValue };
        }

        var action = entry.State switch
        {
            EntityState.Added => "Created",
            EntityState.Deleted => "Deleted",
            _ when entity is ISoftDeletable { IsDeleted: true } && entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified => "Deleted",
            _ => PendingAuditAction ?? "Updated",
        };
        if (entry.State == EntityState.Added && PendingAuditAction is not null) action = PendingAuditAction;

        var (rootType, rootId) = entity switch
        {
            FeatureRequirement r => (nameof(Feature), r.FeatureId),
            FeatureSourceMapping m => (nameof(Feature), m.FeatureId),
            FeatureAttachment a => (nameof(Feature), a.FeatureId),
            FeatureDependency d => (nameof(Feature), d.FeatureId),
            TestCaseDependency d => (nameof(TestCase), d.TestCaseId),
            TestPackageVersion v => (nameof(TestPackage), v.PackageId),
            ProductVersion v => (nameof(Product), v.ProductId),
            GateWaiver w => (nameof(ProductVersion), w.ReleaseId),
            ReleaseApproval a => (nameof(ProductVersion), a.ReleaseId),
            ReleaseFeatureChange c => (nameof(ProductVersion), c.ReleaseId),
            _ => (entry.Metadata.ClrType.Name, entity.Id),
        };

        return new AuditLog
        {
            Timestamp = now,
            UserName = user,
            Action = action,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = entity.Id,
            RootEntityType = rootType,
            RootEntityId = rootId,
            ChangesJson = changes.Count == 0 ? null : JsonSerializer.Serialize(changes),
            Summary = PendingAuditSummary,
        };
    }
}

public sealed class SqliteWingsDbContext(DbContextOptions<SqliteWingsDbContext> options, ICurrentUser? currentUser = null, TimeProvider? clock = null)
    : WingsDbContext(options, currentUser, clock)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        base.ConfigureConventions(b);
        // SQLite has no native DateTimeOffset; store as sortable 64-bit values so ORDER BY and range filters translate.
        b.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        b.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}

public sealed class SqlServerWingsDbContext(DbContextOptions<SqlServerWingsDbContext> options, ICurrentUser? currentUser = null, TimeProvider? clock = null)
    : WingsDbContext(options, currentUser, clock);
