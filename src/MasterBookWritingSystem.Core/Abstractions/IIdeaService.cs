using MasterBookWritingSystem.Core.Domain.Tools;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IIdeaService
{
    Task<IReadOnlyList<Idea>> GetAllAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<Idea> GetAsync(Guid projectId, Guid ideaId, CancellationToken cancellationToken = default);

    Task<Idea> CreateAsync(Guid projectId, string title, string notes = "", CancellationToken cancellationToken = default);

    Task<Idea> UpdateAsync(Guid projectId, Idea idea, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid projectId, Guid ideaId, CancellationToken cancellationToken = default);

    Task<Idea> SaveScoreAsync(Guid projectId, Guid ideaId, IdeaScore score, CancellationToken cancellationToken = default);
}

public interface IToolsReportExporter
{
    Task<string> ExportMarkdownAsync(
        Guid projectId,
        string exportFileName,
        string markdownContent,
        CancellationToken cancellationToken = default);
}
