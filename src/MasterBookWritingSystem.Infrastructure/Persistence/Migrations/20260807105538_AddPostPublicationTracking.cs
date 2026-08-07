using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterBookWritingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostPublicationTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Corrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CorrectionText = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ReportedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CorrectedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FormatsUpdated = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NewEdition = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Corrections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Series = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SeriesNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Author = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Categories = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SearchTerms = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ReaderAge = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PublicationDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Edition = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Publisher = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PricingNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    TerritoryRights = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Isbn = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FormatName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PublicationStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PerformanceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodLabel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Format = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Sales = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    ReadThrough = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MailingList = table.Column<int>(type: "INTEGER", nullable: true),
                    Reviews = table.Column<int>(type: "INTEGER", nullable: true),
                    AdCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Availability = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ReturnsOrIssues = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublishingFormats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FormatKind = table.Column<int>(type: "INTEGER", nullable: false),
                    IsbnOrAsin = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TrimOrFileSpec = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Distributor = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PublicationStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    AssetLink = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishingFormats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Corrections_ProjectId_ReportedDate",
                table: "Corrections",
                columns: new[] { "ProjectId", "ReportedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Corrections_ProjectId_Status",
                table: "Corrections",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataRecords_ProjectId_PublicationStatus",
                table: "MetadataRecords",
                columns: new[] { "ProjectId", "PublicationStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataRecords_ProjectId_Title",
                table: "MetadataRecords",
                columns: new[] { "ProjectId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceRecords_ProjectId_Format",
                table: "PerformanceRecords",
                columns: new[] { "ProjectId", "Format" });

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceRecords_ProjectId_PeriodStart",
                table: "PerformanceRecords",
                columns: new[] { "ProjectId", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_PublishingFormats_ProjectId_Name",
                table: "PublishingFormats",
                columns: new[] { "ProjectId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_PublishingFormats_ProjectId_PublicationStatus",
                table: "PublishingFormats",
                columns: new[] { "ProjectId", "PublicationStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Corrections");

            migrationBuilder.DropTable(
                name: "MetadataRecords");

            migrationBuilder.DropTable(
                name: "PerformanceRecords");

            migrationBuilder.DropTable(
                name: "PublishingFormats");
        }
    }
}
