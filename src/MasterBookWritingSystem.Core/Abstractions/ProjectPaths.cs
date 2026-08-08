namespace MasterBookWritingSystem.Core.Abstractions;

public static class ProjectPaths
{
    public const string DatabaseFileName = "project.mbws";

    public const string MetadataFileName = "project.json";

    public const string DraftChaptersRelativeDirectory = "09 Draft/Chapters";

    public const string PublishingDirectory = "11 Publishing";

    public const string MarketingDirectory = "12 Marketing";

    public const string PublishingPlanRelativePath = "11 Publishing/20_Publishing_Plan.md";

    public const string MetadataSheetRelativePath = "11 Publishing/21_Metadata_Sheet.md";

    public const string LaunchPlanRelativePath = "12 Marketing/22_Launch_Plan.md";

    public const string RightsContractsRegisterRelativePath = "11 Publishing/23_Rights_Contracts_Register.md";

    public static readonly string[] StandardDirectories =
    [
        "01 Project Definition",
        "02 Ideas and Research",
        "03 Premise and Theme",
        "04 Series Architecture",
        "05 World Bible",
        "06 Character Bible",
        "07 Plot and Beats",
        "08 Scene Outline",
        "09 Draft",
        DraftChaptersRelativeDirectory.Replace('/', Path.DirectorySeparatorChar),
        "10 Revision",
        "11 Publishing",
        "12 Marketing",
        "13 Archive",
        Path.Combine("13 Archive", "Snapshots"),
        Path.Combine("13 Archive", "Snapshots", "Retired"),
        "Assets",
        "Exports",
    ];
}
