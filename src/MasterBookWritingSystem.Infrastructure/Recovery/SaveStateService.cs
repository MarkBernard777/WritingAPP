using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Recovery;

namespace MasterBookWritingSystem.Infrastructure.Recovery;

public sealed class SaveStateService : ISaveStateService
{
    private readonly IRecoveryJournalService _journal;
    private readonly IProjectService _projects;
    private readonly object _gate = new();
    private SaveState _state = SaveState.Clean;
    private string _message = "Clean";
    private DateTimeOffset? _lastSavedUtc;

    public SaveStateService(IRecoveryJournalService journal, IProjectService projects)
    {
        _journal = journal;
        _projects = projects;
    }

    public SaveState State
    {
        get { lock (_gate) { return _state; } }
    }

    public string Message
    {
        get { lock (_gate) { return _message; } }
    }

    public DateTimeOffset? LastSavedUtc
    {
        get { lock (_gate) { return _lastSavedUtc; } }
    }

    public event EventHandler? Changed;

    public void Report(SaveState state, string? message = null)
    {
        lock (_gate)
        {
            _state = state;
            _message = message ?? DefaultMessage(state);
            if (state == SaveState.Saved)
            {
                _lastSavedUtc = DateTimeOffset.UtcNow;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshRecoveryAvailabilityAsync(
        Guid? projectId,
        CancellationToken cancellationToken = default)
    {
        var id = projectId ?? _projects.ActiveProject?.Id;
        if (id is null)
        {
            Report(SaveState.Clean, "Clean");
            return;
        }

        try
        {
            if (await _journal.HasRecoverableEntriesAsync(id.Value, cancellationToken).ConfigureAwait(false))
            {
                Report(SaveState.RecoveryAvailable, "Recovery available");
            }
            else if (State is SaveState.RecoveryAvailable or SaveState.SaveFailed)
            {
                Report(SaveState.Clean, "Clean");
            }
        }
        catch
        {
            // Keep prior state if journal inspection fails.
        }
    }

    private static string DefaultMessage(SaveState state)
        => state switch
        {
            SaveState.Saving => "Saving…",
            SaveState.Saved => "Saved",
            SaveState.SaveFailed => "Save failed",
            SaveState.RecoveryAvailable => "Recovery available",
            _ => "Clean",
        };
}
