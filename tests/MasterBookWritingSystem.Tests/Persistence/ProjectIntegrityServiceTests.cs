using System.Text;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Persistence;

public sealed class ProjectIntegrityServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IProjectIntegrityService _integrity;

    public ProjectIntegrityServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-integrity-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var services = new ServiceCollection();
        services.AddInfrastructure();
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _integrity = _provider.GetRequiredService<IProjectIntegrityService>();
    }

    public void Dispose()
    {
        _projects.CloseAsync().GetAwaiter().GetResult();
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task IntegrityCheck_ReportsHealthyProject()
    {
        var project = await CreateProjectAsync("Healthy Integrity");

        var result = await _integrity.CheckAsync(project.RootPath);

        Assert.True(result.IsHealthy, result.Summary);
        Assert.Empty(result.Details);
    }

    [Fact]
    public async Task Validation_RejectsCorruptedSqliteDatabase()
    {
        var project = await CreateProjectAsync("Damaged Integrity");
        var root = project.RootPath;
        await _projects.CloseAsync();
        SqliteConnection.ClearAllPools();
        await File.WriteAllBytesAsync(
            Path.Combine(root, ProjectPaths.DatabaseFileName),
            Encoding.UTF8.GetBytes("not a sqlite database"));

        var integrity = await _integrity.CheckAsync(root);
        var validation = await _projects.ValidateAsync(root);

        Assert.False(integrity.IsHealthy);
        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Errors,
            error => error.Contains("integrity", StringComparison.OrdinalIgnoreCase));
    }

    private Task<MasterBookWritingSystem.Core.Domain.Project> CreateProjectAsync(string title)
        => _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = _tempRoot,
            Title = title,
            Author = "Reliability Tester",
            Genre = "Test",
        });
}
