using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Story;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class StoryDataViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IStoryDataService _storyData;

    public StoryDataViewModel(IProjectService projectService, IStoryDataService storyData)
    {
        _projectService = projectService;
        _storyData = storyData;
        _ = RefreshAsync();
    }

    public ObservableCollection<CharacterListItemViewModel> Characters { get; } = [];

    public ObservableCollection<WorldEntryListItemViewModel> WorldEntries { get; } = [];

    public ObservableCollection<BeatListItemViewModel> Beats { get; } = [];

    public ObservableCollection<SceneListItemViewModel> Scenes { get; } = [];

    public IReadOnlyList<WorldDepth> WorldDepthOptions { get; } = Enum.GetValues<WorldDepth>();

    public IReadOnlyList<BeatStatus> BeatStatusOptions { get; } = Enum.GetValues<BeatStatus>();

    public IReadOnlyList<SceneDraftStatus> SceneStatusOptions { get; } = Enum.GetValues<SceneDraftStatus>();

    [ObservableProperty]
    private bool _hasProject;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private CharacterListItemViewModel? _selectedCharacter;

    [ObservableProperty]
    private WorldEntryListItemViewModel? _selectedWorldEntry;

    [ObservableProperty]
    private BeatListItemViewModel? _selectedBeat;

    [ObservableProperty]
    private SceneListItemViewModel? _selectedScene;

    [ObservableProperty]
    private CharacterEditorViewModel? _characterEditor;

    [ObservableProperty]
    private WorldEntryEditorViewModel? _worldEditor;

    [ObservableProperty]
    private BeatEditorViewModel? _beatEditor;

    [ObservableProperty]
    private SceneEditorViewModel? _sceneEditor;

    partial void OnSelectedCharacterChanged(CharacterListItemViewModel? value)
        => CharacterEditor = value is null ? null : CharacterEditorViewModel.From(value.Source);

    partial void OnSelectedWorldEntryChanged(WorldEntryListItemViewModel? value)
        => WorldEditor = value is null ? null : WorldEntryEditorViewModel.From(value.Source);

    partial void OnSelectedBeatChanged(BeatListItemViewModel? value)
        => BeatEditor = value is null ? null : BeatEditorViewModel.From(value.Source);

    partial void OnSelectedSceneChanged(SceneListItemViewModel? value)
        => SceneEditor = value is null ? null : SceneEditorViewModel.From(value.Source);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var project = _projectService.ActiveProject;
        HasProject = project is not null;
        Characters.Clear();
        WorldEntries.Clear();
        Beats.Clear();
        Scenes.Clear();
        CharacterEditor = null;
        WorldEditor = null;
        BeatEditor = null;
        SceneEditor = null;

        if (project is null)
        {
            StatusMessage = "Open or create a project to edit story databases.";
            return;
        }

        try
        {
            foreach (var character in await _storyData.GetCharactersAsync(project.Id).ConfigureAwait(true))
            {
                Characters.Add(new CharacterListItemViewModel(character));
            }

            foreach (var entry in await _storyData.GetWorldEntriesAsync(project.Id).ConfigureAwait(true))
            {
                WorldEntries.Add(new WorldEntryListItemViewModel(entry));
            }

            foreach (var beat in await _storyData.GetBeatsAsync(project.Id).ConfigureAwait(true))
            {
                Beats.Add(new BeatListItemViewModel(beat));
            }

            foreach (var scene in await _storyData.GetScenesAsync(project.Id).ConfigureAwait(true))
            {
                Scenes.Add(new SceneListItemViewModel(scene));
            }

            SelectedCharacter = Characters.FirstOrDefault();
            SelectedWorldEntry = WorldEntries.FirstOrDefault();
            SelectedBeat = Beats.FirstOrDefault();
            SelectedScene = Scenes.FirstOrDefault();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddCharacterAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var created = await _storyData.CreateCharacterAsync(project.Id, new Character
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "New character",
            }).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedCharacter = Characters.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "Character created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveCharacterAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = CharacterEditor;
        if (project is null || editor is null)
        {
            return;
        }

        try
        {
            var updated = await _storyData.UpdateCharacterAsync(project.Id, editor.ToModel()).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedCharacter = Characters.FirstOrDefault(item => item.Id == updated.Id);
            StatusMessage = "Character saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteCharacterAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedCharacter;
        if (project is null || selected is null)
        {
            return;
        }

        try
        {
            await _storyData.DeleteCharacterAsync(project.Id, selected.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            StatusMessage = "Character deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddWorldEntryAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var created = await _storyData.CreateWorldEntryAsync(project.Id, new WorldEntry
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "New world entry",
                Category = "Location",
            }).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedWorldEntry = WorldEntries.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "World entry created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveWorldEntryAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = WorldEditor;
        if (project is null || editor is null)
        {
            return;
        }

        try
        {
            var updated = await _storyData.UpdateWorldEntryAsync(project.Id, editor.ToModel()).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedWorldEntry = WorldEntries.FirstOrDefault(item => item.Id == updated.Id);
            StatusMessage = "World entry saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteWorldEntryAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedWorldEntry;
        if (project is null || selected is null)
        {
            return;
        }

        try
        {
            await _storyData.DeleteWorldEntryAsync(project.Id, selected.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            StatusMessage = "World entry deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddBeatAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var nextNumber = Beats.Count == 0 ? 1 : Beats.Max(item => item.Number) + 1;
            var created = await _storyData.CreateBeatAsync(project.Id, new Beat
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Number = nextNumber,
                Name = $"Beat {nextNumber}",
            }).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedBeat = Beats.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "Beat created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveBeatAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = BeatEditor;
        if (project is null || editor is null)
        {
            return;
        }

        try
        {
            var updated = await _storyData.UpdateBeatAsync(project.Id, editor.ToModel()).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedBeat = Beats.FirstOrDefault(item => item.Id == updated.Id);
            StatusMessage = "Beat saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteBeatAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedBeat;
        if (project is null || selected is null)
        {
            return;
        }

        try
        {
            await _storyData.DeleteBeatAsync(project.Id, selected.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            StatusMessage = "Beat deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddSceneAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var nextSequence = Scenes.Count == 0 ? 1 : Scenes.Max(item => item.SequenceNumber) + 1;
            var created = await _storyData.CreateSceneAsync(project.Id, new Scene
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                SequenceNumber = nextSequence,
                Title = $"Scene {nextSequence}",
            }).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedScene = Scenes.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "Scene created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveSceneAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = SceneEditor;
        if (project is null || editor is null)
        {
            return;
        }

        try
        {
            var updated = await _storyData.UpdateSceneAsync(project.Id, editor.ToModel()).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedScene = Scenes.FirstOrDefault(item => item.Id == updated.Id);
            StatusMessage = "Scene saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteSceneAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedScene;
        if (project is null || selected is null)
        {
            return;
        }

        try
        {
            await _storyData.DeleteSceneAsync(project.Id, selected.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            StatusMessage = "Scene deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}

public sealed class CharacterListItemViewModel(Character source)
{
    public Character Source { get; } = source;

    public Guid Id => Source.Id;

    public string DisplayName => string.IsNullOrWhiteSpace(Source.Role)
        ? Source.Name
        : $"{Source.Name} ({Source.Role})";
}

public sealed class WorldEntryListItemViewModel(WorldEntry source)
{
    public WorldEntry Source { get; } = source;

    public Guid Id => Source.Id;

    public string DisplayName => string.IsNullOrWhiteSpace(Source.Category)
        ? Source.Name
        : $"{Source.Category}: {Source.Name}";
}

public sealed class BeatListItemViewModel(Beat source)
{
    public Beat Source { get; } = source;

    public Guid Id => Source.Id;

    public int Number => Source.Number;

    public string DisplayName => $"{Source.Number}. {Source.Name}";
}

public sealed class SceneListItemViewModel(Scene source)
{
    public Scene Source { get; } = source;

    public Guid Id => Source.Id;

    public int SequenceNumber => Source.SequenceNumber;

    public string DisplayName => $"{Source.SequenceNumber}. {Source.Title}";
}

public partial class CharacterEditorViewModel : ObservableObject
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _role = string.Empty;
    [ObservableProperty] private string _goal = string.Empty;
    [ObservableProperty] private string _need = string.Empty;
    [ObservableProperty] private string _fear = string.Empty;
    [ObservableProperty] private string _wound = string.Empty;
    [ObservableProperty] private string _falseBelief = string.Empty;
    [ObservableProperty] private string _contradiction = string.Empty;
    [ObservableProperty] private string _skills = string.Empty;
    [ObservableProperty] private string _weaknesses = string.Empty;
    [ObservableProperty] private string _resources = string.Empty;
    [ObservableProperty] private string _relationshipsNotes = string.Empty;
    [ObservableProperty] private string _startingState = string.Empty;
    [ObservableProperty] private string _endingState = string.Empty;
    [ObservableProperty] private string _bookArc = string.Empty;
    [ObservableProperty] private string _seriesArc = string.Empty;
    [ObservableProperty] private bool _isViewpoint;
    [ObservableProperty] private string _sceneAppearancesNotes = string.Empty;

    public static CharacterEditorViewModel From(Character character) => new()
    {
        Id = character.Id,
        ProjectId = character.ProjectId,
        Name = character.Name,
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
    };

    public Character ToModel() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        Name = Name,
        Role = Role,
        Goal = Goal,
        Need = Need,
        Fear = Fear,
        Wound = Wound,
        FalseBelief = FalseBelief,
        Contradiction = Contradiction,
        Skills = Skills,
        Weaknesses = Weaknesses,
        Resources = Resources,
        RelationshipsNotes = RelationshipsNotes,
        StartingState = StartingState,
        EndingState = EndingState,
        BookArc = BookArc,
        SeriesArc = SeriesArc,
        IsViewpoint = IsViewpoint,
        SceneAppearancesNotes = SceneAppearancesNotes,
    };
}

public partial class WorldEntryEditorViewModel : ObservableObject
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private WorldDepth _depth = WorldDepth.OnPage;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _travelDistance = string.Empty;
    [ObservableProperty] private string _travelTime = string.Empty;
    [ObservableProperty] private string _canonicalFacts = string.Empty;
    [ObservableProperty] private string _conflictingEntries = string.Empty;

    public static WorldEntryEditorViewModel From(WorldEntry entry) => new()
    {
        Id = entry.Id,
        ProjectId = entry.ProjectId,
        Name = entry.Name,
        Category = entry.Category,
        Depth = entry.Depth,
        Notes = entry.Notes,
        TravelDistance = entry.TravelDistance,
        TravelTime = entry.TravelTime,
        CanonicalFacts = entry.CanonicalFacts,
        ConflictingEntries = entry.ConflictingEntries,
    };

    public WorldEntry ToModel() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        Name = Name,
        Category = Category,
        Depth = Depth,
        Notes = Notes,
        TravelDistance = TravelDistance,
        TravelTime = TravelTime,
        CanonicalFacts = CanonicalFacts,
        ConflictingEntries = ConflictingEntries,
    };
}

public partial class BeatEditorViewModel : ObservableObject
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    [ObservableProperty] private int _number = 1;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _chapterRef = string.Empty;
    [ObservableProperty] private string _sceneRef = string.Empty;
    [ObservableProperty] private string _viewpointCharacterIdText = string.Empty;
    [ObservableProperty] private string _event = string.Empty;
    [ObservableProperty] private string _cause = string.Empty;
    [ObservableProperty] private string _consequence = string.Empty;
    [ObservableProperty] private string _arcFunction = string.Empty;
    [ObservableProperty] private string _theme = string.Empty;
    [ObservableProperty] private string _escalation = string.Empty;
    [ObservableProperty] private BeatStatus _status = BeatStatus.Planned;

    public static BeatEditorViewModel From(Beat beat) => new()
    {
        Id = beat.Id,
        ProjectId = beat.ProjectId,
        Number = beat.Number,
        Name = beat.Name,
        Summary = beat.Summary,
        ChapterRef = beat.ChapterRef,
        SceneRef = beat.SceneRef,
        ViewpointCharacterIdText = beat.ViewpointCharacterId?.ToString() ?? string.Empty,
        Event = beat.Event,
        Cause = beat.Cause,
        Consequence = beat.Consequence,
        ArcFunction = beat.ArcFunction,
        Theme = beat.Theme,
        Escalation = beat.Escalation,
        Status = beat.Status,
    };

    public Beat ToModel() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        Number = Number,
        Name = Name,
        Summary = Summary,
        ChapterRef = ChapterRef,
        SceneRef = SceneRef,
        ViewpointCharacterId = ParseOptionalGuid(ViewpointCharacterIdText),
        Event = Event,
        Cause = Cause,
        Consequence = Consequence,
        ArcFunction = ArcFunction,
        Theme = Theme,
        Escalation = Escalation,
        Status = Status,
    };

    private static Guid? ParseOptionalGuid(string? text)
        => Guid.TryParse(text, out var id) ? id : null;
}

public partial class SceneEditorViewModel : ObservableObject
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    [ObservableProperty] private string _chapterIdText = string.Empty;
    [ObservableProperty] private int _sequenceNumber = 1;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _viewpointCharacterIdText = string.Empty;
    [ObservableProperty] private string _location = string.Empty;
    [ObservableProperty] private string _time = string.Empty;
    [ObservableProperty] private string _goal = string.Empty;
    [ObservableProperty] private string _opposition = string.Empty;
    [ObservableProperty] private string _stakes = string.Empty;
    [ObservableProperty] private string _mainEvent = string.Empty;
    [ObservableProperty] private string _revelation = string.Empty;
    [ObservableProperty] private string _emotionalTurn = string.Empty;
    [ObservableProperty] private string _choice = string.Empty;
    [ObservableProperty] private string _outcome = string.Empty;
    [ObservableProperty] private string _consequence = string.Empty;
    [ObservableProperty] private string _setupObligations = string.Empty;
    [ObservableProperty] private string _payoffObligations = string.Empty;
    [ObservableProperty] private string _nextSceneIdText = string.Empty;
    [ObservableProperty] private SceneDraftStatus _status = SceneDraftStatus.Outlined;
    [ObservableProperty] private int _wordCount;

    public static SceneEditorViewModel From(Scene scene) => new()
    {
        Id = scene.Id,
        ProjectId = scene.ProjectId,
        ChapterIdText = scene.ChapterId?.ToString() ?? string.Empty,
        SequenceNumber = scene.SequenceNumber,
        Title = scene.Title,
        ViewpointCharacterIdText = scene.ViewpointCharacterId?.ToString() ?? string.Empty,
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
        NextSceneIdText = scene.NextSceneId?.ToString() ?? string.Empty,
        Status = scene.Status,
        WordCount = scene.WordCount,
    };

    public Scene ToModel() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        ChapterId = ParseOptionalGuid(ChapterIdText),
        SequenceNumber = SequenceNumber,
        Title = Title,
        ViewpointCharacterId = ParseOptionalGuid(ViewpointCharacterIdText),
        Location = Location,
        Time = Time,
        Goal = Goal,
        Opposition = Opposition,
        Stakes = Stakes,
        MainEvent = MainEvent,
        Revelation = Revelation,
        EmotionalTurn = EmotionalTurn,
        Choice = Choice,
        Outcome = Outcome,
        Consequence = Consequence,
        SetupObligations = SetupObligations,
        PayoffObligations = PayoffObligations,
        NextSceneId = ParseOptionalGuid(NextSceneIdText),
        Status = Status,
        WordCount = WordCount,
    };

    private static Guid? ParseOptionalGuid(string? text)
        => Guid.TryParse(text, out var id) ? id : null;
}
