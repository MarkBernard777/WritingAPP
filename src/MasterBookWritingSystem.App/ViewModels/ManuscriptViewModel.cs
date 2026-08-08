using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Navigation;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.Manuscript;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class ManuscriptViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IChapterService _chapterService;
    private readonly IStoryDataService _storyData;
    private readonly INavigationService _navigation;
    private readonly IProjectDialogService _dialogs;
    private readonly IEditorAutosaveService _autosave;
    private readonly ISaveStateService _saveState;
    private string _savedContent = string.Empty;
    private bool _suppressDirty;
    private bool _restoringSelection;
    private Guid? _loadedChapterId;
    private int _editorSessionVersion;

    public ManuscriptViewModel(
        IProjectService projectService,
        IChapterService chapterService,
        IStoryDataService storyData,
        INavigationService navigation,
        IProjectDialogService dialogs,
        IEditorAutosaveService autosave,
        ISaveStateService saveState)
    {
        _projectService = projectService;
        _chapterService = chapterService;
        _storyData = storyData;
        _navigation = navigation;
        _dialogs = dialogs;
        _autosave = autosave;
        _saveState = saveState;
        _autosave.SaveCompleted += OnAutosaveCompleted;
        _saveState.Changed += (_, _) => SyncSaveState();
        SyncSaveState();
        _ = RefreshAsync();
    }

    public ObservableCollection<ChapterListItemViewModel> Chapters { get; } = [];

    public ObservableCollection<BracketNoteItemViewModel> BracketNotes { get; } = [];

    public ObservableCollection<LinkedSceneItemViewModel> LinkedScenes { get; } = [];

    [ObservableProperty]
    private bool _hasProject;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

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

    /// <summary>
    /// Increments when chapter/project content is rebased so the prose TextBox can clear its native undo stack.
    /// </summary>
    public int EditorSessionVersion => _editorSessionVersion;

    partial void OnSelectedChapterChanged(ChapterListItemViewModel? value)
    {
        if (_restoringSelection)
        {
            return;
        }

        _ = SwitchChapterAsync(value);
    }

    partial void OnMarkdownTextChanged(string value)
    {
        if (_suppressDirty)
        {
            return;
        }

        IsDirty = !string.Equals(value, _savedContent, StringComparison.Ordinal);
        WordCount = ManuscriptTextAnalytics.CountWords(value);
        PreviewHtml = MarkdownPreviewRenderer.ToHtmlDocument(value);
        RefreshBracketNotes(value);

        var project = _projectService.ActiveProject;
        var chapterId = _loadedChapterId;
        if (IsDirty && project is not null && chapterId is { } id)
        {
            _autosave.ScheduleManuscriptSave(project.Id, id, value);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsDirty && !ConfirmDiscard())
        {
            return;
        }

        var project = _projectService.ActiveProject;
        HasProject = project is not null;
        Chapters.Clear();
        BracketNotes.Clear();
        LinkedScenes.Clear();
        SelectedLinkedScene = null;
        _loadedChapterId = null;
        SetEditorContent(string.Empty, markClean: true);
        ChapterTitle = string.Empty;

        if (project is null)
        {
            SelectedChapter = null;
            StatusMessage = "Open or create a project to edit the manuscript.";
            return;
        }

        try
        {
            foreach (var chapter in await _chapterService.GetAllAsync(project.Id).ConfigureAwait(true))
            {
                Chapters.Add(new ChapterListItemViewModel(chapter));
            }

            SelectedChapter = Chapters.FirstOrDefault();
            StatusMessage = string.Empty;
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
            var created = await _chapterService.CreateAsync(project.Id, $"Chapter {Chapters.Count + 1}")
                .ConfigureAwait(true);
            await ReloadListAsync(selectId: created.Id).ConfigureAwait(true);
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
        var selected = SelectedChapter;
        if (project is null || selected is null)
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
            var renamed = await _chapterService.RenameAsync(project.Id, selected.Id, ChapterTitle)
                .ConfigureAwait(true);
            selected.Apply(renamed);
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
        var selected = SelectedChapter;
        if (project is null || selected is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Delete chapter '{selected.Title}'? This removes the markdown file.", "Delete Chapter"))
        {
            return;
        }

        try
        {
            await _chapterService.DeleteAsync(project.Id, selected.Id).ConfigureAwait(true);
            IsDirty = false;
            _loadedChapterId = null;
            await ReloadListAsync(selectId: null).ConfigureAwait(true);
            StatusMessage = "Chapter deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task MoveChapterUpAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedChapter;
        if (project is null || selected is null)
        {
            return;
        }

        if (IsDirty && !ConfirmDiscard())
        {
            return;
        }

        try
        {
            await _chapterService.MoveUpAsync(project.Id, selected.Id).ConfigureAwait(true);
            await ReloadListAsync(selectId: selected.Id).ConfigureAwait(true);
            StatusMessage = "Chapter moved up.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task MoveChapterDownAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedChapter;
        if (project is null || selected is null)
        {
            return;
        }

        if (IsDirty && !ConfirmDiscard())
        {
            return;
        }

        try
        {
            await _chapterService.MoveDownAsync(project.Id, selected.Id).ConfigureAwait(true);
            await ReloadListAsync(selectId: selected.Id).ConfigureAwait(true);
            StatusMessage = "Chapter moved down.";
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
        var selected = SelectedChapter;
        if (project is null || selected is null)
        {
            return;
        }

        try
        {
            _saveState.Report(SaveState.Saving, "Saving…");
            var saved = await _chapterService.SaveContentAsync(project.Id, selected.Id, MarkdownText)
                .ConfigureAwait(true);
            selected.Apply(saved);
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

    private async Task SwitchChapterAsync(ChapterListItemViewModel? value)
    {
        if (value?.Id == _loadedChapterId)
        {
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
                SelectedChapter = Chapters.FirstOrDefault(item => item.Id == _loadedChapterId);
                _restoringSelection = false;
                return;
            }
        }

        var project = _projectService.ActiveProject;
        if (project is null || value is null)
        {
            ChapterTitle = string.Empty;
            _loadedChapterId = null;
            LinkedScenes.Clear();
            SelectedLinkedScene = null;
            SetEditorContent(string.Empty, markClean: true);
            return;
        }

        try
        {
            ChapterTitle = value.Title;
            var content = await _chapterService.LoadContentAsync(project.Id, value.Id).ConfigureAwait(true);
            SetEditorContent(content, markClean: true);
            _loadedChapterId = value.Id;
            await LoadLinkedScenesAsync(project.Id, value.Id).ConfigureAwait(true);
            StatusMessage = string.Empty;
            SyncSaveState();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
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
        => SaveStateDisplay = _saveState.Message;

    [RelayCommand]
    private void OpenLinkedSceneInStoryData()
    {
        var scene = SelectedLinkedScene;
        if (scene is null)
        {
            StatusMessage = "Select a linked scene first.";
            return;
        }

        _navigation.NavigateToStoryDataScene(scene.Id);
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

        SelectedLinkedScene = LinkedScenes.FirstOrDefault();
    }

    private async Task ReloadListAsync(Guid? selectId)
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        var selectedForCompile = Chapters
            .Where(item => item.IsSelectedForCompile)
            .Select(item => item.Id)
            .ToHashSet();

        Chapters.Clear();
        foreach (var chapter in await _chapterService.GetAllAsync(project.Id).ConfigureAwait(true))
        {
            var item = new ChapterListItemViewModel(chapter)
            {
                IsSelectedForCompile = selectedForCompile.Count == 0 || selectedForCompile.Contains(chapter.Id),
            };
            Chapters.Add(item);
        }

        _loadedChapterId = null;
        SelectedChapter = selectId is null
            ? Chapters.FirstOrDefault()
            : Chapters.FirstOrDefault(item => item.Id == selectId) ?? Chapters.FirstOrDefault();
    }

    private void SetEditorContent(string content, bool markClean)
    {
        _suppressDirty = true;
        MarkdownText = content;
        _suppressDirty = false;
        _savedContent = content;
        IsDirty = !markClean;
        WordCount = ManuscriptTextAnalytics.CountWords(content);
        PreviewHtml = MarkdownPreviewRenderer.ToHtmlDocument(content);
        RefreshBracketNotes(content);
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

public partial class ChapterListItemViewModel : ObservableObject
{
    public ChapterListItemViewModel(Core.Domain.Manuscript.Chapter chapter)
    {
        Id = chapter.Id;
        SequenceNumber = chapter.SequenceNumber;
        Title = chapter.Title;
        WordCount = chapter.WordCount;
        RelativeMarkdownPath = chapter.RelativeMarkdownPath;
    }

    public Guid Id { get; }

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
        OnPropertyChanged(nameof(DisplayName));
    }
}

public sealed class BracketNoteItemViewModel(BracketNote note)
{
    public string Display => $"Line {note.LineNumber}: {note.Marker} — {note.LineText}";
}

public sealed class LinkedSceneItemViewModel(Core.Domain.Story.Scene source)
{
    public Core.Domain.Story.Scene Source { get; } = source;

    public Guid Id => Source.Id;

    public string DisplayName => $"{Source.SequenceNumber}. {Source.Title} ({Source.Status})";
}
