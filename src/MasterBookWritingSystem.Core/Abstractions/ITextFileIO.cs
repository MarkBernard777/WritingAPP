namespace MasterBookWritingSystem.Core.Abstractions;

/// <summary>Testable atomic text I/O used by multi-file manuscript operations.</summary>
public interface ITextFileIO
{
    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

    bool FileExists(string path);

    void DeleteFile(string path);

    void MoveFile(string sourcePath, string destinationPath, bool overwrite);
}
