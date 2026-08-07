using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterBookWritingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Author = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Genre = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PublishingRoute = table.Column<int>(type: "INTEGER", nullable: false),
                    NorthStar = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SchemaVersions",
                columns: table => new
                {
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AppliedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemaVersions", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "Chapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RelativeMarkdownPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chapters_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_ProjectId_SequenceNumber",
                table: "Chapters",
                columns: new[] { "ProjectId", "SequenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Chapters");

            migrationBuilder.DropTable(
                name: "SchemaVersions");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
