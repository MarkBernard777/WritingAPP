using MasterBookWritingSystem.Core.Domain.Story;

namespace MasterBookWritingSystem.Core.Story;

public sealed class SceneCorkboardFilter
{
    public Guid? BookId { get; init; }

    public Guid? PartId { get; init; }

    public Guid? ChapterId { get; init; }

    public bool UnassignedChaptersOnly { get; init; }

    public Guid? ViewpointCharacterId { get; init; }

    public SceneDraftStatus? Status { get; init; }

    public bool IsClear =>
        BookId is null
        && PartId is null
        && ChapterId is null
        && !UnassignedChaptersOnly
        && ViewpointCharacterId is null
        && Status is null;
}

/// <summary>
/// Composable corkboard filters over canonical <see cref="Scene"/> rows.
/// Chapter→Part→Book maps come from hierarchy metadata (not duplicated scene data).
/// </summary>
public static class SceneCorkboardFiltering
{
    public static IReadOnlyList<Scene> Apply(
        IEnumerable<Scene> scenes,
        SceneCorkboardFilter filter,
        IReadOnlyDictionary<Guid, Guid?> chapterPartById,
        IReadOnlyDictionary<Guid, Guid> partBookById,
        IReadOnlyDictionary<Guid, int> chapterSequenceById)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(chapterPartById);
        ArgumentNullException.ThrowIfNull(partBookById);
        ArgumentNullException.ThrowIfNull(chapterSequenceById);

        IEnumerable<Scene> query = scenes;

        if (filter.UnassignedChaptersOnly)
        {
            query = query.Where(scene => scene.ChapterId is null);
        }
        else if (filter.ChapterId is { } chapterId)
        {
            query = query.Where(scene => scene.ChapterId == chapterId);
        }
        else if (filter.PartId is { } partId)
        {
            query = query.Where(scene =>
                scene.ChapterId is { } cid
                && chapterPartById.TryGetValue(cid, out var part)
                && part == partId);
        }
        else if (filter.BookId is { } bookId)
        {
            query = query.Where(scene =>
                scene.ChapterId is { } cid
                && chapterPartById.TryGetValue(cid, out var part)
                && part is { } pid
                && partBookById.TryGetValue(pid, out var book)
                && book == bookId);
        }

        if (filter.ViewpointCharacterId is { } viewpointId)
        {
            query = query.Where(scene => scene.ViewpointCharacterId == viewpointId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(scene => scene.Status == status);
        }

        return SceneChapterOrdering.OrderByChapterThenSequence(query, chapterSequenceById);
    }
}
