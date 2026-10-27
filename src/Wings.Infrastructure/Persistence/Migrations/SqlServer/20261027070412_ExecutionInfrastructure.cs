using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wings.Infrastructure.Persistence.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class ExecutionInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutionAgents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Pool = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    InMaintenance = table.Column<bool>(type: "bit", nullable: false),
                    MachineName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OperatingSystem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AgentVersion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Slots = table.Column<int>(type: "int", nullable: false),
                    FrameworksCsv = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    InteractiveSession = table.Column<bool>(type: "bit", nullable: false),
                    ScreenResolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DpiScalePercent = table.Column<int>(type: "int", nullable: true),
                    ProcessorCount = table.Column<int>(type: "int", nullable: false),
                    TotalMemoryMb = table.Column<long>(type: "bigint", nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionAgents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionArtifacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ScopeDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ScopeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    SchedulingPaused = table.Column<bool>(type: "bit", nullable: false),
                    CancelRequested = table.Column<bool>(type: "bit", nullable: false),
                    VariablesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaxInfrastructureRetries = table.Column<int>(type: "int", nullable: false),
                    TimeoutMinutes = table.Column<int>(type: "int", nullable: false),
                    SourceRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TotalTests = table.Column<int>(type: "int", nullable: false),
                    Passed = table.Column<int>(type: "int", nullable: false),
                    Failed = table.Column<int>(type: "int", nullable: false),
                    Skipped = table.Column<int>(type: "int", nullable: false),
                    Blocked = table.Column<int>(type: "int", nullable: false),
                    NotRun = table.Column<int>(type: "int", nullable: false),
                    InfrastructureErrors = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Framework = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequiresDesktop = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LeasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelRequested = table.Column<bool>(type: "bit", nullable: false),
                    TestIdentifiersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExitCode = table.Column<int>(type: "int", nullable: true),
                    RetryOfJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LogLineCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionJobs_ExecutionAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "ExecutionAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ExecutionJobs_ExecutionRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ExecutionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExecutionJobs_TestPackageVersions_PackageVersionId",
                        column: x => x.PackageVersionId,
                        principalTable: "TestPackageVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    PackageVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PackageTestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TestCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FeatureId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Identifier = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StackTrace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Output = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsSuperseded = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestExecutions_ExecutionRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ExecutionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionAgents_KeyHash",
                table: "ExecutionAgents",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionAgents_Name",
                table: "ExecutionAgents",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionArtifacts_RunId",
                table: "ExecutionArtifacts",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionArtifacts_TestExecutionId",
                table: "ExecutionArtifacts",
                column: "TestExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionJobs_AgentId_Status",
                table: "ExecutionJobs",
                columns: new[] { "AgentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionJobs_PackageVersionId",
                table: "ExecutionJobs",
                column: "PackageVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionJobs_RunId",
                table: "ExecutionJobs",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionJobs_Status_Framework",
                table: "ExecutionJobs",
                columns: new[] { "Status", "Framework" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLogs_JobId_Sequence",
                table: "ExecutionLogs",
                columns: new[] { "JobId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRuns_CreatedAt",
                table: "ExecutionRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRuns_Number",
                table: "ExecutionRuns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRuns_Status",
                table: "ExecutionRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_FeatureId_CompletedAt",
                table: "TestExecutions",
                columns: new[] { "FeatureId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_JobId_Identifier",
                table: "TestExecutions",
                columns: new[] { "JobId", "Identifier" });

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_RunId_Outcome",
                table: "TestExecutions",
                columns: new[] { "RunId", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_TestCaseId_CompletedAt",
                table: "TestExecutions",
                columns: new[] { "TestCaseId", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionArtifacts");

            migrationBuilder.DropTable(
                name: "ExecutionJobs");

            migrationBuilder.DropTable(
                name: "ExecutionLogs");

            migrationBuilder.DropTable(
                name: "TestExecutions");

            migrationBuilder.DropTable(
                name: "ExecutionAgents");

            migrationBuilder.DropTable(
                name: "ExecutionRuns");
        }
    }
}
