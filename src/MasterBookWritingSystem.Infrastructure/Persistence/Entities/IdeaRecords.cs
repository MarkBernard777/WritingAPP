namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class IdeaRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string Title { get; set; }

    public string Notes { get; set; } = string.Empty;

    public string Decision { get; set; } = string.Empty;

    public DateTimeOffset? LastEditedUtc { get; set; }

    public IdeaScoreRecord? Score { get; set; }
}

public sealed class IdeaScoreRecord
{
    public Guid Id { get; set; }

    public Guid IdeaId { get; set; }

    public Guid ProjectId { get; set; }

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

    public IdeaRecord? Idea { get; set; }
}
