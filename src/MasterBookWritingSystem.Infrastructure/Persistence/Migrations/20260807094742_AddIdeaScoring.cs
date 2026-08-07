using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterBookWritingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdeaScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ideas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ideas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdeaScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdeaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fascination = table.Column<int>(type: "INTEGER", nullable: false),
                    EmotionalPower = table.Column<int>(type: "INTEGER", nullable: false),
                    Conflict = table.Column<int>(type: "INTEGER", nullable: false),
                    Character = table.Column<int>(type: "INTEGER", nullable: false),
                    Visual = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalCombination = table.Column<int>(type: "INTEGER", nullable: false),
                    NovelLength = table.Column<int>(type: "INTEGER", nullable: false),
                    DifficultChoices = table.Column<int>(type: "INTEGER", nullable: false),
                    AudienceFit = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesFit = table.Column<int>(type: "INTEGER", nullable: false),
                    Total = table.Column<int>(type: "INTEGER", nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ScoredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdeaScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdeaScores_Ideas_IdeaId",
                        column: x => x.IdeaId,
                        principalTable: "Ideas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ideas_ProjectId_Title",
                table: "Ideas",
                columns: new[] { "ProjectId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_IdeaScores_IdeaId",
                table: "IdeaScores",
                column: "IdeaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdeaScores_ProjectId",
                table: "IdeaScores",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdeaScores");

            migrationBuilder.DropTable(
                name: "Ideas");
        }
    }
}
