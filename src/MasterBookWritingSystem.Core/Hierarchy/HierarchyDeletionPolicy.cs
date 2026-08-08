namespace MasterBookWritingSystem.Core.Hierarchy;

/// <summary>
/// Confirmed policy required before deleting a parent that still has children.
/// Without a policy, destructive parent deletion is rejected.
/// </summary>
public sealed class HierarchyDeletionPolicy
{
    /// <summary>When deleting a book, move its parts to this book.</summary>
    public Guid? MovePartsToBookId { get; init; }

    /// <summary>When deleting a part, move its chapters to this part.</summary>
    public Guid? MoveChaptersToPartId { get; init; }

    /// <summary>When deleting a part, set <c>Chapter.PartId</c> to null (chapters and files kept).</summary>
    public bool UnassignChapters { get; init; }
}
