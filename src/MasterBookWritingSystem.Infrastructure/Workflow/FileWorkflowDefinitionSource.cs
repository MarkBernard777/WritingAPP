using System.Text.Json;
using System.Text.Json.Serialization;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Workflow;

namespace MasterBookWritingSystem.Infrastructure.Workflow;

internal sealed class WorkflowPhaseDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("deliverable")]
    public string? Deliverable { get; set; }

    [JsonPropertyName("gate")]
    public string? Gate { get; set; }

    [JsonPropertyName("template")]
    public string? Template { get; set; }

    [JsonPropertyName("steps")]
    public List<WorkflowStepDto> Steps { get; set; } = [];
}

internal sealed class WorkflowStepDto
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public JsonElement Content { get; set; }
}

public sealed class FileWorkflowDefinitionSource : IWorkflowDefinitionSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _workflowPath;

    public FileWorkflowDefinitionSource(string workflowPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);
        _workflowPath = workflowPath;
    }

    public async Task<IReadOnlyList<WorkflowPhase>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(_workflowPath);
        var dtos = await JsonSerializer
            .DeserializeAsync<List<WorkflowPhaseDto>>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workflow definition was empty: {_workflowPath}");

        return dtos.Select(ToDomain).ToList();
    }

    private static WorkflowPhase ToDomain(WorkflowPhaseDto dto)
    {
        var affinity = ResolveRouteAffinity(dto.Id);
        var phase = new WorkflowPhase
        {
            Id = dto.Id,
            Title = dto.Title,
            Deliverable = dto.Deliverable ?? string.Empty,
            GateStatement = dto.Gate ?? string.Empty,
            TemplatePath = dto.Template,
            RouteAffinity = affinity,
        };

        foreach (var step in dto.Steps.OrderBy(item => item.Number))
        {
            phase.Steps.Add(new WorkflowStep
            {
                Number = step.Number,
                Title = step.Title,
                PhaseId = dto.Id,
                RouteAffinity = affinity,
            });
        }

        return phase;
    }

    internal static PublishingRoute? ResolveRouteAffinity(string phaseId)
        => phaseId switch
        {
            "20A" => PublishingRoute.Traditional,
            "20B" => PublishingRoute.SelfPublishing,
            _ => null,
        };
}

public sealed class EmbeddedOrSeedWorkflowDefinitionSource : IWorkflowDefinitionSource
{
    private readonly IWorkflowDefinitionSource _inner;

    public EmbeddedOrSeedWorkflowDefinitionSource()
    {
        _inner = new FileWorkflowDefinitionSource(ResolveDefaultPath());
    }

    public Task<IReadOnlyList<WorkflowPhase>> LoadAsync(CancellationToken cancellationToken = default)
        => _inner.LoadAsync(cancellationToken);

    private static string ResolveDefaultPath()
    {
        var seedRoot = SeedPaths.FindSeedRoot();
        return SeedPaths.GetWorkflowPath(seedRoot);
    }
}
