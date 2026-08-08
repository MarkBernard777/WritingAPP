using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Backup;

public sealed class AutomaticSnapshotServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly MutableTimeProvider _clock;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly ISnapshotService _snapshots;

    public AutomaticSnapshotServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-auto-snapshot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddInfrastructure();
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _snapshots = _provider.GetRequiredService<ISnapshotService>();
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
    public async Task DailySnapshot_CreatesOnce_SkipsUnchanged_AndCapturesLaterChanges()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = _tempRoot,
            Title = "Daily Protection",
            Author = "Reliability Tester",
            Genre = "Test",
        });

        var first = await _snapshots.CreateAutomaticSnapshotIfNeededAsync(project.Id);
        var sameDay = await _snapshots.CreateAutomaticSnapshotIfNeededAsync(project.Id);

        Assert.Equal(AutomaticSnapshotOutcome.Created, first.Outcome);
        Assert.Equal(SnapshotKind.Automatic, first.Snapshot!.Manifest.Kind);
        Assert.Equal(AutomaticSnapshotOutcome.AlreadyProtectedToday, sameDay.Outcome);

        _clock.Advance(TimeSpan.FromDays(1));
        var unchanged = await _snapshots.CreateAutomaticSnapshotIfNeededAsync(project.Id);
        Assert.Equal(AutomaticSnapshotOutcome.NoChanges, unchanged.Outcome);
        Assert.Single(await _snapshots.ListSnapshotsAsync(project.Id));

        await _chapters.CreateAsync(project.Id, "New work");
        var changed = await _snapshots.CreateAutomaticSnapshotIfNeededAsync(project.Id);

        Assert.Equal(AutomaticSnapshotOutcome.Created, changed.Outcome);
        Assert.Equal(2, (await _snapshots.ListSnapshotsAsync(project.Id)).Count);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan interval) => _utcNow = _utcNow.Add(interval);
    }
}
