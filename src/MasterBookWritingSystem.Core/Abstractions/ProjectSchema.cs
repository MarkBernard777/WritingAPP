namespace MasterBookWritingSystem.Core.Abstractions;

public static class ProjectSchema
{
    public const int CurrentVersion = 7;

    public const string InitialMigrationName = "InitialCreate";

    public const string WorkflowMigrationName = "AddWorkflowTables";

    public const string DocumentsMigrationName = "AddDocumentTables";

    public const string StoryDataMigrationName = "AddCoreStoryData";

    public const string IdeaScoringMigrationName = "AddIdeaScoring";

    public const string PublishingWorkflowMigrationName = "AddPublishingWorkflow";

    public const string PostPublicationTrackingMigrationName = "AddPostPublicationTracking";
}
