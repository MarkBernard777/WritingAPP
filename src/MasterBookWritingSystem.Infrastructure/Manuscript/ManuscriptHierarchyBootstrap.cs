using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Manuscript;

/// <summary>
/// Idempotent Book/Part backfill for new projects and v7→v8 upgrades.
/// Does not touch chapter Markdown files.
/// </summary>
internal static class ManuscriptHierarchyBootstrap
{
    public const string DefaultPartTitle = "Part 1";

    public static async Task EnsureAsync(
        ProjectDbContext context,
        Guid projectId,
        string projectTitle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectTitle);

        var now = DateTimeOffset.UtcNow;
        var createdBook = false;
        var createdPart = false;

        var books = await context.Books
            .Where(book => book.ProjectId == projectId)
            .OrderBy(book => book.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        BookRecord book;
        if (books.Count == 0)
        {
            book = new BookRecord
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                SequenceNumber = 1,
                Title = projectTitle.Trim(),
                CreatedUtc = now,
                LastEditedUtc = now,
            };
            context.Books.Add(book);
            books.Add(book);
            createdBook = true;
        }
        else
        {
            book = books[0];
        }

        var parts = await context.Parts
            .Where(part => part.ProjectId == projectId && part.BookId == book.Id)
            .OrderBy(part => part.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        PartRecord part;
        if (parts.Count == 0)
        {
            part = new PartRecord
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                BookId = book.Id,
                SequenceNumber = 1,
                Title = DefaultPartTitle,
                CreatedUtc = now,
                LastEditedUtc = now,
            };
            context.Parts.Add(part);
            parts.Add(part);
            createdPart = true;
        }
        else
        {
            part = parts[0];
        }

        // Only auto-attach chapters when establishing the default hierarchy (upgrade / first seed).
        // Intentional PartId=null after an unassign policy must be preserved on later opens.
        if (createdBook || createdPart)
        {
            var unassignedChapters = await context.Chapters
                .Where(chapter => chapter.ProjectId == projectId && chapter.PartId == null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var chapter in unassignedChapters)
            {
                chapter.PartId = part.Id;
            }
        }

        if (createdBook)
        {
            await RenumberScenesDenseAsync(context, projectId, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RenumberScenesDenseAsync(
        ProjectDbContext context,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var scenes = await context.Scenes
            .Where(scene => scene.ProjectId == projectId)
            .OrderBy(scene => scene.ChapterId)
            .ThenBy(scene => scene.SequenceNumber)
            .ThenBy(scene => scene.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (scenes.Count == 0)
        {
            return;
        }

        foreach (var group in scenes.GroupBy(scene => scene.ChapterId))
        {
            var sequence = 1;
            foreach (var scene in group)
            {
                scene.SequenceNumber = -sequence;
                sequence++;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var scene in scenes)
        {
            scene.SequenceNumber = Math.Abs(scene.SequenceNumber);
        }
    }
}
