namespace MasterBookWritingSystem.Core.Hierarchy;

public enum HierarchyNodeKind
{
    Book = 0,
    Part = 1,
    Chapter = 2,
    Scene = 3,
    UnassignedGroup = 4,
    UngroupedChaptersGroup = 5,
}

public enum HierarchyDropAction
{
    None = 0,
    ReorderBefore = 1,
    MoveChapterToPart = 2,
    AssignSceneToChapter = 3,
}

public readonly record struct HierarchyNodeKey(HierarchyNodeKind Kind, Guid Id);

public readonly record struct HierarchySelection(
    HierarchyNodeKind Kind,
    Guid Id,
    Guid? EditorChapterId,
    Guid? ContextSceneId);

public static class ManuscriptHierarchyInteractions
{
    public static readonly Guid UnassignedGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid UngroupedChaptersGroupId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static HierarchySelection ResolveSelection(
        HierarchyNodeKind kind,
        Guid id,
        Guid? parentChapterId)
        => kind switch
        {
            HierarchyNodeKind.Chapter => new HierarchySelection(kind, id, id, null),
            HierarchyNodeKind.Scene => new HierarchySelection(kind, id, parentChapterId, id),
            _ => new HierarchySelection(kind, id, null, null),
        };

    public static HierarchyDropAction ClassifyDrop(
        HierarchyNodeKind sourceKind,
        Guid sourceId,
        HierarchyNodeKind targetKind,
        Guid targetId,
        Guid? sourceParentId,
        Guid? targetParentId)
    {
        if (sourceId == targetId && sourceKind == targetKind)
        {
            return HierarchyDropAction.None;
        }

        if (sourceKind == HierarchyNodeKind.Chapter && targetKind == HierarchyNodeKind.Part)
        {
            return HierarchyDropAction.MoveChapterToPart;
        }

        if (sourceKind == HierarchyNodeKind.Scene && targetKind == HierarchyNodeKind.Chapter)
        {
            return HierarchyDropAction.AssignSceneToChapter;
        }

        if (sourceKind == targetKind
            && sourceKind is HierarchyNodeKind.Book or HierarchyNodeKind.Part or HierarchyNodeKind.Chapter or HierarchyNodeKind.Scene
            && sourceParentId == targetParentId)
        {
            return HierarchyDropAction.ReorderBefore;
        }

        return HierarchyDropAction.None;
    }

    public static bool IsValidDrop(HierarchyDropAction action) => action != HierarchyDropAction.None;

    public static IReadOnlySet<HierarchyNodeKey> CaptureExpanded(
        IEnumerable<(HierarchyNodeKind Kind, Guid Id, bool IsExpanded)> nodes)
    {
        var set = new HashSet<HierarchyNodeKey>();
        foreach (var node in nodes)
        {
            if (node.IsExpanded)
            {
                set.Add(new HierarchyNodeKey(node.Kind, node.Id));
            }
        }

        return set;
    }

    public static bool ShouldExpand(
        HierarchyNodeKind kind,
        Guid id,
        IReadOnlySet<HierarchyNodeKey>? previouslyExpanded,
        bool defaultExpanded)
    {
        if (previouslyExpanded is null)
        {
            return defaultExpanded;
        }

        return previouslyExpanded.Contains(new HierarchyNodeKey(kind, id));
    }

    public static bool MatchesFilter(string? filter, params string?[] titles)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        foreach (var title in titles)
        {
            if (!string.IsNullOrEmpty(title)
                && title.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
