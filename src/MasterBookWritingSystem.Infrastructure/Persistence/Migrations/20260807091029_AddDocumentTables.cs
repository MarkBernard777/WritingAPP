using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterBookWritingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentType = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    RelativeMarkdownPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    TemplatePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CompletionPercentage = table.Column<int>(type: "INTEGER", nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentFields_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFields_DocumentId_Key",
                table: "DocumentFields",
                columns: new[] { "DocumentId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ProjectId_DocumentType",
                table: "Documents",
                columns: new[] { "ProjectId", "DocumentType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentFields");

            migrationBuilder.DropTable(
                name: "Documents");
        }
    }
}
