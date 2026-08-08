using MasterBookWritingSystem.Core.Drafting;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IDraftingTargetService
{
    Task<DraftingTarget> GetTargetAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<DraftingTarget> SaveTargetAsync(DraftingTarget target, CancellationToken cancellationToken = default);
}

public interface IDraftingSessionRepository
{
    Task<DraftingSession?> GetAsync(
        Guid projectId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DraftingSession>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DraftingSession>> ListInterruptedAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(DraftingSession session, CancellationToken cancellationToken = default);
}

public sealed class DraftingTimerSnapshot
{
    public required DraftingTimerState State { get; init; }

    public Guid? SessionId { get; init; }

    public Guid? ProjectId { get; init; }

    public TimeSpan ActiveElapsed { get; init; }

    public DateTimeOffset? StartedUtc { get; init; }

    public bool HasInterruptedSession { get; init; }

    public DraftingSession? InterruptedSession { get; init; }
}

public interface IDraftingTimerService
{
    event EventHandler? Changed;

    DraftingTimerSnapshot GetSnapshot();

    Task StartAsync(
        Guid projectId,
        Guid? chapterId = null,
        Guid? sceneId = null,
        CancellationToken cancellationToken = default);

    Task PauseAsync(CancellationToken cancellationToken = default);

    Task ResumeAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task OnProjectOpenedAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task OnProjectClosedAsync(CancellationToken cancellationToken = default);

    Task ResumeInterruptedAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task CloseInterruptedAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after a successful manuscript chapter save. Opens/extends auto-sessions
    /// only when word-count change is meaningful; never blocks the editor.
    /// </summary>
    Task NoteManuscriptSaveAsync(
        Guid projectId,
        Guid chapterId,
        string markdown,
        CancellationToken cancellationToken = default);
}

public interface IDraftingProgressQueryService
{
    Task<DraftingProgressReport> GetProgressAsync(
        Guid projectId,
        DateOnly fromLocalDate,
        DateOnly toLocalDate,
        CancellationToken cancellationToken = default);
}
