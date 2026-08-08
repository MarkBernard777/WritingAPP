using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Navigation;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.Core.Accessibility;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Hierarchy;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.Manuscript;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class ManuscriptViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IChapterService _chapterService;
    private readonly IStoryDataService _storyData;
    private readonly IManuscriptHierarchyService _hierarchy;
    private readonly INavigationService _navigation;
    private readonly IProjectDialogService _dialogs;
    private readonly IEditorAutosaveService _autosave;
    private readonly ISaveStateService _saveState;
    private readonly IStoryChangeNotifier _changes;
    private string _savedContent = string.Empty;
    private bool _suppressDirty;
    private bool _restoringSelection;
    private Guid? _loadedChapterId;
    private Guid? _contextSceneId;
    private int _editorSessionVersion;
    private CancellationTokenSource? _previewDebounceCts;
    private CancellationTokenSource? _refreshCts;
    private static readonly TimeSpan PreviewDebounceDelay = TimeSpan.FromMilliseconds(250);

    public ManuscriptViewModel(
        IProjectService projectService,
        IChapterService chapterService,
        IStoryDataService storyData,
        IManuscriptHierarchyService hierarchy,
        INavigationService navigation,
        IProjectDialogService dialogs,
        IEditorAutosaveService autosave,
        ISaveStateService saveState,
        SceneCorkboardViewModel corkboard,
        IStoryChangeNotifier changes)
    {
        _projectService = projectService;
        _chapterService = chapterService;
        _storyData = storyData;
        _hierarchy = hierarchy;
        _navigation = navigation;
        _dialogs = dialogs;
        _autosave = autosave;
        _saveState = saveState;
        Corkboard = corkboard;
        _changes = changes;
        _autosave.SaveCompleted += OnAutosaveCompleted;
        _saveState.Changed += (_, _) => SyncSaveState();
        _changes.Changed += OnStoryChanged;
        SyncSaveState();
        _ = RefreshAsync();
    }

    public SceneCorkboardViewModel Corkboard { get; }

    public ObservableCollection<HierarchyNodeViewModel> HierarchyRoots { get; } = [];

    public ObservableCollection<ChapterListItemViewModel> Chapters { get; } = [];

    public ObservableCollection<BracketNoteItemViewModel> BracketNotes { get; } = [];

    public ObservableCollection<LinkedSceneItemViewModel> LinkedScenes { get; } = [];

    [ObservableProperty]
    private bool _hasProject;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private HierarchyNodeViewModel? _selectedNode;

    [ObservableProperty]
    private ChapterListItemViewModel? _selectedChapter;

    [ObservableProperty]
    private string _markdownText = string.Empty;

    [ObservableProperty]
    private string _previewHtml = string.Empty;

    [ObservableProperty]
    private int _wordCount;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _chapterTitle = string.Empty;

    [ObservableProperty]
    private string _exportFileName = $"manuscript-{DateTime.Now:yyyyMMdd-HHmmss}.md";

    [ObservableProperty]
    private LinkedSceneItemViewModel? _selectedLinkedScene;

    [ObservableProperty]
    private string _saveStateDisplay = "Clean";

    [ObservableProperty]
    private string _saveStateAccessibleName = "Save status: Clean";

    [ObservableProperty]
    private string _hierarchyFilter = string.Empty;

    [ObservableProperty]
    private bool _isLeftPanelCollapsed;

    [ObservableProperty]
    private bool _isRightPanelCollapsed;

    [ObservableProperty]
    private string _contextTitle = "Context";

    [ObservableProperty]
    private string _contextSummary = "Select a chapter or scene to see metadata.";

    [ObservableProperty]
    private string _contextDetail = string.Empty;

    [ObservableProperty]
    private string _contextStatusText = string.Empty;

    [ObservableProperty]
    private bool _canMoveSelectedUp;

    [ObservableProperty]
    private bool _canMoveSelectedDown;

    [ObservableProperty]
    private bool _canMoveSelectedTo;

    [ObservableProperty]
    private bool _isCorkboardMode;

    [ObservableProperty]
    private bool _isDistractionFreeMode;

    [ObservableProperty]
    private SceneDraftEditorViewModel? _sceneDraftEditor;

    [ObservableProperty]
    private string _associationCountsDisplay = "Chapter: 0 words";

    [ObservableProperty]
    private SceneOutlineItemViewModel? _selectedOutlineItem;

    [ObservableProperty]
    private BracketNoteItemViewModel? _selectedBracketNote;

    public ObservableCollection<SceneOutlineItemViewModel> SceneOutline { get; } = [];

    public ObservableCollection<CharacterOptionViewModel> ViewpointOptions { get; } = [];

    public IReadOnlyList<SceneDraftStatus> SceneStatusOptions { get; } = Enum.GetValues<SceneDraftStatus>();

    public void NotifyProseAssociationChanged()
    {
        RebuildSceneOutline();
        RefreshAssociationCounts(MarkdownText);
        RefreshBracketNotes(MarkdownText);
    }

    public int EditorSessionVersion => _editorSessionVersion;

    public string LeftPanelToggleLabel => IsLeftPanelCollapsed ? "Show hierarchy" : "Hide hierarchy";

    public string RightPanelToggleLabel => IsRightPanelCollapsed ? "Show context" : "Hide context";

    public string CorkboardToggleLabel => IsCorkboardMode ? "Show editor" : "Show corkboard";

    public bool IsLeftChromeVisible => !IsDistractionFreeMode && !IsLeftPanelCollapsed;

    public bool IsRightChromeVisible => !IsDistractionFreeMode && !IsRightPanelCollapsed;

    public bool HasSceneDraftEditor => SceneDraftEditor is not null;

    public event EventHandler<MarkdownFormatKind>? FormatRequested;

    public event EventHandler<SceneAssociationRequest>? AssociateSelectionRequested;

    public event EventHandler<EditorCaretRequest>? CaretRequested;

    public void Detach()
    {
        _changes.Changed -= OnStoryChanged;
        Corkboard.Detach();
        _autosave.SaveCompleted -= OnAutosaveCompleted;
        _previewDebounceCts?.Cancel();
        _previewDebounceCts?.Dispose();
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
    }

    public async Task FocusChapterAsync(Guid chapterId, Guid? sceneId = null)
    {
        IsCorkboardMode = false;
        if (sceneId is { } sid)
        {
            await ReloadHierarchyAsync(HierarchyNodeKind.Scene, sid, preserveExpansion: true)
                .ConfigureAwait(true);
            if (SelectedNode?.Id == sid)
            {
                return;
            }
        }

        await ReloadHierarchyAsync(HierarchyNodeKind.Chapter, chapterId, preserveExpansion: true)
            .ConfigureAwait(true);
    }

    public async Task FocusCorkboardAsync(Guid? sceneId = null)
    {
        IsCorkboardMode = true;
        await Corkboard.RefreshAsync().ConfigureAwait(true);
        if (sceneId is { } id)
        {
            Corkboard.SelectedCard = Corkboard.Cards.FirstOrDefault(card => card.Id == id);
        }
    }

    partial void OnIsLeftPanelCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(LeftPanelToggleLabel));
        OnPropertyChanged(nameof(IsLeftChromeVisible));
    }

    partial void OnIsRightPanelCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(RightPanelToggleLabel));
        OnPropertyChanged(nameof(IsRightChromeVisible));
    }

    partial void OnIsCorkboardModeChanged(bool value)
        => OnPropertyChanged(nameof(CorkboardToggleLabel));

    partial void OnIsDistractionFreeModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLeftChromeVisible));
        OnPropertyChanged(nameof(IsRightChromeVisible));
    }

    partial void OnSelectedLinkedSceneChanged(LinkedSceneItemViewModel? value)
    {
        SceneDraftEditor = value is null ? null : SceneDraftEditorViewModel.From(value.Source);
        OnPropertyChanged(nameof(HasSceneDraftEditor));
        SelectedOutlineItem = SceneOutline.FirstOrDefault(item => item.SceneId == value?.Id);
        _contextSceneId = value?.Id;
    }

    partial void OnSelectedOutlineItemChanged(SceneOutlineItemViewModel? value)
    {
        if (value is null || _restoringSelection)
        {
            return;
        }

        SelectedLinkedScene = LinkedScenes.FirstOrDefault(item => item.Id == value.SceneId);
        NavigateToSelectedSceneProse();
    }

    partial void OnSelectedBracketNoteChanged(BracketNoteItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        CaretRequested?.Invoke(this, new EditorCaretRequest
        {
            CaretIndex = value.CharIndex,
            SelectionLength = value.MarkerLength,
        });
    }

    partial void OnHierarchyFilterChanged(string value) => ApplyHierarchyFilter();

    partial void OnSelectedNodeChanged(HierarchyNodeViewModel? value)
    {
        if (_restoringSelection)
        {
            return;
        }

        _ = OnHierarchySelectionAsync(value);
    }

    partial void OnSelectedChapterChanged(ChapterListItemViewModel? value)
    {
        // Kept for compile/list compatibility; hierarchy selection drives editor loading.
        _ = value;
    }

    partial void OnMarkdownTextChanged(string value)
    {
        if (_suppressDirty)
        {
            return;
        }

        IsDirty = !string.Equals(value, _savedContent, StringComparison.Ordinal);
        WordCount = ManuscriptTextAnalytics.CountWords(SceneProseAssociation.StripMarkers(value));
        RefreshAssociationCounts(value);
        SchedulePreviewUpdate(value);

        var project = _projectService.ActiveProject;
        var chapterId = _loadedChapterId;
        if (IsDirty && project is not null && chapterId is { } id)
        {
            _autosave.ScheduleManuscriptSave(project.Id, id, value);
        }
    }

    private void SchedulePreviewUpdate(string markdown)
    {
        _previewDebounceCts?.Cancel();
        _previewDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _previewDebounceCts = cts;
        _ = UpdatePreviewAfterDelayAsync(markdown, cts.Token);
    }

    private async Task UpdatePreviewAfterDelayAsync(string markdown, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PreviewDebounceDelay, cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var html = await Task.Run(
                    () => MarkdownPreviewRenderer.ToHtmlDocument(markdown),
                    cancellationToken)
                .ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            PreviewHtml = html;
            RefreshBracketNotes(markdown);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private void ToggleLeftPanel() => IsLeftPanelCollapsed = !IsLeftPanelCollapsed;

    [RelayCommand]
    private void ToggleRightPanel() => IsRightPanelCollapsed = !IsRightPanelCollapsed;

    [RelayCommand]
    private async Task ToggleCorkboardAsync()
    {
        if (IsCorkboardMode)
        {
            IsCorkboardMode = false;
            return;
        }

        await FocusCorkboardAsync(Corkboard.SelectedCard?.Id).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ToggleDistractionFree()
    {
        IsDistractionFreeMode = !IsDistractionFreeMode;
        if (IsDistractionFreeMode)
        {
            IsCorkboardMode = false;
            StatusMessage = "Distraction-free mode. Press Esc or click Exit focus to restore panels.";
        }
    }

    [RelayCommand]
    private void ExitDistractionFree() => IsDistractionFreeMode = false;

    [RelayCommand]
    private void FormatMarkdown(MarkdownFormatKind kind)
        => FormatRequested?.Invoke(this, kind);

    [RelayCommand]
    private void AssociateSelectedSceneWithSelection()
    {
        if (SelectedLinkedScene is null)
        {
            StatusMessage = "Select a linked scene before associating prose.";
            return;
        }

        AssociateSelectionRequested?.Invoke(this, new SceneAssociationRequest
        {
            SceneId = SelectedLinkedScene.Id,
        });
    }

    [RelayCommand]
    private void NavigateToSelectedSceneProse()
    {
        if (SelectedLinkedScene is null)
        {
            StatusMessage = "Select a scene in the outline to navigate.";
            return;
        }

        var doc = SceneProseAssociation.Parse(MarkdownText);
        var caret = doc.GetCaretIndexForScene(SelectedLinkedScene.Id);
        if (caret is null)
        {
            StatusMessage =
                "This scene has no prose region yet. Select text and use Associate selection, or assign the scene to append an empty region.";
            return;
        }

        CaretRequested?.Invoke(this, new EditorCaretRequest
        {
            CaretIndex = caret.Value,
            SelectionLength = 0,
        });
        StatusMessage = $"Jumped to scene '{SelectedLinkedScene.Source.Title}'.";
    }

    [RelayCommand]
    private async Task SaveSceneMetadataAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = SceneDraftEditor;
        if (project is null || editor is null)
        {
            return;
        }

        try
        {
            // One user operation: chapter file first (journaled), then scene metadata.
            if (IsDirty && _loadedChapterId is { } chapterId)
            {
                await _autosave.FlushAsync().ConfigureAwait(true);
                if (IsDirty)
                {
                    await _chapterService
                        .SaveContentAsync(project.Id, chapterId, MarkdownText)
                        .ConfigureAwait(true);
                    _savedContent = MarkdownText;
                    IsDirty = false;
                }
            }

            var model = editor.ToModel();
            var updated = await _storyData.UpdateSceneAsync(project.Id, model).ConfigureAwait(true);
            if (_loadedChapterId is { } loaded)
            {
                await LoadLinkedScenesAsync(project.Id, loaded).ConfigureAwait(true);
                SelectLinkedScene(updated.Id);
            }

            SceneDraftEditor = SceneDraftEditorViewModel.From(updated);
            StatusMessage = "Scene metadata saved.";
            SyncSaveState();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsDirty && !ConfirmDiscard())
        {
            return;
        }

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var cancellationToken = _refreshCts.Token;

        var project = _projectService.ActiveProject;
        HasProject = project is not null;
        if (project is null)
        {
            HierarchyRoots.Clear();
            Chapters.Clear();
            BracketNotes.Clear();
            LinkedScenes.Clear();
            SelectedLinkedScene = null;
            SelectedNode = null;
            SelectedChapter = null;
            _loadedChapterId = null;
            _contextSceneId = null;
            SetEditorContent(string.Empty, markClean: true);
            ChapterTitle = string.Empty;
            ClearContext();
            StatusMessage = "Open or create a project to edit the manuscript.";
            UpdateMoveCommandStates();
            await Corkboard.RefreshAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        try
        {
            await ReloadHierarchyAsync(
                    selectKind: SelectedNode?.Kind,
                    selectId: SelectedNode?.Id,
                    preserveExpansion: true,
                    cancellationToken)
                .ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Corkboard.RefreshAsync(cancellationToken).ConfigureAwait(true);
            if (_loadedChapterId is { } chapterId)
            {
                await LoadLinkedScenesAsync(project.Id, chapterId).ConfigureAwait(true);
                SelectLinkedScene(_contextSceneId);
            }

            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Refresh cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddChapterAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        if (IsDirty && !ConfirmDiscard())
        {
            return;
        }

        try
        {
            var created = await _chapterService
                .CreateAsync(project.Id, $"Chapter {Chapters.Count + 1}")
                .ConfigureAwait(true);
            await ReloadHierarchyAsync(HierarchyNodeKind.Chapter, created.Id, preserveExpansion: true)
                .ConfigureAwait(true);
            StatusMessage = "Chapter created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RenameChapterAsync()
    {
        var project = _projectService.ActiveProject;
        var chapterId = _loadedChapterId;
        if (project is null || chapterId is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ChapterTitle))
        {
            StatusMessage = "Chapter title is required.";
            return;
        }

        try
        {
            var renamed = await _chapterService.RenameAsync(project.Id, chapterId.Value, ChapterTitle)
                .ConfigureAwait(true);
            await ReloadHierarchyAsync(HierarchyNodeKind.Chapter, renamed.Id, preserveExpansion: true)
                .ConfigureAwait(true);
            StatusMessage = "Chapter renamed.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteChapterAsync()
    {
        var project = _projectService.ActiveProject;
        var node = SelectedNode;
        if (project is null || node is null || node.Kind != HierarchyNodeKind.Chapter)
        {
            StatusMessage = "Select a chapter to delete.";
            return;
        }

        if (!_dialogs.Confirm($"Delete chapter '{node.Title}'? This removes the markdown file.", "Delete Chapter"))
        {
            return;
        }

        try
        {
            await _chapterService.DeleteAsync(project.Id, node.Id).ConfigureAwait(true);
            IsDirty = false;
            _loadedChapterId = null;
            await ReloadHierarchyAsync(selectKind: null, selectId: null, preserveExpansion: true)
                .ConfigureAwait(true);
            StatusMessage = "Chapter deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddBookAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        var title = _dialogs.PromptText("New Book", "Book title:", "Book");
        if (title is null)
        {
            return;
        }

        try
        {
            var book = await _hierarchy.CreateBookAsync(project.Id, title).ConfigureAwait(true);
            await ReloadHierarchyAsync(HierarchyNodeKind.Book, book.Id, preserveExpansion: true)
                .ConfigureAwait(true);
            StatusMessage = "Book created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddPartAsync()
    {
        var project = _projectService.ActiveProject;
        var node = SelectedNode;
        if (project is null)
        {
            return;
        }

        var bookId = node?.Kind switch
        {
            HierarchyNodeKind.Book => node.Id,
            HierarchyNodeKind.Part => node.ParentId,
            HierarchyNodeKind.Chapter => FindAncestorBookId(node),
            _ => HierarchyRoots.FirstOrDefault(item => item.Kind == HierarchyNodeKind.Book)?.Id,
        };
        if (bookId is null)
        {
            StatusMessage = "Select a book (or item inside a book) before adding a part.";
            return;
        }

        var title = _dialogs.PromptText("New Part", "Part title:", "Part");
        if (title is null)
        {
            return;
        }

        try
        {
            var part = await _hierarchy.CreatePartAsync(project.Id, bookId.Value, title).ConfigureAwait(true);
            await ReloadHierarchyAsync(HierarchyNodeKind.Part, part.Id, preserveExpansion: true)
                .ConfigureAwait(true);
            StatusMessage = "Part created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RenameSelectedAsync()
    {
        var project = _projectService.ActiveProject;
        var node = SelectedNode;
        if (project is null || node is null)
        {
            return;
        }

        if (node.Kind is HierarchyNodeKind.Scene or HierarchyNodeKind.UnassignedGroup)
        {
            StatusMessage = "Rename scenes in Story Data. Select a book, part, or chapter here.";
            return;
        }

        if (node.Kind == HierarchyNodeKind.Chapter)
        {
            await RenameChapterAsync().ConfigureAwait(true);
            return;
        }

        var title = _dialogs.PromptText($"Rename {node.Kind}", "New title:", node.Title);
        if (title is null)
        {
            return;
        }

        try
        {
            if (node.Kind == HierarchyNodeKind.Book)
            {
                await _hierarchy.RenameBookAsync(project.Id, node.Id, title).ConfigureAwait(true);
            }
            else if (node.Kind == HierarchyNodeKind.Part)
            {
                await _hierarchy.RenamePartAsync(project.Id, node.Id, title).ConfigureAwait(true);
            }

            await ReloadHierarchyAsync(node.Kind, node.Id, preserveExpansion: true).ConfigureAwait(true);
            StatusMessage = $"{node.Kind} renamed.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var project = _projectService.ActiveProject;
        var node = SelectedNode;
        if (project is null || node is null)
        {
            return;
        }

        if (node.Kind == HierarchyNodeKind.Chapter)
        {
            await DeleteChapterAsync().ConfigureAwait(true);
            return;
        }

        if (node.Kind == HierarchyNodeKind.Book)
        {
            if (!_dialogs.Confirm(
                    $"Delete book '{node.Title}'? Parts must be moved or removed first (child protection).",
                    "Delete Book"))
            {
                return;
            }

            try
            {
                await _hierarchy.DeleteBookAsync(project.Id, node.Id).ConfigureAwait(true);
                await ReloadHierarchyAsync(null, null, preserveExpansion: true).ConfigureAwait(true);
                StatusMessage = "Book deleted.";
            }
            catch (InvalidOperationException ex)
            {
                _dialogs.ShowMessage(ex.Message, "Cannot Delete Book");
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }

            return;
        }

        if (node.Kind == HierarchyNodeKind.Part)
        {
            if (!_dialogs.Confirm(
                    $"Delete part '{node.Title}'? Chapters are protected until moved or unassigned.",
                    "Delete Part"))
            {
                return;
            }

            try
            {
                await _hierarchy.DeletePartAsync(project.Id, node.Id).ConfigureAwait(true);
                await ReloadHierarchyAsync(null, null, preserveExpansion: true).ConfigureAwait(true);
                StatusMessage = "Part deleted.";
            }
            catch (InvalidOperationException)
            {
                if (!_dialogs.Confirm(
                        "This part still has chapters. Unassign those chapters (keep files) and delete the part?",
                        "Unassign Chapters and Delete Part"))
                {
                    StatusMessage = "Delete cancelled — chapters remain protected.";
                    return;
                }

                try
                {
                    await _hierarchy.DeletePartAsync(
                            project.Id,
                            node.Id,
                            new HierarchyDeletionPolicy { UnassignChapters = true })
                        .ConfigureAwait(true);
                    await ReloadHierarchyAsync(null, null, preserveExpansion: true).ConfigureAwait(true);
                    StatusMessage = "Part deleted; chapters unassigned.";
                }
                catch (Exception ex)
                {
                    StatusMessage = ex.Message;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }
    }

    [RelayCommand]
    private async Task MoveSelectedUpAsync() => await MoveSelectedAsync(-1).ConfigureAwait(true);

    [RelayCommand]
    private async Task MoveSelectedDownAsync() => await MoveSelectedAsync(1).ConfigureAwait(true);

    [RelayCommand]
    private async Task MoveSelectedToAsync()
    {
        var project = _projectService.ActiveProject;
        var node = SelectedNode;
        if (project is null || node is null)
        {
            return;
        }

        try
        {
            if (node.Kind == HierarchyNodeKind.Chapter)
            {
                var parts = Flatten(HierarchyRoots)
                    .Where(item => item.Kind == HierarchyNodeKind.Part)
                    .Select(item => new DialogChoice { Id = item.Id, Label = item.DisplayName })
                    .ToList();
                var choice = _dialogs.PromptChoice("Move Chapter To Part", "Choose a destination part:", parts);
                if (choice is null)
                {
                    return;
                }

                if (IsDirty && !ConfirmDiscard())
                {
                    return;
                }

                await _hierarchy.MoveChapterToPartAsync(project.Id, node.Id, choice.Id).ConfigureAwait(true);
                await ReloadHierarchyAsync(HierarchyNodeKind.Chapter, node.Id, preserveExpansion: true)
                    .ConfigureAwait(true);
                StatusMessage = "Chapter moved to part.";
                return;
            }

            if (node.Kind == HierarchyNodeKind.Scene)
            {
                var chapters = Flatten(HierarchyRoots)
                    .Where(item => item.Kind == HierarchyNodeKind.Chapter)
                    .Select(item => new DialogChoice { Id = item.Id, Label = item.DisplayName })
                    .ToList();
                var choice = _dialogs.PromptChoice("Move Scene To Chapter", "Choose a destination chapter:", chapters);
                if (choice is null)
                {
                    return;
                }

                await _hierarchy.MoveSceneToChapterAsync(project.Id, node.Id, choice.Id).ConfigureAwait(true);
                await ReloadHierarchyAsync(HierarchyNodeKind.Scene, node.Id, preserveExpansion: true)
                    .ConfigureAwait(true);
                StatusMessage = "Scene moved to chapter.";
                return;
            }

            StatusMessage = "Move To applies to chapters and scenes.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task HandleHierarchyDropAsync(HierarchyDropRequest? request)
    {
        if (request is null)
        {
            return;
        }

        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        var source = FindNode(request.SourceKind, request.SourceId);
        var target = FindNode(request.TargetKind, request.TargetId);
        if (source is null || target is null)
        {
            StatusMessage = "Drop failed: item not found.";
            return;
        }

        var action = ManuscriptHierarchyInteractions.ClassifyDrop(
            source.Kind,
            source.Id,
            target.Kind,
            target.Id,
            source.ParentId,
            target.ParentId);
        if (!ManuscriptHierarchyInteractions.IsValidDrop(action))
        {
            StatusMessage = "That drop is not allowed. Use Move Up/Down or Move To.";
            return;
        }

        try
        {
            switch (action)
            {
                case HierarchyDropAction.MoveChapterToPart:
                    if (IsDirty && !ConfirmDiscard())
                    {
                        return;
                    }

                    await _hierarchy.MoveChapterToPartAsync(project.Id, source.Id, target.Id)
                        .ConfigureAwait(true);
                    break;
                case HierarchyDropAction.AssignSceneToChapter:
                    await _hierarchy.MoveSceneToChapterAsync(project.Id, source.Id, target.Id)
                        .ConfigureAwait(true);
                    break;
                case HierarchyDropAction.ReorderBefore:
                    await ReorderSiblingTowardAsync(project.Id, source, target).ConfigureAwait(true);
                    break;
                default:
                    return;
            }

            await ReloadHierarchyAsync(source.Kind, source.Id, preserveExpansion: true).ConfigureAwait(true);
            StatusMessage = "Hierarchy updated.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveChapterAsync()
    {
        var project = _projectService.ActiveProject;
        var chapterId = _loadedChapterId;
        if (project is null || chapterId is null)
        {
            return;
        }

        try
        {
            _saveState.Report(SaveState.Saving, "Saving…");
            var saved = await _chapterService.SaveContentAsync(project.Id, chapterId.Value, MarkdownText)
                .ConfigureAwait(true);
            var chapterItem = Chapters.FirstOrDefault(item => item.Id == saved.Id);
            chapterItem?.Apply(saved);
            var chapterNode = FindNode(HierarchyNodeKind.Chapter, saved.Id);
            if (chapterNode is not null)
            {
                chapterNode.WordCount = saved.WordCount;
                chapterNode.Title = saved.Title;
            }

            SetEditorContent(MarkdownText, markClean: true);
            await _saveState.RefreshRecoveryAvailabilityAsync(project.Id).ConfigureAwait(true);
            if (_saveState.State != SaveState.RecoveryAvailable)
            {
                _saveState.Report(SaveState.Saved, "Saved");
            }

            StatusMessage = $"Saved ({saved.WordCount} words).";
            SyncSaveState();
        }
        catch (Exception ex)
        {
            _saveState.Report(SaveState.SaveFailed, ex.Message);
            StatusMessage = ex.Message;
            SyncSaveState();
        }
    }

    [RelayCommand]
    private async Task CompileSelectedAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        if (IsDirty && !ConfirmDiscard())
        {
            return;
        }

        SyncCompileFlagsFromTree();
        var selectedIds = Chapters
            .Where(item => item.IsSelectedForCompile)
            .OrderBy(item => item.SequenceNumber)
            .Select(item => item.Id)
            .ToList();

        if (selectedIds.Count == 0)
        {
            StatusMessage = "Select at least one chapter to compile.";
            return;
        }

        try
        {
            var result = await _chapterService
                .CompileSelectedAsync(project.Id, selectedIds, ExportFileName)
                .ConfigureAwait(true);
            StatusMessage =
                $"Compiled {result.ChapterCount} chapters ({result.WordCount} words) to {result.RelativePath}.";
            ExportFileName = $"manuscript-{DateTime.Now:yyyyMMdd-HHmmss}.md";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenContextInStoryData()
    {
        if (_contextSceneId is { } sceneId)
        {
            _navigation.NavigateToStoryDataScene(sceneId);
            return;
        }

        if (SelectedLinkedScene is not null)
        {
            _navigation.NavigateToStoryDataScene(SelectedLinkedScene.Id);
            return;
        }

        StatusMessage = "Select a scene to open in Story Data.";
    }

    [RelayCommand]
    private void OpenLinkedSceneInStoryData() => OpenContextInStoryData();

    private async Task MoveSelectedAsync(int direction)
    {
        var project = _projectService.ActiveProject;
        var node = SelectedNode;
        if (project is null || node is null)
        {
            return;
        }

        if (node.Kind is HierarchyNodeKind.Chapter && IsDirty && !ConfirmDiscard())
        {
            return;
        }

        try
        {
            switch (node.Kind)
            {
                case HierarchyNodeKind.Book:
                    await _hierarchy.MoveBookAsync(project.Id, node.Id, direction).ConfigureAwait(true);
                    break;
                case HierarchyNodeKind.Part:
                    await _hierarchy.MovePartAsync(project.Id, node.Id, direction).ConfigureAwait(true);
                    break;
                case HierarchyNodeKind.Chapter:
                    if (direction < 0)
                    {
                        await _chapterService.MoveUpAsync(project.Id, node.Id).ConfigureAwait(true);
                    }
                    else
                    {
                        await _chapterService.MoveDownAsync(project.Id, node.Id).ConfigureAwait(true);
                    }

                    break;
                case HierarchyNodeKind.Scene:
                    await _hierarchy.MoveSceneAsync(project.Id, node.Id, direction).ConfigureAwait(true);
                    break;
                default:
                    StatusMessage = "Nothing to move.";
                    return;
            }

            await ReloadHierarchyAsync(node.Kind, node.Id, preserveExpansion: true).ConfigureAwait(true);
            StatusMessage = direction < 0 ? "Moved up." : "Moved down.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task ReorderSiblingTowardAsync(
        Guid projectId,
        HierarchyNodeViewModel source,
        HierarchyNodeViewModel target)
    {
        var siblings = source.Kind switch
        {
            HierarchyNodeKind.Book => HierarchyRoots.Where(item => item.Kind == HierarchyNodeKind.Book).ToList(),
            _ => FindParent(source)?.Children.Where(item => item.Kind == source.Kind).ToList()
                 ?? [],
        };
        var sourceIndex = siblings.FindIndex(item => item.Id == source.Id);
        var targetIndex = siblings.FindIndex(item => item.Id == target.Id);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var direction = targetIndex < sourceIndex ? -1 : 1;
        var steps = Math.Abs(targetIndex - sourceIndex);
        for (var step = 0; step < steps; step++)
        {
            switch (source.Kind)
            {
                case HierarchyNodeKind.Book:
                    await _hierarchy.MoveBookAsync(projectId, source.Id, direction).ConfigureAwait(true);
                    break;
                case HierarchyNodeKind.Part:
                    await _hierarchy.MovePartAsync(projectId, source.Id, direction).ConfigureAwait(true);
                    break;
                case HierarchyNodeKind.Chapter:
                    if (direction < 0)
                    {
                        await _chapterService.MoveUpAsync(projectId, source.Id).ConfigureAwait(true);
                    }
                    else
                    {
                        await _chapterService.MoveDownAsync(projectId, source.Id).ConfigureAwait(true);
                    }

                    break;
                case HierarchyNodeKind.Scene:
                    await _hierarchy.MoveSceneAsync(projectId, source.Id, direction).ConfigureAwait(true);
                    break;
            }
        }
    }

    private async Task OnHierarchySelectionAsync(HierarchyNodeViewModel? node)
    {
        UpdateMoveCommandStates();
        if (node is null)
        {
            ClearContext();
            return;
        }

        var selection = ManuscriptHierarchyInteractions.ResolveSelection(
            node.Kind,
            node.Id,
            node.EditorChapterId);

        UpdateContextPanel(node);

        if (selection.EditorChapterId is { } chapterId)
        {
            await EnsureChapterLoadedAsync(chapterId, selection.ContextSceneId).ConfigureAwait(true);
        }
    }

    private async Task EnsureChapterLoadedAsync(Guid chapterId, Guid? contextSceneId)
    {
        if (chapterId == _loadedChapterId)
        {
            _contextSceneId = contextSceneId;
            SelectLinkedScene(contextSceneId);
            return;
        }

        if (IsDirty)
        {
            try
            {
                await _autosave.FlushAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }

            if (IsDirty && !ConfirmDiscard())
            {
                _restoringSelection = true;
                SelectedNode = FindNode(HierarchyNodeKind.Chapter, _loadedChapterId ?? Guid.Empty)
                    ?? SelectedNode;
                _restoringSelection = false;
                UpdateMoveCommandStates();
                return;
            }
        }

        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var chapter = Chapters.FirstOrDefault(item => item.Id == chapterId)
                ?? new ChapterListItemViewModel(
                    await _chapterService.GetAsync(project.Id, chapterId).ConfigureAwait(true));
            SelectedChapter = Chapters.FirstOrDefault(item => item.Id == chapterId) ?? chapter;
            ChapterTitle = chapter.Title;
            var content = await _chapterService.LoadContentAsync(project.Id, chapterId).ConfigureAwait(true);
            SetEditorContent(content, markClean: true);
            _loadedChapterId = chapterId;
            _contextSceneId = contextSceneId;
            await LoadLinkedScenesAsync(project.Id, chapterId).ConfigureAwait(true);
            SelectLinkedScene(contextSceneId);
            StatusMessage = string.Empty;
            SyncSaveState();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void SelectLinkedScene(Guid? sceneId)
    {
        SelectedLinkedScene = sceneId is null
            ? LinkedScenes.FirstOrDefault()
            : LinkedScenes.FirstOrDefault(item => item.Id == sceneId) ?? LinkedScenes.FirstOrDefault();
    }

    private async Task ReloadHierarchyAsync(
        HierarchyNodeKind? selectKind,
        Guid? selectId,
        bool preserveExpansion,
        CancellationToken cancellationToken = default)
    {
        var project = _projectService.ActiveProject
            ?? throw new InvalidOperationException("Open a project before loading the hierarchy.");

        var expanded = preserveExpansion
            ? ManuscriptHierarchyInteractions.CaptureExpanded(
                Flatten(HierarchyRoots).Select(node => (node.Kind, node.Id, node.IsExpanded)))
            : null;
        var compileFlags = Flatten(HierarchyRoots)
            .Where(node => node.Kind == HierarchyNodeKind.Chapter)
            .ToDictionary(node => node.Id, node => node.IsSelectedForCompile);

        var tree = await _hierarchy.GetHierarchyAsync(project.Id, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();

        HierarchyRoots.Clear();
        Chapters.Clear();

        foreach (var book in tree.Books)
        {
            var bookNode = new HierarchyNodeViewModel(
                HierarchyNodeKind.Book,
                book.Id,
                book.Title,
                book.SequenceNumber,
                parentId: null)
            {
                IsExpanded = ManuscriptHierarchyInteractions.ShouldExpand(
                    HierarchyNodeKind.Book, book.Id, expanded, defaultExpanded: true),
            };

            foreach (var part in book.Parts)
            {
                var partNode = new HierarchyNodeViewModel(
                    HierarchyNodeKind.Part,
                    part.Id,
                    part.Title,
                    part.SequenceNumber,
                    parentId: book.Id)
                {
                    IsExpanded = ManuscriptHierarchyInteractions.ShouldExpand(
                        HierarchyNodeKind.Part, part.Id, expanded, defaultExpanded: true),
                };

                foreach (var chapter in part.Chapters)
                {
                    var chapterNode = new HierarchyNodeViewModel(
                        HierarchyNodeKind.Chapter,
                        chapter.Id,
                        chapter.Title,
                        chapter.SequenceNumber,
                        parentId: part.Id,
                        editorChapterId: chapter.Id,
                        relativeMarkdownPath: chapter.RelativeMarkdownPath,
                        wordCount: 0)
                    {
                        IsExpanded = ManuscriptHierarchyInteractions.ShouldExpand(
                            HierarchyNodeKind.Chapter, chapter.Id, expanded, defaultExpanded: true),
                        IsSelectedForCompile = !compileFlags.TryGetValue(chapter.Id, out var flag) || flag,
                    };

                    var chapterListItem = new ChapterListItemViewModel(
                        new Core.Domain.Manuscript.Chapter
                        {
                            Id = chapter.Id,
                            ProjectId = chapter.ProjectId,
                            PartId = chapter.PartId,
                            SequenceNumber = chapter.SequenceNumber,
                            Title = chapter.Title,
                            RelativeMarkdownPath = chapter.RelativeMarkdownPath,
                        })
                    {
                        IsSelectedForCompile = chapterNode.IsSelectedForCompile,
                    };
                    Chapters.Add(chapterListItem);

                    foreach (var scene in chapter.Scenes)
                    {
                        chapterNode.Children.Add(
                            new HierarchyNodeViewModel(
                                HierarchyNodeKind.Scene,
                                scene.Id,
                                scene.Title,
                                scene.SequenceNumber,
                                parentId: chapter.Id,
                                editorChapterId: chapter.Id,
                                scene: scene)
                            {
                                IsExpanded = false,
                            });
                    }

                    partNode.Children.Add(chapterNode);
                }

                bookNode.Children.Add(partNode);
            }

            HierarchyRoots.Add(bookNode);
        }

        if (tree.UngroupedChapters.Count > 0
            || selectKind == HierarchyNodeKind.UngroupedChaptersGroup)
        {
            var ungrouped = new HierarchyNodeViewModel(
                HierarchyNodeKind.UngroupedChaptersGroup,
                ManuscriptHierarchyInteractions.UngroupedChaptersGroupId,
                "Ungrouped chapters",
                0,
                parentId: null)
            {
                IsExpanded = ManuscriptHierarchyInteractions.ShouldExpand(
                    HierarchyNodeKind.UngroupedChaptersGroup,
                    ManuscriptHierarchyInteractions.UngroupedChaptersGroupId,
                    expanded,
                    defaultExpanded: true),
            };
            foreach (var chapter in tree.UngroupedChapters)
            {
                var chapterNode = new HierarchyNodeViewModel(
                    HierarchyNodeKind.Chapter,
                    chapter.Id,
                    chapter.Title,
                    chapter.SequenceNumber,
                    parentId: ManuscriptHierarchyInteractions.UngroupedChaptersGroupId,
                    editorChapterId: chapter.Id,
                    relativeMarkdownPath: chapter.RelativeMarkdownPath)
                {
                    IsExpanded = ManuscriptHierarchyInteractions.ShouldExpand(
                        HierarchyNodeKind.Chapter, chapter.Id, expanded, defaultExpanded: true),
                    IsSelectedForCompile = !compileFlags.TryGetValue(chapter.Id, out var flag) || flag,
                };
                if (Chapters.All(item => item.Id != chapter.Id))
                {
                    Chapters.Add(new ChapterListItemViewModel(
                        new Core.Domain.Manuscript.Chapter
                        {
                            Id = chapter.Id,
                            ProjectId = chapter.ProjectId,
                            PartId = chapter.PartId,
                            SequenceNumber = chapter.SequenceNumber,
                            Title = chapter.Title,
                            RelativeMarkdownPath = chapter.RelativeMarkdownPath,
                        })
                    {
                        IsSelectedForCompile = chapterNode.IsSelectedForCompile,
                    });
                }

                foreach (var scene in chapter.Scenes)
                {
                    chapterNode.Children.Add(
                        new HierarchyNodeViewModel(
                            HierarchyNodeKind.Scene,
                            scene.Id,
                            scene.Title,
                            scene.SequenceNumber,
                            parentId: chapter.Id,
                            editorChapterId: chapter.Id,
                            scene: scene));
                }

                ungrouped.Children.Add(chapterNode);
            }

            HierarchyRoots.Add(ungrouped);
        }

        if (tree.UnassignedScenes.Count > 0
            || selectKind == HierarchyNodeKind.UnassignedGroup
            || selectKind == HierarchyNodeKind.Scene)
        {
            var unassigned = new HierarchyNodeViewModel(
                HierarchyNodeKind.UnassignedGroup,
                ManuscriptHierarchyInteractions.UnassignedGroupId,
                "Unassigned scenes",
                0,
                parentId: null)
            {
                IsExpanded = ManuscriptHierarchyInteractions.ShouldExpand(
                    HierarchyNodeKind.UnassignedGroup,
                    ManuscriptHierarchyInteractions.UnassignedGroupId,
                    expanded,
                    defaultExpanded: true),
            };
            foreach (var scene in tree.UnassignedScenes)
            {
                unassigned.Children.Add(
                    new HierarchyNodeViewModel(
                        HierarchyNodeKind.Scene,
                        scene.Id,
                        scene.Title,
                        scene.SequenceNumber,
                        parentId: ManuscriptHierarchyInteractions.UnassignedGroupId,
                        editorChapterId: null,
                        scene: scene));
            }

            HierarchyRoots.Add(unassigned);
        }

        var chapterRecords = await _chapterService.GetAllAsync(project.Id, cancellationToken)
            .ConfigureAwait(true);
        foreach (var record in chapterRecords)
        {
            var node = FindNode(HierarchyNodeKind.Chapter, record.Id);
            if (node is not null)
            {
                node.WordCount = record.WordCount;
            }

            var listItem = Chapters.FirstOrDefault(item => item.Id == record.Id);
            listItem?.Apply(record);
        }

        ApplyHierarchyFilter();

        _restoringSelection = true;
        SelectedNode = selectKind is { } kind && selectId is { } id
            ? FindNode(kind, id) ?? FirstChapterNode()
            : SelectedNode is not null
                ? FindNode(SelectedNode.Kind, SelectedNode.Id) ?? FirstChapterNode()
                : FirstChapterNode();
        _restoringSelection = false;

        if (SelectedNode is not null)
        {
            await OnHierarchySelectionAsync(SelectedNode).ConfigureAwait(true);
        }
        else
        {
            SelectedChapter = null;
            _loadedChapterId = null;
            SetEditorContent(string.Empty, markClean: true);
            ClearContext();
        }

        UpdateMoveCommandStates();
    }

    private void ApplyHierarchyFilter()
    {
        foreach (var root in HierarchyRoots)
        {
            ApplyFilterRecursive(root);
        }
    }

    private bool ApplyFilterRecursive(HierarchyNodeViewModel node)
    {
        var childVisible = false;
        foreach (var child in node.Children)
        {
            childVisible |= ApplyFilterRecursive(child);
        }

        var selfMatch = ManuscriptHierarchyInteractions.MatchesFilter(HierarchyFilter, node.Title, node.DisplayName);
        node.IsVisible = selfMatch || childVisible || string.IsNullOrWhiteSpace(HierarchyFilter);
        if (node.IsVisible && childVisible && !string.IsNullOrWhiteSpace(HierarchyFilter))
        {
            node.IsExpanded = true;
        }

        return node.IsVisible;
    }

    private void SyncCompileFlagsFromTree()
    {
        foreach (var chapterNode in Flatten(HierarchyRoots).Where(node => node.Kind == HierarchyNodeKind.Chapter))
        {
            var item = Chapters.FirstOrDefault(chapter => chapter.Id == chapterNode.Id);
            if (item is not null)
            {
                item.IsSelectedForCompile = chapterNode.IsSelectedForCompile;
            }
        }
    }

    private void UpdateContextPanel(HierarchyNodeViewModel node)
    {
        ContextTitle = node.DisplayName;
        ContextStatusText = node.Kind.ToString();
        if (node.Kind == HierarchyNodeKind.Scene && node.Scene is { } scene)
        {
            ContextSummary = $"{scene.Status} · POV linked · Seq {scene.SequenceNumber}";
            ContextDetail =
                $"Goal: {scene.Goal}\nOpposition: {scene.Opposition}\nStakes: {scene.Stakes}\n" +
                $"Location: {scene.Location}\nTime: {scene.Time}\nMain event: {scene.MainEvent}";
            return;
        }

        if (node.Kind == HierarchyNodeKind.Chapter)
        {
            ContextSummary = $"{node.WordCount} words · {node.Children.Count} scene(s)";
            ContextDetail = $"Path: {node.RelativeMarkdownPath}\nSelect a scene for outline metadata, or open Story Data.";
            return;
        }

        ContextSummary = $"{node.Kind} selected";
        ContextDetail = "Use Add / Rename / Delete and Move commands to manage the hierarchy. Chapter prose stays in Markdown files.";
    }

    private void ClearContext()
    {
        ContextTitle = "Context";
        ContextSummary = "Select a chapter or scene to see metadata.";
        ContextDetail = string.Empty;
        ContextStatusText = string.Empty;
    }

    private void UpdateMoveCommandStates()
    {
        var node = SelectedNode;
        if (node is null
            || node.Kind is HierarchyNodeKind.UnassignedGroup
                or HierarchyNodeKind.UngroupedChaptersGroup)
        {
            CanMoveSelectedUp = false;
            CanMoveSelectedDown = false;
            CanMoveSelectedTo = false;
            return;
        }

        var siblings = node.Kind == HierarchyNodeKind.Book
            ? HierarchyRoots.Where(item => item.Kind == HierarchyNodeKind.Book).ToList()
            : FindParent(node)?.Children.Where(item => item.Kind == node.Kind).ToList()
              ?? [];
        var index = siblings.FindIndex(item => item.Id == node.Id);
        CanMoveSelectedUp = index > 0;
        CanMoveSelectedDown = index >= 0 && index < siblings.Count - 1;
        CanMoveSelectedTo = node.Kind is HierarchyNodeKind.Chapter or HierarchyNodeKind.Scene;
    }

    private HierarchyNodeViewModel? FirstChapterNode()
        => Flatten(HierarchyRoots).FirstOrDefault(node => node.Kind == HierarchyNodeKind.Chapter);

    private HierarchyNodeViewModel? FindNode(HierarchyNodeKind kind, Guid id)
        => Flatten(HierarchyRoots).FirstOrDefault(node => node.Kind == kind && node.Id == id);

    private HierarchyNodeViewModel? FindParent(HierarchyNodeViewModel node)
    {
        if (node.ParentId is null)
        {
            return null;
        }

        return Flatten(HierarchyRoots).FirstOrDefault(item => item.Id == node.ParentId);
    }

    private Guid? FindAncestorBookId(HierarchyNodeViewModel node)
    {
        var current = node;
        while (current is not null)
        {
            if (current.Kind == HierarchyNodeKind.Book)
            {
                return current.Id;
            }

            current = FindParent(current);
        }

        return null;
    }

    private static IEnumerable<HierarchyNodeViewModel> Flatten(IEnumerable<HierarchyNodeViewModel> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Flatten(root.Children))
            {
                yield return child;
            }
        }
    }

    private void OnAutosaveCompleted(object? sender, EditorSaveCompletedEventArgs e)
    {
        if (e.EntityType != RecoveryEntityType.ManuscriptChapter
            || e.EntityId != _loadedChapterId)
        {
            return;
        }

        void Apply()
        {
            if (!e.Succeeded)
            {
                StatusMessage = e.ErrorMessage ?? "Save failed";
                SyncSaveState();
                return;
            }

            if (_loadedChapterId == e.EntityId
                && string.Equals(MarkdownText, e.SavedText, StringComparison.Ordinal))
            {
                _savedContent = e.SavedText;
                IsDirty = false;
                StatusMessage = "Saved";
            }

            SyncSaveState();
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            dispatcher.Invoke(Apply);
        }
    }

    private void SyncSaveState()
    {
        SaveStateDisplay = SaveStateAccessibility.FormatDisplay(_saveState.State, _saveState.Message);
        SaveStateAccessibleName = SaveStateAccessibility.FormatAccessibleName(_saveState.State, _saveState.Message);
    }

    private void OnStoryChanged(object? sender, StoryChangeEventArgs e)
    {
        if (_projectService.ActiveProject?.Id != e.ProjectId)
        {
            return;
        }

        if (e.Kind is not (StoryChangeKind.SceneUpserted
            or StoryChangeKind.SceneDeleted
            or StoryChangeKind.HierarchyChanged))
        {
            return;
        }

        void Apply() => _ = SoftRefreshScenesAsync();

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

    private async Task SoftRefreshScenesAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            await ReloadHierarchyAsync(
                    selectKind: SelectedNode?.Kind,
                    selectId: SelectedNode?.Id,
                    preserveExpansion: true)
                .ConfigureAwait(true);
            if (_loadedChapterId is { } chapterId)
            {
                var keep = _contextSceneId ?? SelectedLinkedScene?.Id;
                await LoadLinkedScenesAsync(project.Id, chapterId).ConfigureAwait(true);
                SelectLinkedScene(keep);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task LoadLinkedScenesAsync(Guid projectId, Guid chapterId)
    {
        LinkedScenes.Clear();
        SelectedLinkedScene = null;
        var scenes = await _storyData.GetScenesAsync(projectId).ConfigureAwait(true);
        foreach (var scene in scenes
                     .Where(item => item.ChapterId == chapterId)
                     .OrderBy(item => item.SequenceNumber)
                     .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
        {
            LinkedScenes.Add(new LinkedSceneItemViewModel(scene));
        }

        ViewpointOptions.Clear();
        ViewpointOptions.Add(CharacterOptionViewModel.Unassigned());
        foreach (var character in await _storyData.GetCharactersAsync(projectId).ConfigureAwait(true))
        {
            ViewpointOptions.Add(CharacterOptionViewModel.From(character));
        }

        RebuildSceneOutline();
        RefreshAssociationCounts(MarkdownText);
    }

    private void RebuildSceneOutline()
    {
        var keep = SelectedOutlineItem?.SceneId ?? SelectedLinkedScene?.Id;
        _restoringSelection = true;
        SceneOutline.Clear();
        var doc = SceneProseAssociation.Parse(MarkdownText);
        foreach (var linked in LinkedScenes)
        {
            var hasProse = doc.Spans.Any(span => span.SceneId == linked.Id);
            SceneOutline.Add(new SceneOutlineItemViewModel(
                linked.Id,
                linked.Source.SequenceNumber,
                linked.Source.Title,
                hasProse));
        }

        SelectedOutlineItem = keep is { } id
            ? SceneOutline.FirstOrDefault(item => item.SceneId == id)
            : SceneOutline.FirstOrDefault();
        _restoringSelection = false;
    }

    private void RefreshAssociationCounts(string markdown)
    {
        var viewpoint = LinkedScenes.ToDictionary(
            item => item.Id,
            item => item.Source.ViewpointCharacterId);
        var counts = SceneProseAssociation.CalculateWordCounts(markdown, viewpoint);
        WordCount = counts.ChapterWordCount;
        var sceneParts = LinkedScenes.Select(item =>
        {
            counts.SceneWordCounts.TryGetValue(item.Id, out var words);
            return $"{item.Source.Title}: {words}";
        });
        var viewpointParts = counts.ViewpointWordCounts.Select(pair =>
        {
            var label = pair.Key == Guid.Empty
                ? "No viewpoint"
                : $"POV {pair.Key.ToString("N")[..8]}";
            return $"{label}: {pair.Value}";
        });
        AssociationCountsDisplay =
            $"Chapter: {counts.ChapterWordCount} words"
            + (LinkedScenes.Count == 0
                ? string.Empty
                : " | Scenes: " + string.Join(", ", sceneParts))
            + (counts.ViewpointWordCounts.Count == 0
                ? string.Empty
                : " | Viewpoints: " + string.Join(", ", viewpointParts));
    }

    private void SetEditorContent(string content, bool markClean)
    {
        _suppressDirty = true;
        MarkdownText = content;
        _suppressDirty = false;
        _savedContent = content;
        IsDirty = !markClean;
        PreviewHtml = MarkdownPreviewRenderer.ToHtmlDocument(content);
        RefreshBracketNotes(content);
        RefreshAssociationCounts(content);
        _editorSessionVersion++;
        OnPropertyChanged(nameof(EditorSessionVersion));
    }

    private void RefreshBracketNotes(string markdown)
    {
        BracketNotes.Clear();
        foreach (var note in ManuscriptTextAnalytics.FindBracketNotes(markdown))
        {
            BracketNotes.Add(new BracketNoteItemViewModel(note));
        }
    }

    private bool ConfirmDiscard()
        => _dialogs.Confirm(
            "You have unsaved chapter changes. Discard them?",
            "Unsaved Changes");
}

public sealed class HierarchyDropRequest
{
    public required HierarchyNodeKind SourceKind { get; init; }

    public required Guid SourceId { get; init; }

    public required HierarchyNodeKind TargetKind { get; init; }

    public required Guid TargetId { get; init; }
}

public partial class ChapterListItemViewModel : ObservableObject
{
    public ChapterListItemViewModel(Core.Domain.Manuscript.Chapter chapter)
    {
        Id = chapter.Id;
        SequenceNumber = chapter.SequenceNumber;
        Title = chapter.Title;
        WordCount = chapter.WordCount;
        RelativeMarkdownPath = chapter.RelativeMarkdownPath;
        PartId = chapter.PartId;
    }

    public Guid Id { get; }

    public Guid? PartId { get; private set; }

    public string RelativeMarkdownPath { get; private set; }

    [ObservableProperty]
    private int _sequenceNumber;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private int _wordCount;

    [ObservableProperty]
    private bool _isSelectedForCompile = true;

    public string DisplayName => $"{SequenceNumber}. {Title}";

    public void Apply(Core.Domain.Manuscript.Chapter chapter)
    {
        SequenceNumber = chapter.SequenceNumber;
        Title = chapter.Title;
        WordCount = chapter.WordCount;
        RelativeMarkdownPath = chapter.RelativeMarkdownPath;
        PartId = chapter.PartId;
        OnPropertyChanged(nameof(DisplayName));
    }
}

public sealed class BracketNoteItemViewModel(BracketNote note)
{
    public string Display => $"Line {note.LineNumber}: {note.Marker} — {note.LineText}";

    public int CharIndex { get; } = note.CharIndex;

    public int MarkerLength { get; } = note.MarkerLength;
}

public sealed class LinkedSceneItemViewModel(Core.Domain.Story.Scene source)
{
    public Core.Domain.Story.Scene Source { get; } = source;

    public Guid Id => Source.Id;

    public string DisplayName => $"{Source.SequenceNumber}. {Source.Title} ({Source.Status})";
}

public sealed class SceneOutlineItemViewModel(Guid sceneId, int sequenceNumber, string title, bool hasProseRegion)
{
    public Guid SceneId { get; } = sceneId;

    public string DisplayName =>
        $"{sequenceNumber}. {title}" + (hasProseRegion ? string.Empty : " (outline only)");
}

public sealed class SceneAssociationRequest
{
    public required Guid SceneId { get; init; }
}

public sealed class EditorCaretRequest
{
    public required int CaretIndex { get; init; }

    public int SelectionLength { get; init; }
}

public partial class SceneDraftEditorViewModel : ObservableObject
{
    private Scene _source = null!;

    public Guid Id => _source.Id;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private Guid? _viewpointCharacterId;
    [ObservableProperty] private string _location = string.Empty;
    [ObservableProperty] private string _time = string.Empty;
    [ObservableProperty] private string _goal = string.Empty;
    [ObservableProperty] private string _opposition = string.Empty;
    [ObservableProperty] private string _stakes = string.Empty;
    [ObservableProperty] private string _outcome = string.Empty;
    [ObservableProperty] private string _consequence = string.Empty;
    [ObservableProperty] private string _setupObligations = string.Empty;
    [ObservableProperty] private string _payoffObligations = string.Empty;
    [ObservableProperty] private SceneDraftStatus _status;

    public static SceneDraftEditorViewModel From(Scene scene)
    {
        var editor = new SceneDraftEditorViewModel { _source = scene };
        editor.Title = scene.Title;
        editor.ViewpointCharacterId = scene.ViewpointCharacterId;
        editor.Location = scene.Location;
        editor.Time = scene.Time;
        editor.Goal = scene.Goal;
        editor.Opposition = scene.Opposition;
        editor.Stakes = scene.Stakes;
        editor.Outcome = scene.Outcome;
        editor.Consequence = scene.Consequence;
        editor.SetupObligations = scene.SetupObligations;
        editor.PayoffObligations = scene.PayoffObligations;
        editor.Status = scene.Status;
        return editor;
    }

    public Scene ToModel() => new()
    {
        Id = _source.Id,
        ProjectId = _source.ProjectId,
        ChapterId = _source.ChapterId,
        SequenceNumber = _source.SequenceNumber,
        NextSceneId = _source.NextSceneId,
        Title = Title.Trim(),
        ViewpointCharacterId = ViewpointCharacterId,
        Location = Location.Trim(),
        Time = Time.Trim(),
        Goal = Goal.Trim(),
        Opposition = Opposition.Trim(),
        Stakes = Stakes.Trim(),
        MainEvent = _source.MainEvent,
        Revelation = _source.Revelation,
        EmotionalTurn = _source.EmotionalTurn,
        Choice = _source.Choice,
        Outcome = Outcome.Trim(),
        Consequence = Consequence.Trim(),
        SetupObligations = SetupObligations.Trim(),
        PayoffObligations = PayoffObligations.Trim(),
        Status = Status,
        WordCount = _source.WordCount,
        LastEditedUtc = _source.LastEditedUtc,
    };
}
