using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Drafting;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Drafting;

public sealed class DraftingTargetValidationTests
{
    [Fact]
    public void EnsureValid_RejectsNegativeAndEnabledEmptyGoals()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DraftingTargetValidation.EnsureValid(new DraftingTarget
            {
                ProjectId = Guid.NewGuid(),
                DailyWordGoal = -1,
            }));
        Assert.Throws<InvalidOperationException>(() =>
            DraftingTargetValidation.EnsureValid(new DraftingTarget
            {
                ProjectId = Guid.NewGuid(),
                IsEnabled = true,
            }));
        Assert.Throws<InvalidOperationException>(() =>
            DraftingTargetValidation.EnsureValid(new DraftingTarget
            {
                ProjectId = Guid.NewGuid(),
                DailyWordGoal = 1000,
                WeeklyWordGoal = 100,
            }));
    }
}

public sealed class DraftingProgressAggregatorTests
{
    [Fact]
    public void Aggregate_RespectsLocalMidnightTimezoneAndNegativeNet()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Pacific Standard Time" : "America/Los_Angeles");
        // 2026-08-09 01:30 UTC = 2026-08-08 evening PDT
        var started = new DateTimeOffset(2026, 8, 9, 1, 30, 0, TimeSpan.Zero);
        var sessions = new[]
        {
            new DraftingSession
            {
                Id = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(),
                ChapterId = Guid.NewGuid(),
                ViewpointLabel = "Ava",
                StartedUtc = started,
                EndedUtc = started.AddMinutes(20),
                LastActivityUtc = started.AddMinutes(20),
                ActiveDuration = TimeSpan.FromMinutes(15),
                StartingWordCount = 100,
                EndingWordCount = 80,
                NetWordChange = -20,
                CompletionReason = DraftingSessionCompletionReason.ManualStop,
            },
        };

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(started, zone).DateTime);
        var report = DraftingProgressAggregator.Aggregate(
            sessions,
            localDate,
            localDate,
            zone,
            new Dictionary<Guid, string> { [sessions[0].ChapterId!.Value] = "Ch 1" });

        Assert.Equal(-20, report.NetWords);
        Assert.Single(report.ByDate);
        Assert.Equal(-20, report.ByDate[0].NetWords);
        Assert.Equal("Ava", report.ByViewpoint[0].Label);

        var utcDay = DateOnly.FromDateTime(started.UtcDateTime);
        if (utcDay != localDate)
        {
            var wrongDay = DraftingProgressAggregator.Aggregate(sessions, utcDay, utcDay, zone);
            Assert.Equal(0, wrongDay.SessionCount);
        }
    }

    [Fact]
    public void ThisWeekRange_StartsOnSundayLocal()
    {
        var zone = TimeZoneInfo.Utc;
        var wednesday = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var (from, to) = DraftingProgressAggregator.ThisWeekRange(wednesday, zone);
        Assert.Equal(new DateOnly(2026, 8, 2), from);
        Assert.Equal(new DateOnly(2026, 8, 5), to);
    }
}

public sealed class DraftingWorkspaceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly MutableTimeProvider _clock;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IDraftingTargetService _targets;
    private readonly IDraftingTimerService _timer;
    private readonly IDraftingSessionRepository _sessions;
    private readonly IDraftingProgressQueryService _progress;
    private readonly ISnapshotService _snapshots;

    public DraftingWorkspaceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-drafting-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _targets = _provider.GetRequiredService<IDraftingTargetService>();
        _timer = _provider.GetRequiredService<IDraftingTimerService>();
        _sessions = _provider.GetRequiredService<IDraftingSessionRepository>();
        _progress = _provider.GetRequiredService<IDraftingProgressQueryService>();
        _snapshots = _provider.GetRequiredService<ISnapshotService>();
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
    public async Task NewProject_UsesSchemaVersion9_AndDraftingMigrationName()
    {
        var project = await CreateAsync("Schema9");
        Assert.Equal(9, ProjectSchema.CurrentVersion);
        var validation = await _projects.ValidateAsync(project.RootPath);
        Assert.Equal(9, validation.SchemaVersion);

        await using var context = ProjectDbContextFactory.Create(
            Path.Combine(project.RootPath, ProjectPaths.DatabaseFileName));
        var version = await context.SchemaVersions
            .AsNoTracking()
            .SingleAsync(item => item.Version == ProjectSchema.CurrentVersion);
        Assert.Equal(ProjectSchema.DraftingProgressMigrationName, version.Name);
    }

    [Fact]
    public async Task UpgradeFromV8_CreatesSafetySnapshot_AndDraftingTables()
    {
        var root = Path.Combine(_tempRoot, "v8-upgrade");
        Directory.CreateDirectory(root);
        foreach (var relative in ProjectPaths.StandardDirectories)
        {
            Directory.CreateDirectory(Path.Combine(root, relative));
        }

        var projectId = Guid.NewGuid();
        var dbPath = Path.Combine(root, ProjectPaths.DatabaseFileName);
        await using (var context = ProjectDbContextFactory.Create(dbPath))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(ProjectSchema.ManuscriptHierarchyMigrationName);
            context.Projects.Add(new ProjectRecord
            {
                Id = projectId,
                Title = "V8 Book",
                Author = "Tester",
                Genre = "Fantasy",
                NorthStar = "Upgrade",
                PublishingRoute = PublishingRoute.SelfPublishing,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastEditedUtc = DateTimeOffset.UtcNow,
            });
            context.SchemaVersions.Add(new SchemaVersionRecord
            {
                Version = 8,
                Name = ProjectSchema.ManuscriptHierarchyMigrationName,
                AppliedUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();
        await File.WriteAllTextAsync(
            Path.Combine(root, ProjectPaths.MetadataFileName),
            $$"""
            {
              "id": "{{projectId}}",
              "title": "V8 Book",
              "author": "Tester",
              "genre": "Fantasy",
              "publishingRoute": 1,
              "northStar": "Upgrade",
              "schemaVersion": 8,
              "createdUtc": "2026-01-01T00:00:00Z",
              "lastEditedUtc": "2026-01-01T00:00:00Z"
            }
            """);

        var opened = await _projects.OpenAsync(root);
        var snapshots = await _snapshots.ListSnapshotsAsync(opened.Id);
        Assert.Contains(snapshots, item =>
            item.Name.StartsWith("safety-migration-", StringComparison.OrdinalIgnoreCase));

        await using (var context = ProjectDbContextFactory.Create(dbPath))
        {
            Assert.Equal(0, await context.DraftingSessions.CountAsync());
            Assert.NotNull(context.Model.FindEntityType(typeof(DraftingTargetRecord)));
            var version = await context.SchemaVersions
                .AsNoTracking()
                .SingleAsync(item => item.Version == ProjectSchema.CurrentVersion);
            Assert.Equal(ProjectSchema.DraftingProgressMigrationName, version.Name);
        }
    }

    [Fact]
    public async Task Timer_PauseResumeStop_IsDeterministicWithTimeProvider()
    {
        var project = await CreateAsync("Timer");
        await _timer.OnProjectOpenedAsync(project.Id);
        await _timer.StartAsync(project.Id);
        _clock.Advance(TimeSpan.FromMinutes(2));
        await _timer.PauseAsync();
        var paused = _timer.GetSnapshot();
        Assert.Equal(DraftingTimerState.Paused, paused.State);
        Assert.Equal(TimeSpan.FromMinutes(2), paused.ActiveElapsed);

        _clock.Advance(TimeSpan.FromMinutes(5));
        await _timer.ResumeAsync();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _timer.StopAsync();
        var stopped = _timer.GetSnapshot();
        Assert.Equal(DraftingTimerState.Stopped, stopped.State);

        var sessions = await _sessions.ListAsync(project.Id);
        var completed = Assert.Single(sessions, item => item.CompletionReason == DraftingSessionCompletionReason.ManualStop);
        Assert.Equal(TimeSpan.FromMinutes(3), completed.ActiveDuration);
    }

    [Fact]
    public async Task AutoSession_IgnoresBaselineAndZeroDelta_ThenLogsMeaningfulChange()
    {
        var project = await CreateAsync("Auto");
        await _timer.OnProjectOpenedAsync(project.Id);
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        await _timer.NoteManuscriptSaveAsync(project.Id, chapter.Id, "one two three");
        Assert.Equal(DraftingTimerState.Stopped, _timer.GetSnapshot().State);

        await _timer.NoteManuscriptSaveAsync(project.Id, chapter.Id, "one two three!");
        Assert.Equal(DraftingTimerState.Stopped, _timer.GetSnapshot().State);

        await _timer.NoteManuscriptSaveAsync(project.Id, chapter.Id, "one two three four five");
        Assert.Equal(DraftingTimerState.Running, _timer.GetSnapshot().State);
    }

    [Fact]
    public async Task IdleThreshold_ClosesSessionWithoutCountingIdleGap()
    {
        var project = await CreateAsync("Idle");
        await _timer.OnProjectOpenedAsync(project.Id);
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        await _timer.NoteManuscriptSaveAsync(project.Id, chapter.Id, "alpha");
        await _timer.NoteManuscriptSaveAsync(project.Id, chapter.Id, "alpha beta gamma");
        _clock.Advance(TimeSpan.FromMinutes(2));
        _clock.Advance(DraftingSessionRules.IdleThreshold);
        await Task.Delay(50);

        var sessions = await _sessions.ListAsync(project.Id);
        var idle = Assert.Single(sessions, item => item.CompletionReason == DraftingSessionCompletionReason.IdleTimeout);
        Assert.True(idle.ActiveDuration <= TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        Assert.Equal(DraftingTimerState.Stopped, _timer.GetSnapshot().State);
    }

    [Fact]
    public async Task CrashRecovery_SurfacesInterrupted_WithoutInventingActiveTime()
    {
        var project = await CreateAsync("Crash");
        await _timer.OnProjectOpenedAsync(project.Id);
        await _timer.StartAsync(project.Id);
        _clock.Advance(TimeSpan.FromMinutes(4));
        await _timer.PauseAsync();
        var before = Assert.Single(await _sessions.ListAsync(project.Id));
        Assert.Equal(TimeSpan.FromMinutes(4), before.ActiveDuration);

        await _timer.OnProjectOpenedAsync(project.Id);
        var snapshot = _timer.GetSnapshot();
        Assert.True(snapshot.HasInterruptedSession);
        Assert.Equal(TimeSpan.FromMinutes(4), snapshot.InterruptedSession!.ActiveDuration);

        await _timer.CloseInterruptedAsync(snapshot.InterruptedSession.Id);
        var closed = Assert.Single(await _sessions.ListAsync(project.Id));
        Assert.Equal(DraftingSessionCompletionReason.ClosedAfterInterrupt, closed.CompletionReason);
        Assert.Equal(TimeSpan.FromMinutes(4), closed.ActiveDuration);
        Assert.NotNull(closed.EndedUtc);
    }

    [Fact]
    public async Task Progress_IsIsolatedPerProject_AndSupportsNegativeNet()
    {
        var first = await CreateAsync("P1");
        await _timer.OnProjectOpenedAsync(first.Id);
        await _targets.SaveTargetAsync(new DraftingTarget
        {
            ProjectId = first.Id,
            DailyWordGoal = 500,
            WeeklyWordGoal = 2500,
            IsEnabled = true,
        });
        var chapter = await _chapters.CreateAsync(first.Id, "Ch");
        await _sessions.UpsertAsync(new DraftingSession
        {
            Id = Guid.NewGuid(),
            ProjectId = first.Id,
            ChapterId = chapter.Id,
            StartedUtc = _clock.GetUtcNow(),
            EndedUtc = _clock.GetUtcNow().AddMinutes(10),
            LastActivityUtc = _clock.GetUtcNow().AddMinutes(10),
            ActiveDuration = TimeSpan.FromMinutes(10),
            StartingWordCount = 50,
            EndingWordCount = 30,
            NetWordChange = -20,
            CompletionReason = DraftingSessionCompletionReason.ManualStop,
        });

        var secondParent = Path.Combine(_tempRoot, "library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(secondParent);
        var second = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = secondParent,
            Title = "P2",
            Author = "Tester",
            Genre = "Fantasy",
            NorthStar = "Other",
        });
        await _timer.OnProjectOpenedAsync(second.Id);
        var otherReport = await _progress.GetProgressAsync(
            second.Id,
            new DateOnly(2026, 8, 8),
            new DateOnly(2026, 8, 8));
        Assert.Equal(0, otherReport.SessionCount);

        await _projects.CloseAsync();
        await _projects.OpenAsync(first.RootPath);
        await _timer.OnProjectOpenedAsync(first.Id);
        var report = await _progress.GetProgressAsync(
            first.Id,
            new DateOnly(2026, 8, 8),
            new DateOnly(2026, 8, 8));
        Assert.Equal(-20, report.NetWords);
        Assert.Single(report.ByChapter);
    }

    private async Task<Project> CreateAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        return await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = title,
            Author = "Tester",
            Genre = "Fantasy",
            NorthStar = "Draft",
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

        throw new DirectoryNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        private readonly List<MutableTimer> _timers = [];

        public MutableTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localZone)
        {
            _utcNow = utcNow;
            LocalTimeZone = localZone;
        }

        public override TimeZoneInfo LocalTimeZone { get; }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new MutableTimer(this, callback, state, dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan interval)
        {
            _utcNow += interval;
            foreach (var timer in _timers.ToList())
            {
                timer.OnClockAdvanced(_utcNow);
            }
        }

        private sealed class MutableTimer : ITimer
        {
            private readonly MutableTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private readonly TimeSpan _period;
            private DateTimeOffset? _nextDue;
            private bool _disposed;

            public MutableTimer(
                MutableTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                _period = period;
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    _nextDue = owner._utcNow + (dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime);
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;

            public void OnClockAdvanced(DateTimeOffset now)
            {
                if (_disposed || _nextDue is null || now < _nextDue)
                {
                    return;
                }

                _callback(_state);
                _nextDue = _period == Timeout.InfiniteTimeSpan || _period < TimeSpan.Zero
                    ? null
                    : now + _period;
            }

            public void Dispose()
            {
                _disposed = true;
                _nextDue = null;
                _owner._timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
