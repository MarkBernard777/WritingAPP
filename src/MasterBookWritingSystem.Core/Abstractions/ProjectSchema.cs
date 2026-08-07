namespace MasterBookWritingSystem.Core.Abstractions;

public static class ProjectSchema
{
    public const int CurrentVersion = 2;

    public const string InitialMigrationName = "InitialCreate";

    public const string WorkflowMigrationName = "AddWorkflowTables";
}
