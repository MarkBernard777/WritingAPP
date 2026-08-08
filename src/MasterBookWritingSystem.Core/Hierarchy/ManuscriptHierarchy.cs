using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Domain.Story;

namespace MasterBookWritingSystem.Core.Hierarchy;

public sealed class ManuscriptHierarchy
{
    public required Guid ProjectId { get; init; }

    public required IReadOnlyList<BookNode> Books { get; init; }

    public required IReadOnlyList<Scene> UnassignedScenes { get; init; }
}

public sealed class BookNode
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required int SequenceNumber { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<PartNode> Parts { get; init; }
}

public sealed class PartNode
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required Guid BookId { get; init; }

    public required int SequenceNumber { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<ChapterNode> Chapters { get; init; }
}

public sealed class ChapterNode
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public Guid? PartId { get; init; }

    public required int SequenceNumber { get; init; }

    public required string Title { get; init; }

    public required string RelativeMarkdownPath { get; init; }

    public required IReadOnlyList<Scene> Scenes { get; init; }
}
