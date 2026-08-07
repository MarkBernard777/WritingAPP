using MasterBookWritingSystem.Core.Domain.Story;

namespace MasterBookWritingSystem.Core.Story;

/// <summary>
/// Orders scenes by manuscript chapter sequence, then scene sequence.
/// Unassigned scenes sort after all assigned chapters.
/// </summary>
public static class SceneChapterOrdering
{
    public const string UnassignedLabel = "Unassigned";

    public static IReadOnlyList<Scene> OrderByChapterThenSequence(
        IEnumerable<Scene> scenes,
        IReadOnlyDictionary<Guid, int> chapterSequenceById)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(chapterSequenceById);

        return scenes
            .OrderBy(scene => ResolveChapterSortKey(scene.ChapterId, chapterSequenceById))
            .ThenBy(scene => scene.SequenceNumber)
            .ThenBy(scene => scene.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<Scene> FilterByChapter(
        IEnumerable<Scene> scenes,
        Guid? chapterFilterId,
        bool unassignedOnly)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        if (unassignedOnly)
        {
            return scenes.Where(scene => scene.ChapterId is null).ToList();
        }

        if (chapterFilterId is { } chapterId)
        {
            return scenes.Where(scene => scene.ChapterId == chapterId).ToList();
        }

        return scenes.ToList();
    }

    public static string FormatChapterLabel(int sequenceNumber, string title)
        => $"{sequenceNumber}. {title}";

    private static int ResolveChapterSortKey(
        Guid? chapterId,
        IReadOnlyDictionary<Guid, int> chapterSequenceById)
    {
        if (chapterId is null)
        {
            return int.MaxValue;
        }

        return chapterSequenceById.TryGetValue(chapterId.Value, out var sequence)
            ? sequence
            : int.MaxValue - 1;
    }
}
