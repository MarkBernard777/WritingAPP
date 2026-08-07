using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterBookWritingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhaseGates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PhaseId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsPassed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfirmedByUser = table.Column<bool>(type: "INTEGER", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    OverrideReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PassedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhaseGates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StepProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PhaseId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StepNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowPhases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Deliverable = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    GateStatement = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    TemplatePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RouteAffinity = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowPhases", x => new { x.ProjectId, x.Id });
                    table.ForeignKey(
                        name: "FK_WorkflowPhases_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PhaseId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    RouteAffinity = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowSteps_WorkflowPhases_ProjectId_PhaseId",
                        columns: x => new { x.ProjectId, x.PhaseId },
                        principalTable: "WorkflowPhases",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhaseGates_ProjectId_PhaseId",
                table: "PhaseGates",
                columns: new[] { "ProjectId", "PhaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StepProgress_ProjectId_PhaseId_StepNumber",
                table: "StepProgress",
                columns: new[] { "ProjectId", "PhaseId", "StepNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowPhases_ProjectId_SortOrder",
                table: "WorkflowPhases",
                columns: new[] { "ProjectId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSteps_ProjectId_PhaseId_Number",
                table: "WorkflowSteps",
                columns: new[] { "ProjectId", "PhaseId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhaseGates");

            migrationBuilder.DropTable(
                name: "StepProgress");

            migrationBuilder.DropTable(
                name: "WorkflowSteps");

            migrationBuilder.DropTable(
                name: "WorkflowPhases");
        }
    }
}
