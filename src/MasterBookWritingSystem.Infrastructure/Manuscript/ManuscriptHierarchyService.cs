using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Hierarchy;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Manuscript;

public sealed class ManuscriptHierarchyService : IManuscriptHierarchyService
{
    private readonly IProjectService _projectService;
    private readonly IStoryChangeNotifier _changes;

    public ManuscriptHierarchyService(IProjectService projectService, IStoryChangeNotifier changes)
    {
        _projectService = projectService;
        _changes = changes;
    }

    public async Task<ManuscriptHierarchy> GetHierarchyAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var books = await context.Books
            .AsNoTracking()
            .Where(book => book.ProjectId == projectId)
            .OrderBy(book => book.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var parts = await context.Parts
            .AsNoTracking()
            .Where(part => part.ProjectId == projectId)
            .OrderBy(part => part.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var chapters = await context.Chapters
            .AsNoTracking()
            .Where(chapter => chapter.ProjectId == projectId)
            .OrderBy(chapter => chapter.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var scenes = await context.Scenes
            .AsNoTracking()
            .Where(scene => scene.ProjectId == projectId)
            .OrderBy(scene => scene.SequenceNumber)
            .ThenBy(scene => scene.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();

        var scenesByChapter = scenes
            .Where(scene => scene.ChapterId is not null)
            .GroupBy(scene => scene.ChapterId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Scene>)group.Select(ToScene).ToList());

        var unassigned = scenes
            .Where(scene => scene.ChapterId is null)
            .Select(ToScene)
            .ToList();

        static IReadOnlyList<ChapterNode> BuildChapterNodes(
            IEnumerable<ChapterRecord> source,
            IReadOnlyDictionary<Guid, IReadOnlyList<Scene>> scenesByChapterId)
            => source.Select(chapter => new ChapterNode
            {
                Id = chapter.Id,
                ProjectId = chapter.ProjectId,
                PartId = chapter.PartId,
                SequenceNumber = chapter.SequenceNumber,
                Title = chapter.Title,
                RelativeMarkdownPath = chapter.RelativeMarkdownPath,
                Scenes = scenesByChapterId.TryGetValue(chapter.Id, out var linked)
                    ? linked
                    : Array.Empty<Scene>(),
            }).ToList();

        var bookNodes = books.Select(book =>
        {
            var bookParts = parts
                .Where(part => part.BookId == book.Id)
                .OrderBy(part => part.SequenceNumber)
                .Select(part => new PartNode
                {
                    Id = part.Id,
                    ProjectId = part.ProjectId,
                    BookId = part.BookId,
                    SequenceNumber = part.SequenceNumber,
                    Title = part.Title,
                    Chapters = BuildChapterNodes(
                        chapters.Where(chapter => chapter.PartId == part.Id),
                        scenesByChapter),
                })
                .ToList();

            return new BookNode
            {
                Id = book.Id,
                ProjectId = book.ProjectId,
                SequenceNumber = book.SequenceNumber,
                Title = book.Title,
                Parts = bookParts,
            };
        }).ToList();

        var ungroupedChapters = BuildChapterNodes(
            chapters.Where(chapter => chapter.PartId is null),
            scenesByChapter);

        return new ManuscriptHierarchy
        {
            ProjectId = projectId,
            Books = bookNodes,
            UngroupedChapters = ungroupedChapters,
            UnassignedScenes = unassigned,
        };
    }

    public async Task EnsureDefaultHierarchyAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);
        var title = _projectService.ActiveProject!.Title;
        await using var context = ProjectDbContextFactory.Create(
            Path.Combine(rootPath, ProjectPaths.DatabaseFileName));
        await ManuscriptHierarchyBootstrap
            .EnsureAsync(context, projectId, title, cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public async Task<Book> CreateBookAsync(
        Guid projectId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await using var context = Open(projectId);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var next = await NextBookSequenceAsync(context, projectId, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var record = new BookRecord
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            SequenceNumber = next,
            Title = title.Trim(),
            CreatedUtc = now,
            LastEditedUtc = now,
        };
        context.Books.Add(record);
        await TouchProjectAsync(context, projectId, now, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToBook(record);
    }

    public async Task<Book> RenameBookAsync(
        Guid projectId,
        Guid bookId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await using var context = Open(projectId);
        var record = await RequireBookAsync(context, projectId, bookId, cancellationToken)
            .ConfigureAwait(false);
        record.Title = title.Trim();
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await TouchProjectAsync(context, projectId, record.LastEditedUtc.Value, cancellationToken)
            .ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToBook(record);
    }

    public async Task DeleteBookAsync(
        Guid projectId,
        Guid bookId,
        HierarchyDeletionPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var record = await RequireBookAsync(context, projectId, bookId, cancellationToken)
            .ConfigureAwait(false);
        var parts = await context.Parts
            .Where(part => part.ProjectId == projectId && part.BookId == bookId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (parts.Count > 0)
        {
            if (policy?.MovePartsToBookId is not { } targetBookId)
            {
                throw new InvalidOperationException(
                    $"Book '{bookId}' still has {parts.Count} part(s). Move or delete parts first, or supply MovePartsToBookId.");
            }

            if (targetBookId == bookId)
            {
                throw new InvalidOperationException("Cannot move parts onto the book being deleted.");
            }

            await RequireBookAsync(context, projectId, targetBookId, cancellationToken)
                .ConfigureAwait(false);
            var next = await context.Parts
                .Where(part => part.BookId == targetBookId)
                .Select(part => (int?)part.SequenceNumber)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0;
            foreach (var part in parts.OrderBy(part => part.SequenceNumber))
            {
                part.BookId = targetBookId;
                part.SequenceNumber = ++next;
                part.LastEditedUtc = DateTimeOffset.UtcNow;
            }
        }

        context.Books.Remove(record);
        await RenumberBooksDenseAsync(context, projectId, cancellationToken).ConfigureAwait(false);
        await TouchProjectAsync(context, projectId, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public Task<Book> MoveBookAsync(
        Guid projectId,
        Guid bookId,
        int direction,
        CancellationToken cancellationToken = default)
        => MoveOrderedAsync(
            projectId,
            bookId,
            direction,
            loadAsync: (context, id, ct) => context.Books
                .Where(book => book.ProjectId == id)
                .OrderBy(book => book.SequenceNumber)
                .ToListAsync(ct),
            getId: book => book.Id,
            getSequence: book => book.SequenceNumber,
            setSequence: (book, sequence) => book.SequenceNumber = sequence,
            touch: book => book.LastEditedUtc = DateTimeOffset.UtcNow,
            toDomain: ToBook,
            cancellationToken);

    public async Task<Book> ReorderBookAsync(
        Guid projectId,
        Guid bookId,
        int newSequenceNumber,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var books = await context.Books
            .Where(book => book.ProjectId == projectId)
            .OrderBy(book => book.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (newSequenceNumber < 1 || newSequenceNumber > books.Count)
        {
            throw new InvalidOperationException(
                $"Book sequence must be between 1 and {books.Count}.");
        }

        var target = books.FirstOrDefault(book => book.Id == bookId)
            ?? throw new InvalidOperationException($"Book '{bookId}' was not found.");
        books.Remove(target);
        books.Insert(newSequenceNumber - 1, target);
        for (var index = 0; index < books.Count; index++)
        {
            books[index].SequenceNumber = -(index + 1);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < books.Count; index++)
        {
            books[index].SequenceNumber = index + 1;
            books[index].LastEditedUtc = DateTimeOffset.UtcNow;
        }

        await TouchProjectAsync(context, projectId, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToBook(target);
    }

    public async Task<Part> CreatePartAsync(
        Guid projectId,
        Guid bookId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await using var context = Open(projectId);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await RequireBookAsync(context, projectId, bookId, cancellationToken).ConfigureAwait(false);
        var next = await context.Parts
            .Where(part => part.BookId == bookId)
            .Select(part => (int?)part.SequenceNumber)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;
        var now = DateTimeOffset.UtcNow;
        var record = new PartRecord
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            BookId = bookId,
            SequenceNumber = next + 1,
            Title = title.Trim(),
            CreatedUtc = now,
            LastEditedUtc = now,
        };
        context.Parts.Add(record);
        await TouchProjectAsync(context, projectId, now, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToPart(record);
    }

    public async Task<Part> RenamePartAsync(
        Guid projectId,
        Guid partId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await using var context = Open(projectId);
        var record = await RequirePartAsync(context, projectId, partId, cancellationToken)
            .ConfigureAwait(false);
        record.Title = title.Trim();
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await TouchProjectAsync(context, projectId, record.LastEditedUtc.Value, cancellationToken)
            .ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToPart(record);
    }

    public async Task DeletePartAsync(
        Guid projectId,
        Guid partId,
        HierarchyDeletionPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var record = await RequirePartAsync(context, projectId, partId, cancellationToken)
            .ConfigureAwait(false);
        var chapters = await context.Chapters
            .Where(chapter => chapter.ProjectId == projectId && chapter.PartId == partId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (chapters.Count > 0)
        {
            if (policy?.MoveChaptersToPartId is { } targetPartId)
            {
                if (targetPartId == partId)
                {
                    throw new InvalidOperationException("Cannot move chapters onto the part being deleted.");
                }

                await RequirePartAsync(context, projectId, targetPartId, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var chapter in chapters)
                {
                    chapter.PartId = targetPartId;
                }
            }
            else if (policy?.UnassignChapters == true)
            {
                foreach (var chapter in chapters)
                {
                    chapter.PartId = null;
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Part '{partId}' still has {chapters.Count} chapter(s). Move or unassign them first, or supply a deletion policy.");
            }
        }

        context.Parts.Remove(record);
        await RenumberPartsDenseAsync(context, projectId, record.BookId, cancellationToken)
            .ConfigureAwait(false);
        await TouchProjectAsync(context, projectId, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public Task<Part> MovePartAsync(
        Guid projectId,
        Guid partId,
        int direction,
        CancellationToken cancellationToken = default)
        => MovePartInternalAsync(projectId, partId, direction, cancellationToken);

    public async Task<Chapter> MoveChapterToPartAsync(
        Guid projectId,
        Guid chapterId,
        Guid? partId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var chapter = await context.Chapters
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == chapterId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Chapter '{chapterId}' was not found.");

        if (partId is { } targetPartId)
        {
            await RequirePartAsync(context, projectId, targetPartId, cancellationToken)
                .ConfigureAwait(false);
            chapter.PartId = targetPartId;
        }
        else
        {
            chapter.PartId = null;
        }

        await TouchProjectAsync(context, projectId, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToChapter(chapter);
    }

    public Task<Scene> AssignSceneToChapterAsync(
        Guid projectId,
        Guid sceneId,
        Guid chapterId,
        CancellationToken cancellationToken = default)
        => MoveSceneToChapterAsync(projectId, sceneId, chapterId, cancellationToken);

    public Task<Scene> UnassignSceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
        => MoveSceneToChapterAsync(projectId, sceneId, chapterId: null, cancellationToken);

    public async Task<Scene> MoveSceneToChapterAsync(
        Guid projectId,
        Guid sceneId,
        Guid? chapterId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var scene = await context.Scenes
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == sceneId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Scene '{sceneId}' was not found.");

        var sourceChapterId = scene.ChapterId;
        if (sourceChapterId == chapterId)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            SqliteConnection.ClearAllPools();
            return ToScene(scene);
        }

        if (chapterId is { } targetChapterId)
        {
            var exists = await context.Chapters.AnyAsync(
                    item => item.ProjectId == projectId && item.Id == targetChapterId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!exists)
            {
                throw new InvalidOperationException($"Chapter '{targetChapterId}' was not found.");
            }
        }

        // Park the scene outside both unique buckets while source/destination are renumbered.
        scene.ChapterId = null;
        scene.SequenceNumber = int.MinValue;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await RenumberScenesInBucketAsync(
                context,
                projectId,
                sourceChapterId,
                excludingSceneId: scene.Id,
                cancellationToken)
            .ConfigureAwait(false);

        var next = await NextSceneSequenceAsync(context, projectId, chapterId, excludingSceneId: scene.Id, cancellationToken)
            .ConfigureAwait(false);
        scene.ChapterId = chapterId;
        scene.SequenceNumber = next;
        scene.LastEditedUtc = DateTimeOffset.UtcNow;

        await TouchProjectAsync(context, projectId, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        PublishScene(projectId, scene.Id);
        return ToScene(scene);
    }

    public async Task<Scene> MoveSceneAsync(
        Guid projectId,
        Guid sceneId,
        int direction,
        CancellationToken cancellationToken = default)
    {
        if (direction is not (1 or -1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Direction must be -1 or 1.");
        }

        await using var context = Open(projectId);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var scene = await context.Scenes
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == sceneId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Scene '{sceneId}' was not found.");

        var bucket = await context.Scenes
            .Where(item => item.ProjectId == projectId && item.ChapterId == scene.ChapterId)
            .OrderBy(item => item.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var index = bucket.FindIndex(item => item.Id == sceneId);
        var swapIndex = index + direction;
        if (index < 0 || swapIndex < 0 || swapIndex >= bucket.Count)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            SqliteConnection.ClearAllPools();
            return ToScene(scene);
        }

        var currentSequence = bucket[index].SequenceNumber;
        var neighborSequence = bucket[swapIndex].SequenceNumber;
        bucket[index].SequenceNumber = -Math.Abs(currentSequence) - 1;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        bucket[swapIndex].SequenceNumber = currentSequence;
        bucket[index].SequenceNumber = neighborSequence;
        bucket[index].LastEditedUtc = DateTimeOffset.UtcNow;
        bucket[swapIndex].LastEditedUtc = DateTimeOffset.UtcNow;
        await TouchProjectAsync(context, projectId, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        PublishScene(projectId, bucket[index].Id);
        return ToScene(bucket[index]);
    }

    private void PublishScene(Guid projectId, Guid sceneId)
        => _changes.Publish(new StoryChangeEventArgs
        {
            ProjectId = projectId,
            Kind = StoryChangeKind.SceneUpserted,
            EntityId = sceneId,
        });

    public async Task<Guid?> GetDefaultPartIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var partId = await context.Parts
            .AsNoTracking()
            .Where(part => part.ProjectId == projectId)
            .OrderBy(part => part.BookId)
            .ThenBy(part => part.SequenceNumber)
            .Select(part => (Guid?)part.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return partId;
    }

    private async Task<Part> MovePartInternalAsync(
        Guid projectId,
        Guid partId,
        int direction,
        CancellationToken cancellationToken)
    {
        if (direction is not (1 or -1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Direction must be -1 or 1.");
        }

        await using var context = Open(projectId);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var part = await RequirePartAsync(context, projectId, partId, cancellationToken)
            .ConfigureAwait(false);
        var siblings = await context.Parts
            .Where(item => item.BookId == part.BookId)
            .OrderBy(item => item.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var index = siblings.FindIndex(item => item.Id == partId);
        var swapIndex = index + direction;
        if (index < 0 || swapIndex < 0 || swapIndex >= siblings.Count)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            SqliteConnection.ClearAllPools();
            return ToPart(part);
        }

        var current = siblings[index].SequenceNumber;
        var neighbor = siblings[swapIndex].SequenceNumber;
        siblings[index].SequenceNumber = -Math.Abs(current) - 1;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        siblings[swapIndex].SequenceNumber = current;
        siblings[index].SequenceNumber = neighbor;
        siblings[index].LastEditedUtc = DateTimeOffset.UtcNow;
        await TouchProjectAsync(context, projectId, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToPart(siblings[index]);
    }

    private async Task<TDomain> MoveOrderedAsync<TRecord, TDomain>(
        Guid projectId,
        Guid entityId,
        int direction,
        Func<ProjectDbContext, Guid, CancellationToken, Task<List<TRecord>>> loadAsync,
        Func<TRecord, Guid> getId,
        Func<TRecord, int> getSequence,
        Action<TRecord, int> setSequence,
        Action<TRecord> touch,
        Func<TRecord, TDomain> toDomain,
        CancellationToken cancellationToken)
        where TRecord : class
    {
        if (direction is not (1 or -1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Direction must be -1 or 1.");
        }

        await using var context = Open(projectId);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await loadAsync(context, projectId, cancellationToken).ConfigureAwait(false);
        var index = items.FindIndex(item => getId(item) == entityId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Entity '{entityId}' was not found.");
        }

        var swapIndex = index + direction;
        if (swapIndex < 0 || swapIndex >= items.Count)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            SqliteConnection.ClearAllPools();
            return toDomain(items[index]);
        }

        var current = getSequence(items[index]);
        var neighbor = getSequence(items[swapIndex]);
        setSequence(items[index], -Math.Abs(current) - 1);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        setSequence(items[swapIndex], current);
        setSequence(items[index], neighbor);
        touch(items[index]);
        touch(items[swapIndex]);
        await TouchProjectAsync(context, projectId, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return toDomain(items[index]);
    }

    private ProjectDbContext Open(Guid projectId)
    {
        var rootPath = RequireActiveRoot(projectId);
        return ProjectDbContextFactory.Create(Path.Combine(rootPath, ProjectPaths.DatabaseFileName));
    }

    private string RequireActiveRoot(Guid projectId)
    {
        var active = _projectService.ActiveProject
            ?? throw new InvalidOperationException("Open a project before using manuscript hierarchy services.");
        if (active.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return active.RootPath;
    }

    private static async Task<BookRecord> RequireBookAsync(
        ProjectDbContext context,
        Guid projectId,
        Guid bookId,
        CancellationToken cancellationToken)
        => await context.Books
               .FirstOrDefaultAsync(
                   book => book.ProjectId == projectId && book.Id == bookId,
                   cancellationToken)
               .ConfigureAwait(false)
           ?? throw new InvalidOperationException($"Book '{bookId}' was not found.");

    private static async Task<PartRecord> RequirePartAsync(
        ProjectDbContext context,
        Guid projectId,
        Guid partId,
        CancellationToken cancellationToken)
        => await context.Parts
               .FirstOrDefaultAsync(
                   part => part.ProjectId == projectId && part.Id == partId,
                   cancellationToken)
               .ConfigureAwait(false)
           ?? throw new InvalidOperationException($"Part '{partId}' was not found.");

    private static async Task<int> NextBookSequenceAsync(
        ProjectDbContext context,
        Guid projectId,
        CancellationToken cancellationToken)
        => (await context.Books
                .Where(book => book.ProjectId == projectId)
                .Select(book => (int?)book.SequenceNumber)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0) + 1;

    private static async Task<int> NextSceneSequenceAsync(
        ProjectDbContext context,
        Guid projectId,
        Guid? chapterId,
        Guid? excludingSceneId,
        CancellationToken cancellationToken)
        => (await context.Scenes
                .Where(scene => scene.ProjectId == projectId
                    && scene.ChapterId == chapterId
                    && (!excludingSceneId.HasValue || scene.Id != excludingSceneId.Value)
                    && scene.SequenceNumber > 0)
                .Select(scene => (int?)scene.SequenceNumber)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0) + 1;

    private static async Task RenumberBooksDenseAsync(
        ProjectDbContext context,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var books = await context.Books
            .Where(book => book.ProjectId == projectId)
            .OrderBy(book => book.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        for (var index = 0; index < books.Count; index++)
        {
            books[index].SequenceNumber = -(index + 1);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < books.Count; index++)
        {
            books[index].SequenceNumber = index + 1;
        }
    }

    private static async Task RenumberPartsDenseAsync(
        ProjectDbContext context,
        Guid projectId,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var parts = await context.Parts
            .Where(part => part.ProjectId == projectId && part.BookId == bookId)
            .OrderBy(part => part.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        for (var index = 0; index < parts.Count; index++)
        {
            parts[index].SequenceNumber = -(index + 1);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < parts.Count; index++)
        {
            parts[index].SequenceNumber = index + 1;
        }
    }

    private static async Task RenumberScenesInBucketAsync(
        ProjectDbContext context,
        Guid projectId,
        Guid? chapterId,
        Guid? excludingSceneId,
        CancellationToken cancellationToken)
    {
        var scenes = await context.Scenes
            .Where(scene => scene.ProjectId == projectId
                && scene.ChapterId == chapterId
                && (!excludingSceneId.HasValue || scene.Id != excludingSceneId.Value)
                && scene.SequenceNumber != int.MinValue)
            .OrderBy(scene => scene.SequenceNumber)
            .ThenBy(scene => scene.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        for (var index = 0; index < scenes.Count; index++)
        {
            scenes[index].SequenceNumber = -(index + 1);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < scenes.Count; index++)
        {
            scenes[index].SequenceNumber = index + 1;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task TouchProjectAsync(
        ProjectDbContext context,
        Guid projectId,
        DateTimeOffset when,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects
            .FirstOrDefaultAsync(item => item.Id == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is not null)
        {
            project.LastEditedUtc = when;
        }
    }

    private static Book ToBook(BookRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        SequenceNumber = record.SequenceNumber,
        Title = record.Title,
        CreatedUtc = record.CreatedUtc,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static Part ToPart(PartRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        BookId = record.BookId,
        SequenceNumber = record.SequenceNumber,
        Title = record.Title,
        CreatedUtc = record.CreatedUtc,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static Chapter ToChapter(ChapterRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        PartId = record.PartId,
        SequenceNumber = record.SequenceNumber,
        Title = record.Title,
        RelativeMarkdownPath = record.RelativeMarkdownPath,
        WordCount = record.WordCount,
    };

    private static Scene ToScene(SceneRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        ChapterId = record.ChapterId,
        SequenceNumber = record.SequenceNumber,
        Title = record.Title,
        ViewpointCharacterId = record.ViewpointCharacterId,
        Location = record.Location,
        Time = record.Time,
        Goal = record.Goal,
        Opposition = record.Opposition,
        Stakes = record.Stakes,
        MainEvent = record.MainEvent,
        Revelation = record.Revelation,
        EmotionalTurn = record.EmotionalTurn,
        Choice = record.Choice,
        Outcome = record.Outcome,
        Consequence = record.Consequence,
        SetupObligations = record.SetupObligations,
        PayoffObligations = record.PayoffObligations,
        NextSceneId = record.NextSceneId,
        Status = record.Status,
        WordCount = record.WordCount,
        LastEditedUtc = record.LastEditedUtc,
    };
}
