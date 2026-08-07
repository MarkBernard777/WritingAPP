using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Navigation;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Documents;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Story;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class DocumentsViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IDocumentService _documentService;
    private readonly IStoryDataService _storyData;
    private readonly IChapterService _chapters;
    private readonly INavigationService _navigation;
    private readonly Dictionary<Guid, Character> _charactersById = [];

    public DocumentsViewModel(
        IProjectService projectService,
        IDocumentService documentService,
        IStoryDataService storyData,
        IChapterService chapters,
        INavigationService navigation)
    {
        _projectService = projectService;
        _documentService = documentService;
        _storyData = storyData;
        _chapters = chapters;
        _navigation = navigation;
        _ = RefreshAsync();
    }

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

    partial void OnSelectedDocumentChanged(DocumentListItemViewModel? value)
        => _ = LoadSelectedAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var project = _projectService.ActiveProject;
        HasProject = project is not null;
        Documents.Clear();
        Fields.Clear();
        SceneInventory.Clear();

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
            await _documentService
                .UpdateFieldAsync(project.Id, document.Id, field.Key, field.Value)
                .ConfigureAwait(true);
            var updated = await _documentService.GetAsync(project.Id, document.DocumentType).ConfigureAwait(true);
            document.CompletionPercentage = updated.CompletionPercentage;
            await RefreshValidationAsync().ConfigureAwait(true);
            StatusMessage = $"Saved {field.Label}";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
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
            await _documentService.UpdateNotesAsync(project.Id, document.Id, Notes).ConfigureAwait(true);
            StatusMessage = "Notes saved";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
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
        Fields.Clear();
        SceneInventory.Clear();
        Notes = string.Empty;
        ValidationMessage = string.Empty;
        SelectedInventoryScene = null;

        var project = _projectService.ActiveProject;
        var selected = SelectedDocument;
        if (project is null || selected is null)
        {
            IsSceneListDocument = false;
            return;
        }

        IsSceneListDocument = selected.DocumentType == DocumentType.SceneList;
        var document = await _documentService.GetAsync(project.Id, selected.DocumentType).ConfigureAwait(true);
        Notes = document.Notes;

        if (IsSceneListDocument)
        {
            await LoadSceneInventoryAsync(project.Id).ConfigureAwait(true);
            ValidationMessage =
                "Detailed scene editing uses the shared scene inventory in Story Data. "
                + "Optional document notes can still be saved below.";
        }
        else
        {
            foreach (var field in document.Fields)
            {
                Fields.Add(new DocumentFieldItemViewModel(field));
            }

            await RefreshValidationAsync().ConfigureAwait(true);
        }
    }

    private async Task LoadSceneInventoryAsync(Guid projectId)
    {
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

        SelectedInventoryScene = SceneInventory.FirstOrDefault();
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
