using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Manuscript;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class ManuscriptMaintenanceViewModel : ObservableObject
{
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IManuscriptHierarchyService _hierarchy;
    private readonly IManuscriptSearchReplaceService _search;
    private readonly IManuscriptVersionService _versions;
    private readonly IProjectDialogService _dialogs;
    private CancellationTokenSource? _operationCts;
    private ManuscriptSearchOptions? _lastOptions;

    public ManuscriptMaintenanceViewModel(
        IProjectService projects,
        IChapterService chapters,
        IManuscriptHierarchyService hierarchy,
        IManuscriptSearchReplaceService search,
        IManuscriptVersionService versions,
        IProjectDialogService dialogs)
    {
        _projects = projects;
        _chapters = chapters;
        _hierarchy = hierarchy;
        _search = search;
        _versions = versions;
        _dialogs = dialogs;
        _ = RefreshScopeAsync();
    }

    public ObservableCollection<ManuscriptChapterPickItemViewModel> Chapters { get; } = [];

    public ObservableCollection<BookFilterOption> Books { get; } = [];

    public ObservableCollection<PartFilterOption> Parts { get; } = [];

    public ObservableCollection<SearchHitItemViewModel> Hits { get; } = [];

    public ObservableCollection<ManuscriptVersionInfo> Versions { get; } = [];

    public ObservableCollection<ManuscriptVersionDiffLine> DiffLines { get; } = [];

    public IReadOnlyList<ManuscriptSearchScopeKind> ScopeKinds { get; } =
        Enum.GetValues<ManuscriptSearchScopeKind>();

    [ObservableProperty] private bool _hasProject;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _findText = string.Empty;
    [ObservableProperty] private string _replacementText = string.Empty;
    [ObservableProperty] private bool _caseSensitive;
    [ObservableProperty] private bool _wholeWord;
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private ManuscriptSearchScopeKind _scopeKind = ManuscriptSearchScopeKind.EntireManuscript;
    [ObservableProperty] private BookFilterOption? _selectedBook;
    [ObservableProperty] private PartFilterOption? _selectedPart;
    [ObservableProperty] private string _versionLabel = "Draft checkpoint";
    [ObservableProperty] private string _versionNotes = string.Empty;
    [ObservableProperty] private ManuscriptVersionInfo? _selectedVersion;
    [ObservableProperty] private string _rollbackSummary = "No replace rollback available.";
    [ObservableProperty] private Guid? _rollbackToken;
    [ObservableProperty] private bool _includeAllHits = true;

    [RelayCommand]
    private async Task RefreshScopeAsync()
    {
        var project = _projects.ActiveProject;
        HasProject = project is not null;
        Chapters.Clear();
        Books.Clear();
        Parts.Clear();
        if (project is null)
        {
            StatusMessage = "Open a project to search or version the manuscript.";
            return;
        }

        try
        {
            var tree = await _hierarchy.GetHierarchyAsync(project.Id).ConfigureAwait(true);
            Books.Add(new BookFilterOption(null, "All books"));
            Parts.Add(new PartFilterOption(null, "All parts"));
            foreach (var book in tree.Books)
            {
                Books.Add(new BookFilterOption(book.Id, book.Title));
                foreach (var part in book.Parts)
                {
                    Parts.Add(new PartFilterOption(part.Id, $"{book.Title} / {part.Title}"));
                }
            }

            SelectedBook = Books[0];
            SelectedPart = Parts[0];
            foreach (var chapter in await _chapters.GetAllAsync(project.Id).ConfigureAwait(true))
            {
                Chapters.Add(new ManuscriptChapterPickItemViewModel(chapter) { IsSelected = true });
            }

            await RefreshVersionsAsync().ConfigureAwait(true);
            await RefreshRollbackAsync().ConfigureAwait(true);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task PreviewReplaceAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        await RunBusyAsync("Scanning manuscript…", async (progress, token) =>
        {
            _lastOptions = BuildOptions();
            var preview = await _search.PreviewAsync(project.Id, _lastOptions, token, progress)
                .ConfigureAwait(true);
            Hits.Clear();
            foreach (var hit in preview.Hits)
            {
                Hits.Add(new SearchHitItemViewModel(hit));
            }

            StatusMessage = $"Preview: {Hits.Count} hit(s) in {preview.ChapterCountScanned} chapter(s). Confirm before replace.";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ToggleIncludeAll()
    {
        IncludeAllHits = !IncludeAllHits;
        foreach (var hit in Hits)
        {
            hit.Include = IncludeAllHits;
        }
    }

    [RelayCommand]
    private async Task ApplyReplaceAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null || _lastOptions is null || Hits.Count == 0)
        {
            StatusMessage = "Run preview first.";
            return;
        }

        var included = Hits.Count(item => item.Include);
        if (!_dialogs.Confirm(
                $"Apply {included} replacement(s)? A safety snapshot will be created first.",
                "Confirm manuscript replace"))
        {
            return;
        }

        await RunBusyAsync("Applying replacements…", async (progress, token) =>
        {
            var result = await _search.ApplyAsync(
                    project.Id,
                    _lastOptions,
                    Hits.Select(item => item.ToHit()).ToList(),
                    confirmed: true,
                    token,
                    progress)
                .ConfigureAwait(true);
            RollbackToken = result.RollbackToken;
            RollbackSummary = result.RollbackToken is null
                ? "No replace rollback available."
                : $"Rollback ready: {result.Message}";
            StatusMessage = result.Message;
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RollbackReplaceAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null || RollbackToken is null)
        {
            StatusMessage = "No one-operation rollback is available.";
            return;
        }

        if (!_dialogs.Confirm(
                "Roll back the last successful bulk replace? This restores the pre-replace chapter files.",
                "Rollback replace"))
        {
            return;
        }

        var token = RollbackToken.Value;
        await RunBusyAsync("Rolling back replace…", async (progress, ct) =>
        {
            await _search.RollbackAsync(project.Id, token, confirmed: true, ct, progress)
                .ConfigureAwait(true);
            RollbackToken = null;
            RollbackSummary = "No replace rollback available.";
            StatusMessage = "Last bulk replace was rolled back.";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelOperation() => _operationCts?.Cancel();

    [RelayCommand]
    private async Task CreateVersionAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        await RunBusyAsync("Creating manuscript version…", async (progress, token) =>
        {
            var created = await _versions.CreateAsync(project.Id, VersionLabel, VersionNotes, token, progress)
                .ConfigureAwait(true);
            await RefreshVersionsAsync().ConfigureAwait(true);
            SelectedVersion = Versions.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = $"Created manuscript version '{created.Label}'.";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PreviewVersionDiffAsync()
    {
        var project = _projects.ActiveProject;
        var version = SelectedVersion;
        if (project is null || version is null)
        {
            return;
        }

        await RunBusyAsync("Building readable diff…", async (progress, token) =>
        {
            var diff = await _versions.DiffAgainstCurrentAsync(project.Id, version.Id, token, progress)
                .ConfigureAwait(true);
            DiffLines.Clear();
            foreach (var line in diff.Lines)
            {
                DiffLines.Add(line);
            }

            StatusMessage = $"Diff for '{version.Label}' ready ({DiffLines.Count} line(s)).";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestoreVersionAsync()
    {
        var project = _projects.ActiveProject;
        var version = SelectedVersion;
        if (project is null || version is null)
        {
            return;
        }

        if (!_dialogs.Confirm(
                $"Restore manuscript version '{version.Label}'? A full safety snapshot will be created first. This does not use the Snapshots list UI.",
                "Restore manuscript version"))
        {
            return;
        }

        await RunBusyAsync("Restoring manuscript version…", async (progress, token) =>
        {
            await _versions.RestoreAsync(project.Id, version.Id, confirmed: true, token, progress)
                .ConfigureAwait(true);
            StatusMessage = $"Restored manuscript version '{version.Label}'.";
        }).ConfigureAwait(true);
    }

    private ManuscriptSearchOptions BuildOptions()
        => new()
        {
            FindText = FindText,
            ReplacementText = ReplacementText,
            CaseSensitive = CaseSensitive,
            WholeWord = WholeWord,
            UseRegex = UseRegex,
            ScopeKind = ScopeKind,
            BookId = SelectedBook?.BookId,
            PartId = SelectedPart?.PartId,
            SelectedChapterIds = Chapters.Where(item => item.IsSelected).Select(item => item.Id).ToList(),
        };

    private async Task RefreshVersionsAsync()
    {
        Versions.Clear();
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        foreach (var version in await _versions.ListAsync(project.Id).ConfigureAwait(true))
        {
            Versions.Add(version);
        }
    }

    private async Task RefreshRollbackAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            RollbackToken = null;
            RollbackSummary = "No replace rollback available.";
            return;
        }

        var info = await _search.GetLastRollbackAsync(project.Id).ConfigureAwait(true);
        RollbackToken = info?.Token;
        RollbackSummary = info is null
            ? "No replace rollback available."
            : $"One-operation rollback: {info.Label} ({info.CreatedUtc:u})";
    }

    private async Task RunBusyAsync(
        string progressText,
        Func<IProgress<OperationProgress>, CancellationToken, Task> work)
    {
        if (IsBusy)
        {
            StatusMessage = "Another manuscript maintenance operation is running.";
            return;
        }

        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        var token = _operationCts.Token;
        IsBusy = true;
        ProgressMessage = progressText;
        try
        {
            var progress = new Progress<OperationProgress>(report =>
            {
                ProgressMessage = string.IsNullOrWhiteSpace(report.Message)
                    ? progressText
                    : report.Message;
            });
            // Services use ConfigureAwait(false) for IO; keep collection updates on the UI sync context.
            await work(progress, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            ProgressMessage = string.Empty;
        }
    }
}

public sealed class SearchHitItemViewModel(ManuscriptSearchHit hit) : ObservableObject
{
    private bool _include = hit.Include;

    public Guid ChapterId { get; } = hit.ChapterId;

    public string ChapterTitle { get; } = hit.ChapterTitle;

    public int StartIndex { get; } = hit.StartIndex;

    public int Length { get; } = hit.Length;

    public string MatchedText { get; } = hit.MatchedText;

    public string ProposedReplacement { get; } = hit.ProposedReplacement;

    public string ContextDisplay => $"…{hit.ContextBefore}[{hit.MatchedText}→{hit.ProposedReplacement}]{hit.ContextAfter}…";

    public bool Include
    {
        get => _include;
        set => SetProperty(ref _include, value);
    }

    public ManuscriptSearchHit ToHit() => new()
    {
        ChapterId = ChapterId,
        ChapterTitle = ChapterTitle,
        ChapterSequence = hit.ChapterSequence,
        StartIndex = StartIndex,
        Length = Length,
        MatchedText = MatchedText,
        ContextBefore = hit.ContextBefore,
        ContextAfter = hit.ContextAfter,
        ProposedReplacement = ProposedReplacement,
        Include = Include,
    };
}

public partial class ManuscriptChapterPickItemViewModel : ObservableObject
{
    public ManuscriptChapterPickItemViewModel(Chapter chapter)
    {
        Id = chapter.Id;
        DisplayName = $"{chapter.SequenceNumber}. {chapter.Title}";
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isSelected;
}
