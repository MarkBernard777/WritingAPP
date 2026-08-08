using MasterBookWritingSystem.Core.Hierarchy;

namespace MasterBookWritingSystem.Tests.Hierarchy;

public sealed class ManuscriptHierarchyInteractionTests
{
    [Fact]
    public void ResolveSelection_Scene_UsesParentChapterForEditor()
    {
        var chapterId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var selection = ManuscriptHierarchyInteractions.ResolveSelection(
            HierarchyNodeKind.Scene,
            sceneId,
            chapterId);

        Assert.Equal(HierarchyNodeKind.Scene, selection.Kind);
        Assert.Equal(sceneId, selection.Id);
        Assert.Equal(chapterId, selection.EditorChapterId);
        Assert.Equal(sceneId, selection.ContextSceneId);
    }

    [Fact]
    public void ResolveSelection_Chapter_LoadsItself()
    {
        var chapterId = Guid.NewGuid();
        var selection = ManuscriptHierarchyInteractions.ResolveSelection(
            HierarchyNodeKind.Chapter,
            chapterId,
            parentChapterId: null);

        Assert.Equal(chapterId, selection.EditorChapterId);
        Assert.Null(selection.ContextSceneId);
    }

    [Fact]
    public void ClassifyDrop_RejectsInvalidCombinations()
    {
        var action = ManuscriptHierarchyInteractions.ClassifyDrop(
            HierarchyNodeKind.Scene,
            Guid.NewGuid(),
            HierarchyNodeKind.Book,
            Guid.NewGuid(),
            sourceParentId: Guid.NewGuid(),
            targetParentId: null);

        Assert.Equal(HierarchyDropAction.None, action);
        Assert.False(ManuscriptHierarchyInteractions.IsValidDrop(action));
    }

    [Fact]
    public void ClassifyDrop_AllowsSceneOntoChapter_AndChapterOntoPart()
    {
        Assert.Equal(
            HierarchyDropAction.AssignSceneToChapter,
            ManuscriptHierarchyInteractions.ClassifyDrop(
                HierarchyNodeKind.Scene,
                Guid.NewGuid(),
                HierarchyNodeKind.Chapter,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null));

        Assert.Equal(
            HierarchyDropAction.MoveChapterToPart,
            ManuscriptHierarchyInteractions.ClassifyDrop(
                HierarchyNodeKind.Chapter,
                Guid.NewGuid(),
                HierarchyNodeKind.Part,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid()));
    }

    [Fact]
    public void ClassifyDrop_SiblingReorder_RequiresSameParent()
    {
        var parent = Guid.NewGuid();
        Assert.Equal(
            HierarchyDropAction.ReorderBefore,
            ManuscriptHierarchyInteractions.ClassifyDrop(
                HierarchyNodeKind.Part,
                Guid.NewGuid(),
                HierarchyNodeKind.Part,
                Guid.NewGuid(),
                parent,
                parent));

        Assert.Equal(
            HierarchyDropAction.None,
            ManuscriptHierarchyInteractions.ClassifyDrop(
                HierarchyNodeKind.Part,
                Guid.NewGuid(),
                HierarchyNodeKind.Part,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid()));
    }

    [Fact]
    public void Expansion_PreservedAcrossRefreshKeys()
    {
        var bookId = Guid.NewGuid();
        var partId = Guid.NewGuid();
        var expanded = ManuscriptHierarchyInteractions.CaptureExpanded(
        [
            (HierarchyNodeKind.Book, bookId, true),
            (HierarchyNodeKind.Part, partId, false),
        ]);

        Assert.True(ManuscriptHierarchyInteractions.ShouldExpand(
            HierarchyNodeKind.Book, bookId, expanded, defaultExpanded: false));
        Assert.False(ManuscriptHierarchyInteractions.ShouldExpand(
            HierarchyNodeKind.Part, partId, expanded, defaultExpanded: true));
    }

    [Fact]
    public void Filter_MatchesAncestorWhenChildTitleHits()
    {
        Assert.True(ManuscriptHierarchyInteractions.MatchesFilter("cave", "Chapter 1", "Into the Cave"));
        Assert.False(ManuscriptHierarchyInteractions.MatchesFilter("zzz", "Chapter 1", "Scene A"));
    }
}
