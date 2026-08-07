using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Story;

public sealed class StoryDataServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IStoryDataService _story;

    public StoryDataServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-story-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _story = _provider.GetRequiredService<IStoryDataService>();
    }

    public void Dispose()
    {
        _projects.CloseAsync().GetAwaiter().GetResult();
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task Character_Crud_PersistsExpandedFields()
    {
        var project = await CreateAsync("Chars");
        var created = await _story.CreateCharacterAsync(project.Id, new Character
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Kael",
            Role = "Protagonist",
            Goal = "Restore the oath",
            Need = "Belonging",
            Fear = "Becoming a tyrant",
            Wound = "Exile",
            FalseBelief = "Power protects",
            Contradiction = "Craves peace but trains for war",
            Skills = "Swordcraft",
            Weaknesses = "Pride",
            Resources = "Hidden allies",
            RelationshipsNotes = "Rival to Mira",
            StartingState = "Bitter exile",
            EndingState = "Reluctant steward",
            BookArc = "From vengeance to duty",
            SeriesArc = "Founder of the new order",
            IsViewpoint = true,
            SceneAppearancesNotes = "Opens Act I",
        });

        var loaded = await _story.GetCharacterAsync(project.Id, created.Id);
        Assert.Equal("Kael", loaded.Name);
        Assert.Equal("Belonging", loaded.Need);
        Assert.True(loaded.IsViewpoint);

        loaded.Fear = "Failing the city";
        var updated = await _story.UpdateCharacterAsync(project.Id, loaded);
        Assert.Equal("Failing the city", updated.Fear);

        await _story.DeleteCharacterAsync(project.Id, created.Id);
        Assert.Empty(await _story.GetCharactersAsync(project.Id));
    }

    [Fact]
    public async Task WorldEntry_Crud_PersistsTravelAndFacts()
    {
        var project = await CreateAsync("World");
        var created = await _story.CreateWorldEntryAsync(project.Id, new WorldEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Ash Harbor",
            Category = "Location",
            Depth = WorldDepth.OnPage,
            TravelDistance = "40 leagues",
            TravelTime = "3 days",
            CanonicalFacts = "Free port under merchant council",
            ConflictingEntries = "Empire claims sovereignty",
        });

        var loaded = await _story.GetWorldEntryAsync(project.Id, created.Id);
        Assert.Equal(WorldDepth.OnPage, loaded.Depth);
        Assert.Equal("3 days", loaded.TravelTime);

        loaded.Depth = WorldDepth.FutureExpansion;
        await _story.UpdateWorldEntryAsync(project.Id, loaded);
        Assert.Equal(WorldDepth.FutureExpansion, (await _story.GetWorldEntryAsync(project.Id, created.Id)).Depth);

        await _story.DeleteWorldEntryAsync(project.Id, created.Id);
        Assert.Empty(await _story.GetWorldEntriesAsync(project.Id));
    }

    [Fact]
    public async Task Beat_Crud_EnforcesUniqueNumberAndViewpoint()
    {
        var project = await CreateAsync("Beats");
        var character = await _story.CreateCharacterAsync(project.Id, new Character
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Mira",
            IsViewpoint = true,
        });

        var beat = await _story.CreateBeatAsync(project.Id, new Beat
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Number = 1,
            Name = "Opening Image",
            Cause = "Empire falls",
            Consequence = "Refugees arrive",
            Escalation = "Low",
            ViewpointCharacterId = character.Id,
            Theme = "Belonging",
        });

        Assert.Equal(character.Id, beat.ViewpointCharacterId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _story.CreateBeatAsync(project.Id, new Beat
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Number = 1,
                Name = "Duplicate",
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _story.DeleteCharacterAsync(project.Id, character.Id));

        await _story.DeleteBeatAsync(project.Id, beat.Id);
        await _story.DeleteCharacterAsync(project.Id, character.Id);
    }

    [Fact]
    public async Task Scene_LinksNextScene_AndViewpointCharacter()
    {
        var project = await CreateAsync("Scenes");
        var character = await _story.CreateCharacterAsync(project.Id, new Character
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Kael",
        });

        var first = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 1,
            Title = "Harbor Arrival",
            ViewpointCharacterId = character.Id,
            Location = "Ash Harbor",
            Time = "Dawn",
            Goal = "Find passage",
            Opposition = "Dock inspectors",
            Stakes = "Arrest",
            MainEvent = "Bribe fails",
            Revelation = "Warrant already posted",
            EmotionalTurn = "Hope to dread",
            Choice = "Flee inland",
            Outcome = "Escapes",
            Consequence = "Leaves ally behind",
            SetupObligations = "Introduce warrant",
            PayoffObligations = "Warrant returns in Act II",
            WordCount = 1200,
        });

        var second = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 2,
            Title = "Road Ambush",
        });

        first.NextSceneId = second.Id;
        first.Status = SceneDraftStatus.Drafting;
        var updated = await _story.UpdateSceneAsync(project.Id, first);

        Assert.Equal(second.Id, updated.NextSceneId);
        Assert.Equal(character.Id, updated.ViewpointCharacterId);
        Assert.Equal("Ash Harbor", updated.Location);

        await _story.DeleteSceneAsync(project.Id, second.Id);
        var remaining = await _story.GetSceneAsync(project.Id, first.Id);
        Assert.Null(remaining.NextSceneId);
    }

    [Fact]
    public async Task CreateRejectsInvalidCharacter()
    {
        var project = await CreateAsync("Invalid");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _story.CreateCharacterAsync(project.Id, new Character
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "   ",
            }));
    }

    [Fact]
    public async Task NewProject_UsesSchemaVersion4()
    {
        var project = await CreateAsync("Schema");
        var validation = await _projects.ValidateAsync(project.RootPath);
        Assert.True(validation.IsValid);
        Assert.Equal(ProjectSchema.CurrentVersion, validation.SchemaVersion);
        Assert.Equal(5, validation.SchemaVersion);
    }

    private async Task<Project> CreateAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);
        return await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = title,
            Author = "Tester",
            Genre = "Fantasy",
            NorthStar = "Finish",
        });
    }

    private static string FindPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }
}
