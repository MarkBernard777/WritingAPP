using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Story;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Story;

public sealed class StoryDataService : IStoryDataService
{
    private readonly IProjectService _projectService;

    public StoryDataService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public StoryValidationResult ValidateCharacter(Character character)
        => StoryDataValidation.Validate(character);

    public StoryValidationResult ValidateWorldEntry(WorldEntry entry)
        => StoryDataValidation.Validate(entry);

    public StoryValidationResult ValidateBeat(Beat beat)
        => StoryDataValidation.Validate(beat);

    public StoryValidationResult ValidateScene(Scene scene)
        => StoryDataValidation.Validate(scene);

    public async Task<IReadOnlyList<Character>> GetCharactersAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var records = await context.Characters
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<Character> GetCharacterAsync(
        Guid projectId,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.Characters
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == characterId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Character '{characterId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Character> CreateCharacterAsync(
        Guid projectId,
        Character character,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        EnsureValid(ValidateCharacter(character));
        character = WithProject(character, projectId);
        if (character.Id == Guid.Empty)
        {
            character = CloneCharacter(character, Guid.NewGuid());
        }

        await using var context = Open(projectId);
        var record = ToRecord(character);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        context.Characters.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Character> UpdateCharacterAsync(
        Guid projectId,
        Character character,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        EnsureValid(ValidateCharacter(character));
        character = WithProject(character, projectId);

        await using var context = Open(projectId);
        var record = await context.Characters
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == character.Id,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Character '{character.Id}' was not found.");

        Apply(record, character);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task DeleteCharacterAsync(
        Guid projectId,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.Characters
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == characterId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Character '{characterId}' was not found.");

        var referencedByScenes = await context.Scenes
            .AnyAsync(
                scene => scene.ProjectId == projectId && scene.ViewpointCharacterId == characterId,
                cancellationToken)
            .ConfigureAwait(false);
        var referencedByBeats = await context.Beats
            .AnyAsync(
                beat => beat.ProjectId == projectId && beat.ViewpointCharacterId == characterId,
                cancellationToken)
            .ConfigureAwait(false);
        if (referencedByScenes || referencedByBeats)
        {
            throw new InvalidOperationException(
                "Cannot delete a character that is referenced as a viewpoint on beats or scenes.");
        }

        context.Characters.Remove(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public async Task<IReadOnlyList<WorldEntry>> GetWorldEntriesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var records = await context.WorldEntries
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .OrderBy(item => item.Category)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<WorldEntry> GetWorldEntryAsync(
        Guid projectId,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.WorldEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == entryId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"World entry '{entryId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<WorldEntry> CreateWorldEntryAsync(
        Guid projectId,
        WorldEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureValid(ValidateWorldEntry(entry));
        entry = WithProject(entry, projectId);
        if (entry.Id == Guid.Empty)
        {
            entry = CloneWorldEntry(entry, Guid.NewGuid());
        }

        await using var context = Open(projectId);
        var record = ToRecord(entry);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        context.WorldEntries.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<WorldEntry> UpdateWorldEntryAsync(
        Guid projectId,
        WorldEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureValid(ValidateWorldEntry(entry));
        entry = WithProject(entry, projectId);

        await using var context = Open(projectId);
        var record = await context.WorldEntries
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == entry.Id,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"World entry '{entry.Id}' was not found.");

        Apply(record, entry);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task DeleteWorldEntryAsync(
        Guid projectId,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.WorldEntries
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == entryId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"World entry '{entryId}' was not found.");

        context.WorldEntries.Remove(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public async Task<IReadOnlyList<Beat>> GetBeatsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var records = await context.Beats
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .OrderBy(item => item.Number)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<Beat> GetBeatAsync(
        Guid projectId,
        Guid beatId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.Beats
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == beatId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Beat '{beatId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Beat> CreateBeatAsync(
        Guid projectId,
        Beat beat,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beat);
        EnsureValid(ValidateBeat(beat));
        beat = WithProject(beat, projectId);
        if (beat.Id == Guid.Empty)
        {
            beat = CloneBeat(beat, Guid.NewGuid());
        }

        await using var context = Open(projectId);
        await EnsureCharacterExistsAsync(context, projectId, beat.ViewpointCharacterId, cancellationToken)
            .ConfigureAwait(false);
        await EnsureBeatNumberAvailableAsync(context, projectId, beat.Number, excludingId: null, cancellationToken)
            .ConfigureAwait(false);

        var record = ToRecord(beat);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        context.Beats.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Beat> UpdateBeatAsync(
        Guid projectId,
        Beat beat,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beat);
        EnsureValid(ValidateBeat(beat));
        beat = WithProject(beat, projectId);

        await using var context = Open(projectId);
        await EnsureCharacterExistsAsync(context, projectId, beat.ViewpointCharacterId, cancellationToken)
            .ConfigureAwait(false);
        await EnsureBeatNumberAvailableAsync(context, projectId, beat.Number, beat.Id, cancellationToken)
            .ConfigureAwait(false);

        var record = await context.Beats
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == beat.Id,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Beat '{beat.Id}' was not found.");

        Apply(record, beat);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task DeleteBeatAsync(
        Guid projectId,
        Guid beatId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.Beats
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == beatId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Beat '{beatId}' was not found.");

        context.Beats.Remove(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public async Task<IReadOnlyList<Scene>> GetScenesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var records = await context.Scenes
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .OrderBy(item => item.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<Scene> GetSceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.Scenes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == sceneId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Scene '{sceneId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Scene> CreateSceneAsync(
        Guid projectId,
        Scene scene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        EnsureValid(ValidateScene(scene));
        scene = WithProject(scene, projectId);
        if (scene.Id == Guid.Empty)
        {
            scene = CloneScene(scene, Guid.NewGuid());
        }

        await using var context = Open(projectId);
        await EnsureCharacterExistsAsync(context, projectId, scene.ViewpointCharacterId, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSceneExistsAsync(context, projectId, scene.NextSceneId, cancellationToken)
            .ConfigureAwait(false);
        await EnsureChapterExistsAsync(context, projectId, scene.ChapterId, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSceneSequenceAvailableAsync(
                context,
                projectId,
                scene.ChapterId,
                scene.SequenceNumber,
                excludingId: null,
                cancellationToken)
            .ConfigureAwait(false);

        var record = ToRecord(scene);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        context.Scenes.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Scene> UpdateSceneAsync(
        Guid projectId,
        Scene scene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        EnsureValid(ValidateScene(scene));
        scene = WithProject(scene, projectId);

        await using var context = Open(projectId);
        await EnsureCharacterExistsAsync(context, projectId, scene.ViewpointCharacterId, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSceneExistsAsync(context, projectId, scene.NextSceneId, cancellationToken)
            .ConfigureAwait(false);
        await EnsureChapterExistsAsync(context, projectId, scene.ChapterId, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSceneSequenceAvailableAsync(
                context,
                projectId,
                scene.ChapterId,
                scene.SequenceNumber,
                scene.Id,
                cancellationToken)
            .ConfigureAwait(false);

        var record = await context.Scenes
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == scene.Id,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Scene '{scene.Id}' was not found.");

        Apply(record, scene);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task DeleteSceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.Scenes
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == sceneId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Scene '{sceneId}' was not found.");

        var linked = await context.Scenes
            .Where(scene => scene.ProjectId == projectId && scene.NextSceneId == sceneId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var scene in linked)
        {
            scene.NextSceneId = null;
        }

        context.Scenes.Remove(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    private ProjectDbContext Open(Guid projectId)
    {
        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);
        return ProjectDbContextFactory.Create(databasePath);
    }

    private string RequireActiveRoot(Guid projectId)
    {
        var active = _projectService.ActiveProject
            ?? throw new InvalidOperationException("Open a project before using story data services.");
        if (active.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return active.RootPath;
    }

    private static void EnsureValid(StoryValidationResult result)
    {
        if (!result.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors));
        }
    }

    private static async Task EnsureBeatNumberAvailableAsync(
        ProjectDbContext context,
        Guid projectId,
        int number,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        var conflict = await context.Beats
            .AnyAsync(
                item => item.ProjectId == projectId
                    && item.Number == number
                    && (!excludingId.HasValue || item.Id != excludingId.Value),
                cancellationToken)
            .ConfigureAwait(false);
        if (conflict)
        {
            throw new InvalidOperationException($"Beat number {number} is already used in this project.");
        }
    }

    private static async Task EnsureSceneSequenceAvailableAsync(
        ProjectDbContext context,
        Guid projectId,
        Guid? chapterId,
        int sequenceNumber,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        var conflict = await context.Scenes
            .AnyAsync(
                item => item.ProjectId == projectId
                    && item.ChapterId == chapterId
                    && item.SequenceNumber == sequenceNumber
                    && (!excludingId.HasValue || item.Id != excludingId.Value),
                cancellationToken)
            .ConfigureAwait(false);
        if (conflict)
        {
            var scope = chapterId is null ? "unassigned scenes" : $"chapter '{chapterId}'";
            throw new InvalidOperationException($"Scene sequence {sequenceNumber} is already used in {scope}.");
        }
    }

    private static async Task EnsureCharacterExistsAsync(
        ProjectDbContext context,
        Guid projectId,
        Guid? characterId,
        CancellationToken cancellationToken)
    {
        if (!characterId.HasValue)
        {
            return;
        }

        var exists = await context.Characters
            .AnyAsync(
                item => item.ProjectId == projectId && item.Id == characterId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            throw new InvalidOperationException($"Viewpoint character '{characterId}' was not found.");
        }
    }

    private static async Task EnsureSceneExistsAsync(
        ProjectDbContext context,
        Guid projectId,
        Guid? sceneId,
        CancellationToken cancellationToken)
    {
        if (!sceneId.HasValue)
        {
            return;
        }

        var exists = await context.Scenes
            .AnyAsync(
                item => item.ProjectId == projectId && item.Id == sceneId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            throw new InvalidOperationException($"Next scene '{sceneId}' was not found.");
        }
    }

    private static async Task EnsureChapterExistsAsync(
        ProjectDbContext context,
        Guid projectId,
        Guid? chapterId,
        CancellationToken cancellationToken)
    {
        if (!chapterId.HasValue)
        {
            return;
        }

        var exists = await context.Chapters
            .AnyAsync(
                item => item.ProjectId == projectId && item.Id == chapterId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            throw new InvalidOperationException($"Chapter '{chapterId}' was not found.");
        }
    }

    private static Character WithProject(Character character, Guid projectId)
        => character.ProjectId == projectId ? character : CloneCharacter(character, character.Id, projectId);

    private static WorldEntry WithProject(WorldEntry entry, Guid projectId)
        => entry.ProjectId == projectId ? entry : CloneWorldEntry(entry, entry.Id, projectId);

    private static Beat WithProject(Beat beat, Guid projectId)
        => beat.ProjectId == projectId ? beat : CloneBeat(beat, beat.Id, projectId);

    private static Scene WithProject(Scene scene, Guid projectId)
        => scene.ProjectId == projectId ? scene : CloneScene(scene, scene.Id, projectId);

    private static Character CloneCharacter(Character source, Guid id, Guid? projectId = null)
        => new()
        {
            Id = id,
            ProjectId = projectId ?? source.ProjectId,
            Name = source.Name,
            Role = source.Role,
            Goal = source.Goal,
            Need = source.Need,
            Fear = source.Fear,
            Wound = source.Wound,
            FalseBelief = source.FalseBelief,
            Contradiction = source.Contradiction,
            Skills = source.Skills,
            Weaknesses = source.Weaknesses,
            Resources = source.Resources,
            RelationshipsNotes = source.RelationshipsNotes,
            StartingState = source.StartingState,
            EndingState = source.EndingState,
            BookArc = source.BookArc,
            SeriesArc = source.SeriesArc,
            IsViewpoint = source.IsViewpoint,
            SceneAppearancesNotes = source.SceneAppearancesNotes,
            LastEditedUtc = source.LastEditedUtc,
        };

    private static WorldEntry CloneWorldEntry(WorldEntry source, Guid id, Guid? projectId = null)
        => new()
        {
            Id = id,
            ProjectId = projectId ?? source.ProjectId,
            Name = source.Name,
            Category = source.Category,
            Depth = source.Depth,
            Notes = source.Notes,
            TravelDistance = source.TravelDistance,
            TravelTime = source.TravelTime,
            CanonicalFacts = source.CanonicalFacts,
            ConflictingEntries = source.ConflictingEntries,
            LastEditedUtc = source.LastEditedUtc,
        };

    private static Beat CloneBeat(Beat source, Guid id, Guid? projectId = null)
        => new()
        {
            Id = id,
            ProjectId = projectId ?? source.ProjectId,
            Number = source.Number,
            Name = source.Name,
            Summary = source.Summary,
            ChapterRef = source.ChapterRef,
            SceneRef = source.SceneRef,
            ViewpointCharacterId = source.ViewpointCharacterId,
            Event = source.Event,
            Cause = source.Cause,
            Consequence = source.Consequence,
            ArcFunction = source.ArcFunction,
            Theme = source.Theme,
            Escalation = source.Escalation,
            Status = source.Status,
            LastEditedUtc = source.LastEditedUtc,
        };

    private static Scene CloneScene(Scene source, Guid id, Guid? projectId = null)
        => new()
        {
            Id = id,
            ProjectId = projectId ?? source.ProjectId,
            ChapterId = source.ChapterId,
            SequenceNumber = source.SequenceNumber,
            Title = source.Title,
            ViewpointCharacterId = source.ViewpointCharacterId,
            Location = source.Location,
            Time = source.Time,
            Goal = source.Goal,
            Opposition = source.Opposition,
            Stakes = source.Stakes,
            MainEvent = source.MainEvent,
            Revelation = source.Revelation,
            EmotionalTurn = source.EmotionalTurn,
            Choice = source.Choice,
            Outcome = source.Outcome,
            Consequence = source.Consequence,
            SetupObligations = source.SetupObligations,
            PayoffObligations = source.PayoffObligations,
            NextSceneId = source.NextSceneId,
            Status = source.Status,
            WordCount = source.WordCount,
            LastEditedUtc = source.LastEditedUtc,
        };

    private static Character ToDomain(CharacterRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        Name = record.Name,
        Role = record.Role,
        Goal = record.Goal,
        Need = record.Need,
        Fear = record.Fear,
        Wound = record.Wound,
        FalseBelief = record.FalseBelief,
        Contradiction = record.Contradiction,
        Skills = record.Skills,
        Weaknesses = record.Weaknesses,
        Resources = record.Resources,
        RelationshipsNotes = record.RelationshipsNotes,
        StartingState = record.StartingState,
        EndingState = record.EndingState,
        BookArc = record.BookArc,
        SeriesArc = record.SeriesArc,
        IsViewpoint = record.IsViewpoint,
        SceneAppearancesNotes = record.SceneAppearancesNotes,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static WorldEntry ToDomain(WorldEntryRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        Name = record.Name,
        Category = record.Category,
        Depth = record.Depth,
        Notes = record.Notes,
        TravelDistance = record.TravelDistance,
        TravelTime = record.TravelTime,
        CanonicalFacts = record.CanonicalFacts,
        ConflictingEntries = record.ConflictingEntries,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static Beat ToDomain(BeatRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        Number = record.Number,
        Name = record.Name,
        Summary = record.Summary,
        ChapterRef = record.ChapterRef,
        SceneRef = record.SceneRef,
        ViewpointCharacterId = record.ViewpointCharacterId,
        Event = record.Event,
        Cause = record.Cause,
        Consequence = record.Consequence,
        ArcFunction = record.ArcFunction,
        Theme = record.Theme,
        Escalation = record.Escalation,
        Status = record.Status,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static Scene ToDomain(SceneRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        ChapterId = record.ChapterId,
        SequenceNumber = record.SequenceNumber,
        Title = record.Title,
        ViewpointCharacterId = record.ViewpointCharacterId,
        Location = record.Location,
        Time = record.Time,
        Goal = record.Goal,
        Opposition = record.Opposition,
        Stakes = record.Stakes,
        MainEvent = record.MainEvent,
        Revelation = record.Revelation,
        EmotionalTurn = record.EmotionalTurn,
        Choice = record.Choice,
        Outcome = record.Outcome,
        Consequence = record.Consequence,
        SetupObligations = record.SetupObligations,
        PayoffObligations = record.PayoffObligations,
        NextSceneId = record.NextSceneId,
        Status = record.Status,
        WordCount = record.WordCount,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static CharacterRecord ToRecord(Character character) => new()
    {
        Id = character.Id,
        ProjectId = character.ProjectId,
        Name = character.Name.Trim(),
        Role = character.Role,
        Goal = character.Goal,
        Need = character.Need,
        Fear = character.Fear,
        Wound = character.Wound,
        FalseBelief = character.FalseBelief,
        Contradiction = character.Contradiction,
        Skills = character.Skills,
        Weaknesses = character.Weaknesses,
        Resources = character.Resources,
        RelationshipsNotes = character.RelationshipsNotes,
        StartingState = character.StartingState,
        EndingState = character.EndingState,
        BookArc = character.BookArc,
        SeriesArc = character.SeriesArc,
        IsViewpoint = character.IsViewpoint,
        SceneAppearancesNotes = character.SceneAppearancesNotes,
        LastEditedUtc = character.LastEditedUtc,
    };

    private static WorldEntryRecord ToRecord(WorldEntry entry) => new()
    {
        Id = entry.Id,
        ProjectId = entry.ProjectId,
        Name = entry.Name.Trim(),
        Category = entry.Category,
        Depth = entry.Depth,
        Notes = entry.Notes,
        TravelDistance = entry.TravelDistance,
        TravelTime = entry.TravelTime,
        CanonicalFacts = entry.CanonicalFacts,
        ConflictingEntries = entry.ConflictingEntries,
        LastEditedUtc = entry.LastEditedUtc,
    };

    private static BeatRecord ToRecord(Beat beat) => new()
    {
        Id = beat.Id,
        ProjectId = beat.ProjectId,
        Number = beat.Number,
        Name = beat.Name.Trim(),
        Summary = beat.Summary,
        ChapterRef = beat.ChapterRef,
        SceneRef = beat.SceneRef,
        ViewpointCharacterId = beat.ViewpointCharacterId,
        Event = beat.Event,
        Cause = beat.Cause,
        Consequence = beat.Consequence,
        ArcFunction = beat.ArcFunction,
        Theme = beat.Theme,
        Escalation = beat.Escalation,
        Status = beat.Status,
        LastEditedUtc = beat.LastEditedUtc,
    };

    private static SceneRecord ToRecord(Scene scene) => new()
    {
        Id = scene.Id,
        ProjectId = scene.ProjectId,
        ChapterId = scene.ChapterId,
        SequenceNumber = scene.SequenceNumber,
        Title = scene.Title.Trim(),
        ViewpointCharacterId = scene.ViewpointCharacterId,
        Location = scene.Location,
        Time = scene.Time,
        Goal = scene.Goal,
        Opposition = scene.Opposition,
        Stakes = scene.Stakes,
        MainEvent = scene.MainEvent,
        Revelation = scene.Revelation,
        EmotionalTurn = scene.EmotionalTurn,
        Choice = scene.Choice,
        Outcome = scene.Outcome,
        Consequence = scene.Consequence,
        SetupObligations = scene.SetupObligations,
        PayoffObligations = scene.PayoffObligations,
        NextSceneId = scene.NextSceneId,
        Status = scene.Status,
        WordCount = scene.WordCount,
        LastEditedUtc = scene.LastEditedUtc,
    };

    private static void Apply(CharacterRecord record, Character character)
    {
        record.Name = character.Name.Trim();
        record.Role = character.Role;
        record.Goal = character.Goal;
        record.Need = character.Need;
        record.Fear = character.Fear;
        record.Wound = character.Wound;
        record.FalseBelief = character.FalseBelief;
        record.Contradiction = character.Contradiction;
        record.Skills = character.Skills;
        record.Weaknesses = character.Weaknesses;
        record.Resources = character.Resources;
        record.RelationshipsNotes = character.RelationshipsNotes;
        record.StartingState = character.StartingState;
        record.EndingState = character.EndingState;
        record.BookArc = character.BookArc;
        record.SeriesArc = character.SeriesArc;
        record.IsViewpoint = character.IsViewpoint;
        record.SceneAppearancesNotes = character.SceneAppearancesNotes;
    }

    private static void Apply(WorldEntryRecord record, WorldEntry entry)
    {
        record.Name = entry.Name.Trim();
        record.Category = entry.Category;
        record.Depth = entry.Depth;
        record.Notes = entry.Notes;
        record.TravelDistance = entry.TravelDistance;
        record.TravelTime = entry.TravelTime;
        record.CanonicalFacts = entry.CanonicalFacts;
        record.ConflictingEntries = entry.ConflictingEntries;
    }

    private static void Apply(BeatRecord record, Beat beat)
    {
        record.Number = beat.Number;
        record.Name = beat.Name.Trim();
        record.Summary = beat.Summary;
        record.ChapterRef = beat.ChapterRef;
        record.SceneRef = beat.SceneRef;
        record.ViewpointCharacterId = beat.ViewpointCharacterId;
        record.Event = beat.Event;
        record.Cause = beat.Cause;
        record.Consequence = beat.Consequence;
        record.ArcFunction = beat.ArcFunction;
        record.Theme = beat.Theme;
        record.Escalation = beat.Escalation;
        record.Status = beat.Status;
    }

    private static void Apply(SceneRecord record, Scene scene)
    {
        record.ChapterId = scene.ChapterId;
        record.SequenceNumber = scene.SequenceNumber;
        record.Title = scene.Title.Trim();
        record.ViewpointCharacterId = scene.ViewpointCharacterId;
        record.Location = scene.Location;
        record.Time = scene.Time;
        record.Goal = scene.Goal;
        record.Opposition = scene.Opposition;
        record.Stakes = scene.Stakes;
        record.MainEvent = scene.MainEvent;
        record.Revelation = scene.Revelation;
        record.EmotionalTurn = scene.EmotionalTurn;
        record.Choice = scene.Choice;
        record.Outcome = scene.Outcome;
        record.Consequence = scene.Consequence;
        record.SetupObligations = scene.SetupObligations;
        record.PayoffObligations = scene.PayoffObligations;
        record.NextSceneId = scene.NextSceneId;
        record.Status = scene.Status;
        record.WordCount = scene.WordCount;
    }
}
