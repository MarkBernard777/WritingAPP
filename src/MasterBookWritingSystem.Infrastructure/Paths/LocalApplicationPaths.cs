using MasterBookWritingSystem.Core.Abstractions;

namespace MasterBookWritingSystem.Infrastructure.Paths;

public sealed class LocalApplicationPaths : IApplicationPaths
{
    public LocalApplicationPaths()
    {
        LocalAppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MasterBookWritingSystem");
    }

    public string LocalAppDataDirectory { get; }
}
