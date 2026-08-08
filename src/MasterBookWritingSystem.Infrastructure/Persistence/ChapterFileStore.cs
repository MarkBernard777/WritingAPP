using System.Security.Cryptography;
using System.Text;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Infrastructure.IO;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Persistence;

public sealed class ChapterFileStore : IChapterFileStore
{
    public async Task SaveAsync(
        string projectRootPath,
        Chapter chapter,
        string markdownContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(chapter.RelativeMarkdownPath);
        ArgumentNullException.ThrowIfNull(markdownContent);

        var rootPath = Path.GetFullPath(projectRootPath);
        var relativePath = NormalizeRelativePath(chapter.RelativeMarkdownPath);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Chapter paths must be relative to the project root.");
        }

        var absolutePath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        await AtomicFileWriter.WriteAllTextAsync(absolutePath, markdownContent, cancellationToken)
            .ConfigureAwait(false);

        var wordCount = ManuscriptTextAnalytics.CountWords(markdownContent);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdownContent)));

        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);
        await using (var context = ProjectDbContextFactory.Create(databasePath))
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var existing = await context.Chapters
                .FirstOrDefaultAsync(record => record.Id == chapter.Id, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                context.Chapters.Add(new ChapterRecord
                {
                    Id = chapter.Id,
                    ProjectId = chapter.ProjectId,
                    PartId = chapter.PartId,
                    SequenceNumber = chapter.SequenceNumber,
                    Title = chapter.Title,
                    RelativeMarkdownPath = relativePath,
                    WordCount = wordCount,
                    ContentHash = hash,
                });
            }
            else
            {
                existing.PartId = chapter.PartId ?? existing.PartId;
                existing.SequenceNumber = chapter.SequenceNumber;
                existing.Title = chapter.Title;
                existing.RelativeMarkdownPath = relativePath;
                existing.WordCount = wordCount;
                existing.ContentHash = hash;
            }

            var project = await context.Projects
                .FirstOrDefaultAsync(record => record.Id == chapter.ProjectId, cancellationToken)
                .ConfigureAwait(false);
            if (project is not null)
            {
                project.LastEditedUtc = DateTimeOffset.UtcNow;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();

        chapter.RelativeMarkdownPath = relativePath;
        chapter.WordCount = wordCount;
    }

    public async Task<string> LoadAsync(
        string projectRootPath,
        Chapter chapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        ArgumentNullException.ThrowIfNull(chapter);

        var relativePath = NormalizeRelativePath(chapter.RelativeMarkdownPath);
        var absolutePath = Path.Combine(
            Path.GetFullPath(projectRootPath),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException($"Chapter markdown was not found: {absolutePath}", absolutePath);
        }

        return await File.ReadAllTextAsync(absolutePath, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string NormalizeRelativePath(string path)
        => path.Replace('\\', '/').TrimStart('/');
}
