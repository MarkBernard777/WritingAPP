using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterBookWritingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftingProgressV12 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PartId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChapterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SceneId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ViewpointCharacterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ViewpointLabel = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    StartedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastActivityUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ActiveDurationSeconds = table.Column<double>(type: "REAL", nullable: false),
                    StartingWordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EndingWordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NetWordChange = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletionReason = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DraftingSessions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DraftingTargets",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DailyWordGoal = table.Column<int>(type: "INTEGER", nullable: false),
                    WeeklyWordGoal = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftingTargets", x => x.ProjectId);
                    table.ForeignKey(
                        name: "FK_DraftingTargets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftingSessions_ProjectId_CompletionReason_EndedUtc",
                table: "DraftingSessions",
                columns: new[] { "ProjectId", "CompletionReason", "EndedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftingSessions_ProjectId_StartedUtc",
                table: "DraftingSessions",
                columns: new[] { "ProjectId", "StartedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DraftingSessions");

            migrationBuilder.DropTable(
                name: "DraftingTargets");
        }
    }
}
