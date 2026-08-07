using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterBookWritingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LaunchItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Asset = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Audience = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Link = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    RouteAffinity = table.Column<int>(type: "INTEGER", nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaunchItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RightsAndContracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Party = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RightOrService = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Territory = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndOrReversionDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Payment = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    PaymentNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Restrictions = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    AgreementFile = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RightsAndContracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AgencyOrPublisher = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Website = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Fit = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Requirements = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    MaterialSent = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SentDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FollowUpDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Response = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    RouteAffinity = table.Column<int>(type: "INTEGER", nullable: false),
                    LastEditedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LaunchItems_ProjectId_Date",
                table: "LaunchItems",
                columns: new[] { "ProjectId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_LaunchItems_ProjectId_Status",
                table: "LaunchItems",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RightsAndContracts_ProjectId_Party",
                table: "RightsAndContracts",
                columns: new[] { "ProjectId", "Party" });

            migrationBuilder.CreateIndex(
                name: "IX_RightsAndContracts_ProjectId_Status",
                table: "RightsAndContracts",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ProjectId_Name",
                table: "Submissions",
                columns: new[] { "ProjectId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ProjectId_Outcome",
                table: "Submissions",
                columns: new[] { "ProjectId", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ProjectId_RouteAffinity",
                table: "Submissions",
                columns: new[] { "ProjectId", "RouteAffinity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LaunchItems");

            migrationBuilder.DropTable(
                name: "RightsAndContracts");

            migrationBuilder.DropTable(
                name: "Submissions");
        }
    }
}
