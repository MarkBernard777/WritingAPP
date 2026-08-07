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

public sealed class PublishingServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IPublishingService _publishing;
    private readonly IWorkflowService _workflow;

    public PublishingServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-publishing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _publishing = _provider.GetRequiredService<IPublishingService>();
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
    public async Task Submission_Launch_Rights_CrudAndPersistence()
    {
        var project = await CreateAsync("PubCrud");

        var submission = await _publishing.CreateSubmissionAsync(project.Id, new Submission
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Nightshade Agency",
            AgencyOrPublisher = "Nightshade",
            Website = "11 Publishing/query-packet.md",
            MaterialSent = "Query + first 3",
            SentDate = new DateOnly(2026, 8, 1),
            FollowUpDate = new DateOnly(2026, 8, 15),
            Response = SubmissionResponse.Queried,
            Outcome = SubmissionOutcome.Pending,
            RouteAffinity = PublishingRoute.Traditional,
        });

        var launch = await _publishing.CreateLaunchItemAsync(project.Id, new LaunchItem
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Date = new DateOnly(2026, 9, 1),
            Phase = "Launch week",
            Channel = "Newsletter",
            Asset = "Launch email",
            Link = "12 Marketing/launch-email.md",
            Status = LaunchItemStatus.Planned,
        });

        var rights = await _publishing.CreateRightsAndContractAsync(project.Id, new RightsAndContract
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Party = "AudioCo",
            RightOrService = "Audiobook",
            Territory = "World English",
            Format = "Audio",
            StartDate = new DateOnly(2026, 1, 1),
            EndOrReversionDate = new DateOnly(2031, 1, 1),
            Payment = 2500m,
            AgreementFile = "11 Publishing/audioco.pdf",
            Status = RightsContractStatus.Active,
        });

        submission.Outcome = SubmissionOutcome.FullRequest;
        await _publishing.UpdateSubmissionAsync(project.Id, submission);
        launch.Status = LaunchItemStatus.Done;
        await _publishing.UpdateLaunchItemAsync(project.Id, launch);
        rights.Payment = 3000m;
        await _publishing.UpdateRightsAndContractAsync(project.Id, rights);

        Assert.Equal(SubmissionOutcome.FullRequest, (await _publishing.GetSubmissionAsync(project.Id, submission.Id)).Outcome);
        Assert.Equal(LaunchItemStatus.Done, (await _publishing.GetLaunchItemAsync(project.Id, launch.Id)).Status);
        Assert.Equal(3000m, (await _publishing.GetRightsAndContractAsync(project.Id, rights.Id)).Payment);

        await _publishing.DeleteSubmissionAsync(project.Id, submission.Id, confirmed: true);
        await _publishing.DeleteLaunchItemAsync(project.Id, launch.Id, confirmed: true);
        await _publishing.DeleteRightsAndContractAsync(project.Id, rights.Id, confirmed: true);

        Assert.Empty(await _publishing.GetSubmissionsAsync(project.Id, includeInactiveRouteRows: true));
        Assert.Empty(await _publishing.GetLaunchItemsAsync(project.Id));
        Assert.Empty(await _publishing.GetRightsAndContractsAsync(project.Id));
    }

    [Fact]
    public async Task DeleteRequiresConfirmation()
    {
        var project = await CreateAsync("Confirm");
        var created = await _publishing.CreateSubmissionAsync(project.Id, new Submission
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Keep me",
            RouteAffinity = PublishingRoute.Traditional,
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _publishing.DeleteSubmissionAsync(project.Id, created.Id, confirmed: false));
        Assert.Contains("not confirmed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await _publishing.GetSubmissionsAsync(project.Id, includeInactiveRouteRows: true));
    }

    [Fact]
    public async Task RouteSwitch_HidesTraditionalSubmissions_ButPreservesThem()
    {
        var project = await CreateAsync("RoutePreserve");
        await _workflow.SetPublishingRouteAsync(project.Id, PublishingRoute.Traditional);

        var traditional = await _publishing.CreateSubmissionAsync(project.Id, new Submission
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Traditional query",
            RouteAffinity = PublishingRoute.Traditional,
        });
        var hybrid = await _publishing.CreateSubmissionAsync(project.Id, new Submission
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Hybrid note",
            RouteAffinity = PublishingRoute.Hybrid,
        });
        var launch = await _publishing.CreateLaunchItemAsync(project.Id, new LaunchItem
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Asset = "Always available",
            Status = LaunchItemStatus.Ready,
        });
        var rights = await _publishing.CreateRightsAndContractAsync(project.Id, new RightsAndContract
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Party = "Self",
            RightOrService = "Copyright",
            Status = RightsContractStatus.Active,
        });

        await _workflow.SetPublishingRouteAsync(project.Id, PublishingRoute.SelfPublishing);
        project = _projects.ActiveProject!;

        var visible = await _publishing.GetSubmissionsAsync(project.Id, project.PublishingRoute);
        Assert.DoesNotContain(visible, item => item.Id == traditional.Id);
        Assert.Contains(visible, item => item.Id == hybrid.Id);

        var all = await _publishing.GetSubmissionsAsync(
            project.Id,
            project.PublishingRoute,
            includeInactiveRouteRows: true);
        Assert.Contains(all, item => item.Id == traditional.Id);

        Assert.Contains(await _publishing.GetLaunchItemsAsync(project.Id), item => item.Id == launch.Id);
        Assert.Contains(await _publishing.GetRightsAndContractsAsync(project.Id), item => item.Id == rights.Id);

        await _workflow.SetPublishingRouteAsync(project.Id, PublishingRoute.Traditional);
        project = _projects.ActiveProject!;
        var restored = await _publishing.GetSubmissionsAsync(project.Id, project.PublishingRoute);
        Assert.Contains(restored, item => item.Id == traditional.Id);
    }

    [Fact]
    public async Task FilteringAndSorting_Work()
    {
        var project = await CreateAsync("Filters");
        await _publishing.CreateLaunchItemAsync(project.Id, new LaunchItem
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Asset = "Early",
            Date = new DateOnly(2026, 1, 1),
            Status = LaunchItemStatus.Done,
            RouteAffinity = PublishingRoute.Traditional,
        });
        await _publishing.CreateLaunchItemAsync(project.Id, new LaunchItem
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Asset = "Late",
            Date = new DateOnly(2026, 12, 1),
            Status = LaunchItemStatus.Planned,
            RouteAffinity = PublishingRoute.SelfPublishing,
        });

        var planned = await _publishing.GetLaunchItemsAsync(project.Id, LaunchItemStatus.Planned);
        Assert.Single(planned);
        Assert.Equal("Late", planned[0].Asset);

        var traditional = await _publishing.GetLaunchItemsAsync(
            project.Id,
            routeFilter: PublishingRoute.Traditional);
        Assert.Contains(traditional, item => item.Asset == "Early");
        Assert.DoesNotContain(traditional, item => item.Asset == "Late");

        var desc = await _publishing.GetLaunchItemsAsync(project.Id, sortByDateDescending: true);
        Assert.Equal("Late", desc[0].Asset);

        await _publishing.CreateSubmissionAsync(project.Id, new Submission
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Pending one",
            Outcome = SubmissionOutcome.Pending,
            RouteAffinity = PublishingRoute.Traditional,
        });
        await _publishing.CreateSubmissionAsync(project.Id, new Submission
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Signed one",
            Outcome = SubmissionOutcome.Signed,
            RouteAffinity = PublishingRoute.Traditional,
        });
        var signed = await _publishing.GetSubmissionsAsync(
            project.Id,
            outcomeFilter: SubmissionOutcome.Signed,
            includeInactiveRouteRows: true);
        Assert.Single(signed);
    }

    [Fact]
    public async Task ProjectRelativeLinks_AndMissingFileHandling()
    {
        var project = await CreateAsync("Links");
        var agreement = Path.Combine(project.RootPath, "11 Publishing", "agreement.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(agreement)!);
        await File.WriteAllTextAsync(agreement, "pdf-bytes");

        var created = await _publishing.CreateRightsAndContractAsync(project.Id, new RightsAndContract
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Party = "Publisher",
            RightOrService = "Print",
            AgreementFile = "11 Publishing/agreement.pdf",
            Status = RightsContractStatus.Draft,
        });

        var resolved = _publishing.ResolveLocalLink(project.RootPath, created.AgreementFile, out var missing);
        Assert.NotNull(resolved);
        Assert.Null(missing);
        Assert.True(File.Exists(resolved));

        var missingPath = _publishing.ResolveLocalLink(
            project.RootPath,
            "11 Publishing/missing.pdf",
            out var missingMessage);
        Assert.Null(missingPath);
        Assert.Contains("not found", missingMessage, StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _publishing.CreateSubmissionAsync(project.Id, new Submission
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Bad path",
                Website = "../outside.txt",
                RouteAffinity = PublishingRoute.Traditional,
            }));
    }

    [Fact]
    public async Task NewProject_UsesSchemaVersion6()
    {
        var project = await CreateAsync("Schema6");
        var validation = await _projects.ValidateAsync(project.RootPath);
        Assert.Equal(7, validation.SchemaVersion);
        Assert.Equal(ProjectSchema.CurrentVersion, validation.SchemaVersion);
    }

    [Fact]
    public async Task MigratesFromSchemaV5ToV6()
    {
        var dbPath = Path.Combine(_tempRoot, "upgrade", "project.mbws");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        await using (var context = ProjectDbContextFactory.Create(dbPath))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(ProjectSchema.IdeaScoringMigrationName);

            var projectId = Guid.NewGuid();
            context.Projects.Add(new ProjectRecord
            {
                Id = projectId,
                Title = "Upgrade",
                Author = "Tester",
                Genre = "Fantasy",
                NorthStar = "Ship",
                PublishingRoute = PublishingRoute.Traditional,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastEditedUtc = DateTimeOffset.UtcNow,
            });
            context.SchemaVersions.Add(new SchemaVersionRecord
            {
                Version = 5,
                Name = ProjectSchema.IdeaScoringMigrationName,
                AppliedUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();

        await using (var context = ProjectDbContextFactory.Create(dbPath))
        {
            Assert.False(await TableExistsAsync(context, "Submissions"));
            await context.Database.MigrateAsync();
            Assert.True(await TableExistsAsync(context, "Submissions"));
            Assert.True(await TableExistsAsync(context, "LaunchItems"));
            Assert.True(await TableExistsAsync(context, "RightsAndContracts"));

            var projectId = await context.Projects.Select(item => item.Id).FirstAsync();
            context.Submissions.Add(new SubmissionRecord
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = "Post-upgrade",
                RouteAffinity = PublishingRoute.Traditional,
            });
            await context.SaveChangesAsync();
            Assert.Equal(1, await context.Submissions.CountAsync());
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
