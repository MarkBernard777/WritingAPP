namespace MasterBookWritingSystem.Core.Domain.Tools;

public sealed class Idea
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string Title { get; set; }

    public string Notes { get; set; } = string.Empty;

    public string Decision { get; set; } = string.Empty;

    public DateTimeOffset? LastEditedUtc { get; set; }

    public IdeaScore? Score { get; set; }
}

public sealed class IdeaScore
{
    public required Guid Id { get; init; }

    public required Guid IdeaId { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>Each criterion is scored 0–10.</summary>
    public int Fascination { get; set; }

    public int EmotionalPower { get; set; }

    public int Conflict { get; set; }

    public int Character { get; set; }

    public int Visual { get; set; }

    public int OriginalCombination { get; set; }

    public int NovelLength { get; set; }

    public int DifficultChoices { get; set; }

    public int AudienceFit { get; set; }

    public int SeriesFit { get; set; }

    public int Total { get; set; }

    public string Decision { get; set; } = string.Empty;

    public DateTimeOffset? ScoredUtc { get; set; }
}

public sealed class BetaFeedbackEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Reader { get; set; } = string.Empty;

    public string ReaderType { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Observation { get; set; } = string.Empty;

    public string SuggestedFix { get; set; } = string.Empty;
}
