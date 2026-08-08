using System.Text;
using MasterBookWritingSystem.Core.Abstractions;

namespace MasterBookWritingSystem.Infrastructure.IO;

public sealed class FileSystemTextFileIO : ITextFileIO
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
        => AtomicFileWriter.WriteAllTextAsync(path, contents, cancellationToken);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllTextAsync(path, Utf8NoBom, cancellationToken);

    public bool FileExists(string path) => File.Exists(path);

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        => File.Move(sourcePath, destinationPath, overwrite);
}
