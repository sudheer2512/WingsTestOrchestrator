using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wings.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Orchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutedBy",
                table: "TestExecutions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrerequisitesJson",
                table: "TestExecutions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataRequirements",
                table: "TestCases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDestructive",
                table: "TestCases",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ResourceKeys",
                table: "TestCases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuildVersion",
                table: "ExecutionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                table: "ExecutionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EnvironmentId",
                table: "ExecutionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeManual",
                table: "ExecutionRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                table: "ExecutionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScheduleId",
                table: "ExecutionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggeredBy",
                table: "ExecutionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentPools",
                table: "ExecutionJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataRequirementsJson",
                table: "ExecutionJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EnvironmentId",
                table: "ExecutionJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceKeys",
                table: "ExecutionJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Wave",
                table: "ExecutionJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ExecutionJobDependencies",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DependsOnJobId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionJobDependencies", x => new { x.JobId, x.DependsOnJobId });
                    table.ForeignKey(
                        name: "FK_ExecutionJobDependencies_ExecutionJobs_DependsOnJobId",
                        column: x => x.DependsOnJobId,
                        principalTable: "ExecutionJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExecutionJobDependencies_ExecutionJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ExecutionJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceLocks",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcquiredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceLocks", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TestCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    BuildVersion = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
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
                    table.PrimaryKey("PK_TestCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestEnvironments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsProduction = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowedDestructiveTestCaseIds = table.Column<string>(type: "TEXT", nullable: true),
                    BaseUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ApplicationBuildVersion = table.Column<string>(type: "TEXT", nullable: true),
                    DatabaseReference = table.Column<string>(type: "TEXT", nullable: true),
                    TenantCompany = table.Column<string>(type: "TEXT", nullable: true),
                    AuthenticationNotes = table.Column<string>(type: "TEXT", nullable: true),
                    Dependencies = table.Column<string>(type: "TEXT", nullable: true),
                    AgentPools = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    HealthCheckUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Health = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    HealthMessage = table.Column<string>(type: "TEXT", nullable: true),
                    LastHealthCheckAt = table.Column<long>(type: "INTEGER", nullable: true),
                    InMaintenance = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResetProcedure = table.Column<string>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_TestEnvironments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Objective = table.Column<string>(type: "TEXT", nullable: true),
                    Scope = table.Column<string>(type: "TEXT", nullable: true),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetReleaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BuildVersion = table.Column<string>(type: "TEXT", nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Owners = table.Column<string>(type: "TEXT", nullable: true),
                    Testers = table.Column<string>(type: "TEXT", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EntryCriteria = table.Column<string>(type: "TEXT", nullable: true),
                    ExitCriteria = table.Column<string>(type: "TEXT", nullable: true),
                    Risk = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    DefectAcceptanceRules = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IsTemplate = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ApprovalComment = table.Column<string>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_TestPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CronExpression = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RunOnceAt = table.Column<long>(type: "INTEGER", nullable: true),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VariablesJson = table.Column<string>(type: "TEXT", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    NextRunAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastTriggeredAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    WebhookTokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    WebhookTokenPrefix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnvironmentVariables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    ProtectedValue = table.Column<string>(type: "TEXT", nullable: true),
                    IsSecret = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvironmentVariables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnvironmentVariables_TestEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "TestEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestCampaignEnvironments",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestCampaignEnvironments", x => new { x.CampaignId, x.EnvironmentId });
                    table.ForeignKey(
                        name: "FK_TestCampaignEnvironments_TestCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "TestCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestCampaignEnvironments_TestEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "TestEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestDataSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DataVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SetupNotes = table.Column<string>(type: "TEXT", nullable: true),
                    CleanupNotes = table.Column<string>(type: "TEXT", nullable: true),
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
                    table.PrimaryKey("PK_TestDataSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestDataSets_TestEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "TestEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TestCampaignPlans",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestCampaignPlans", x => new { x.CampaignId, x.PlanId });
                    table.ForeignKey(
                        name: "FK_TestCampaignPlans_TestCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "TestCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestCampaignPlans_TestPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TestPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestPlanSuites",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SuiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestPlanSuites", x => new { x.PlanId, x.SuiteId });
                    table.ForeignKey(
                        name: "FK_TestPlanSuites_TestPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TestPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestPlanSuites_TestSuites_SuiteId",
                        column: x => x.SuiteId,
                        principalTable: "TestSuites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestDataRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DataSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestDataRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestDataRecords_TestDataSets_DataSetId",
                        column: x => x.DataSetId,
                        principalTable: "TestDataSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestDataReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DataSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReservedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReleasedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Active = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestDataReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestDataReservations_TestDataRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "TestDataRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRuns_CampaignId",
                table: "ExecutionRuns",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRuns_PlanId",
                table: "ExecutionRuns",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRuns_ScheduleId",
                table: "ExecutionRuns",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_EnvironmentVariables_EnvironmentId_Name",
                table: "EnvironmentVariables",
                columns: new[] { "EnvironmentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionJobDependencies_DependsOnJobId",
                table: "ExecutionJobDependencies",
                column: "DependsOnJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceLocks_JobId",
                table: "ResourceLocks",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCampaignEnvironments_EnvironmentId",
                table: "TestCampaignEnvironments",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCampaignPlans_PlanId",
                table: "TestCampaignPlans",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCampaigns_Number",
                table: "TestCampaigns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestDataRecords_DataSetId_Key",
                table: "TestDataRecords",
                columns: new[] { "DataSetId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestDataReservations_JobId",
                table: "TestDataReservations",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_TestDataReservations_RecordId",
                table: "TestDataReservations",
                column: "RecordId",
                unique: true,
                filter: "\"Active\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TestDataSets_Code",
                table: "TestDataSets",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestDataSets_EnvironmentId",
                table: "TestDataSets",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TestEnvironments_Code",
                table: "TestEnvironments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_Number",
                table: "TestPlans",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanSuites_SuiteId",
                table: "TestPlanSuites",
                column: "SuiteId");

            migrationBuilder.CreateIndex(
                name: "IX_TestSchedules_IsEnabled_NextRunAt",
                table: "TestSchedules",
                columns: new[] { "IsEnabled", "NextRunAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnvironmentVariables");

            migrationBuilder.DropTable(
                name: "ExecutionJobDependencies");

            migrationBuilder.DropTable(
                name: "ResourceLocks");

            migrationBuilder.DropTable(
                name: "TestCampaignEnvironments");

            migrationBuilder.DropTable(
                name: "TestCampaignPlans");

            migrationBuilder.DropTable(
                name: "TestDataReservations");

            migrationBuilder.DropTable(
                name: "TestPlanSuites");

            migrationBuilder.DropTable(
                name: "TestSchedules");

            migrationBuilder.DropTable(
                name: "TestCampaigns");

            migrationBuilder.DropTable(
                name: "TestDataRecords");

            migrationBuilder.DropTable(
                name: "TestPlans");

            migrationBuilder.DropTable(
                name: "TestDataSets");

            migrationBuilder.DropTable(
                name: "TestEnvironments");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionRuns_CampaignId",
                table: "ExecutionRuns");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionRuns_PlanId",
                table: "ExecutionRuns");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionRuns_ScheduleId",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "ExecutedBy",
                table: "TestExecutions");

            migrationBuilder.DropColumn(
                name: "PrerequisitesJson",
                table: "TestExecutions");

            migrationBuilder.DropColumn(
                name: "DataRequirements",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "IsDestructive",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "ResourceKeys",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "BuildVersion",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "EnvironmentId",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "IncludeManual",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "ScheduleId",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "TriggeredBy",
                table: "ExecutionRuns");

            migrationBuilder.DropColumn(
                name: "AgentPools",
                table: "ExecutionJobs");

            migrationBuilder.DropColumn(
                name: "DataRequirementsJson",
                table: "ExecutionJobs");

            migrationBuilder.DropColumn(
                name: "EnvironmentId",
                table: "ExecutionJobs");

            migrationBuilder.DropColumn(
                name: "ResourceKeys",
                table: "ExecutionJobs");

            migrationBuilder.DropColumn(
                name: "Wave",
                table: "ExecutionJobs");
        }
    }
}
