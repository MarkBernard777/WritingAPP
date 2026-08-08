using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterBookWritingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManuscriptHierarchyV12 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scenes_ProjectId_SequenceNumber",
                table: "Scenes");

            migrationBuilder.AddColumn<Guid>(
                name: "PartId",
                table: "Chapters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Books_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Parts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Parts_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_ChapterId",
                table: "Scenes",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_ProjectId_ChapterId_SequenceNumber",
                table: "Scenes",
                columns: new[] { "ProjectId", "ChapterId", "SequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_PartId",
                table: "Chapters",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_ProjectId_SequenceNumber",
                table: "Books",
                columns: new[] { "ProjectId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parts_BookId_SequenceNumber",
                table: "Parts",
                columns: new[] { "BookId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parts_ProjectId_BookId",
                table: "Parts",
                columns: new[] { "ProjectId", "BookId" });

            // Repair soft-link orphans before enforcing the chapter FK.
            migrationBuilder.Sql(
                """
                UPDATE Scenes
                SET ChapterId = NULL
                WHERE ChapterId IS NOT NULL
                  AND ChapterId NOT IN (SELECT Id FROM Chapters);
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IX_Scenes_ChapterId_SequenceNumber_Assigned
                ON Scenes(ChapterId, SequenceNumber)
                WHERE ChapterId IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IX_Scenes_ProjectId_SequenceNumber_Unassigned
                ON Scenes(ProjectId, SequenceNumber)
                WHERE ChapterId IS NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Chapters_Parts_PartId",
                table: "Chapters",
                column: "PartId",
                principalTable: "Parts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Scenes_Chapters_ChapterId",
                table: "Scenes",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Chapters_Parts_PartId",
                table: "Chapters");

            migrationBuilder.DropForeignKey(
                name: "FK_Scenes_Chapters_ChapterId",
                table: "Scenes");

            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Scenes_ChapterId_SequenceNumber_Assigned;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Scenes_ProjectId_SequenceNumber_Unassigned;");

            migrationBuilder.DropTable(
                name: "Parts");

            migrationBuilder.DropTable(
                name: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Scenes_ChapterId",
                table: "Scenes");

            migrationBuilder.DropIndex(
                name: "IX_Scenes_ProjectId_ChapterId_SequenceNumber",
                table: "Scenes");

            migrationBuilder.DropIndex(
                name: "IX_Chapters_PartId",
                table: "Chapters");

            migrationBuilder.DropColumn(
                name: "PartId",
                table: "Chapters");

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_ProjectId_SequenceNumber",
                table: "Scenes",
                columns: new[] { "ProjectId", "SequenceNumber" },
                unique: true);
        }
    }
}
