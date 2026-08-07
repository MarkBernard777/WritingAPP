using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.Core.Domain.Publishing;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class PublishingViewModel
{
    [RelayCommand]
    private async Task AddFormatAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var created = await _publishing.CreatePublishingFormatAsync(project.Id, new PublishingFormat
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "New format",
                FormatKind = PublishingFormatKind.Ebook,
                PublicationStatus = PublicationStatus.Planned,
            }).ConfigureAwait(true);
            await RefreshFormatsAsync().ConfigureAwait(true);
            SelectedFormat = Formats.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "Format created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveFormatAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = FormatEditor;
        if (project is null || editor is null)
        {
            return;
        }

        try
        {
            var model = editor.ToModel();
            var validation = _publishing.ValidatePublishingFormat(model);
            if (!validation.IsValid)
            {
                StatusMessage = string.Join(" ", validation.Errors);
                return;
            }

            var saved = await _publishing.UpdatePublishingFormatAsync(project.Id, model).ConfigureAwait(true);
            await RefreshFormatsAsync().ConfigureAwait(true);
            SelectedFormat = Formats.FirstOrDefault(item => item.Id == saved.Id);
            StatusMessage = "Format saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteFormatAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedFormat;
        if (project is null || selected is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Delete format '{selected.DisplayName}'?", "Delete Format"))
        {
            return;
        }

        try
        {
            await _publishing.DeletePublishingFormatAsync(project.Id, selected.Id, confirmed: true).ConfigureAwait(true);
            await RefreshFormatsAsync().ConfigureAwait(true);
            StatusMessage = "Format deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenFormatAsset()
    {
        if (FormatEditor is null)
        {
            return;
        }

        OpenProjectRelative(FormatEditor.AssetLink, isDirectory: false);
    }

    [RelayCommand]
    private async Task AddMetadataRecordAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var created = await _publishing.CreateMetadataRecordAsync(project.Id, new MetadataRecord
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Title = string.IsNullOrWhiteSpace(project.Title) ? "New metadata" : project.Title,
                Author = project.Author ?? string.Empty,
                PublicationStatus = PublicationStatus.Planned,
            }).ConfigureAwait(true);
            await RefreshMetadataAsync().ConfigureAwait(true);
            SelectedMetadataRecord = MetadataRecords.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "Metadata record created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveMetadataRecordAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = MetadataEditor;
        if (project is null || editor is null)
        {
            return;
        }

        try
        {
            var model = editor.ToModel();
            var validation = _publishing.ValidateMetadataRecord(model);
            if (!validation.IsValid)
            {
                StatusMessage = string.Join(" ", validation.Errors);
                return;
            }

            var saved = await _publishing.UpdateMetadataRecordAsync(project.Id, model).ConfigureAwait(true);
            await RefreshMetadataAsync().ConfigureAwait(true);
            SelectedMetadataRecord = MetadataRecords.FirstOrDefault(item => item.Id == saved.Id);
            StatusMessage = "Metadata record saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteMetadataRecordAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedMetadataRecord;
        if (project is null || selected is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Delete metadata '{selected.DisplayName}'?", "Delete Metadata"))
        {
            return;
        }

        try
        {
            await _publishing.DeleteMetadataRecordAsync(project.Id, selected.Id, confirmed: true).ConfigureAwait(true);
            await RefreshMetadataAsync().ConfigureAwait(true);
            StatusMessage = "Metadata record deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddPerformanceRecordAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var created = await _publishing.CreatePerformanceRecordAsync(project.Id, new PerformanceRecord
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                PeriodLabel = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM"),
                PeriodStart = DateOnly.FromDateTime(DateTime.Today),
            }).ConfigureAwait(true);
            await RefreshPerformanceAsync().ConfigureAwait(true);
            SelectedPerformanceRecord = PerformanceRecords.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "Performance record created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SavePerformanceRecordAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = PerformanceEditor;
        if (project is null || editor is null)
        {
            return;
        }

        try
        {
            var model = editor.ToModel();
            var validation = _publishing.ValidatePerformanceRecord(model);
            if (!validation.IsValid)
            {
                StatusMessage = string.Join(" ", validation.Errors);
                return;
            }

            var saved = await _publishing.UpdatePerformanceRecordAsync(project.Id, model).ConfigureAwait(true);
            await RefreshPerformanceAsync().ConfigureAwait(true);
            SelectedPerformanceRecord = PerformanceRecords.FirstOrDefault(item => item.Id == saved.Id);
            StatusMessage = "Performance record saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeletePerformanceRecordAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedPerformanceRecord;
        if (project is null || selected is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Delete performance period '{selected.DisplayName}'?", "Delete Performance"))
        {
            return;
        }

        try
        {
            await _publishing.DeletePerformanceRecordAsync(project.Id, selected.Id, confirmed: true).ConfigureAwait(true);
            await RefreshPerformanceAsync().ConfigureAwait(true);
            StatusMessage = "Performance record deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddCorrectionAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var created = await _publishing.CreateCorrectionAsync(project.Id, new Correction
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Error = "New error",
                CorrectionText = "Correction pending",
                ReportedDate = DateOnly.FromDateTime(DateTime.Today),
                Status = CorrectionStatus.Open,
            }).ConfigureAwait(true);
            await RefreshCorrectionsAsync().ConfigureAwait(true);
            SelectedCorrection = Corrections.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "Correction created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveCorrectionAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = CorrectionEditor;
        if (project is null || editor is null)
        {
            return;
        }

        try
        {
            var model = editor.ToModel();
            var validation = _publishing.ValidateCorrection(model);
            if (!validation.IsValid)
            {
                StatusMessage = string.Join(" ", validation.Errors);
                return;
            }

            var saved = await _publishing.UpdateCorrectionAsync(project.Id, model).ConfigureAwait(true);
            await RefreshCorrectionsAsync().ConfigureAwait(true);
            SelectedCorrection = Corrections.FirstOrDefault(item => item.Id == saved.Id);
            StatusMessage = "Correction saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteCorrectionAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedCorrection;
        if (project is null || selected is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Delete correction '{selected.DisplayName}'?", "Delete Correction"))
        {
            return;
        }

        try
        {
            await _publishing.DeleteCorrectionAsync(project.Id, selected.Id, confirmed: true).ConfigureAwait(true);
            await RefreshCorrectionsAsync().ConfigureAwait(true);
            StatusMessage = "Correction deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task RefreshFormatsAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        PublicationStatus? status = null;
        if (SelectedFormatStatusFilter != "(All statuses)"
            && Enum.TryParse<PublicationStatus>(SelectedFormatStatusFilter, out var parsed))
        {
            status = parsed;
        }

        var previousId = SelectedFormat?.Id;
        Formats.Clear();
        FormatEditor = null;

        foreach (var item in await _publishing.GetPublishingFormatsAsync(project.Id, status).ConfigureAwait(true))
        {
            Formats.Add(new PublishingFormatListItemViewModel(item));
        }

        SelectedFormat = Formats.FirstOrDefault(item => item.Id == previousId)
            ?? Formats.FirstOrDefault();
    }

    private async Task RefreshMetadataAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        PublicationStatus? status = null;
        if (SelectedMetadataStatusFilter != "(All statuses)"
            && Enum.TryParse<PublicationStatus>(SelectedMetadataStatusFilter, out var parsed))
        {
            status = parsed;
        }

        var previousId = SelectedMetadataRecord?.Id;
        MetadataRecords.Clear();
        MetadataEditor = null;

        foreach (var item in await _publishing.GetMetadataRecordsAsync(project.Id, status).ConfigureAwait(true))
        {
            MetadataRecords.Add(new MetadataRecordListItemViewModel(item));
        }

        SelectedMetadataRecord = MetadataRecords.FirstOrDefault(item => item.Id == previousId)
            ?? MetadataRecords.FirstOrDefault();
    }

    private async Task RefreshPerformanceAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        var previousId = SelectedPerformanceRecord?.Id;
        PerformanceRecords.Clear();
        PerformanceEditor = null;

        foreach (var item in await _publishing.GetPerformanceRecordsAsync(
                     project.Id,
                     string.IsNullOrWhiteSpace(PerformanceFormatFilter) ? null : PerformanceFormatFilter,
                     PerformanceSortDescending).ConfigureAwait(true))
        {
            PerformanceRecords.Add(new PerformanceRecordListItemViewModel(item));
        }

        SelectedPerformanceRecord = PerformanceRecords.FirstOrDefault(item => item.Id == previousId)
            ?? PerformanceRecords.FirstOrDefault();
    }

    private async Task RefreshCorrectionsAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        CorrectionStatus? status = null;
        if (SelectedCorrectionStatusFilter != "(All statuses)"
            && Enum.TryParse<CorrectionStatus>(SelectedCorrectionStatusFilter, out var parsed))
        {
            status = parsed;
        }

        var previousId = SelectedCorrection?.Id;
        Corrections.Clear();
        CorrectionEditor = null;

        foreach (var item in await _publishing.GetCorrectionsAsync(
                     project.Id,
                     status,
                     CorrectionSortDescending).ConfigureAwait(true))
        {
            Corrections.Add(new CorrectionListItemViewModel(item));
        }

        SelectedCorrection = Corrections.FirstOrDefault(item => item.Id == previousId)
            ?? Corrections.FirstOrDefault();
    }
}

public sealed class PublishingFormatListItemViewModel(PublishingFormat source)
{
    public PublishingFormat Source { get; } = source;

    public Guid Id => Source.Id;

    public string DisplayName => $"{Source.Name} ({Source.FormatKind})";
}

public sealed class MetadataRecordListItemViewModel(MetadataRecord source)
{
    public MetadataRecord Source { get; } = source;

    public Guid Id => Source.Id;

    public string DisplayName => string.IsNullOrWhiteSpace(Source.Edition)
        ? Source.Title
        : $"{Source.Title} — {Source.Edition}";
}

public sealed class PerformanceRecordListItemViewModel(PerformanceRecord source)
{
    public PerformanceRecord Source { get; } = source;

    public Guid Id => Source.Id;

    public string DisplayName => string.IsNullOrWhiteSpace(Source.Format)
        ? Source.PeriodLabel
        : $"{Source.PeriodLabel} — {Source.Format}";
}

public sealed class CorrectionListItemViewModel(Correction source)
{
    public Correction Source { get; } = source;

    public Guid Id => Source.Id;

    public string DisplayName => Source.Error.Length > 60
        ? Source.Error[..57] + "..."
        : Source.Error;
}

public partial class PublishingFormatEditorViewModel : ObservableObject
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private PublishingFormatKind _formatKind;

    [ObservableProperty]
    private string _isbnOrAsin = string.Empty;

    [ObservableProperty]
    private string _trimOrFileSpec = string.Empty;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    private string _distributor = string.Empty;

    [ObservableProperty]
    private PublicationStatus _publicationStatus;

    [ObservableProperty]
    private string _assetLink = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    public static PublishingFormatEditorViewModel From(PublishingFormat source) => new()
    {
        Id = source.Id,
        ProjectId = source.ProjectId,
        Name = source.Name,
        FormatKind = source.FormatKind,
        IsbnOrAsin = source.IsbnOrAsin,
        TrimOrFileSpec = source.TrimOrFileSpec,
        PriceText = source.Price?.ToString("0.##") ?? string.Empty,
        Distributor = source.Distributor,
        PublicationStatus = source.PublicationStatus,
        AssetLink = source.AssetLink,
        Notes = source.Notes,
    };

    public PublishingFormat ToModel()
    {
        decimal? price = null;
        if (!string.IsNullOrWhiteSpace(PriceText))
        {
            if (!decimal.TryParse(PriceText, out var parsed))
            {
                throw new InvalidOperationException("Price must be a valid number.");
            }

            price = parsed;
        }

        return new PublishingFormat
        {
            Id = Id,
            ProjectId = ProjectId,
            Name = Name,
            FormatKind = FormatKind,
            IsbnOrAsin = IsbnOrAsin,
            TrimOrFileSpec = TrimOrFileSpec,
            Price = price,
            Distributor = Distributor,
            PublicationStatus = PublicationStatus,
            AssetLink = AssetLink,
            Notes = Notes,
        };
    }
}

public partial class MetadataRecordEditorViewModel : ObservableObject
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _series = string.Empty;

    [ObservableProperty]
    private string _seriesNumber = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _categories = string.Empty;

    [ObservableProperty]
    private string _searchTerms = string.Empty;

    [ObservableProperty]
    private string _readerAge = string.Empty;

    [ObservableProperty]
    private string _language = string.Empty;

    [ObservableProperty]
    private string _publicationDateText = string.Empty;

    [ObservableProperty]
    private string _edition = string.Empty;

    [ObservableProperty]
    private string _publisher = string.Empty;

    [ObservableProperty]
    private string _pricingNotes = string.Empty;

    [ObservableProperty]
    private string _territoryRights = string.Empty;

    [ObservableProperty]
    private string _isbn = string.Empty;

    [ObservableProperty]
    private string _formatName = string.Empty;

    [ObservableProperty]
    private PublicationStatus _publicationStatus;

    public static MetadataRecordEditorViewModel From(MetadataRecord source) => new()
    {
        Id = source.Id,
        ProjectId = source.ProjectId,
        Title = source.Title,
        Subtitle = source.Subtitle,
        Series = source.Series,
        SeriesNumber = source.SeriesNumber,
        Author = source.Author,
        Description = source.Description,
        Categories = source.Categories,
        SearchTerms = source.SearchTerms,
        ReaderAge = source.ReaderAge,
        Language = source.Language,
        PublicationDateText = source.PublicationDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        Edition = source.Edition,
        Publisher = source.Publisher,
        PricingNotes = source.PricingNotes,
        TerritoryRights = source.TerritoryRights,
        Isbn = source.Isbn,
        FormatName = source.FormatName,
        PublicationStatus = source.PublicationStatus,
    };

    public MetadataRecord ToModel() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        Title = Title,
        Subtitle = Subtitle,
        Series = Series,
        SeriesNumber = SeriesNumber,
        Author = Author,
        Description = Description,
        Categories = Categories,
        SearchTerms = SearchTerms,
        ReaderAge = ReaderAge,
        Language = Language,
        PublicationDate = DateOnly.TryParse(PublicationDateText, out var date) ? date : null,
        Edition = Edition,
        Publisher = Publisher,
        PricingNotes = PricingNotes,
        TerritoryRights = TerritoryRights,
        Isbn = Isbn,
        FormatName = FormatName,
        PublicationStatus = PublicationStatus,
    };
}

public partial class PerformanceRecordEditorViewModel : ObservableObject
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    [ObservableProperty]
    private string _periodLabel = string.Empty;

    [ObservableProperty]
    private string _periodStartText = string.Empty;

    [ObservableProperty]
    private string _periodEndText = string.Empty;

    [ObservableProperty]
    private string _format = string.Empty;

    [ObservableProperty]
    private string _salesText = string.Empty;

    [ObservableProperty]
    private string _readThroughText = string.Empty;

    [ObservableProperty]
    private string _mailingListText = string.Empty;

    [ObservableProperty]
    private string _reviewsText = string.Empty;

    [ObservableProperty]
    private string _adCostText = string.Empty;

    [ObservableProperty]
    private string _availability = string.Empty;

    [ObservableProperty]
    private string _returnsOrIssues = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    public static PerformanceRecordEditorViewModel From(PerformanceRecord source) => new()
    {
        Id = source.Id,
        ProjectId = source.ProjectId,
        PeriodLabel = source.PeriodLabel,
        PeriodStartText = source.PeriodStart?.ToString("yyyy-MM-dd") ?? string.Empty,
        PeriodEndText = source.PeriodEnd?.ToString("yyyy-MM-dd") ?? string.Empty,
        Format = source.Format,
        SalesText = source.Sales?.ToString("0.##") ?? string.Empty,
        ReadThroughText = source.ReadThrough?.ToString("0.####") ?? string.Empty,
        MailingListText = source.MailingList?.ToString() ?? string.Empty,
        ReviewsText = source.Reviews?.ToString() ?? string.Empty,
        AdCostText = source.AdCost?.ToString("0.##") ?? string.Empty,
        Availability = source.Availability,
        ReturnsOrIssues = source.ReturnsOrIssues,
        Notes = source.Notes,
    };

    public PerformanceRecord ToModel()
    {
        return new PerformanceRecord
        {
            Id = Id,
            ProjectId = ProjectId,
            PeriodLabel = PeriodLabel,
            PeriodStart = DateOnly.TryParse(PeriodStartText, out var start) ? start : null,
            PeriodEnd = DateOnly.TryParse(PeriodEndText, out var end) ? end : null,
            Format = Format,
            Sales = ParseDecimal(SalesText, "Sales"),
            ReadThrough = ParseDecimal(ReadThroughText, "Read-through"),
            MailingList = ParseInt(MailingListText, "Mailing list"),
            Reviews = ParseInt(ReviewsText, "Reviews"),
            AdCost = ParseDecimal(AdCostText, "Ad cost"),
            Availability = Availability,
            ReturnsOrIssues = ReturnsOrIssues,
            Notes = Notes,
        };
    }

    private static decimal? ParseDecimal(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!decimal.TryParse(text, out var value))
        {
            throw new InvalidOperationException($"{field} must be a valid number.");
        }

        return value;
    }

    private static int? ParseInt(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!int.TryParse(text, out var value))
        {
            throw new InvalidOperationException($"{field} must be a whole number.");
        }

        return value;
    }
}

public partial class CorrectionEditorViewModel : ObservableObject
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    [ObservableProperty]
    private string _error = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private string _correctionText = string.Empty;

    [ObservableProperty]
    private string _reportedDateText = string.Empty;

    [ObservableProperty]
    private string _correctedDateText = string.Empty;

    [ObservableProperty]
    private string _formatsUpdated = string.Empty;

    [ObservableProperty]
    private string _newEdition = string.Empty;

    [ObservableProperty]
    private CorrectionStatus _status;

    public static CorrectionEditorViewModel From(Correction source) => new()
    {
        Id = source.Id,
        ProjectId = source.ProjectId,
        Error = source.Error,
        Location = source.Location,
        CorrectionText = source.CorrectionText,
        ReportedDateText = source.ReportedDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        CorrectedDateText = source.CorrectedDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        FormatsUpdated = source.FormatsUpdated,
        NewEdition = source.NewEdition,
        Status = source.Status,
    };

    public Correction ToModel() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        Error = Error,
        Location = Location,
        CorrectionText = CorrectionText,
        ReportedDate = DateOnly.TryParse(ReportedDateText, out var reported) ? reported : null,
        CorrectedDate = DateOnly.TryParse(CorrectedDateText, out var corrected) ? corrected : null,
        FormatsUpdated = FormatsUpdated,
        NewEdition = NewEdition,
        Status = Status,
    };
}
