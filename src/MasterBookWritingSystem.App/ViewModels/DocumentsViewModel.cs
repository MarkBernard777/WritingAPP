using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Documents;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class DocumentsViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IDocumentService _documentService;

    public DocumentsViewModel(IProjectService projectService, IDocumentService documentService)
    {
        _projectService = projectService;
        _documentService = documentService;
        _ = RefreshAsync();
    }

    public ObservableCollection<DocumentListItemViewModel> Documents { get; } = [];

    public ObservableCollection<DocumentFieldItemViewModel> Fields { get; } = [];

    [ObservableProperty]
    private bool _hasProject;

    [ObservableProperty]
    private DocumentListItemViewModel? _selectedDocument;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    partial void OnSelectedDocumentChanged(DocumentListItemViewModel? value)
        => _ = LoadSelectedAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var project = _projectService.ActiveProject;
        HasProject = project is not null;
        Documents.Clear();
        Fields.Clear();

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

    private async Task LoadSelectedAsync()
    {
        Fields.Clear();
        Notes = string.Empty;
        ValidationMessage = string.Empty;

        var project = _projectService.ActiveProject;
        var selected = SelectedDocument;
        if (project is null || selected is null)
        {
            return;
        }

        var document = await _documentService.GetAsync(project.Id, selected.DocumentType).ConfigureAwait(true);
        Notes = document.Notes;
        foreach (var field in document.Fields)
        {
            Fields.Add(new DocumentFieldItemViewModel(field));
        }

        await RefreshValidationAsync().ConfigureAwait(true);
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
