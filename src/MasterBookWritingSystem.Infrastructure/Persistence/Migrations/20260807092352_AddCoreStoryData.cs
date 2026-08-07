using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterBookWritingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreStoryData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Beats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    ChapterRef = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SceneRef = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ViewpointCharacterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Event = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Cause = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Consequence = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ArcFunction = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Theme = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Escalation = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Beats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Goal = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Need = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Fear = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Wound = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    FalseBelief = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Contradiction = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Skills = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Weaknesses = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Resources = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    RelationshipsNotes = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    StartingState = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    EndingState = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    BookArc = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    SeriesArc = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    IsViewpoint = table.Column<bool>(type: "INTEGER", nullable: false),
                    SceneAppearancesNotes = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChapterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ViewpointCharacterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Location = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Time = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Goal = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Opposition = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Stakes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    MainEvent = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Revelation = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    EmotionalTurn = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Choice = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Consequence = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    SetupObligations = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    PayoffObligations = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    NextSceneId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorldEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    TravelDistance = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TravelTime = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CanonicalFacts = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    ConflictingEntries = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorldEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Beats_ProjectId_Number",
                table: "Beats",
                columns: new[] { "ProjectId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_ProjectId_Name",
                table: "Characters",
                columns: new[] { "ProjectId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_ProjectId_SequenceNumber",
                table: "Scenes",
                columns: new[] { "ProjectId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorldEntries_ProjectId_Category_Name",
                table: "WorldEntries",
                columns: new[] { "ProjectId", "Category", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Beats");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "Scenes");

            migrationBuilder.DropTable(
                name: "WorldEntries");
        }
    }
}
