using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Tools;
using MasterBookWritingSystem.Core.Tools;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Tools;

public sealed class IdeaService : IIdeaService
{
    private readonly IProjectService _projectService;

    public IdeaService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public async Task<IReadOnlyList<Idea>> GetAllAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var records = await context.Ideas
            .AsNoTracking()
            .Include(idea => idea.Score)
            .Where(idea => idea.ProjectId == projectId)
            .OrderByDescending(idea => idea.Score != null ? idea.Score.Total : -1)
            .ThenBy(idea => idea.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<Idea> GetAsync(
        Guid projectId,
        Guid ideaId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.Ideas
            .AsNoTracking()
            .Include(idea => idea.Score)
            .FirstOrDefaultAsync(idea => idea.ProjectId == projectId && idea.Id == ideaId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Idea '{ideaId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Idea> CreateAsync(
        Guid projectId,
        string title,
        string notes = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await using var context = Open(projectId);
        var record = new IdeaRecord
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Title = title.Trim(),
            Notes = notes ?? string.Empty,
            LastEditedUtc = DateTimeOffset.UtcNow,
        };
        context.Ideas.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Idea> UpdateAsync(
        Guid projectId,
        Idea idea,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(idea);
        ArgumentException.ThrowIfNullOrWhiteSpace(idea.Title);
        await using var context = Open(projectId);
        var record = await context.Ideas
            .Include(item => item.Score)
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Id == idea.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Idea '{idea.Id}' was not found.");

        record.Title = idea.Title.Trim();
        record.Notes = idea.Notes ?? string.Empty;
        record.Decision = idea.Decision ?? string.Empty;
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task DeleteAsync(
        Guid projectId,
        Guid ideaId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.Ideas
            .Include(idea => idea.Score)
            .FirstOrDefaultAsync(idea => idea.ProjectId == projectId && idea.Id == ideaId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Idea '{ideaId}' was not found.");

        context.Ideas.Remove(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public async Task<Idea> SaveScoreAsync(
        Guid projectId,
        Guid ideaId,
        IdeaScore score,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        IdeaScorecardCalculator.Apply(score);

        await using var context = Open(projectId);
        var idea = await context.Ideas
            .Include(item => item.Score)
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Id == ideaId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Idea '{ideaId}' was not found.");

        if (idea.Score is null)
        {
            idea.Score = new IdeaScoreRecord
            {
                Id = score.Id == Guid.Empty ? Guid.NewGuid() : score.Id,
                IdeaId = ideaId,
                ProjectId = projectId,
            };
            context.IdeaScores.Add(idea.Score);
        }

        ApplyScore(idea.Score, score);
        idea.Decision = idea.Score.Decision;
        idea.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(idea);
    }

    private ProjectDbContext Open(Guid projectId)
    {
        var root = RequireActiveRoot(projectId);
        return ProjectDbContextFactory.Create(Path.Combine(root, ProjectPaths.DatabaseFileName));
    }

    private string RequireActiveRoot(Guid projectId)
    {
        var active = _projectService.ActiveProject
            ?? throw new InvalidOperationException("Open a project before using idea services.");
        if (active.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return active.RootPath;
    }

    private static void ApplyScore(IdeaScoreRecord record, IdeaScore score)
    {
        record.Fascination = score.Fascination;
        record.EmotionalPower = score.EmotionalPower;
        record.Conflict = score.Conflict;
        record.Character = score.Character;
        record.Visual = score.Visual;
        record.OriginalCombination = score.OriginalCombination;
        record.NovelLength = score.NovelLength;
        record.DifficultChoices = score.DifficultChoices;
        record.AudienceFit = score.AudienceFit;
        record.SeriesFit = score.SeriesFit;
        record.Total = score.Total;
        record.Decision = score.Decision;
        record.ScoredUtc = score.ScoredUtc;
    }

    private static Idea ToDomain(IdeaRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        Title = record.Title,
        Notes = record.Notes,
        Decision = record.Decision,
        LastEditedUtc = record.LastEditedUtc,
        Score = record.Score is null
            ? null
            : new IdeaScore
            {
                Id = record.Score.Id,
                IdeaId = record.Score.IdeaId,
                ProjectId = record.Score.ProjectId,
                Fascination = record.Score.Fascination,
                EmotionalPower = record.Score.EmotionalPower,
                Conflict = record.Score.Conflict,
                Character = record.Score.Character,
                Visual = record.Score.Visual,
                OriginalCombination = record.Score.OriginalCombination,
                NovelLength = record.Score.NovelLength,
                DifficultChoices = record.Score.DifficultChoices,
                AudienceFit = record.Score.AudienceFit,
                SeriesFit = record.Score.SeriesFit,
                Total = record.Score.Total,
                Decision = record.Score.Decision,
                ScoredUtc = record.Score.ScoredUtc,
            },
    };
}
