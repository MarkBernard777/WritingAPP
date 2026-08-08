using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Editing;
using MasterBookWritingSystem.App.Navigation;
using MasterBookWritingSystem.Core.Accessibility;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Documents;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Editing;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Core.Story;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class DocumentsViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IDocumentService _documentService;
    private readonly IStoryDataService _storyData;
    private readonly IChapterService _chapters;
    private readonly INavigationService _navigation;
    private readonly IEditorAutosaveService _autosave;
    private readonly ISaveStateService _saveState;
    private readonly IStoryChangeNotifier _changes;
    private readonly StructuredDocumentEditTracker _editTracker = new();
    private readonly Dictionary<Guid, Character> _charactersById = [];
    private bool _suppressAutosave;

    public DocumentsViewModel(
        IProjectService projectService,
        IDocumentService documentService,
        IStoryDataService storyData,
        IChapterService chapters,
        INavigationService navigation,
        IEditorAutosaveService autosave,
        ISaveStateService saveState,
        IStoryChangeNotifier changes)
    {
        _projectService = projectService;
        _documentService = documentService;
        _storyData = storyData;
        _chapters = chapters;
        _navigation = navigation;
        _autosave = autosave;
        _saveState = saveState;
        _changes = changes;
        _saveState.Changed += (_, _) => SyncSaveState();
        _changes.Changed += OnStoryChanged;
        SyncSaveState();
        _ = RefreshAsync();
    }

    public void Detach() => _changes.Changed -= OnStoryChanged;

    public ObservableCollection<DocumentListItemViewModel> Documents { get; } = [];

    public ObservableCollection<DocumentFieldItemViewModel> Fields { get; } = [];

    public ObservableCollection<SceneInventoryItemViewModel> SceneInventory { get; } = [];

    [ObservableProperty]
    private bool _hasProject;

    [ObservableProperty]
    private DocumentListItemViewModel? _selectedDocument;

    [ObservableProperty]
    private SceneInventoryItemViewModel? _selectedInventoryScene;

    [ObservableProperty]
    private bool _isSceneListDocument;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _sceneInventoryExplanation =
        "The Scene List shows the shared scene inventory from Story Data → Scenes. "
        + "Use Open / Edit to change a scene there. Optional document notes below are not the canonical scene list.";

    [ObservableProperty]
    private string _saveStateDisplay = "Clean";

    [ObservableProperty]
    private string _saveStateAccessibleName = "Save status: Clean";

    [ObservableProperty]
    private string _undoTooltip = "Undo";

    [ObservableProperty]
    private string _redoTooltip = "Redo";

    public bool CanUndoEdit => NativeTextUndoRouter.CanUndo() || _editTracker.CanUndo;

    public bool CanRedoEdit => NativeTextUndoRouter.CanRedo() || _editTracker.CanRedo;

    partial void OnSelectedDocumentChanged(DocumentListItemViewModel? value)
        => _ = LoadSelectedAsync();

    partial void OnNotesChanged(string value)
    {
        if (_suppressAutosave)
        {
            return;
        }

        // Autosave tracks the newest draft immediately; model undo commits on notes focus leave.
        ScheduleNotesAutosave(value);
    }

    public void BeginFieldEdit(DocumentFieldItemViewModel field)
    {
        // Baseline is whatever the tracker last committed for this field.
        _ = field;
    }

    public void EndFieldEdit(DocumentFieldItemViewModel field)
    {
        if (_suppressAutosave || field is null)
        {
            return;
        }

        if (_editTracker.SetField(field.Key, field.Value))
        {
            NotifyUndoState();
        }
    }

    public void BeginNotesEdit()
    {
    }

    public void EndNotesEdit()
    {
        if (_suppressAutosave)
        {
            return;
        }

        if (_editTracker.SetNotes(Notes))
        {
            NotifyUndoState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndoEdit))]
    private void Undo()
    {
        if (NativeTextUndoRouter.TryUndo())
        {
            NotifyUndoState();
            return;
        }

        if (!_editTracker.TryUndo(out var application) || application is null)
        {
            NotifyUndoState();
            return;
        }

        ApplyStructuredEdit(application);
        NotifyUndoState();
    }

    [RelayCommand(CanExecute = nameof(CanRedoEdit))]
    private void Redo()
    {
        if (NativeTextUndoRouter.TryRedo())
        {
            NotifyUndoState();
            return;
        }

        if (!_editTracker.TryRedo(out var application) || application is null)
        {
            NotifyUndoState();
            return;
        }

        ApplyStructuredEdit(application);
        NotifyUndoState();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var project = _projectService.ActiveProject;
        HasProject = project is not null;
        Documents.Clear();
        Fields.Clear();
        SceneInventory.Clear();
        _editTracker.Reset([], string.Empty);
        NotifyUndoState();

        if (project is null)
        {
            StatusMessage = "Open or create a project to edit working documents.";
            return;
        }

        try
        {
            var docs = await _documentService.GetAllAsync(project.Id).ConfigureAwait(true);
            foreach (var doc in docs)
            {
                Documents.Add(new DocumentListItemViewModel(doc));
            }

            SelectedDocument = Documents.FirstOrDefault();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveFieldAsync(DocumentFieldItemViewModel? field)
    {
        var project = _projectService.ActiveProject;
        var document = SelectedDocument;
        if (project is null || document is null || field is null)
        {
            return;
        }

        try
        {
            _saveState.Report(SaveState.Saving, "Saving…");
            await _documentService
                .UpdateFieldAsync(project.Id, document.Id, field.Key, field.Value)
                .ConfigureAwait(true);
            var updated = await _documentService.GetAsync(project.Id, document.DocumentType).ConfigureAwait(true);
            document.CompletionPercentage = updated.CompletionPercentage;
            await RefreshValidationAsync().ConfigureAwait(true);
            await _saveState.RefreshRecoveryAvailabilityAsync(project.Id).ConfigureAwait(true);
            if (_saveState.State != SaveState.RecoveryAvailable)
            {
                _saveState.Report(SaveState.Saved, "Saved");
            }

            StatusMessage = $"Saved {field.Label}";
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
    private async Task SaveNotesAsync()
    {
        var project = _projectService.ActiveProject;
        var document = SelectedDocument;
        if (project is null || document is null)
        {
            return;
        }

        try
        {
            _saveState.Report(SaveState.Saving, "Saving…");
            await _documentService.UpdateNotesAsync(project.Id, document.Id, Notes).ConfigureAwait(true);
            await _saveState.RefreshRecoveryAvailabilityAsync(project.Id).ConfigureAwait(true);
            if (_saveState.State != SaveState.RecoveryAvailable)
            {
                _saveState.Report(SaveState.Saved, "Saved");
            }

            StatusMessage = "Notes saved";
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
    private void OpenSelectedSceneInStoryData()
    {
        var scene = SelectedInventoryScene;
        if (scene is null)
        {
            StatusMessage = "Select a scene in the inventory first.";
            return;
        }

        _navigation.NavigateToStoryDataScene(scene.Id);
    }

    private async Task LoadSelectedAsync()
    {
        foreach (var field in Fields)
        {
            field.PropertyChanged -= OnFieldPropertyChanged;
        }

        Fields.Clear();
        SceneInventory.Clear();
        ValidationMessage = string.Empty;
        SelectedInventoryScene = null;

        var project = _projectService.ActiveProject;
        var selected = SelectedDocument;
        if (project is null || selected is null)
        {
            IsSceneListDocument = false;
            _suppressAutosave = true;
            Notes = string.Empty;
            _suppressAutosave = false;
            _editTracker.Reset([], string.Empty);
            NotifyUndoState();
            return;
        }

        IsSceneListDocument = selected.DocumentType == DocumentType.SceneList;
        var document = await _documentService.GetAsync(project.Id, selected.DocumentType).ConfigureAwait(true);
        _suppressAutosave = true;
        Notes = document.Notes;
        _suppressAutosave = false;

        if (IsSceneListDocument)
        {
            await LoadSceneInventoryAsync(project.Id).ConfigureAwait(true);
            ValidationMessage =
                "Detailed scene editing uses the shared scene inventory in Story Data. "
                + "Optional document notes can still be saved below.";
            _editTracker.Reset([], document.Notes);
        }
        else
        {
            foreach (var field in document.Fields)
            {
                var item = new DocumentFieldItemViewModel(field);
                item.PropertyChanged += OnFieldPropertyChanged;
                Fields.Add(item);
            }

            _editTracker.Reset(
                document.Fields.Select(item => new KeyValuePair<string, string>(item.Key, item.Value)),
                document.Notes);
            await RefreshValidationAsync().ConfigureAwait(true);
        }

        NotifyUndoState();
        SyncSaveState();
    }

    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DocumentFieldItemViewModel.Value)
            || sender is not DocumentFieldItemViewModel field
            || _suppressAutosave)
        {
            return;
        }

        // Autosave tracks the newest draft immediately; model undo commits on field focus leave.
        ScheduleFieldAutosave(field.Key, field.Value);
    }

    private void ApplyStructuredEdit(StructuredEditApplication application)
    {
        _suppressAutosave = true;
        try
        {
            if (application.Target == StructuredEditTarget.Notes)
            {
                Notes = application.Value;
            }
            else if (application.FieldKey is { } key)
            {
                var field = Fields.FirstOrDefault(item => item.Key == key);
                if (field is not null)
                {
                    field.Value = application.Value;
                }
            }
        }
        finally
        {
            _suppressAutosave = false;
        }

        if (application.Target == StructuredEditTarget.Notes)
        {
            ScheduleNotesAutosave(application.Value);
        }
        else if (application.FieldKey is { } fieldKey)
        {
            ScheduleFieldAutosave(fieldKey, application.Value);
        }
    }

    private void ScheduleFieldAutosave(string fieldKey, string value)
    {
        var project = _projectService.ActiveProject;
        var document = SelectedDocument;
        if (project is null || document is null)
        {
            return;
        }

        _autosave.ScheduleDocumentFieldSave(project.Id, document.Id, fieldKey, value);
    }

    private void ScheduleNotesAutosave(string value)
    {
        var project = _projectService.ActiveProject;
        var document = SelectedDocument;
        if (project is null || document is null)
        {
            return;
        }

        _autosave.ScheduleDocumentNotesSave(project.Id, document.Id, value);
    }

    private void NotifyUndoState()
    {
        UndoTooltip = _editTracker.CanUndo
            ? "Undo last structured edit (Ctrl+Z)"
            : "Nothing to undo";
        RedoTooltip = _editTracker.CanRedo
            ? "Redo last structured edit (Ctrl+Y)"
            : "Nothing to redo";
        OnPropertyChanged(nameof(CanUndoEdit));
        OnPropertyChanged(nameof(CanRedoEdit));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void SyncSaveState()
    {
        SaveStateDisplay = SaveStateAccessibility.FormatDisplay(_saveState.State, _saveState.Message);
        SaveStateAccessibleName = SaveStateAccessibility.FormatAccessibleName(_saveState.State, _saveState.Message);
    }

    private async Task LoadSceneInventoryAsync(Guid projectId, Guid? preserveSelectedId = null)
    {
        var keepId = preserveSelectedId ?? SelectedInventoryScene?.Id;
        SceneInventory.Clear();
        _charactersById.Clear();
        foreach (var character in await _storyData.GetCharactersAsync(projectId).ConfigureAwait(true))
        {
            _charactersById[character.Id] = character;
        }

        var chapters = await _chapters.GetAllAsync(projectId).ConfigureAwait(true);
        var chapterMap = chapters.ToDictionary(item => item.Id);
        var sequences = chapters.ToDictionary(item => item.Id, item => item.SequenceNumber);
        var scenes = SceneChapterOrdering.OrderByChapterThenSequence(
            await _storyData.GetScenesAsync(projectId).ConfigureAwait(true),
            sequences);

        foreach (var scene in scenes)
        {
            var chapterLabel = scene.ChapterId is { } chapterId && chapterMap.TryGetValue(chapterId, out var chapter)
                ? SceneChapterOrdering.FormatChapterLabel(chapter.SequenceNumber, chapter.Title)
                : SceneChapterOrdering.UnassignedLabel;
            var pov = scene.ViewpointCharacterId is { } characterId
                && _charactersById.TryGetValue(characterId, out var character)
                ? character.Name
                : string.Empty;
            SceneInventory.Add(new SceneInventoryItemViewModel(scene, chapterLabel, pov));
        }

        SelectedInventoryScene = keepId is { } id
            ? SceneInventory.FirstOrDefault(item => item.Id == id) ?? SceneInventory.FirstOrDefault()
            : SceneInventory.FirstOrDefault();
    }

    private void OnStoryChanged(object? sender, StoryChangeEventArgs e)
    {
        if (!IsSceneListDocument || _projectService.ActiveProject?.Id != e.ProjectId)
        {
            return;
        }

        if (e.Kind is not (StoryChangeKind.SceneUpserted
            or StoryChangeKind.SceneDeleted
            or StoryChangeKind.CharacterChanged
            or StoryChangeKind.HierarchyChanged))
        {
            return;
        }

        void Apply() => _ = ReloadSceneInventoryProjectionAsync();

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

    private async Task ReloadSceneInventoryProjectionAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null || !IsSceneListDocument)
        {
            return;
        }

        try
        {
            await LoadSceneInventoryAsync(project.Id, SelectedInventoryScene?.Id).ConfigureAwait(true);
            StatusMessage = "Scene List refreshed from canonical Story Data.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task RefreshValidationAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedDocument;
        if (project is null || selected is null)
        {
            return;
        }

        var validation = await _documentService.ValidateAsync(project.Id, selected.DocumentType).ConfigureAwait(true);
        ValidationMessage = validation.IsComplete
            ? "All required fields are complete."
            : $"{validation.MissingRequiredFieldKeys.Count} required fields remaining.";
    }
}

public partial class DocumentListItemViewModel : ObservableObject
{
    public DocumentListItemViewModel(WorkingDocument document)
    {
        Id = document.Id;
        DocumentType = document.DocumentType;
        Title = $"{(int)document.DocumentType:00}. {document.Title}";
        CompletionPercentage = document.CompletionPercentage;
    }

    public Guid Id { get; }

    public DocumentType DocumentType { get; }

    public string Title { get; }

    [ObservableProperty]
    private int _completionPercentage;
}

public partial class DocumentFieldItemViewModel : ObservableObject
{
    public DocumentFieldItemViewModel(DocumentField field)
    {
        Key = field.Key;
        Label = field.IsRequired ? $"{field.Label} *" : field.Label;
        IsRequired = field.IsRequired;
        Value = field.Value;
    }

    public string Key { get; }

    public string Label { get; }

    public bool IsRequired { get; }

    [ObservableProperty]
    private string _value = string.Empty;
}

public sealed class SceneInventoryItemViewModel(Scene source, string chapterDisplayName, string viewpointDisplayName)
{
    public Scene Source { get; } = source;

    public Guid Id => Source.Id;

    public int SequenceNumber => Source.SequenceNumber;

    public string ChapterDisplayName { get; } = chapterDisplayName;

    public string Title => Source.Title;

    public string ViewpointDisplayName { get; } = viewpointDisplayName;

    public string Location => Source.Location;

    public string StatusDisplay => Source.Status.ToString();
}
