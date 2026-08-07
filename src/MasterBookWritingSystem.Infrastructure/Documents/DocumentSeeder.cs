using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Documents;
using MasterBookWritingSystem.Core.Domain.Documents;
using MasterBookWritingSystem.Infrastructure.IO;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Documents;

public static class DocumentSeeder
{
    public static async Task SeedAsync(
        ProjectDbContext context,
        Guid projectId,
        string projectRootPath,
        IReadOnlyList<DocumentTemplateDefinition> templates,
        string seedRootPath,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.Documents
            .CountAsync(document => document.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (existing > 0)
        {
            return;
        }

        foreach (var template in templates)
        {
            var documentId = Guid.NewGuid();
            var relativePath = template.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar);
            var absolutePath = Path.Combine(projectRootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

            var sourceTemplate = Path.Combine(
                seedRootPath,
                template.TemplateFile.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(sourceTemplate))
            {
                if (!File.Exists(absolutePath))
                {
                    File.Copy(sourceTemplate, absolutePath, overwrite: false);
                }
            }
            else if (!File.Exists(absolutePath))
            {
                await AtomicFileWriter.WriteAllTextAsync(
                    absolutePath,
                    $"# {template.Title}\n",
                    cancellationToken).ConfigureAwait(false);
            }

            context.Documents.Add(new DocumentRecord
            {
                Id = documentId,
                ProjectId = projectId,
                DocumentType = template.DocumentType,
                Title = template.Title,
                RelativeMarkdownPath = template.RelativeMarkdownPath,
                TemplatePath = template.TemplateFile,
                CompletionPercentage = 0,
                LastEditedUtc = DateTimeOffset.UtcNow,
            });

            var order = 0;
            foreach (var field in template.Fields)
            {
                context.DocumentFields.Add(new DocumentFieldRecord
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    ProjectId = projectId,
                    Key = field.Key,
                    Label = field.Label,
                    IsRequired = field.IsRequired,
                    SortOrder = order++,
                    Value = string.Empty,
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public static string FindSeedRoot() => SeedPaths.FindSeedRoot();
}
