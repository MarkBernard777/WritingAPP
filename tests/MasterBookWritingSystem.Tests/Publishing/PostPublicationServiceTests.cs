using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Publishing;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Publishing;

public sealed class PostPublicationServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IPublishingService _publishing;

    public PostPublicationServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-postpub-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _publishing = _provider.GetRequiredService<IPublishingService>();
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
    public async Task Formats_Metadata_Performance_Corrections_Crud()
    {
        var project = await CreateAsync("PostPub");

        var format = await _publishing.CreatePublishingFormatAsync(project.Id, new PublishingFormat
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Ebook",
            FormatKind = PublishingFormatKind.Ebook,
            IsbnOrAsin = "B0TEST",
            Price = 4.99m,
            AssetLink = "11 Publishing/ebook.epub",
            PublicationStatus = PublicationStatus.Available,
        });

        var metadata = await _publishing.CreateMetadataRecordAsync(project.Id, new MetadataRecord
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Title = "Oathbreakers",
            Author = "Tester",
            Edition = "1st",
            FormatName = "Ebook",
            Isbn = "9780000000001",
            PublicationStatus = PublicationStatus.Available,
        });

        var performance = await _publishing.CreatePerformanceRecordAsync(project.Id, new PerformanceRecord
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            PeriodLabel = "2026-Q3",
            PeriodStart = new DateOnly(2026, 7, 1),
            PeriodEnd = new DateOnly(2026, 9, 30),
            Format = "Ebook",
            Sales = 1200,
            Reviews = 18,
            AdCost = 200,
        });

        var correction = await _publishing.CreateCorrectionAsync(project.Id, new Correction
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Error = "Typo on page 12",
            Location = "Ch 2",
            CorrectionText = "Fixed spelling",
            ReportedDate = new DateOnly(2026, 8, 1),
            CorrectedDate = new DateOnly(2026, 8, 2),
            FormatsUpdated = "Ebook",
            Status = CorrectionStatus.Corrected,
        });

        format.Price = 5.99m;
        await _publishing.UpdatePublishingFormatAsync(project.Id, format);
        metadata.Edition = "2nd";
        await _publishing.UpdateMetadataRecordAsync(project.Id, metadata);
        performance.Sales = 1500;
        await _publishing.UpdatePerformanceRecordAsync(project.Id, performance);
        correction.Status = CorrectionStatus.WonNotFix;
        await _publishing.UpdateCorrectionAsync(project.Id, correction);

        Assert.Equal(5.99m, (await _publishing.GetPublishingFormatAsync(project.Id, format.Id)).Price);
        Assert.Equal("2nd", (await _publishing.GetMetadataRecordAsync(project.Id, metadata.Id)).Edition);
        Assert.Equal(1500m, (await _publishing.GetPerformanceRecordAsync(project.Id, performance.Id)).Sales);
        Assert.Equal(CorrectionStatus.WonNotFix, (await _publishing.GetCorrectionAsync(project.Id, correction.Id)).Status);

        await _publishing.DeletePublishingFormatAsync(project.Id, format.Id, confirmed: true);
        await _publishing.DeleteMetadataRecordAsync(project.Id, metadata.Id, confirmed: true);
        await _publishing.DeletePerformanceRecordAsync(project.Id, performance.Id, confirmed: true);
        await _publishing.DeleteCorrectionAsync(project.Id, correction.Id, confirmed: true);

        Assert.Empty(await _publishing.GetPublishingFormatsAsync(project.Id));
        Assert.Empty(await _publishing.GetMetadataRecordsAsync(project.Id));
        Assert.Empty(await _publishing.GetPerformanceRecordsAsync(project.Id));
        Assert.Empty(await _publishing.GetCorrectionsAsync(project.Id));
    }

    [Fact]
    public async Task DeleteRequiresConfirmation_ForNewTypes()
    {
        var project = await CreateAsync("Confirm");
        var format = await _publishing.CreatePublishingFormatAsync(project.Id, new PublishingFormat
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Keep",
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _publishing.DeletePublishingFormatAsync(project.Id, format.Id, confirmed: false));
        Assert.Contains("not confirmed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await _publishing.GetPublishingFormatsAsync(project.Id));
    }

    [Fact]
    public async Task FilteringAndSorting_Work()
    {
        var project = await CreateAsync("Filters");
        await _publishing.CreatePublishingFormatAsync(project.Id, new PublishingFormat
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Print",
            PublicationStatus = PublicationStatus.Available,
        });
        await _publishing.CreatePublishingFormatAsync(project.Id, new PublishingFormat
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Draft ebook",
            PublicationStatus = PublicationStatus.Planned,
        });

        Assert.Single(await _publishing.GetPublishingFormatsAsync(project.Id, PublicationStatus.Available));

        await _publishing.CreatePerformanceRecordAsync(project.Id, new PerformanceRecord
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            PeriodLabel = "Early",
            PeriodStart = new DateOnly(2026, 1, 1),
            Format = "Ebook",
        });
        await _publishing.CreatePerformanceRecordAsync(project.Id, new PerformanceRecord
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            PeriodLabel = "Late",
            PeriodStart = new DateOnly(2026, 12, 1),
            Format = "Print",
        });

        var ebookOnly = await _publishing.GetPerformanceRecordsAsync(project.Id, formatFilter: "Ebook");
        Assert.Single(ebookOnly);
        var desc = await _publishing.GetPerformanceRecordsAsync(project.Id, sortByPeriodDescending: true);
        Assert.Equal("Late", desc[0].PeriodLabel);

        await _publishing.CreateCorrectionAsync(project.Id, new Correction
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Error = "Open issue",
            CorrectionText = "TBD",
            ReportedDate = new DateOnly(2026, 8, 1),
            Status = CorrectionStatus.Open,
        });
        await _publishing.CreateCorrectionAsync(project.Id, new Correction
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Error = "Fixed issue",
            CorrectionText = "Done",
            ReportedDate = new DateOnly(2026, 7, 1),
            Status = CorrectionStatus.Corrected,
        });
        Assert.Single(await _publishing.GetCorrectionsAsync(project.Id, CorrectionStatus.Open));
    }

    [Fact]
    public async Task FormatAssetLink_MissingFileHandling()
    {
        var project = await CreateAsync("Links");
        var asset = Path.Combine(project.RootPath, "11 Publishing", "book.epub");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        await File.WriteAllTextAsync(asset, "epub");

        var created = await _publishing.CreatePublishingFormatAsync(project.Id, new PublishingFormat
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Ebook",
            AssetLink = "11 Publishing/book.epub",
        });

        Assert.NotNull(_publishing.ResolveLocalLink(project.RootPath, created.AssetLink, out var okMissing));
        Assert.Null(okMissing);

        Assert.Null(_publishing.ResolveLocalLink(project.RootPath, "11 Publishing/missing.epub", out var missing));
        Assert.Contains("not found", missing, StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _publishing.CreatePublishingFormatAsync(project.Id, new PublishingFormat
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Bad",
                AssetLink = "../escape.epub",
            }));
    }

    [Fact]
    public async Task NewProject_UsesSchemaVersion7()
    {
        var project = await CreateAsync("Schema7");
        var validation = await _projects.ValidateAsync(project.RootPath);
        Assert.Equal(7, validation.SchemaVersion);
        Assert.Equal(ProjectSchema.CurrentVersion, validation.SchemaVersion);
    }

    [Fact]
    public async Task MigratesFromSchemaV6ToV7()
    {
        var dbPath = Path.Combine(_tempRoot, "upgrade", "project.mbws");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        await using (var context = ProjectDbContextFactory.Create(dbPath))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(ProjectSchema.PublishingWorkflowMigrationName);

            var projectId = Guid.NewGuid();
            context.Projects.Add(new ProjectRecord
            {
                Id = projectId,
                Title = "Upgrade",
                Author = "Tester",
                Genre = "Fantasy",
                NorthStar = "Ship",
                PublishingRoute = PublishingRoute.SelfPublishing,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastEditedUtc = DateTimeOffset.UtcNow,
            });
            context.SchemaVersions.Add(new SchemaVersionRecord
            {
                Version = 6,
                Name = ProjectSchema.PublishingWorkflowMigrationName,
                AppliedUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();

        await using (var context = ProjectDbContextFactory.Create(dbPath))
        {
            Assert.False(await TableExistsAsync(context, "PublishingFormats"));
            await context.Database.MigrateAsync();
            Assert.True(await TableExistsAsync(context, "PublishingFormats"));
            Assert.True(await TableExistsAsync(context, "MetadataRecords"));
            Assert.True(await TableExistsAsync(context, "PerformanceRecords"));
            Assert.True(await TableExistsAsync(context, "Corrections"));

            var projectId = await context.Projects.Select(item => item.Id).FirstAsync();
            context.PublishingFormats.Add(new PublishingFormatRecord
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = "Post-upgrade",
            });
            await context.SaveChangesAsync();
            Assert.Equal(1, await context.PublishingFormats.CountAsync());
        }
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

    private static async Task<bool> TableExistsAsync(ProjectDbContext context, string tableName)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
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
