using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Documents;
using MasterBookWritingSystem.Core.Workflow;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Documents;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Documents;

public sealed class DocumentServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IDocumentService _documents;
    private readonly IWorkflowService _workflow;

    public DocumentServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-doc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        services.AddSingleton<IDocumentTemplateCatalog>(
            new FileDocumentTemplateCatalog(FindPath("seed", "schemas", "document-templates.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _documents = _provider.GetRequiredService<IDocumentService>();
        _workflow = _provider.GetRequiredService<IWorkflowService>();
    }

    public void Dispose()
    {
        _projects.CloseAsync().GetAwaiter().GetResult();
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task CreateProject_Seeds_All_24_Documents_WithFields()
    {
        var project = await CreateAsync("Docs Seed");

        var docs = await _documents.GetAllAsync(project.Id);

        Assert.Equal(24, docs.Count);
        Assert.All(Enum.GetValues<DocumentType>(), type =>
            Assert.Contains(docs, doc => doc.DocumentType == type));

        var definition = await _documents.GetAsync(project.Id, DocumentType.ProjectDefinition);
        Assert.True(definition.Fields.Count >= 20);
        Assert.Equal(0, definition.CompletionPercentage);
        Assert.True(File.Exists(Path.Combine(project.RootPath, definition.RelativeMarkdownPath)));
    }

    [Fact]
    public async Task SavingFields_UpdatesCompletionPercentage()
    {
        var project = await CreateAsync("Completion");
        var document = await _documents.GetAsync(project.Id, DocumentType.ThemeMap);
        var required = document.Fields.Where(field => field.IsRequired).Take(3).ToList();

        foreach (var field in required)
        {
            await _documents.UpdateFieldAsync(project.Id, document.Id, field.Key, "Filled");
        }

        var updated = await _documents.GetAsync(project.Id, DocumentType.ThemeMap);
        Assert.True(updated.CompletionPercentage > 0);
        Assert.True(updated.CompletionPercentage < 100);
        Assert.All(required, field =>
            Assert.Equal("Filled", updated.Fields.Single(item => item.Key == field.Key).Value));
    }

    [Fact]
    public async Task Validate_ReportsMissingRequiredFields()
    {
        var project = await CreateAsync("Validate");
        var result = await _documents.ValidateAsync(project.Id, DocumentType.PremiseDocument);

        Assert.False(result.IsComplete);
        Assert.NotEmpty(result.MissingRequiredFieldKeys);
    }

    [Fact]
    public async Task Gate_RequiresDocumentFields_InAdditionToSteps()
    {
        var project = await CreateAsync("Gate Docs");
        var phase = (await _workflow.GetAllPhasesAsync(project.Id)).Single(item => item.Id == "4");

        foreach (var step in phase.Steps)
        {
            await _workflow.CompleteStepAsync(project.Id, phase.Id, step.Number);
        }

        Assert.False(await _workflow.CanPassGateAsync(project.Id, phase.Id));

        var document = await _documents.GetAsync(project.Id, DocumentType.ThemeMap);
        foreach (var field in document.Fields.Where(item => item.IsRequired))
        {
            await _documents.UpdateFieldAsync(project.Id, document.Id, field.Key, "Ready");
        }

        Assert.True(await _workflow.CanPassGateAsync(project.Id, phase.Id));

        var passed = await _workflow.PassGateAsync(project.Id, new PhaseGateCompletion
        {
            PhaseId = phase.Id,
            ConfirmedByUser = true,
            Evidence = "Theme Map complete",
            Notes = "Validated",
            PassedUtc = DateTimeOffset.UtcNow,
        });

        Assert.True(passed.IsPassed);
    }

    [Fact]
    public async Task OpenExistingProject_SeedsDocuments_WhenMissing()
    {
        var project = await CreateAsync("Legacy Docs");
        var root = project.RootPath;
        var databasePath = Path.Combine(root, ProjectPaths.DatabaseFileName);

        await using (var context = ProjectDbContextFactory.Create(databasePath))
        {
            context.DocumentFields.RemoveRange(context.DocumentFields);
            context.Documents.RemoveRange(context.Documents);
            await context.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();
        await _projects.CloseAsync();
        var reopened = await _projects.OpenAsync(root);
        var docs = await _documents.GetAllAsync(reopened.Id);

        Assert.Equal(24, docs.Count);
    }

    [Fact]
    public async Task TemplateCatalog_Defines_24_VersionedSchemas()
    {
        var catalog = _provider.GetRequiredService<IDocumentTemplateCatalog>();
        var templates = await catalog.GetAllAsync();

        Assert.Equal(24, templates.Count);
        Assert.Equal(1, catalog.SchemaVersion);
        Assert.Contains(templates, template => template.DocumentType == DocumentType.SeriesContinuityLedger);
    }

    private async Task<Project> CreateAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);
        return await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = title,
            Author = "Tester",
            Genre = "Fantasy",
            NorthStar = "Finish",
        });
    }

    private static string FindPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }
}
