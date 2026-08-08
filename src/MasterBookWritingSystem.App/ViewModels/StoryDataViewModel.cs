using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Story;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class StoryDataViewModel : ObservableObject
{
    public const int CharactersTabIndex = 0;
    public const int WorldTabIndex = 1;
    public const int BeatsTabIndex = 2;
    public const int ScenesTabIndex = 3;

    private readonly IProjectService _projectService;
    private readonly IStoryDataService _storyData;
    private readonly IChapterService _chapters;
    private readonly IStoryChangeNotifier _changes;
    private readonly List<Scene> _allScenes = [];
    private readonly Dictionary<Guid, Chapter> _chaptersById = [];
    private readonly Dictionary<Guid, Character> _charactersById = [];
    private bool _suppressSceneFilterReload;
    private bool _loading;

    public StoryDataViewModel(
        IProjectService projectService,
        IStoryDataService storyData,
        IChapterService chapters,
        IStoryChangeNotifier changes)
    {
        _projectService = projectService;
        _storyData = storyData;
        _chapters = chapters;
        _changes = changes;
        _changes.Changed += OnStoryChanged;
        _ = RefreshAsync();
    }

    public void Detach() => _changes.Changed -= OnStoryChanged;

    public ObservableCollection<CharacterListItemViewModel> Characters { get; } = [];

    public ObservableCollection<WorldEntryListItemViewModel> WorldEntries { get; } = [];

    public ObservableCollection<BeatListItemViewModel> Beats { get; } = [];

    public ObservableCollection<SceneListItemViewModel> Scenes { get; } = [];

    public ObservableCollection<ChapterOptionViewModel> ChapterOptions { get; } = [];

    public ObservableCollection<ChapterFilterOptionViewModel> SceneChapterFilters { get; } = [];

    public ObservableCollection<CharacterOptionViewModel> ViewpointCharacterOptions { get; } = [];

    public IReadOnlyList<WorldDepth> WorldDepthOptions { get; } = Enum.GetValues<WorldDepth>();

    public IReadOnlyList<BeatStatus> BeatStatusOptions { get; } = Enum.GetValues<BeatStatus>();

    public IReadOnlyList<SceneDraftStatus> SceneStatusOptions { get; } = Enum.GetValues<SceneDraftStatus>();

    [ObservableProperty]
    private bool _hasProject;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private ChapterFilterOptionViewModel? _selectedSceneChapterFilter;

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
        => SceneEditor = value is null
            ? null
            : SceneEditorViewModel.From(value.Source, ChapterOptions, ViewpointCharacterOptions);

    partial void OnSelectedSceneChapterFilterChanged(ChapterFilterOptionViewModel? value)
    {
        if (_suppressSceneFilterReload)
        {
            return;
        }

        RebuildSceneList(preserveSelectedId: SelectedScene?.Id);
    }

    public async Task FocusSceneAsync(Guid sceneId)
    {
        await RefreshAsync().ConfigureAwait(true);
        SelectedTabIndex = ScenesTabIndex;
        _suppressSceneFilterReload = true;
        SelectedSceneChapterFilter = SceneChapterFilters.FirstOrDefault(item => item.IsAllChapters);
        _suppressSceneFilterReload = false;
        RebuildSceneList(preserveSelectedId: sceneId);
        SelectedScene = Scenes.FirstOrDefault(item => item.Id == sceneId);
        StatusMessage = SelectedScene is null
            ? "Requested scene was not found."
            : $"Opened scene '{SelectedScene.Source.Title}'.";
    }

    public async Task FocusCharacterAsync(Guid characterId)
    {
        await RefreshAsync().ConfigureAwait(true);
        SelectedTabIndex = CharactersTabIndex;
        SelectedCharacter = Characters.FirstOrDefault(item => item.Id == characterId);
        StatusMessage = SelectedCharacter is null
            ? "Requested character was not found."
            : $"Opened character '{SelectedCharacter.Source.Name}'.";
    }

    public async Task FocusWorldEntryAsync(Guid worldEntryId)
    {
        await RefreshAsync().ConfigureAwait(true);
        SelectedTabIndex = WorldTabIndex;
        SelectedWorldEntry = WorldEntries.FirstOrDefault(item => item.Id == worldEntryId);
        StatusMessage = SelectedWorldEntry is null
            ? "Requested world entry was not found."
            : $"Opened world entry '{SelectedWorldEntry.Source.Name}'.";
    }

    public async Task FocusBeatAsync(Guid beatId)
    {
        await RefreshAsync().ConfigureAwait(true);
        SelectedTabIndex = BeatsTabIndex;
        SelectedBeat = Beats.FirstOrDefault(item => item.Id == beatId);
        StatusMessage = SelectedBeat is null
            ? "Requested beat was not found."
            : $"Opened beat '{SelectedBeat.Source.Name}'.";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var project = _projectService.ActiveProject;
        HasProject = project is not null;
        _loading = true;
        Characters.Clear();
        WorldEntries.Clear();
        Beats.Clear();
        Scenes.Clear();
        ChapterOptions.Clear();
        SceneChapterFilters.Clear();
        ViewpointCharacterOptions.Clear();
        _allScenes.Clear();
        _chaptersById.Clear();
        _charactersById.Clear();
        CharacterEditor = null;
        WorldEditor = null;
        BeatEditor = null;
        SceneEditor = null;

        if (project is null)
        {
            StatusMessage = "Open or create a project to edit story databases.";
            _loading = false;
            return;
        }

        try
        {
            foreach (var character in await _storyData.GetCharactersAsync(project.Id).ConfigureAwait(true))
            {
                Characters.Add(new CharacterListItemViewModel(character));
                _charactersById[character.Id] = character;
            }

            foreach (var entry in await _storyData.GetWorldEntriesAsync(project.Id).ConfigureAwait(true))
            {
                WorldEntries.Add(new WorldEntryListItemViewModel(entry));
            }

            foreach (var beat in await _storyData.GetBeatsAsync(project.Id).ConfigureAwait(true))
            {
                Beats.Add(new BeatListItemViewModel(beat));
            }

            RebuildChapterLookups(await _chapters.GetAllAsync(project.Id).ConfigureAwait(true));
            _allScenes.AddRange(await _storyData.GetScenesAsync(project.Id).ConfigureAwait(true));

            SelectedCharacter = Characters.FirstOrDefault();
            SelectedWorldEntry = WorldEntries.FirstOrDefault();
            SelectedBeat = Beats.FirstOrDefault();

            _suppressSceneFilterReload = true;
            SelectedSceneChapterFilter = SceneChapterFilters.FirstOrDefault(item => item.IsAllChapters);
            _suppressSceneFilterReload = false;
            RebuildSceneList(preserveSelectedId: null);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnStoryChanged(object? sender, StoryChangeEventArgs e)
    {
        if (_loading || _projectService.ActiveProject?.Id != e.ProjectId)
        {
            return;
        }

        if (e.Kind is not (StoryChangeKind.SceneUpserted
            or StoryChangeKind.SceneDeleted
            or StoryChangeKind.CharacterChanged
            or StoryChangeKind.WorldEntryChanged
            or StoryChangeKind.BeatChanged
            or StoryChangeKind.HierarchyChanged))
        {
            return;
        }

        void Apply() => _ = SoftReloadFromCanonicalAsync(e.Kind);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            _ = dispatcher.InvokeAsync(Apply);
        }
    }

    private async Task SoftReloadFromCanonicalAsync(StoryChangeKind kind)
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        var preserveSceneId = SelectedScene?.Id;
        var preserveCharacterId = SelectedCharacter?.Id;
        var preserveWorldId = SelectedWorldEntry?.Id;
        var preserveBeatId = SelectedBeat?.Id;
        var previousFilterChapterId = SelectedSceneChapterFilter?.ChapterId;
        var previousFilterUnassigned = SelectedSceneChapterFilter?.IsUnassignedOnly == true;
        var previousFilterAll = SelectedSceneChapterFilter?.IsAllChapters != false;
        var tab = SelectedTabIndex;

        try
        {
            _loading = true;
            if (kind is StoryChangeKind.CharacterChanged
                or StoryChangeKind.SceneUpserted
                or StoryChangeKind.SceneDeleted
                or StoryChangeKind.HierarchyChanged)
            {
                Characters.Clear();
                _charactersById.Clear();
                foreach (var character in await _storyData.GetCharactersAsync(project.Id).ConfigureAwait(true))
                {
                    Characters.Add(new CharacterListItemViewModel(character));
                    _charactersById[character.Id] = character;
                }

                SelectedCharacter = Characters.FirstOrDefault(item => item.Id == preserveCharacterId)
                    ?? Characters.FirstOrDefault();
            }

            if (kind is StoryChangeKind.WorldEntryChanged)
            {
                WorldEntries.Clear();
                foreach (var entry in await _storyData.GetWorldEntriesAsync(project.Id).ConfigureAwait(true))
                {
                    WorldEntries.Add(new WorldEntryListItemViewModel(entry));
                }

                SelectedWorldEntry = WorldEntries.FirstOrDefault(item => item.Id == preserveWorldId)
                    ?? WorldEntries.FirstOrDefault();
            }

            if (kind is StoryChangeKind.BeatChanged)
            {
                Beats.Clear();
                foreach (var beat in await _storyData.GetBeatsAsync(project.Id).ConfigureAwait(true))
                {
                    Beats.Add(new BeatListItemViewModel(beat));
                }

                SelectedBeat = Beats.FirstOrDefault(item => item.Id == preserveBeatId)
                    ?? Beats.FirstOrDefault();
            }

            if (kind is StoryChangeKind.SceneUpserted
                or StoryChangeKind.SceneDeleted
                or StoryChangeKind.CharacterChanged
                or StoryChangeKind.HierarchyChanged)
            {
                RebuildChapterLookups(await _chapters.GetAllAsync(project.Id).ConfigureAwait(true));
                _allScenes.Clear();
                _allScenes.AddRange(await _storyData.GetScenesAsync(project.Id).ConfigureAwait(true));
                _suppressSceneFilterReload = true;
                SelectedSceneChapterFilter = previousFilterUnassigned
                    ? SceneChapterFilters.FirstOrDefault(item => item.IsUnassignedOnly)
                    : previousFilterAll
                        ? SceneChapterFilters.FirstOrDefault(item => item.IsAllChapters)
                        : SceneChapterFilters.FirstOrDefault(item => item.ChapterId == previousFilterChapterId)
                          ?? SceneChapterFilters.FirstOrDefault(item => item.IsAllChapters);
                _suppressSceneFilterReload = false;
                RebuildSceneList(preserveSelectedId: preserveSceneId);
            }

            SelectedTabIndex = tab;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private void RebuildChapterLookups(IReadOnlyList<Chapter> chapters)
    {
        ChapterOptions.Clear();
        SceneChapterFilters.Clear();
        ViewpointCharacterOptions.Clear();
        _chaptersById.Clear();

        ChapterOptions.Add(ChapterOptionViewModel.Unassigned());
        SceneChapterFilters.Add(ChapterFilterOptionViewModel.All());
        SceneChapterFilters.Add(ChapterFilterOptionViewModel.Unassigned());
        ViewpointCharacterOptions.Add(CharacterOptionViewModel.Unassigned());

        foreach (var chapter in chapters.OrderBy(item => item.SequenceNumber))
        {
            _chaptersById[chapter.Id] = chapter;
            var option = ChapterOptionViewModel.From(chapter);
            ChapterOptions.Add(option);
            SceneChapterFilters.Add(ChapterFilterOptionViewModel.From(chapter));
        }

        foreach (var character in Characters)
        {
            ViewpointCharacterOptions.Add(CharacterOptionViewModel.From(character.Source));
        }
    }

    private void RebuildSceneList(Guid? preserveSelectedId)
    {
        var filter = SelectedSceneChapterFilter;
        var filtered = SceneChapterOrdering.FilterByChapter(
            _allScenes,
            filter?.ChapterId,
            unassignedOnly: filter?.IsUnassignedOnly == true);
        var sequences = _chaptersById.ToDictionary(pair => pair.Key, pair => pair.Value.SequenceNumber);
        var ordered = SceneChapterOrdering.OrderByChapterThenSequence(filtered, sequences);

        Scenes.Clear();
        foreach (var scene in ordered)
        {
            var chapterLabel = scene.ChapterId is { } chapterId && _chaptersById.TryGetValue(chapterId, out var chapter)
                ? SceneChapterOrdering.FormatChapterLabel(chapter.SequenceNumber, chapter.Title)
                : SceneChapterOrdering.UnassignedLabel;
            var pov = scene.ViewpointCharacterId is { } characterId
                && _charactersById.TryGetValue(characterId, out var character)
                ? character.Name
                : string.Empty;
            Scenes.Add(new SceneListItemViewModel(scene, chapterLabel, pov));
        }

        SelectedScene = preserveSelectedId is { } id
            ? Scenes.FirstOrDefault(item => item.Id == id) ?? Scenes.FirstOrDefault()
            : Scenes.FirstOrDefault();
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
            var nextSequence = _allScenes.Count == 0 ? 1 : _allScenes.Max(item => item.SequenceNumber) + 1;
            Guid? preferredChapterId = SelectedSceneChapterFilter is { IsAllChapters: false, IsUnassignedOnly: false } filter
                ? filter.ChapterId
                : null;
            var created = await _storyData.CreateSceneAsync(project.Id, new Scene
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                SequenceNumber = nextSequence,
                Title = $"Scene {nextSequence}",
                ChapterId = preferredChapterId,
            }).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedTabIndex = ScenesTabIndex;
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

public sealed class SceneListItemViewModel(Scene source, string chapterDisplayName, string viewpointDisplayName)
{
    public Scene Source { get; } = source;

    public Guid Id => Source.Id;

    public int SequenceNumber => Source.SequenceNumber;

    public string ChapterDisplayName { get; } = chapterDisplayName;

    public string ViewpointDisplayName { get; } = viewpointDisplayName;

    public string Title => Source.Title;

    public string Location => Source.Location;

    public SceneDraftStatus Status => Source.Status;

    public string DisplayName => $"{Source.SequenceNumber}. {Source.Title} — {ChapterDisplayName}";
}

public sealed class ChapterOptionViewModel
{
    private ChapterOptionViewModel(Guid? chapterId, string displayName, int sequenceNumber)
    {
        ChapterId = chapterId;
        DisplayName = displayName;
        SequenceNumber = sequenceNumber;
    }

    public Guid? ChapterId { get; }

    public string DisplayName { get; }

    public int SequenceNumber { get; }

    public static ChapterOptionViewModel Unassigned()
        => new(null, SceneChapterOrdering.UnassignedLabel, int.MaxValue);

    public static ChapterOptionViewModel From(Chapter chapter)
        => new(chapter.Id, SceneChapterOrdering.FormatChapterLabel(chapter.SequenceNumber, chapter.Title), chapter.SequenceNumber);
}

public sealed class ChapterFilterOptionViewModel
{
    private ChapterFilterOptionViewModel(Guid? chapterId, string displayName, bool isAllChapters, bool isUnassignedOnly)
    {
        ChapterId = chapterId;
        DisplayName = displayName;
        IsAllChapters = isAllChapters;
        IsUnassignedOnly = isUnassignedOnly;
    }

    public Guid? ChapterId { get; }

    public string DisplayName { get; }

    public bool IsAllChapters { get; }

    public bool IsUnassignedOnly { get; }

    public static ChapterFilterOptionViewModel All()
        => new(null, "All Chapters", isAllChapters: true, isUnassignedOnly: false);

    public static ChapterFilterOptionViewModel Unassigned()
        => new(null, SceneChapterOrdering.UnassignedLabel, isAllChapters: false, isUnassignedOnly: true);

    public static ChapterFilterOptionViewModel From(Chapter chapter)
        => new(
            chapter.Id,
            SceneChapterOrdering.FormatChapterLabel(chapter.SequenceNumber, chapter.Title),
            isAllChapters: false,
            isUnassignedOnly: false);
}

public sealed class CharacterOptionViewModel
{
    private CharacterOptionViewModel(Guid? characterId, string displayName)
    {
        CharacterId = characterId;
        DisplayName = displayName;
    }

    public Guid? CharacterId { get; }

    public string DisplayName { get; }

    public static CharacterOptionViewModel Unassigned()
        => new(null, "(None)");

    public static CharacterOptionViewModel All(string label = "All viewpoints")
        => new(null, label);

    public static CharacterOptionViewModel From(Character character)
        => new(character.Id, character.Name);
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

    public ObservableCollection<ChapterOptionViewModel> ChapterOptions { get; private set; } = [];

    public ObservableCollection<CharacterOptionViewModel> ViewpointCharacterOptions { get; private set; } = [];

    [ObservableProperty] private ChapterOptionViewModel? _selectedChapterOption;
    [ObservableProperty] private int _sequenceNumber = 1;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private CharacterOptionViewModel? _selectedViewpointCharacter;
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

    public static SceneEditorViewModel From(
        Scene scene,
        IEnumerable<ChapterOptionViewModel> chapterOptions,
        IEnumerable<CharacterOptionViewModel> viewpointOptions)
    {
        var chapters = new ObservableCollection<ChapterOptionViewModel>(chapterOptions);
        var viewpoints = new ObservableCollection<CharacterOptionViewModel>(viewpointOptions);
        return new SceneEditorViewModel
        {
            Id = scene.Id,
            ProjectId = scene.ProjectId,
            ChapterOptions = chapters,
            ViewpointCharacterOptions = viewpoints,
            SelectedChapterOption = chapters.FirstOrDefault(item => item.ChapterId == scene.ChapterId)
                ?? chapters.FirstOrDefault(item => item.ChapterId is null),
            SequenceNumber = scene.SequenceNumber,
            Title = scene.Title,
            SelectedViewpointCharacter = viewpoints.FirstOrDefault(item => item.CharacterId == scene.ViewpointCharacterId)
                ?? viewpoints.FirstOrDefault(item => item.CharacterId is null),
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
    }

    public Scene ToModel() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        ChapterId = SelectedChapterOption?.ChapterId,
        SequenceNumber = SequenceNumber,
        Title = Title,
        ViewpointCharacterId = SelectedViewpointCharacter?.CharacterId,
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
