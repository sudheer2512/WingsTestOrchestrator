using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wings.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AiAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiInteractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SubjectType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectLabel = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    RedactionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    InputSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InputChars = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ContextManifestJson = table.Column<string>(type: "TEXT", nullable: true),
                    InputTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    OutputTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    OutcomeBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    OutcomeAt = table.Column<long>(type: "INTEGER", nullable: true),
                    OutcomeNote = table.Column<string>(type: "TEXT", nullable: true),
                    AppliedJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiInteractions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowSourceCode = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowScreenshots = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaskEmails = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaskPhoneNumbers = table.Column<bool>(type: "INTEGER", nullable: false),
                    AdditionalRedactionPatterns = table.Column<string>(type: "TEXT", nullable: true),
                    DailyRequestsPerUser = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectConventions = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPolicies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiInteractions_SubjectType_SubjectId",
                table: "AiInteractions",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiInteractions_UserName_CreatedAt",
                table: "AiInteractions",
                columns: new[] { "UserName", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiInteractions");

            migrationBuilder.DropTable(
                name: "AiPolicies");
        }
    }
}
