using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Navigation;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Hierarchy;
using MasterBookWritingSystem.Core.Story;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class SceneCorkboardViewModel : ObservableObject
{
    private readonly IProjectService _projects;
    private readonly IStoryDataService _story;
    private readonly IManuscriptHierarchyService _hierarchy;
    private readonly IChapterService _chapters;
    private readonly INavigationService _navigation;
    private readonly IProjectDialogService _dialogs;
    private readonly IStoryChangeNotifier _changes;
    private readonly List<Scene> _allScenes = [];
    private readonly Dictionary<Guid, Chapter> _chaptersById = [];
    private readonly Dictionary<Guid, Character> _charactersById = [];
    private readonly Dictionary<Guid, Guid?> _chapterPartById = [];
    private readonly Dictionary<Guid, Guid> _partBookById = [];
    private readonly Dictionary<Guid, string> _bookTitles = [];
    private readonly Dictionary<Guid, string> _partTitles = [];
    private bool _suppressFilterReload;
    private bool _loading;

    public SceneCorkboardViewModel(
        IProjectService projects,
        IStoryDataService story,
        IManuscriptHierarchyService hierarchy,
        IChapterService chapters,
        INavigationService navigation,
        IProjectDialogService dialogs,
        IStoryChangeNotifier changes)
    {
        _projects = projects;
        _story = story;
        _hierarchy = hierarchy;
        _chapters = chapters;
        _navigation = navigation;
        _dialogs = dialogs;
        _changes = changes;
        _changes.Changed += OnStoryChanged;
        StatusFilterOptions =
        [
            new StatusFilterOption(null, "All statuses"),
            .. Enum.GetValues<SceneDraftStatus>().Select(status => new StatusFilterOption(status, status.ToString())),
        ];
        SelectedStatusFilter = StatusFilterOptions[0];
    }

    public ObservableCollection<SceneCardViewModel> Cards { get; } = [];

    public ObservableCollection<BookFilterOption> BookFilters { get; } = [];

    public ObservableCollection<PartFilterOption> PartFilters { get; } = [];

    public ObservableCollection<ChapterFilterOptionViewModel> ChapterFilters { get; } = [];

    public ObservableCollection<CharacterOptionViewModel> ViewpointFilters { get; } = [];

    public IReadOnlyList<StatusFilterOption> StatusFilterOptions { get; }

    [ObservableProperty]
    private BookFilterOption? _selectedBookFilter;

    [ObservableProperty]
    private PartFilterOption? _selectedPartFilter;

    [ObservableProperty]
    private ChapterFilterOptionViewModel? _selectedChapterFilter;

    [ObservableProperty]
    private CharacterOptionViewModel? _selectedViewpointFilter;

    [ObservableProperty]
    private StatusFilterOption? _selectedStatusFilter;

    [ObservableProperty]
    private SceneCardViewModel? _selectedCard;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _locationLimitationNote =
        "Scene.Location is free text (not a World Entry link). Matching world names are suggestions only.";

    [ObservableProperty]
    private bool _canMoveSelectedUp;

    [ObservableProperty]
    private bool _canMoveSelectedDown;

    public void Detach() => _changes.Changed -= OnStoryChanged;

    partial void OnSelectedBookFilterChanged(BookFilterOption? value)
    {
        if (_suppressFilterReload)
        {
            return;
        }

        RebuildPartFilters();
        ApplyFiltersPreservingSelection();
    }

    partial void OnSelectedPartFilterChanged(PartFilterOption? value)
    {
        if (_suppressFilterReload)
        {
            return;
        }

        RebuildChapterFilters();
        ApplyFiltersPreservingSelection();
    }

    partial void OnSelectedChapterFilterChanged(ChapterFilterOptionViewModel? value)
    {
        if (!_suppressFilterReload)
        {
            ApplyFiltersPreservingSelection();
        }
    }

    partial void OnSelectedViewpointFilterChanged(CharacterOptionViewModel? value)
    {
        if (!_suppressFilterReload)
        {
            ApplyFiltersPreservingSelection();
        }
    }

    partial void OnSelectedStatusFilterChanged(StatusFilterOption? value)
    {
        if (!_suppressFilterReload)
        {
            ApplyFiltersPreservingSelection();
        }
    }

    partial void OnSelectedCardChanged(SceneCardViewModel? value) => UpdateMoveStates();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            Cards.Clear();
            StatusMessage = "Open a project to use the corkboard.";
            return;
        }

        _loading = true;
        try
        {
            var selectedId = SelectedCard?.Id;
            var tree = await _hierarchy.GetHierarchyAsync(project.Id, cancellationToken).ConfigureAwait(true);
            var chapters = await _chapters.GetAllAsync(project.Id, cancellationToken).ConfigureAwait(true);
            var characters = await _story.GetCharactersAsync(project.Id, cancellationToken).ConfigureAwait(true);
            var scenes = await _story.GetScenesAsync(project.Id, cancellationToken).ConfigureAwait(true);

            _chaptersById.Clear();
            foreach (var chapter in chapters)
            {
                _chaptersById[chapter.Id] = chapter;
            }

            _charactersById.Clear();
            foreach (var character in characters)
            {
                _charactersById[character.Id] = character;
            }

            _chapterPartById.Clear();
            _partBookById.Clear();
            _bookTitles.Clear();
            _partTitles.Clear();
            foreach (var book in tree.Books)
            {
                _bookTitles[book.Id] = book.Title;
                foreach (var part in book.Parts)
                {
                    _partBookById[part.Id] = book.Id;
                    _partTitles[part.Id] = part.Title;
                    foreach (var chapter in part.Chapters)
                    {
                        _chapterPartById[chapter.Id] = part.Id;
                    }
                }
            }

            foreach (var chapter in tree.UngroupedChapters)
            {
                _chapterPartById[chapter.Id] = null;
            }

            _allScenes.Clear();
            _allScenes.AddRange(scenes);

            _suppressFilterReload = true;
            RebuildBookFilters(tree);
            RebuildPartFilters();
            RebuildChapterFilters();
            RebuildViewpointFilters(characters);
            _suppressFilterReload = false;

            ApplyFiltersPreservingSelection(selectedId);
            StatusMessage = $"{Cards.Count} scene card(s).";
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

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressFilterReload = true;
        SelectedBookFilter = BookFilters.FirstOrDefault();
        RebuildPartFilters();
        RebuildChapterFilters();
        SelectedViewpointFilter = ViewpointFilters.FirstOrDefault();
        SelectedStatusFilter = StatusFilterOptions[0];
        _suppressFilterReload = false;
        ApplyFiltersPreservingSelection();
        StatusMessage = "Filters cleared.";
    }

    [RelayCommand]
    private async Task CreateSceneAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        var title = _dialogs.PromptText("New Scene", "Scene title:", "Scene");
        if (title is null)
        {
            return;
        }

        try
        {
            Guid? chapterId = SelectedChapterFilter is { IsUnassignedOnly: false, IsAllChapters: false, ChapterId: { } id }
                ? id
                : null;
            var sequence = _allScenes
                .Where(scene => scene.ChapterId == chapterId)
                .Select(scene => scene.SequenceNumber)
                .DefaultIfEmpty(0)
                .Max() + 1;
            var created = await _story.CreateSceneAsync(
                    project.Id,
                    new Scene
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = project.Id,
                        ChapterId = chapterId,
                        SequenceNumber = sequence,
                        Title = title,
                    })
                .ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedCard = Cards.FirstOrDefault(card => card.Id == created.Id);
            StatusMessage = "Scene created (canonical Story Data record).";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void EditSelectedScene()
    {
        if (SelectedCard is null)
        {
            StatusMessage = "Select a scene card first.";
            return;
        }

        _navigation.NavigateToStoryDataScene(SelectedCard.Id);
    }

    [RelayCommand]
    private async Task DeleteSelectedSceneAsync()
    {
        var project = _projects.ActiveProject;
        var card = SelectedCard;
        if (project is null || card is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Delete scene '{card.Title}'? This removes the canonical scene record.", "Delete Scene"))
        {
            return;
        }

        try
        {
            await _story.DeleteSceneAsync(project.Id, card.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            StatusMessage = "Scene deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task MoveSelectedUpAsync() => await MoveSelectedAsync(-1).ConfigureAwait(true);

    [RelayCommand]
    private async Task MoveSelectedDownAsync() => await MoveSelectedAsync(1).ConfigureAwait(true);

    [RelayCommand]
    private async Task MoveSelectedToChapterAsync()
    {
        var project = _projects.ActiveProject;
        var card = SelectedCard;
        if (project is null || card is null)
        {
            return;
        }

        var choices = _chaptersById.Values
            .OrderBy(chapter => chapter.SequenceNumber)
            .Select(chapter => new DialogChoice
            {
                Id = chapter.Id,
                Label = SceneChapterOrdering.FormatChapterLabel(chapter.SequenceNumber, chapter.Title),
            })
            .ToList();
        choices.Insert(0, new DialogChoice { Id = Guid.Empty, Label = SceneChapterOrdering.UnassignedLabel });
        var choice = _dialogs.PromptChoice("Move Scene To Chapter", "Choose a chapter (or Unassigned):", choices);
        if (choice is null)
        {
            return;
        }

        try
        {
            Guid? chapterId = choice.Id == Guid.Empty ? null : choice.Id;
            await _hierarchy.MoveSceneToChapterAsync(project.Id, card.Id, chapterId).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedCard = Cards.FirstOrDefault(item => item.Id == card.Id);
            StatusMessage = "Scene moved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task UnassignSelectedAsync()
    {
        var project = _projects.ActiveProject;
        var card = SelectedCard;
        if (project is null || card is null)
        {
            return;
        }

        try
        {
            await _hierarchy.UnassignSceneAsync(project.Id, card.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedCard = Cards.FirstOrDefault(item => item.Id == card.Id);
            StatusMessage = "Scene unassigned.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task HandleCardDropAsync(CorkboardDropRequest? request)
    {
        if (request is null)
        {
            return;
        }

        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        var source = Cards.FirstOrDefault(card => card.Id == request.SourceSceneId);
        var target = Cards.FirstOrDefault(card => card.Id == request.TargetSceneId);
        if (source is null || target is null)
        {
            StatusMessage = "Drop failed: card not found.";
            return;
        }

        try
        {
            if (source.ChapterId != target.ChapterId)
            {
                await _hierarchy.MoveSceneToChapterAsync(project.Id, source.Id, target.ChapterId)
                    .ConfigureAwait(true);
            }
            else
            {
                var direction = target.SequenceNumber < source.SequenceNumber ? -1 : 1;
                var steps = Math.Abs(target.SequenceNumber - source.SequenceNumber);
                for (var i = 0; i < steps; i++)
                {
                    await _hierarchy.MoveSceneAsync(project.Id, source.Id, direction).ConfigureAwait(true);
                }
            }

            await RefreshAsync().ConfigureAwait(true);
            SelectedCard = Cards.FirstOrDefault(card => card.Id == source.Id);
            StatusMessage = "Corkboard order updated.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenChapter()
    {
        if (SelectedCard?.ChapterId is not { } chapterId)
        {
            StatusMessage = "Selected scene has no chapter assignment.";
            return;
        }

        _navigation.NavigateToManuscriptChapter(chapterId, SelectedCard.Id);
    }

    [RelayCommand]
    private void OpenViewpoint()
    {
        if (SelectedCard?.ViewpointCharacterId is not { } characterId)
        {
            StatusMessage = "Selected scene has no viewpoint character link.";
            return;
        }

        _navigation.NavigateToStoryDataCharacter(characterId);
    }

    [RelayCommand]
    private async Task OpenLocationAsync()
    {
        var project = _projects.ActiveProject;
        var card = SelectedCard;
        if (project is null || card is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(card.Location))
        {
            StatusMessage = "Location is empty (free-text field; no World Entry FK).";
            return;
        }

        var entries = await _story.GetWorldEntriesAsync(project.Id).ConfigureAwait(true);
        var match = entries.FirstOrDefault(entry =>
            string.Equals(entry.Name, card.Location, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            _dialogs.ShowMessage(
                $"Location text: \"{card.Location}\"\n\n{LocationLimitationNote}",
                "Scene Location");
            StatusMessage = "No World Entry matches this free-text location.";
            return;
        }

        _navigation.NavigateToStoryDataWorldEntry(match.Id);
    }

    [RelayCommand]
    private async Task OpenRelatedBeatAsync()
    {
        var project = _projects.ActiveProject;
        var card = SelectedCard;
        if (project is null || card is null)
        {
            return;
        }

        var beats = await _story.GetBeatsAsync(project.Id).ConfigureAwait(true);
        var match = beats.FirstOrDefault(beat =>
            !string.IsNullOrWhiteSpace(beat.SceneRef)
            && (string.Equals(beat.SceneRef, card.Title, StringComparison.OrdinalIgnoreCase)
                || string.Equals(beat.SceneRef, card.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                || beat.SceneRef.Contains(card.Title, StringComparison.OrdinalIgnoreCase)));
        if (match is null)
        {
            _dialogs.ShowMessage(
                "Beats store SceneRef as free text, not a GUID foreign key. No matching beat SceneRef was found for this scene title/id.",
                "Related Beat");
            StatusMessage = "No related beat (free-text SceneRef).";
            return;
        }

        _navigation.NavigateToStoryDataBeat(match.Id);
    }

    private async Task MoveSelectedAsync(int direction)
    {
        var project = _projects.ActiveProject;
        var card = SelectedCard;
        if (project is null || card is null)
        {
            return;
        }

        try
        {
            await _hierarchy.MoveSceneAsync(project.Id, card.Id, direction).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            SelectedCard = Cards.FirstOrDefault(item => item.Id == card.Id);
            StatusMessage = direction < 0 ? "Moved up." : "Moved down.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void ApplyFiltersPreservingSelection(Guid? preferredId = null)
    {
        var keepId = preferredId ?? SelectedCard?.Id;
        var chapterSequence = _chaptersById.ToDictionary(pair => pair.Key, pair => pair.Value.SequenceNumber);
        var filter = BuildFilter();
        var filtered = SceneCorkboardFiltering.Apply(
            _allScenes,
            filter,
            _chapterPartById,
            _partBookById,
            chapterSequence);

        Cards.Clear();
        foreach (var scene in filtered)
        {
            Cards.Add(ToCard(scene));
        }

        SelectedCard = keepId is { } id
            ? Cards.FirstOrDefault(card => card.Id == id) ?? Cards.FirstOrDefault()
            : Cards.FirstOrDefault();
        UpdateMoveStates();
    }

    private SceneCorkboardFilter BuildFilter()
    {
        var unassignedOnly = SelectedChapterFilter?.IsUnassignedOnly == true;
        return new SceneCorkboardFilter
        {
            BookId = SelectedBookFilter?.BookId,
            PartId = SelectedPartFilter?.PartId,
            ChapterId = unassignedOnly ? null : SelectedChapterFilter?.ChapterId,
            UnassignedChaptersOnly = unassignedOnly,
            ViewpointCharacterId = SelectedViewpointFilter?.CharacterId,
            Status = SelectedStatusFilter?.Status,
        };
    }

    private SceneCardViewModel ToCard(Scene scene)
    {
        _chaptersById.TryGetValue(scene.ChapterId ?? Guid.Empty, out var chapter);
        _charactersById.TryGetValue(scene.ViewpointCharacterId ?? Guid.Empty, out var character);
        var chapterLabel = chapter is null
            ? SceneChapterOrdering.UnassignedLabel
            : SceneChapterOrdering.FormatChapterLabel(chapter.SequenceNumber, chapter.Title);
        string? bookTitle = null;
        string? partTitle = null;
        if (scene.ChapterId is { } cid
            && _chapterPartById.TryGetValue(cid, out var partId)
            && partId is { } pid)
        {
            _partTitles.TryGetValue(pid, out partTitle);
            if (_partBookById.TryGetValue(pid, out var bookId))
            {
                _bookTitles.TryGetValue(bookId, out bookTitle);
            }
        }

        return new SceneCardViewModel(scene, chapterLabel, character?.Name, bookTitle, partTitle);
    }

    private void RebuildBookFilters(ManuscriptHierarchy tree)
    {
        var previous = SelectedBookFilter?.BookId;
        BookFilters.Clear();
        BookFilters.Add(new BookFilterOption(null, "All books"));
        foreach (var book in tree.Books)
        {
            BookFilters.Add(new BookFilterOption(book.Id, book.Title));
        }

        SelectedBookFilter = BookFilters.FirstOrDefault(option => option.BookId == previous) ?? BookFilters[0];
    }

    private void RebuildPartFilters()
    {
        var previous = SelectedPartFilter?.PartId;
        PartFilters.Clear();
        PartFilters.Add(new PartFilterOption(null, "All parts"));
        var bookId = SelectedBookFilter?.BookId;
        foreach (var pair in _partBookById)
        {
            if (bookId is not null && pair.Value != bookId)
            {
                continue;
            }

            PartFilters.Add(new PartFilterOption(pair.Key, _partTitles.GetValueOrDefault(pair.Key, "Part")));
        }

        SelectedPartFilter = PartFilters.FirstOrDefault(option => option.PartId == previous) ?? PartFilters[0];
        RebuildChapterFilters();
    }

    private void RebuildChapterFilters()
    {
        var previousChapter = SelectedChapterFilter?.ChapterId;
        var previousUnassigned = SelectedChapterFilter?.IsUnassignedOnly == true;
        ChapterFilters.Clear();
        ChapterFilters.Add(ChapterFilterOptionViewModel.All());
        ChapterFilters.Add(ChapterFilterOptionViewModel.Unassigned());
        var partId = SelectedPartFilter?.PartId;
        var bookId = SelectedBookFilter?.BookId;
        foreach (var chapter in _chaptersById.Values.OrderBy(item => item.SequenceNumber))
        {
            if (!_chapterPartById.TryGetValue(chapter.Id, out var chapterPart))
            {
                continue;
            }

            if (partId is not null && chapterPart != partId)
            {
                continue;
            }

            if (bookId is not null)
            {
                if (chapterPart is null
                    || !_partBookById.TryGetValue(chapterPart.Value, out var book)
                    || book != bookId)
                {
                    continue;
                }
            }

            ChapterFilters.Add(ChapterFilterOptionViewModel.From(chapter));
        }

        SelectedChapterFilter = previousUnassigned
            ? ChapterFilters.FirstOrDefault(option => option.IsUnassignedOnly)
            : ChapterFilters.FirstOrDefault(option => option.ChapterId == previousChapter)
              ?? ChapterFilters[0];
    }

    private void RebuildViewpointFilters(IReadOnlyList<Character> characters)
    {
        var previous = SelectedViewpointFilter?.CharacterId;
        ViewpointFilters.Clear();
        ViewpointFilters.Add(CharacterOptionViewModel.All());
        foreach (var character in characters.OrderBy(item => item.Name))
        {
            ViewpointFilters.Add(CharacterOptionViewModel.From(character));
        }

        SelectedViewpointFilter =
            ViewpointFilters.FirstOrDefault(option => option.CharacterId == previous) ?? ViewpointFilters[0];
    }

    private void UpdateMoveStates()
    {
        var card = SelectedCard;
        if (card is null)
        {
            CanMoveSelectedUp = false;
            CanMoveSelectedDown = false;
            return;
        }

        var siblings = Cards.Where(item => item.ChapterId == card.ChapterId).OrderBy(item => item.SequenceNumber).ToList();
        var index = siblings.FindIndex(item => item.Id == card.Id);
        CanMoveSelectedUp = index > 0;
        CanMoveSelectedDown = index >= 0 && index < siblings.Count - 1;
    }

    private void OnStoryChanged(object? sender, StoryChangeEventArgs e)
    {
        if (_loading || _projects.ActiveProject?.Id != e.ProjectId)
        {
            return;
        }

        if (e.Kind is StoryChangeKind.SceneUpserted
            or StoryChangeKind.SceneDeleted
            or StoryChangeKind.HierarchyChanged
            or StoryChangeKind.CharacterChanged)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                _ = RefreshAsync();
            }
            else
            {
                dispatcher.InvokeAsync(() => _ = RefreshAsync());
            }
        }
    }
}

public sealed class SceneCardViewModel
{
    public SceneCardViewModel(
        Scene source,
        string chapterLabel,
        string? viewpointName,
        string? bookTitle,
        string? partTitle)
    {
        Source = source;
        ChapterLabel = chapterLabel;
        ViewpointName = viewpointName ?? "—";
        BookTitle = bookTitle ?? "—";
        PartTitle = partTitle ?? "—";
    }

    public Scene Source { get; }

    public Guid Id => Source.Id;

    public string Title => Source.Title;

    public Guid? ChapterId => Source.ChapterId;

    public int SequenceNumber => Source.SequenceNumber;

    public Guid? ViewpointCharacterId => Source.ViewpointCharacterId;

    public string Location => Source.Location;

    public SceneDraftStatus Status => Source.Status;

    public int WordCount => Source.WordCount;

    public string ChapterLabel { get; }

    public string ViewpointName { get; }

    public string BookTitle { get; }

    public string PartTitle { get; }

    public string StatusDisplay => Status.ToString();

    public string AccessibleName =>
        $"Scene {Title}, chapter {ChapterLabel}, status {StatusDisplay}, {WordCount} words";
}

public sealed class CorkboardDropRequest
{
    public required Guid SourceSceneId { get; init; }

    public required Guid TargetSceneId { get; init; }
}

public sealed class BookFilterOption(Guid? bookId, string label)
{
    public Guid? BookId { get; } = bookId;

    public string Label { get; } = label;
}

public sealed class PartFilterOption(Guid? partId, string label)
{
    public Guid? PartId { get; } = partId;

    public string Label { get; } = label;
}

public sealed class StatusFilterOption(SceneDraftStatus? status, string label)
{
    public SceneDraftStatus? Status { get; } = status;

    public string Label { get; } = label;
}
