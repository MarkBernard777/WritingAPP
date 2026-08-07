namespace MasterBookWritingSystem.Core.Abstractions;

/// <summary>
/// Resolves global application paths. Book content must never live only here.
/// </summary>
public interface IApplicationPaths
{
    string LocalAppDataDirectory { get; }
}
