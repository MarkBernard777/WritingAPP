using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Workflow;
using MasterBookWritingSystem.Core.Workflow;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class WorkflowViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IWorkflowService _workflowService;

    public WorkflowViewModel(IProjectService projectService, IWorkflowService workflowService)
    {
        _projectService = projectService;
        _workflowService = workflowService;
        _ = RefreshAsync();
    }

    public ObservableCollection<PhaseListItemViewModel> Phases { get; } = [];

    public ObservableCollection<StepItemViewModel> Steps { get; } = [];

    public Array PublishingRoutes { get; } = Enum.GetValues(typeof(PublishingRoute));

    [ObservableProperty]
    private bool _hasProject;

    [ObservableProperty]
    private PhaseListItemViewModel? _selectedPhase;

    [ObservableProperty]
    private PublishingRoute _selectedRoute;

    [ObservableProperty]
    private string _gateStatement = string.Empty;

    [ObservableProperty]
    private string _deliverable = string.Empty;

    [ObservableProperty]
    private string _templatePath = string.Empty;

    [ObservableProperty]
    private string _gateEvidence = string.Empty;

    [ObservableProperty]
    private string _gateNotes = string.Empty;

    [ObservableProperty]
    private bool _canPassGate;

    [ObservableProperty]
    private bool _isGatePassed;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    partial void OnSelectedPhaseChanged(PhaseListItemViewModel? value)
        => _ = LoadSelectedPhaseAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var project = _projectService.ActiveProject;
        HasProject = project is not null;
        Phases.Clear();
        Steps.Clear();

        if (project is null)
        {
            StatusMessage = "Open or create a project to use the guided workflow.";
            return;
        }

        try
        {
            SelectedRoute = project.PublishingRoute;
            var phases = await _workflowService.GetApplicablePhasesAsync(project.Id).ConfigureAwait(true);
            foreach (var phase in phases)
            {
                var gate = await _workflowService.GetPhaseGateAsync(project.Id, phase.Id).ConfigureAwait(true);
                Phases.Add(new PhaseListItemViewModel(phase, gate?.IsPassed == true));
            }

            SelectedPhase = Phases.FirstOrDefault(phase => !phase.IsGatePassed) ?? Phases.FirstOrDefault();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ChangeRouteAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            await _workflowService.SetPublishingRouteAsync(project.Id, SelectedRoute).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task CompleteStepAsync(StepItemViewModel? step)
    {
        var project = _projectService.ActiveProject;
        var phase = SelectedPhase;
        if (project is null || phase is null || step is null)
        {
            return;
        }

        try
        {
            await _workflowService
                .CompleteStepAsync(project.Id, phase.Id, step.Number, step.Notes)
                .ConfigureAwait(true);
            await LoadSelectedPhaseAsync().ConfigureAwait(true);
            await RefreshPhaseFlagsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task PassGateAsync()
    {
        var project = _projectService.ActiveProject;
        var phase = SelectedPhase;
        if (project is null || phase is null)
        {
            return;
        }

        try
        {
            await _workflowService.PassGateAsync(project.Id, new PhaseGateCompletion
            {
                PhaseId = phase.Id,
                ConfirmedByUser = true,
                Evidence = GateEvidence,
                Notes = GateNotes,
                PassedUtc = DateTimeOffset.UtcNow,
            }).ConfigureAwait(true);

            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task LoadSelectedPhaseAsync()
    {
        Steps.Clear();
        var project = _projectService.ActiveProject;
        var phaseItem = SelectedPhase;
        if (project is null || phaseItem is null)
        {
            GateStatement = string.Empty;
            Deliverable = string.Empty;
            TemplatePath = string.Empty;
            CanPassGate = false;
            IsGatePassed = false;
            return;
        }

        var phase = phaseItem.Phase;
        GateStatement = phase.GateStatement;
        Deliverable = phase.Deliverable;
                TemplatePath = phase.TemplatePath ?? string.Empty;

        var progress = await _workflowService
            .GetPhaseStepProgressAsync(project.Id, phase.Id)
            .ConfigureAwait(true);
        var progressByNumber = progress.ToDictionary(item => item.StepNumber);

        foreach (var step in phase.Steps)
        {
            progressByNumber.TryGetValue(step.Number, out var stepProgress);
            Steps.Add(new StepItemViewModel(step, stepProgress));
        }

        var gate = await _workflowService.GetPhaseGateAsync(project.Id, phase.Id).ConfigureAwait(true);
        IsGatePassed = gate?.IsPassed == true;
        GateEvidence = gate?.Evidence ?? GateEvidence;
        GateNotes = gate?.Notes ?? GateNotes;
        CanPassGate = !IsGatePassed
            && await _workflowService.CanPassGateAsync(project.Id, phase.Id).ConfigureAwait(true);
    }

    private async Task RefreshPhaseFlagsAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null || SelectedPhase is null)
        {
            return;
        }

        var gate = await _workflowService.GetPhaseGateAsync(project.Id, SelectedPhase.Id).ConfigureAwait(true);
        SelectedPhase.IsGatePassed = gate?.IsPassed == true;
        IsGatePassed = SelectedPhase.IsGatePassed;
        CanPassGate = !IsGatePassed
            && await _workflowService.CanPassGateAsync(project.Id, SelectedPhase.Id).ConfigureAwait(true);
    }
}

public partial class PhaseListItemViewModel : ObservableObject
{
    public PhaseListItemViewModel(WorkflowPhase phase, bool isGatePassed)
    {
        Phase = phase;
        Id = phase.Id;
        Title = $"{phase.Id}. {phase.Title}";
        IsGatePassed = isGatePassed;
    }

    public WorkflowPhase Phase { get; }

    public string Id { get; }

    public string Title { get; }

    [ObservableProperty]
    private bool _isGatePassed;
}

public partial class StepItemViewModel : ObservableObject
{
    public StepItemViewModel(WorkflowStep step, StepProgress? progress)
    {
        Number = step.Number;
        Title = step.Title;
        IsComplete = progress?.Status == StepStatus.Complete;
        Notes = progress?.Notes ?? string.Empty;
    }

    public int Number { get; }

    public string Title { get; }

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private string _notes = string.Empty;

    public string DisplayTitle => $"{Number}. {Title}";
}
