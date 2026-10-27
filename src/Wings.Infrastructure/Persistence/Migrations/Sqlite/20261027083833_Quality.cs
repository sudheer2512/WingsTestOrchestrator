using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wings.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Quality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuildVersion",
                table: "ProductVersions",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KnownRisks",
                table: "ProductVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ProductVersions",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "TestingStartDate",
                table: "ProductVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefectId",
                table: "ExecutionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReleaseId",
                table: "ExecutionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OfflineNotifiedAt",
                table: "ExecutionAgents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApplicationBuilds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BuildNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CommitId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BuildUrl = table.Column<string>(type: "TEXT", maxLength: 800, nullable: true),
                    ChangedFilesJson = table.Column<string>(type: "TEXT", nullable: true),
                    CommitsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ImpactedFeatureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TriggeredRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GateStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationBuilds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationBuilds_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Defects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FeatureId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TestCaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FailingTestExecutionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FailingRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BuildVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ReproductionSteps = table.Column<string>(type: "TEXT", nullable: true),
                    ExpectedResult = table.Column<string>(type: "TEXT", nullable: true),
                    ActualResult = table.Column<string>(type: "TEXT", nullable: true),
                    AssignedTo = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RootCause = table.Column<string>(type: "TEXT", nullable: true),
                    FixVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VerificationResult = table.Column<string>(type: "TEXT", nullable: true),
                    DuplicateOfId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OccurrenceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ExternalSystem = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ExternalKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ExternalUrl = table.Column<string>(type: "TEXT", maxLength: 800, nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DeletedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Defects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastUsedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IssueTrackerConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ProtectedToken = table.Column<string>(type: "TEXT", nullable: true),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueTrackerConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Recipients = table.Column<string>(type: "TEXT", nullable: true),
                    ProtectedUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ProtectedSigningSecret = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    Link = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    AcknowledgedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EscalatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    EventType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.EventType);
                });

            migrationBuilder.CreateTable(
                name: "QualityGates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Parameter = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsBlocking = table.Column<bool>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityGates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualityGates_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ApprovedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReportStorageKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ReportSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GateSummaryJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseApprovals_ProductVersions_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "ProductVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseFeatureChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FeatureId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Risk = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseFeatureChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseFeatureChanges_Features_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "Features",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReleaseFeatureChanges_ProductVersions_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "ProductVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ReportKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ParametersJson = table.Column<string>(type: "TEXT", nullable: false),
                    Columns = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IsShared = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScheduleCron = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DeliveryChannelId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Format = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    NextRunAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastRunAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceRepositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 800, nullable: true),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DefaultBranch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceRepositories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BuildFeatureImpacts",
                columns: table => new
                {
                    BuildId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FeatureId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildFeatureImpacts", x => new { x.BuildId, x.FeatureId });
                    table.ForeignKey(
                        name: "FK_BuildFeatureImpacts_ApplicationBuilds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "ApplicationBuilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildFeatureImpacts_Features_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "Features",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceCommits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CommitId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Author = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    CommittedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    FilesJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCommits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceCommits_ApplicationBuilds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "ApplicationBuilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DefectEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DefectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefectEvents_Defects_DefectId",
                        column: x => x.DefectId,
                        principalTable: "Defects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DefectOccurrences",
                columns: table => new
                {
                    DefectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TestExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LinkedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectOccurrences", x => new { x.DefectId, x.TestExecutionId });
                    table.ForeignKey(
                        name: "FK_DefectOccurrences_Defects_DefectId",
                        column: x => x.DefectId,
                        principalTable: "Defects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EscalationPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventTypes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AfterMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EscalationPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EscalationPolicies_NotificationChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "NotificationChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventTypes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MinSeverity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EmailUser = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationRules_NotificationChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "NotificationChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EmailTo = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SentAt = table.Column<long>(type: "INTEGER", nullable: true),
                    IsEscalation = table.Column<bool>(type: "INTEGER", nullable: false),
                    AttachmentKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AttachmentName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_NotificationEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "NotificationEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => new { x.UserId, x.EventId });
                    table.ForeignKey(
                        name: "FK_UserNotifications_NotificationEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "NotificationEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserNotifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GateWaivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Justification = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ApprovedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GateWaivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GateWaivers_QualityGates_GateId",
                        column: x => x.GateId,
                        principalTable: "QualityGates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommitFeatureLinks",
                columns: table => new
                {
                    CommitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FeatureId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitFeatureLinks", x => new { x.CommitId, x.FeatureId });
                    table.ForeignKey(
                        name: "FK_CommitFeatureLinks_SourceCommits_CommitId",
                        column: x => x.CommitId,
                        principalTable: "SourceCommits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRuns_DefectId",
                table: "ExecutionRuns",
                column: "DefectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRuns_ReleaseId",
                table: "ExecutionRuns",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationBuilds_ProductId_CreatedAt",
                table: "ApplicationBuilds",
                columns: new[] { "ProductId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationBuilds_ReleaseId",
                table: "ApplicationBuilds",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildFeatureImpacts_FeatureId",
                table: "BuildFeatureImpacts",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_CommitFeatureLinks_FeatureId",
                table: "CommitFeatureLinks",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectEvents_DefectId_Id",
                table: "DefectEvents",
                columns: new[] { "DefectId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DefectOccurrences_TestExecutionId",
                table: "DefectOccurrences",
                column: "TestExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_FeatureId",
                table: "Defects",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_Fingerprint",
                table: "Defects",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_Number",
                table: "Defects",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Defects_ProductId_Status",
                table: "Defects",
                columns: new[] { "ProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Defects_TestCaseId",
                table: "Defects",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_EscalationPolicies_ChannelId",
                table: "EscalationPolicies",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_GateWaivers_GateId",
                table: "GateWaivers",
                column: "GateId");

            migrationBuilder.CreateIndex(
                name: "IX_GateWaivers_ReleaseId_GateId",
                table: "GateWaivers",
                columns: new[] { "ReleaseId", "GateId" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationKeys_KeyHash",
                table: "IntegrationKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_EventId",
                table: "NotificationDeliveries",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Status_CreatedAt",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_CreatedAt",
                table: "NotificationEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_Type_AcknowledgedAt",
                table: "NotificationEvents",
                columns: new[] { "Type", "AcknowledgedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_ChannelId",
                table: "NotificationRules",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_UserId",
                table: "NotificationRules",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityGates_ProductId_ReleaseId",
                table: "QualityGates",
                columns: new[] { "ProductId", "ReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseApprovals_ReleaseId",
                table: "ReleaseApprovals",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseFeatureChanges_FeatureId",
                table: "ReleaseFeatureChanges",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseFeatureChanges_ReleaseId_FeatureId_Kind",
                table: "ReleaseFeatureChanges",
                columns: new[] { "ReleaseId", "FeatureId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedReports_Owner",
                table: "SavedReports",
                column: "Owner");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCommits_BuildId",
                table: "SourceCommits",
                column: "BuildId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCommits_CommitId",
                table: "SourceCommits",
                column: "CommitId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRepositories_Name",
                table: "SourceRepositories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_EventId",
                table: "UserNotifications",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildFeatureImpacts");

            migrationBuilder.DropTable(
                name: "CommitFeatureLinks");

            migrationBuilder.DropTable(
                name: "DefectEvents");

            migrationBuilder.DropTable(
                name: "DefectOccurrences");

            migrationBuilder.DropTable(
                name: "EscalationPolicies");

            migrationBuilder.DropTable(
                name: "GateWaivers");

            migrationBuilder.DropTable(
                name: "IntegrationKeys");

            migrationBuilder.DropTable(
                name: "IssueTrackerConnections");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationRules");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");

            migrationBuilder.DropTable(
                name: "ReleaseApprovals");

            migrationBuilder.DropTable(
                name: "ReleaseFeatureChanges");

            migrationBuilder.DropTable(
                name: "SavedReports");

            migrationBuilder.DropTable(
                name: "SourceRepositories");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "SourceCommits");

            migrationBuilder.DropTable(
                name: "Defects");

            migrationBuilder.DropTable(
                name: "QualityGates");

            migrationBuilder.DropTable(
                name: "NotificationChannels");

            migrationBuilder.DropTable(
                name: "NotificationEvents");

            migrationBuilder.DropTable(
                name: "ApplicationBuilds");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionRuns_DefectId",
                table: "ExecutionRuns");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionRuns_ReleaseId",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "BuildVersion",
                table: "ProductVersions");

            migrationBuilder.DropColumn(
                name: "KnownRisks",
                table: "ProductVersions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ProductVersions");

            migrationBuilder.DropColumn(
                name: "TestingStartDate",
                table: "ProductVersions");

            migrationBuilder.DropColumn(
                name: "DefectId",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "ReleaseId",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "OfflineNotifiedAt",
                table: "ExecutionAgents");
        }
    }
}
