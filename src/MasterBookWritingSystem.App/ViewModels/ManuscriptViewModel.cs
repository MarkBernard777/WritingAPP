using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Infrastructure.Manuscript;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class ManuscriptViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IChapterService _chapterService;
    private readonly IProjectDialogService _dialogs;
    private string _savedContent = string.Empty;
    private bool _suppressDirty;
    private bool _restoringSelection;
    private Guid? _loadedChapterId;

    public ManuscriptViewModel(
        IProjectService projectService,
        IChapterService chapterService,
        IProjectDialogService dialogs)
    {
        _projectService = projectService;
        _chapterService = chapterService;
        _dialogs = dialogs;
        _ = RefreshAsync();
    }

    public ObservableCollection<ChapterListItemViewModel> Chapters { get; } = [];

    public ObservableCollection<BracketNoteItemViewModel> BracketNotes { get; } = [];

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
            var saved = await _chapterService.SaveContentAsync(project.Id, selected.Id, MarkdownText)
                .ConfigureAwait(true);
            selected.Apply(saved);
            SetEditorContent(MarkdownText, markClean: true);
            StatusMessage = $"Saved ({saved.WordCount} words).";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
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

        if (IsDirty && !ConfirmDiscard())
        {
            _restoringSelection = true;
            SelectedChapter = Chapters.FirstOrDefault(item => item.Id == _loadedChapterId);
            _restoringSelection = false;
            return;
        }

        var project = _projectService.ActiveProject;
        if (project is null || value is null)
        {
            ChapterTitle = string.Empty;
            _loadedChapterId = null;
            SetEditorContent(string.Empty, markClean: true);
            return;
        }

        try
        {
            ChapterTitle = value.Title;
            var content = await _chapterService.LoadContentAsync(project.Id, value.Id).ConfigureAwait(true);
            SetEditorContent(content, markClean: true);
            _loadedChapterId = value.Id;
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
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
