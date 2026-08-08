using MasterBookWritingSystem.Core.Recovery;

namespace MasterBookWritingSystem.Core.Accessibility;

/// <summary>
/// Builds accessible save-status text that never relies on colour alone.
/// </summary>
public static class SaveStateAccessibility
{
    public static string FormatDisplay(SaveState state, string message)
    {
        var trimmed = string.IsNullOrWhiteSpace(message) ? state.ToString() : message.Trim();
        return state switch
        {
            SaveState.Saving => trimmed.Contains("Saving", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"Saving: {trimmed}",
            SaveState.SaveFailed => trimmed.Contains("fail", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"Save failed: {trimmed}",
            SaveState.RecoveryAvailable => trimmed.Contains("Recovery", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"Recovery available: {trimmed}",
            SaveState.Saved => trimmed,
            _ => trimmed,
        };
    }

    public static string FormatAccessibleName(SaveState state, string message)
        => $"Save status: {FormatDisplay(state, message)}";
}
