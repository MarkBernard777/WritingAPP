using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Documents;
using MasterBookWritingSystem.Core.Domain.Tools;
using MasterBookWritingSystem.Core.Tools;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class ToolsViewModel : ObservableObject
{
    private readonly IProjectService _projects;
    private readonly IIdeaService _ideas;
    private readonly IDocumentService _documents;
    private readonly IStoryDataService _story;
    private readonly IChapterService _chapters;
    private readonly IWorkflowService _workflow;
    private readonly IToolsReportExporter _exporter;

    public ToolsViewModel(
        IProjectService projects,
        IIdeaService ideas,
        IDocumentService documents,
        IStoryDataService story,
        IChapterService chapters,
        IWorkflowService workflow,
        IToolsReportExporter exporter)
    {
        _projects = projects;
        _ideas = ideas;
        _documents = documents;
        _story = story;
        _chapters = chapters;
        _workflow = workflow;
        _exporter = exporter;
        _ = RefreshAsync();
    }

    public ObservableCollection<IdeaListItemViewModel> Ideas { get; } = [];
    public ObservableCollection<SceneDiagnosticItemViewModel> SceneDiagnostics { get; } = [];
    public ObservableCollection<ChapterPickItemViewModel> AnalyzerChapters { get; } = [];
    public ObservableCollection<BetaFeedbackEntry> BetaEntries { get; } = [];
    public ObservableCollection<string> BetaSummaryLines { get; } = [];
    public ObservableCollection<string> RouteBreakdownLines { get; } = [];

    [ObservableProperty] private bool _hasProject;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _toolExplanation = string.Empty;

    // Idea scorecard
    [ObservableProperty] private IdeaListItemViewModel? _selectedIdea;
    [ObservableProperty] private string _ideaTitle = string.Empty;
    [ObservableProperty] private string _ideaNotes = string.Empty;
    [ObservableProperty] private int _fascination;
    [ObservableProperty] private int _emotionalPower;
    [ObservableProperty] private int _conflict;
    [ObservableProperty] private int _characterScore;
    [ObservableProperty] private int _visual;
    [ObservableProperty] private int _originalCombination;
    [ObservableProperty] private int _novelLength;
    [ObservableProperty] private int _difficultChoices;
    [ObservableProperty] private int _audienceFit;
    [ObservableProperty] private int _seriesFit;
    [ObservableProperty] private string _ideaScoreSummary = string.Empty;

    // Premise
    [ObservableProperty] private string _protagonist = string.Empty;
    [ObservableProperty] private string _disruption = string.Empty;
    [ObservableProperty] private string _premiseGoal = string.Empty;
    [ObservableProperty] private string _opposition = string.Empty;
    [ObservableProperty] private string _stakes = string.Empty;
    [ObservableProperty] private string _urgency = string.Empty;
    [ObservableProperty] private string _transformation = string.Empty;
    [ObservableProperty] private string _finalSentence = string.Empty;
    [ObservableProperty] private string _generatedPremise = string.Empty;
    [ObservableProperty] private string _premiseValidation = string.Empty;

    // Draft planner
    [ObservableProperty] private int _targetWordCount = 80000;
    [ObservableProperty] private int _wordsCompleted;
    [ObservableProperty] private int _wordsPerSession = 1000;
    [ObservableProperty] private int _sessionsPerWeek = 5;
    [ObservableProperty] private string _startDateText = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
    [ObservableProperty] private string _draftPlanSummary = string.Empty;

    // Text analyzer
    [ObservableProperty] private ChapterPickItemViewModel? _selectedAnalyzerChapter;
    [ObservableProperty] private bool _analyzeAllChapters;
    [ObservableProperty] private string _textAnalyzerSummary = string.Empty;

    // Publishing route
    [ObservableProperty] private int _controlWeight = 5;
    [ObservableProperty] private int _fundingWeight = 5;
    [ObservableProperty] private int _speedWeight = 5;
    [ObservableProperty] private int _distributionWeight = 5;
    [ObservableProperty] private int _rightsWeight = 5;
    [ObservableProperty] private int _productionWeight = 5;
    [ObservableProperty] private string _routeRecommendation = string.Empty;
    [ObservableProperty] private PublishingRoute _activePublishingRoute = PublishingRoute.Unspecified;

    // Beta
    [ObservableProperty] private string _betaReader = string.Empty;
    [ObservableProperty] private string _betaCategory = string.Empty;
    [ObservableProperty] private string _betaObservation = string.Empty;
    [ObservableProperty] private string _betaSuggestedFix = string.Empty;
    [ObservableProperty] private string _exportFileName = $"tool-report-{DateTime.Now:yyyyMMdd-HHmmss}.md";

    partial void OnSelectedIdeaChanged(IdeaListItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        IdeaTitle = value.Title;
        IdeaNotes = value.Notes;
        var score = value.Score;
        Fascination = score?.Fascination ?? 0;
        EmotionalPower = score?.EmotionalPower ?? 0;
        Conflict = score?.Conflict ?? 0;
        CharacterScore = score?.Character ?? 0;
        Visual = score?.Visual ?? 0;
        OriginalCombination = score?.OriginalCombination ?? 0;
        NovelLength = score?.NovelLength ?? 0;
        DifficultChoices = score?.DifficultChoices ?? 0;
        AudienceFit = score?.AudienceFit ?? 0;
        SeriesFit = score?.SeriesFit ?? 0;
        IdeaScoreSummary = score is null
            ? "No score saved yet."
            : $"Total {score.Total}/100 → {score.Decision}. {IdeaScorecardCalculator.ExplainDecision(score.Total)}";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var project = _projects.ActiveProject;
        HasProject = project is not null;
        Ideas.Clear();
        SceneDiagnostics.Clear();
        AnalyzerChapters.Clear();
        if (project is null)
        {
            StatusMessage = "Open or create a project to use mini tools.";
            return;
        }

        try
        {
            foreach (var idea in await _ideas.GetAllAsync(project.Id).ConfigureAwait(true))
            {
                Ideas.Add(new IdeaListItemViewModel(idea));
            }

            SelectedIdea = Ideas.FirstOrDefault();
            await LoadPremiseAsync(project.Id).ConfigureAwait(true);
            await LoadSceneDiagnosticsAsync(project.Id).ConfigureAwait(true);
            foreach (var chapter in await _chapters.GetAllAsync(project.Id).ConfigureAwait(true))
            {
                AnalyzerChapters.Add(new ChapterPickItemViewModel(chapter.Id, chapter.Title, chapter.SequenceNumber));
            }

            SelectedAnalyzerChapter = AnalyzerChapters.FirstOrDefault();
            WordsCompleted = AnalyzerChapters.Count == 0
                ? 0
                : (await _chapters.GetAllAsync(project.Id).ConfigureAwait(true)).Sum(item => item.WordCount);
            ActivePublishingRoute = project.PublishingRoute;
            StatusMessage = string.Empty;
            ToolExplanation = "All calculations run offline in Core services. No cloud or AI calls are used.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddIdeaAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var created = await _ideas.CreateAsync(project.Id, string.IsNullOrWhiteSpace(IdeaTitle) ? "New idea" : IdeaTitle, IdeaNotes)
                .ConfigureAwait(true);
            await ReloadIdeasAsync(created.Id).ConfigureAwait(true);
            StatusMessage = "Idea created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveIdeaAsync()
    {
        var project = _projects.ActiveProject;
        var selected = SelectedIdea;
        if (project is null || selected is null)
        {
            return;
        }

        try
        {
            await _ideas.UpdateAsync(project.Id, new Idea
            {
                Id = selected.Id,
                ProjectId = project.Id,
                Title = IdeaTitle,
                Notes = IdeaNotes,
                Decision = selected.Decision,
            }).ConfigureAwait(true);

            var scored = await _ideas.SaveScoreAsync(project.Id, selected.Id, new IdeaScore
            {
                Id = selected.Score?.Id ?? Guid.NewGuid(),
                IdeaId = selected.Id,
                ProjectId = project.Id,
                Fascination = Fascination,
                EmotionalPower = EmotionalPower,
                Conflict = Conflict,
                Character = CharacterScore,
                Visual = Visual,
                OriginalCombination = OriginalCombination,
                NovelLength = NovelLength,
                DifficultChoices = DifficultChoices,
                AudienceFit = AudienceFit,
                SeriesFit = SeriesFit,
            }).ConfigureAwait(true);

            await ReloadIdeasAsync(scored.Id).ConfigureAwait(true);
            StatusMessage = $"Idea scored {scored.Score?.Total}/100 ({scored.Score?.Decision}).";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteIdeaAsync()
    {
        var project = _projects.ActiveProject;
        var selected = SelectedIdea;
        if (project is null || selected is null)
        {
            return;
        }

        try
        {
            await _ideas.DeleteAsync(project.Id, selected.Id).ConfigureAwait(true);
            await ReloadIdeasAsync(null).ConfigureAwait(true);
            StatusMessage = "Idea deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void BuildPremise()
    {
        var result = PremiseBuilderCalculator.Build(new PremiseComponents
        {
            Protagonist = Protagonist,
            Disruption = Disruption,
            Goal = PremiseGoal,
            Opposition = Opposition,
            Stakes = Stakes,
            Urgency = Urgency,
            Transformation = Transformation,
            FinalSentence = FinalSentence,
        });
        GeneratedPremise = result.GeneratedSentence;
        PremiseValidation = result.IsComplete
            ? "All seven components present."
            : $"Missing: {string.Join(", ", result.MissingComponents)}";
        ToolExplanation = result.Explanation;
    }

    [RelayCommand]
    private async Task SavePremiseAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        BuildPremise();
        try
        {
            var document = await _documents.GetAsync(project.Id, DocumentType.PremiseDocument).ConfigureAwait(true);
            await SetField(project.Id, document.Id, "PROTAGONIST", Protagonist).ConfigureAwait(true);
            await SetField(project.Id, document.Id, "DISRUPTION", Disruption).ConfigureAwait(true);
            await SetField(project.Id, document.Id, "GOAL", PremiseGoal).ConfigureAwait(true);
            await SetField(project.Id, document.Id, "OPPOSITION", Opposition).ConfigureAwait(true);
            await SetField(project.Id, document.Id, "STAKES", Stakes).ConfigureAwait(true);
            await SetField(project.Id, document.Id, "URGENCY", Urgency).ConfigureAwait(true);
            await SetField(project.Id, document.Id, "TRANSFORMATION", Transformation).ConfigureAwait(true);
            await SetField(project.Id, document.Id, "FINAL_SENTENCE",
                string.IsNullOrWhiteSpace(FinalSentence) ? GeneratedPremise : FinalSentence).ConfigureAwait(true);
            StatusMessage = "Premise document fields updated.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void CalculateDraftPlan()
    {
        DateOnly? start = DateOnly.TryParse(StartDateText, out var parsed) ? parsed : null;
        var result = DraftPlannerCalculator.Calculate(new DraftPlannerInput
        {
            TargetWordCount = TargetWordCount,
            WordsCompleted = WordsCompleted,
            WordsPerSession = WordsPerSession,
            SessionsPerWeek = SessionsPerWeek,
            StartDate = start,
        });
        if (!result.IsValid)
        {
            DraftPlanSummary = string.Join(" ", result.Errors);
            return;
        }

        DraftPlanSummary =
            $"Remaining {result.WordsRemaining} words → {result.SessionsRequired} sessions, {result.WeeksRequired} weeks, "
            + $"{result.WordsPerWeek} words/week, ~{result.RequiredDailyPace} words/day. "
            + $"ETA: {(result.EstimatedCompletionDate?.ToString("yyyy-MM-dd") ?? "set a start date")}. {result.Explanation}";
        ToolExplanation = result.Explanation;
    }

    [RelayCommand]
    private async Task RunSceneDiagnosticsAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        await LoadSceneDiagnosticsAsync(project.Id).ConfigureAwait(true);
        StatusMessage = SceneDiagnostics.Count == 0
            ? "No scenes found. Add scenes in Story Data first."
            : $"Evaluated {SceneDiagnostics.Count} scene(s).";
        ToolExplanation = SceneDiagnosticCalculator.Evaluate(new Core.Domain.Story.Scene
        {
            Id = Guid.Empty,
            ProjectId = project.Id,
            SequenceNumber = 1,
            Title = "x",
        }).Explanation;
    }

    [RelayCommand]
    private async Task AnalyzeTextAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            string text;
            if (AnalyzeAllChapters)
            {
                var chapters = await _chapters.GetAllAsync(project.Id).ConfigureAwait(true);
                if (chapters.Count == 0)
                {
                    TextAnalyzerSummary = "No chapters available to analyze.";
                    return;
                }

                var builder = new StringBuilder();
                foreach (var chapter in chapters.OrderBy(item => item.SequenceNumber))
                {
                    builder.AppendLine(await _chapters.LoadContentAsync(project.Id, chapter.Id).ConfigureAwait(true));
                    builder.AppendLine();
                }

                text = builder.ToString();
            }
            else if (SelectedAnalyzerChapter is not null)
            {
                text = await _chapters.LoadContentAsync(project.Id, SelectedAnalyzerChapter.Id).ConfigureAwait(true);
            }
            else
            {
                TextAnalyzerSummary = "Select a chapter or enable Analyze all chapters.";
                return;
            }

            var result = TextAnalyzerCalculator.Analyze(text);
            var repeats = result.RepeatedTerms.Count == 0
                ? "none"
                : string.Join(", ", result.RepeatedTerms.Take(8).Select(item => $"{item.Term}×{item.Count}"));
            var filters = result.FilterWords.Count == 0
                ? "none"
                : string.Join(", ", result.FilterWords.Take(8).Select(item => $"{item.Word}×{item.Count}"));
            TextAnalyzerSummary =
                $"{result.WordCount} words · {result.ReadingTimeMinutes} min reading · {result.LongSentenceCount} long sentences · "
                + $"{result.DraftingMarkers.Count} drafting markers. Repeated: {repeats}. Filter words: {filters}. {result.Explanation}";
            ToolExplanation = result.Explanation;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void EvaluatePublishingRoute()
    {
        var result = PublishingRouteDecisionCalculator.Decide(new PublishingRoutePreferences
        {
            ControlWeight = ControlWeight,
            FundingWeight = FundingWeight,
            SpeedWeight = SpeedWeight,
            DistributionWeight = DistributionWeight,
            RightsWeight = RightsWeight,
            ProductionWeight = ProductionWeight,
        });
        RouteRecommendation = $"Recommended: {result.RecommendedRoute}";
        RouteBreakdownLines.Clear();
        foreach (var row in result.Breakdown)
        {
            RouteBreakdownLines.Add(
                $"{row.Route}: total {row.Total} (control {row.Control}, funding {row.Funding}, speed {row.Speed}, distribution {row.Distribution}, rights {row.Rights}, production {row.Production})");
        }

        ToolExplanation = result.Explanation;
    }

    [RelayCommand]
    private async Task ApplyPublishingRouteAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        EvaluatePublishingRoute();
        var recommended = PublishingRouteDecisionCalculator.Decide(new PublishingRoutePreferences
        {
            ControlWeight = ControlWeight,
            FundingWeight = FundingWeight,
            SpeedWeight = SpeedWeight,
            DistributionWeight = DistributionWeight,
            RightsWeight = RightsWeight,
            ProductionWeight = ProductionWeight,
        }).RecommendedRoute;

        try
        {
            await _workflow.SetPublishingRouteAsync(project.Id, recommended).ConfigureAwait(true);
            ActivePublishingRoute = recommended;
            StatusMessage = $"Publishing route set to {recommended}. Inactive-route workflow data is preserved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void AddBetaEntry()
    {
        if (string.IsNullOrWhiteSpace(BetaObservation) && string.IsNullOrWhiteSpace(BetaSuggestedFix))
        {
            StatusMessage = "Enter an observation and/or suggested fix.";
            return;
        }

        BetaEntries.Add(new BetaFeedbackEntry
        {
            Reader = BetaReader,
            Category = BetaCategory,
            Observation = BetaObservation,
            SuggestedFix = BetaSuggestedFix,
        });
        BetaObservation = string.Empty;
        BetaSuggestedFix = string.Empty;
        StatusMessage = "Feedback entry added (local list only).";
    }

    [RelayCommand]
    private void SynthesizeBetaFeedback()
    {
        var result = BetaFeedbackSynthesizer.Synthesize(BetaEntries);
        BetaSummaryLines.Clear();
        if (result.EntryCount == 0)
        {
            BetaSummaryLines.Add("No feedback entries yet.");
            return;
        }

        BetaSummaryLines.Add(
            $"{result.EntryCount} entries · {result.ObservationOnlyCount} observation-only · {result.SuggestionCount} with suggested fixes.");
        foreach (var group in result.GroupsByCategory)
        {
            BetaSummaryLines.Add(
                $"Category '{group.Key}' ×{group.Count}: observations [{string.Join("; ", group.Observations)}] / fixes [{string.Join("; ", group.SuggestedFixes)}]");
        }

        ToolExplanation = result.Explanation;
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        var markdown = new StringBuilder()
            .AppendLine("# Tools report")
            .AppendLine()
            .AppendLine($"Generated: {DateTimeOffset.Now:u}")
            .AppendLine()
            .AppendLine("## Idea comparison")
            .AppendLine();
        foreach (var idea in Ideas)
        {
            markdown.AppendLine($"- **{idea.Title}**: {idea.Total}/100 ({idea.Decision})");
        }

        markdown.AppendLine().AppendLine("## Premise").AppendLine().AppendLine(GeneratedPremise);
        markdown.AppendLine().AppendLine("## Draft plan").AppendLine().AppendLine(DraftPlanSummary);
        markdown.AppendLine().AppendLine("## Scene diagnostics").AppendLine();
        foreach (var scene in SceneDiagnostics)
        {
            markdown.AppendLine($"- {scene.Display}");
        }

        markdown.AppendLine().AppendLine("## Text analyzer").AppendLine().AppendLine(TextAnalyzerSummary);
        markdown.AppendLine().AppendLine("## Publishing route").AppendLine().AppendLine(RouteRecommendation);
        foreach (var line in RouteBreakdownLines)
        {
            markdown.AppendLine($"- {line}");
        }

        markdown.AppendLine().AppendLine("## Beta feedback synthesis").AppendLine();
        foreach (var line in BetaSummaryLines)
        {
            markdown.AppendLine($"- {line}");
        }

        try
        {
            var path = await _exporter.ExportMarkdownAsync(project.Id, ExportFileName, markdown.ToString())
                .ConfigureAwait(true);
            StatusMessage = $"Exported report to {path}.";
            ExportFileName = $"tool-report-{DateTime.Now:yyyyMMdd-HHmmss}.md";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task SetField(Guid projectId, Guid documentId, string key, string value)
    {
        try
        {
            await _documents.UpdateFieldAsync(projectId, documentId, key, value ?? string.Empty).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            // Field key may be absent on older templates; ignore.
        }
    }

    private async Task LoadPremiseAsync(Guid projectId)
    {
        var document = await _documents.GetAsync(projectId, DocumentType.PremiseDocument).ConfigureAwait(true);
        string Field(string key) => document.Fields.FirstOrDefault(item => item.Key == key)?.Value ?? string.Empty;
        Protagonist = Field("PROTAGONIST");
        Disruption = Field("DISRUPTION");
        PremiseGoal = Field("GOAL");
        Opposition = Field("OPPOSITION");
        Stakes = Field("STAKES");
        Urgency = Field("URGENCY");
        Transformation = Field("TRANSFORMATION");
        FinalSentence = Field("FINAL_SENTENCE");
        BuildPremise();
    }

    private async Task LoadSceneDiagnosticsAsync(Guid projectId)
    {
        SceneDiagnostics.Clear();
        var scenes = await _story.GetScenesAsync(projectId).ConfigureAwait(true);
        foreach (var scene in scenes)
        {
            var result = SceneDiagnosticCalculator.Evaluate(scene);
            SceneDiagnostics.Add(new SceneDiagnosticItemViewModel(result));
        }
    }

    private async Task ReloadIdeasAsync(Guid? selectId)
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        Ideas.Clear();
        foreach (var idea in await _ideas.GetAllAsync(project.Id).ConfigureAwait(true))
        {
            Ideas.Add(new IdeaListItemViewModel(idea));
        }

        SelectedIdea = selectId is null
            ? Ideas.FirstOrDefault()
            : Ideas.FirstOrDefault(item => item.Id == selectId) ?? Ideas.FirstOrDefault();
    }
}

public sealed class IdeaListItemViewModel(Idea idea)
{
    public Guid Id { get; } = idea.Id;
    public string Title { get; } = idea.Title;
    public string Notes { get; } = idea.Notes;
    public string Decision { get; } = idea.Score?.Decision ?? idea.Decision;
    public int Total { get; } = idea.Score?.Total ?? 0;
    public IdeaScore? Score { get; } = idea.Score;
    public string Display => $"{Title} — {Total}/100 ({Decision})";
}

public sealed class SceneDiagnosticItemViewModel(SceneDiagnosticResult result)
{
    public string Display =>
        $"{result.Title}: {result.Score}/{result.MaxScore}. Missing: {(result.MissingParts.Count == 0 ? "none" : string.Join(", ", result.MissingParts))}. {result.PurposeAssessment}";
}

public sealed class ChapterPickItemViewModel(Guid id, string title, int sequence)
{
    public Guid Id { get; } = id;
    public string Display => $"{sequence}. {title}";
}
