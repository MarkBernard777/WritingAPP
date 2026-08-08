using MasterBookWritingSystem.Core.Settings;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IApplicationSettingsStore
{
    ApplicationSettings GetSettings();

    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
