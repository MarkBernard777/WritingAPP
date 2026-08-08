using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Drafting;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Drafting;

public sealed class DraftingTargetService : IDraftingTargetService
{
    private readonly IProjectService _projects;
    private readonly TimeProvider _clock;

    public DraftingTargetService(IProjectService projects, TimeProvider clock)
    {
        _projects = projects;
        _clock = clock;
    }

    public async Task<DraftingTarget> GetTargetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.DraftingTargets
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return record is null
            ? new DraftingTarget
            {
                ProjectId = projectId,
                DailyWordGoal = 0,
                WeeklyWordGoal = 0,
                IsEnabled = false,
                UpdatedUtc = _clock.GetUtcNow(),
            }
            : ToDomain(record);
    }

    public async Task<DraftingTarget> SaveTargetAsync(
        DraftingTarget target,
        CancellationToken cancellationToken = default)
    {
        DraftingTargetValidation.EnsureValid(target);
        var now = _clock.GetUtcNow();
        await using var context = Open(target.ProjectId);
        var record = await context.DraftingTargets
            .FirstOrDefaultAsync(item => item.ProjectId == target.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new DraftingTargetRecord { ProjectId = target.ProjectId };
            context.DraftingTargets.Add(record);
        }

        record.DailyWordGoal = target.DailyWordGoal;
        record.WeeklyWordGoal = target.WeeklyWordGoal;
        record.IsEnabled = target.IsEnabled;
        record.UpdatedUtc = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    private ProjectDbContext Open(Guid projectId)
    {
        var root = RequireRoot(projectId);
        return ProjectDbContextFactory.Create(Path.Combine(root, ProjectPaths.DatabaseFileName));
    }

    private string RequireRoot(Guid projectId)
    {
        var project = _projects.ActiveProject
            ?? throw new InvalidOperationException("Open a project before drafting targets.");
        if (project.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return project.RootPath;
    }

    private static DraftingTarget ToDomain(DraftingTargetRecord record) => new()
    {
        ProjectId = record.ProjectId,
        DailyWordGoal = record.DailyWordGoal,
        WeeklyWordGoal = record.WeeklyWordGoal,
        IsEnabled = record.IsEnabled,
        UpdatedUtc = record.UpdatedUtc,
    };
}

public sealed class DraftingSessionRepository : IDraftingSessionRepository
{
    private readonly IProjectService _projects;

    public DraftingSessionRepository(IProjectService projects) => _projects = projects;

    public async Task<DraftingSession?> GetAsync(
        Guid projectId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.DraftingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<DraftingSession>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var records = await context.DraftingSessions
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records
            .OrderByDescending(item => item.StartedUtc)
            .Select(ToDomain)
            .ToList();
    }

    public async Task<IReadOnlyList<DraftingSession>> ListInterruptedAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var records = await context.DraftingSessions
            .AsNoTracking()
            .Where(item =>
                item.ProjectId == projectId
                && item.EndedUtc == null
                && item.CompletionReason == (int)DraftingSessionCompletionReason.Interrupted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records
            .OrderByDescending(item => item.StartedUtc)
            .Select(ToDomain)
            .ToList();
    }

    public async Task UpsertAsync(DraftingSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var context = Open(session.ProjectId);
        var record = await context.DraftingSessions
            .FirstOrDefaultAsync(item => item.Id == session.Id, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new DraftingSessionRecord { Id = session.Id, ProjectId = session.ProjectId };
            context.DraftingSessions.Add(record);
        }

        record.BookId = session.BookId;
        record.PartId = session.PartId;
        record.ChapterId = session.ChapterId;
        record.SceneId = session.SceneId;
        record.ViewpointCharacterId = session.ViewpointCharacterId;
        record.ViewpointLabel = session.ViewpointLabel;
        record.StartedUtc = session.StartedUtc;
        record.EndedUtc = session.EndedUtc;
        record.LastActivityUtc = session.LastActivityUtc;
        record.ActiveDurationSeconds = session.ActiveDuration.TotalSeconds;
        record.StartingWordCount = session.StartingWordCount;
        record.EndingWordCount = session.EndingWordCount;
        record.NetWordChange = session.NetWordChange;
        record.CompletionReason = (int)session.CompletionReason;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    private ProjectDbContext Open(Guid projectId)
    {
        var project = _projects.ActiveProject
            ?? throw new InvalidOperationException("Open a project before drafting sessions.");
        if (project.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return ProjectDbContextFactory.Create(Path.Combine(project.RootPath, ProjectPaths.DatabaseFileName));
    }

    internal static DraftingSession ToDomain(DraftingSessionRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        BookId = record.BookId,
        PartId = record.PartId,
        ChapterId = record.ChapterId,
        SceneId = record.SceneId,
        ViewpointCharacterId = record.ViewpointCharacterId,
        ViewpointLabel = record.ViewpointLabel,
        StartedUtc = record.StartedUtc,
        EndedUtc = record.EndedUtc,
        LastActivityUtc = record.LastActivityUtc,
        ActiveDuration = TimeSpan.FromSeconds(record.ActiveDurationSeconds),
        StartingWordCount = record.StartingWordCount,
        EndingWordCount = record.EndingWordCount,
        NetWordChange = record.NetWordChange,
        CompletionReason = (DraftingSessionCompletionReason)record.CompletionReason,
    };
}

public sealed class DraftingProgressQueryService : IDraftingProgressQueryService
{
    private readonly IDraftingSessionRepository _sessions;
    private readonly IChapterService _chapters;
    private readonly IStoryDataService _story;
    private readonly TimeProvider _clock;

    public DraftingProgressQueryService(
        IDraftingSessionRepository sessions,
        IChapterService chapters,
        IStoryDataService story,
        TimeProvider clock)
    {
        _sessions = sessions;
        _chapters = chapters;
        _story = story;
        _clock = clock;
    }

    public async Task<DraftingProgressReport> GetProgressAsync(
        Guid projectId,
        DateOnly fromLocalDate,
        DateOnly toLocalDate,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _sessions.ListAsync(projectId, cancellationToken).ConfigureAwait(false);
        var chapters = await _chapters.GetAllAsync(projectId, cancellationToken).ConfigureAwait(false);
        var scenes = await _story.GetScenesAsync(projectId, cancellationToken).ConfigureAwait(false);
        return DraftingProgressAggregator.Aggregate(
            sessions,
            fromLocalDate,
            toLocalDate,
            _clock.LocalTimeZone,
            chapters.ToDictionary(item => item.Id, item => item.Title),
            scenes.ToDictionary(item => item.Id, item => item.Title));
    }
}

public sealed class DraftingTimerService : IDraftingTimerService, IDisposable
{
    private readonly IProjectService _projects;
    private readonly IDraftingSessionRepository _sessions;
    private readonly IChapterService _chapters;
    private readonly IManuscriptHierarchyService _hierarchy;
    private readonly IStoryDataService _story;
    private readonly IEditorAutosaveService _autosave;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, int> _lastChapterWords = new();
    private DraftingTimerState _state = DraftingTimerState.Stopped;
    private Guid? _projectId;
    private Guid? _sessionId;
    private DateTimeOffset? _startedUtc;
    private DateTimeOffset? _runningSinceUtc;
    private DateTimeOffset _lastActivityUtc;
    private TimeSpan _accumulated = TimeSpan.Zero;
    private int _startingWords;
    private Guid? _chapterId;
    private Guid? _sceneId;
    private Guid? _bookId;
    private Guid? _partId;
    private Guid? _viewpointId;
    private string? _viewpointLabel;
    private DraftingSession? _interrupted;
    private ITimer? _idleTimer;

    public DraftingTimerService(
        IProjectService projects,
        IDraftingSessionRepository sessions,
        IChapterService chapters,
        IManuscriptHierarchyService hierarchy,
        IStoryDataService story,
        IEditorAutosaveService autosave,
        TimeProvider clock)
    {
        _projects = projects;
        _sessions = sessions;
        _chapters = chapters;
        _hierarchy = hierarchy;
        _story = story;
        _autosave = autosave;
        _clock = clock;
        _autosave.SaveCompleted += OnSaveCompleted;
    }

    public event EventHandler? Changed;

    public void Dispose()
    {
        _autosave.SaveCompleted -= OnSaveCompleted;
        _idleTimer?.Dispose();
    }

    public DraftingTimerSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new DraftingTimerSnapshot
            {
                State = _state,
                SessionId = _sessionId,
                ProjectId = _projectId,
                ActiveElapsed = CurrentActiveElapsedUnsafe(),
                StartedUtc = _startedUtc,
                HasInterruptedSession = _interrupted is not null,
                InterruptedSession = _interrupted,
            };
        }
    }

    public async Task StartAsync(
        Guid projectId,
        Guid? chapterId = null,
        Guid? sceneId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureContextAsync(projectId, chapterId, sceneId, cancellationToken).ConfigureAwait(false);
        var now = _clock.GetUtcNow();
        DraftingSession session;
        lock (_gate)
        {
            if (_state == DraftingTimerState.Running && _projectId == projectId)
            {
                return;
            }

            if (_state == DraftingTimerState.Paused && _projectId == projectId && _sessionId is not null)
            {
                _state = DraftingTimerState.Running;
                _runningSinceUtc = now;
                _lastActivityUtc = now;
                ArmIdleTimerUnsafe();
            }
            else
            {
                _projectId = projectId;
                _sessionId = Guid.NewGuid();
                _startedUtc = now;
                _runningSinceUtc = now;
                _lastActivityUtc = now;
                _accumulated = TimeSpan.Zero;
                _state = DraftingTimerState.Running;
                ArmIdleTimerUnsafe();
            }

            session = BuildSessionUnsafe(
                endingWords: _startingWords,
                reason: DraftingSessionCompletionReason.None,
                endedUtc: null);
        }

        await _sessions.UpsertAsync(session, cancellationToken).ConfigureAwait(false);
        RaiseChanged();
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        DraftingSession? session = null;
        lock (_gate)
        {
            if (_state != DraftingTimerState.Running || _sessionId is null || _projectId is null)
            {
                return;
            }

            var now = _clock.GetUtcNow();
            FlushRunningUnsafe(now);
            _lastActivityUtc = now;
            _state = DraftingTimerState.Paused;
            _idleTimer?.Dispose();
            _idleTimer = null;
            session = BuildSessionUnsafe(
                endingWords: ResolveEndingWordsUnsafe(),
                reason: DraftingSessionCompletionReason.None,
                endedUtc: null);
        }

        if (session is not null)
        {
            await _sessions.UpsertAsync(session, cancellationToken).ConfigureAwait(false);
        }

        RaiseChanged();
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        Guid? projectId;
        Guid? chapterId;
        Guid? sceneId;
        lock (_gate)
        {
            if (_state != DraftingTimerState.Paused || _projectId is null)
            {
                return;
            }

            projectId = _projectId;
            chapterId = _chapterId;
            sceneId = _sceneId;
        }

        await StartAsync(projectId.Value, chapterId, sceneId, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        DraftingSession? session = null;
        lock (_gate)
        {
            if (_sessionId is null || _projectId is null || _state == DraftingTimerState.Stopped)
            {
                ResetUnsafe();
                return;
            }

            var now = _clock.GetUtcNow();
            if (_state == DraftingTimerState.Running)
            {
                FlushRunningUnsafe(now);
            }

            var ending = ResolveEndingWordsUnsafe();
            session = BuildSessionUnsafe(
                endingWords: ending,
                reason: DraftingSessionCompletionReason.ManualStop,
                endedUtc: now);
            ResetUnsafe();
        }

        if (session is not null)
        {
            await _sessions.UpsertAsync(session, cancellationToken).ConfigureAwait(false);
        }

        RaiseChanged();
    }

    public async Task OnProjectOpenedAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var open = await context.DraftingSessions
            .Where(item =>
                item.ProjectId == projectId
                && item.EndedUtc == null
                && (item.CompletionReason == (int)DraftingSessionCompletionReason.None
                    || item.CompletionReason == (int)DraftingSessionCompletionReason.Interrupted))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        DraftingSession? interrupted = null;
        foreach (var record in open)
        {
            // Do not invent active time across the crash gap.
            record.CompletionReason = (int)DraftingSessionCompletionReason.Interrupted;
            record.EndedUtc = null;
            interrupted = DraftingSessionRepository.ToDomain(record);
        }

        if (open.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();

        lock (_gate)
        {
            ResetUnsafe();
            _projectId = projectId;
            _interrupted = interrupted;
            _lastChapterWords.Clear();
        }

        RaiseChanged();
    }

    public Task OnProjectClosedAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ResetUnsafe();
            _interrupted = null;
            _lastChapterWords.Clear();
        }

        RaiseChanged();
        return Task.CompletedTask;
    }

    public async Task ResumeInterruptedAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var existing = await _sessions.GetAsync(RequireProjectId(), sessionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Interrupted session was not found.");
        if (!existing.IsInterrupted)
        {
            throw new InvalidOperationException("Session is not interrupted.");
        }

        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            _projectId = existing.ProjectId;
            _sessionId = existing.Id;
            _startedUtc = existing.StartedUtc;
            _accumulated = existing.ActiveDuration;
            _runningSinceUtc = now;
            _lastActivityUtc = now;
            _startingWords = existing.StartingWordCount;
            _chapterId = existing.ChapterId;
            _sceneId = existing.SceneId;
            _bookId = existing.BookId;
            _partId = existing.PartId;
            _viewpointId = existing.ViewpointCharacterId;
            _viewpointLabel = existing.ViewpointLabel;
            _state = DraftingTimerState.Running;
            _interrupted = null;
            ArmIdleTimerUnsafe();
        }

        var session = new DraftingSession
        {
            Id = existing.Id,
            ProjectId = existing.ProjectId,
            BookId = existing.BookId,
            PartId = existing.PartId,
            ChapterId = existing.ChapterId,
            SceneId = existing.SceneId,
            ViewpointCharacterId = existing.ViewpointCharacterId,
            ViewpointLabel = existing.ViewpointLabel,
            StartedUtc = existing.StartedUtc,
            EndedUtc = null,
            LastActivityUtc = now,
            ActiveDuration = existing.ActiveDuration,
            StartingWordCount = existing.StartingWordCount,
            EndingWordCount = existing.EndingWordCount,
            NetWordChange = existing.NetWordChange,
            CompletionReason = DraftingSessionCompletionReason.None,
        };
        await _sessions.UpsertAsync(session, cancellationToken).ConfigureAwait(false);
        RaiseChanged();
    }

    public async Task CloseInterruptedAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var existing = await _sessions.GetAsync(RequireProjectId(), sessionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Interrupted session was not found.");
        var closed = new DraftingSession
        {
            Id = existing.Id,
            ProjectId = existing.ProjectId,
            BookId = existing.BookId,
            PartId = existing.PartId,
            ChapterId = existing.ChapterId,
            SceneId = existing.SceneId,
            ViewpointCharacterId = existing.ViewpointCharacterId,
            ViewpointLabel = existing.ViewpointLabel,
            StartedUtc = existing.StartedUtc,
            EndedUtc = existing.LastActivityUtc,
            LastActivityUtc = existing.LastActivityUtc,
            ActiveDuration = existing.ActiveDuration,
            StartingWordCount = existing.StartingWordCount,
            EndingWordCount = existing.EndingWordCount,
            NetWordChange = existing.EndingWordCount - existing.StartingWordCount,
            CompletionReason = DraftingSessionCompletionReason.ClosedAfterInterrupt,
        };
        await _sessions.UpsertAsync(closed, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_interrupted?.Id == sessionId)
            {
                _interrupted = null;
            }
        }

        RaiseChanged();
    }

    public async Task NoteManuscriptSaveAsync(
        Guid projectId,
        Guid chapterId,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var words = ManuscriptTextAnalytics.CountWords(
            SceneProseAssociation.StripMarkers(markdown));
        int previous;
        var isBaseline = false;
        lock (_gate)
        {
            if (!_lastChapterWords.TryGetValue(chapterId, out previous))
            {
                _lastChapterWords[chapterId] = words;
                isBaseline = true;
            }
            else
            {
                _lastChapterWords[chapterId] = words;
            }
        }

        // First observation establishes a baseline; metadata-only / zero-delta saves never open sessions.
        if (isBaseline || !DraftingSessionRules.IsMeaningfulWordDelta(previous, words))
        {
            return;
        }

        var now = _clock.GetUtcNow();
        DraftingSession? toPersist = null;
        var shouldStart = false;
        lock (_gate)
        {
            if (_projectId is not null && _projectId != projectId)
            {
                return;
            }

            _projectId = projectId;
            if (_state == DraftingTimerState.Stopped || _sessionId is null)
            {
                shouldStart = true;
                _sessionId = Guid.NewGuid();
                _startedUtc = now;
                _runningSinceUtc = now;
                _lastActivityUtc = now;
                _accumulated = TimeSpan.Zero;
                _startingWords = previous;
                _chapterId = chapterId;
                _state = DraftingTimerState.Running;
            }
            else if (_state == DraftingTimerState.Paused)
            {
                _state = DraftingTimerState.Running;
                _runningSinceUtc = now;
                _lastActivityUtc = now;
            }
            else
            {
                _lastActivityUtc = now;
            }

            ArmIdleTimerUnsafe();
            toPersist = BuildSessionUnsafe(
                endingWords: words,
                reason: DraftingSessionCompletionReason.None,
                endedUtc: null);
        }

        if (shouldStart)
        {
            await EnsureContextAsync(projectId, chapterId, sceneId: null, cancellationToken)
                .ConfigureAwait(false);
            lock (_gate)
            {
                toPersist = BuildSessionUnsafe(
                    endingWords: words,
                    reason: DraftingSessionCompletionReason.None,
                    endedUtc: null);
            }
        }

        if (toPersist is not null)
        {
            await _sessions.UpsertAsync(toPersist, cancellationToken).ConfigureAwait(false);
        }

        RaiseChanged();
    }

    private void OnSaveCompleted(object? sender, EditorSaveCompletedEventArgs e)
    {
        if (!e.Succeeded
            || e.EntityType != RecoveryEntityType.ManuscriptChapter
            || string.IsNullOrEmpty(e.SavedText))
        {
            return;
        }

        _ = NoteManuscriptSaveAsync(e.ProjectId, e.EntityId, e.SavedText);
    }

    private async Task EnsureContextAsync(
        Guid projectId,
        Guid? chapterId,
        Guid? sceneId,
        CancellationToken cancellationToken)
    {
        Guid? bookId = null;
        Guid? partId = null;
        Guid? viewpointId = null;
        string? viewpointLabel = null;
        var startingWords = 0;

        if (chapterId is { } chapter)
        {
            var chapterEntity = await _chapters.GetAsync(projectId, chapter, cancellationToken)
                .ConfigureAwait(false);
            partId = chapterEntity.PartId;
            startingWords = chapterEntity.WordCount;
            if (partId is { } part)
            {
                var tree = await _hierarchy.GetHierarchyAsync(projectId, cancellationToken)
                    .ConfigureAwait(false);
                var partNode = tree.Books.SelectMany(book => book.Parts)
                    .FirstOrDefault(item => item.Id == part);
                bookId = partNode?.BookId;
            }
        }

        if (sceneId is { } scene)
        {
            var sceneEntity = await _story.GetSceneAsync(projectId, scene, cancellationToken)
                .ConfigureAwait(false);
            viewpointId = sceneEntity.ViewpointCharacterId;
            if (viewpointId is { } characterId)
            {
                var characters = await _story.GetCharactersAsync(projectId, cancellationToken)
                    .ConfigureAwait(false);
                viewpointLabel = characters.FirstOrDefault(item => item.Id == characterId)?.Name;
            }

            chapterId ??= sceneEntity.ChapterId;
        }

        lock (_gate)
        {
            _chapterId = chapterId;
            _sceneId = sceneId;
            _bookId = bookId;
            _partId = partId;
            _viewpointId = viewpointId;
            _viewpointLabel = viewpointLabel;
            if (_state == DraftingTimerState.Stopped)
            {
                _startingWords = startingWords;
            }
        }
    }

    private async Task CloseForIdleAsync()
    {
        DraftingSession? session = null;
        lock (_gate)
        {
            if (_state != DraftingTimerState.Running || _sessionId is null || _projectId is null)
            {
                return;
            }

            // Count active time only through last meaningful activity — never invent idle gap time.
            FlushRunningUnsafe(_lastActivityUtc);
            var ending = ResolveEndingWordsUnsafe();
            session = BuildSessionUnsafe(
                endingWords: ending,
                reason: DraftingSessionCompletionReason.IdleTimeout,
                endedUtc: _lastActivityUtc);
            ResetUnsafe();
        }

        if (session is not null)
        {
            await _sessions.UpsertAsync(session).ConfigureAwait(false);
        }

        RaiseChanged();
    }

    private void ArmIdleTimerUnsafe()
    {
        _idleTimer?.Dispose();
        _idleTimer = _clock.CreateTimer(
            _ => _ = CloseForIdleAsync(),
            state: null,
            dueTime: DraftingSessionRules.IdleThreshold,
            period: Timeout.InfiniteTimeSpan);
    }

    private TimeSpan CurrentActiveElapsedUnsafe()
    {
        var elapsed = _accumulated;
        if (_state == DraftingTimerState.Running && _runningSinceUtc is { } since)
        {
            elapsed += _clock.GetUtcNow() - since;
        }

        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    private void FlushRunningUnsafe(DateTimeOffset until)
    {
        if (_runningSinceUtc is { } since)
        {
            var end = until < since ? since : until;
            _accumulated += end - since;
            if (_accumulated < TimeSpan.Zero)
            {
                _accumulated = TimeSpan.Zero;
            }
        }

        _runningSinceUtc = null;
    }

    private int ResolveEndingWordsUnsafe()
    {
        if (_chapterId is { } chapterId && _lastChapterWords.TryGetValue(chapterId, out var words))
        {
            return words;
        }

        return _startingWords;
    }

    private DraftingSession BuildSessionUnsafe(
        int endingWords,
        DraftingSessionCompletionReason reason,
        DateTimeOffset? endedUtc)
        => new()
        {
            Id = _sessionId ?? Guid.NewGuid(),
            ProjectId = _projectId ?? Guid.Empty,
            BookId = _bookId,
            PartId = _partId,
            ChapterId = _chapterId,
            SceneId = _sceneId,
            ViewpointCharacterId = _viewpointId,
            ViewpointLabel = _viewpointLabel,
            StartedUtc = _startedUtc ?? _clock.GetUtcNow(),
            EndedUtc = endedUtc,
            LastActivityUtc = _lastActivityUtc == default ? _clock.GetUtcNow() : _lastActivityUtc,
            ActiveDuration = CurrentActiveElapsedUnsafe(),
            StartingWordCount = _startingWords,
            EndingWordCount = endingWords,
            NetWordChange = endingWords - _startingWords,
            CompletionReason = reason,
        };

    private void ResetUnsafe()
    {
        _state = DraftingTimerState.Stopped;
        _sessionId = null;
        _startedUtc = null;
        _runningSinceUtc = null;
        _accumulated = TimeSpan.Zero;
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    private Guid RequireProjectId()
    {
        lock (_gate)
        {
            return _projectId
                ?? _projects.ActiveProject?.Id
                ?? throw new InvalidOperationException("Open a project first.");
        }
    }

    private ProjectDbContext Open(Guid projectId)
    {
        var project = _projects.ActiveProject
            ?? throw new InvalidOperationException("Open a project before drafting sessions.");
        if (project.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return ProjectDbContextFactory.Create(Path.Combine(project.RootPath, ProjectPaths.DatabaseFileName));
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
