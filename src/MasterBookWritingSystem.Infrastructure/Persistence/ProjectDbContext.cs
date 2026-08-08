using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Persistence;

public sealed class ProjectDbContext : DbContext
{
    public ProjectDbContext(DbContextOptions<ProjectDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProjectRecord> Projects => Set<ProjectRecord>();

    public DbSet<ChapterRecord> Chapters => Set<ChapterRecord>();

    public DbSet<BookRecord> Books => Set<BookRecord>();

    public DbSet<PartRecord> Parts => Set<PartRecord>();

    public DbSet<SchemaVersionRecord> SchemaVersions => Set<SchemaVersionRecord>();

    public DbSet<WorkflowPhaseRecord> WorkflowPhases => Set<WorkflowPhaseRecord>();

    public DbSet<WorkflowStepRecord> WorkflowSteps => Set<WorkflowStepRecord>();

    public DbSet<StepProgressRecord> StepProgress => Set<StepProgressRecord>();

    public DbSet<PhaseGateRecord> PhaseGates => Set<PhaseGateRecord>();

    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();

    public DbSet<DocumentFieldRecord> DocumentFields => Set<DocumentFieldRecord>();

    public DbSet<CharacterRecord> Characters => Set<CharacterRecord>();

    public DbSet<WorldEntryRecord> WorldEntries => Set<WorldEntryRecord>();

    public DbSet<BeatRecord> Beats => Set<BeatRecord>();

    public DbSet<SceneRecord> Scenes => Set<SceneRecord>();

    public DbSet<IdeaRecord> Ideas => Set<IdeaRecord>();

    public DbSet<IdeaScoreRecord> IdeaScores => Set<IdeaScoreRecord>();

    public DbSet<SubmissionRecord> Submissions => Set<SubmissionRecord>();

    public DbSet<LaunchItemRecord> LaunchItems => Set<LaunchItemRecord>();

    public DbSet<RightsAndContractRecord> RightsAndContracts => Set<RightsAndContractRecord>();

    public DbSet<PublishingFormatRecord> PublishingFormats => Set<PublishingFormatRecord>();

    public DbSet<MetadataRecordEntity> MetadataRecords => Set<MetadataRecordEntity>();

    public DbSet<PerformanceRecordEntity> PerformanceRecords => Set<PerformanceRecordEntity>();

    public DbSet<CorrectionRecord> Corrections => Set<CorrectionRecord>();

    public DbSet<DraftingTargetRecord> DraftingTargets => Set<DraftingTargetRecord>();

    public DbSet<DraftingSessionRecord> DraftingSessions => Set<DraftingSessionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectRecord>(entity =>
        {
            entity.ToTable("Projects");
            entity.HasKey(project => project.Id);
            entity.Property(project => project.Title).HasMaxLength(500).IsRequired();
            entity.Property(project => project.Author).HasMaxLength(500);
            entity.Property(project => project.Genre).HasMaxLength(200);
            entity.Property(project => project.NorthStar).HasMaxLength(4000);
            entity.Property(project => project.PublishingRoute).HasConversion<int>();
        });

        modelBuilder.Entity<BookRecord>(entity =>
        {
            entity.ToTable("Books");
            entity.HasKey(book => book.Id);
            entity.Property(book => book.Title).HasMaxLength(500).IsRequired();
            entity.HasIndex(book => new { book.ProjectId, book.SequenceNumber }).IsUnique();
            entity.HasOne(book => book.Project)
                .WithMany()
                .HasForeignKey(book => book.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PartRecord>(entity =>
        {
            entity.ToTable("Parts");
            entity.HasKey(part => part.Id);
            entity.Property(part => part.Title).HasMaxLength(500).IsRequired();
            entity.HasIndex(part => new { part.BookId, part.SequenceNumber }).IsUnique();
            entity.HasIndex(part => new { part.ProjectId, part.BookId });
            entity.HasOne(part => part.Book)
                .WithMany(book => book.Parts)
                .HasForeignKey(part => part.BookId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ChapterRecord>(entity =>
        {
            entity.ToTable("Chapters");
            entity.HasKey(chapter => chapter.Id);
            entity.Property(chapter => chapter.Title).HasMaxLength(500).IsRequired();
            entity.Property(chapter => chapter.RelativeMarkdownPath).HasMaxLength(1000).IsRequired();
            entity.Property(chapter => chapter.ContentHash).HasMaxLength(128);
            entity.HasIndex(chapter => new { chapter.ProjectId, chapter.SequenceNumber }).IsUnique();
            entity.HasIndex(chapter => chapter.PartId);
            entity.HasOne(chapter => chapter.Project)
                .WithMany(project => project.Chapters)
                .HasForeignKey(chapter => chapter.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(chapter => chapter.Part)
                .WithMany(part => part.Chapters)
                .HasForeignKey(chapter => chapter.PartId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SchemaVersionRecord>(entity =>
        {
            entity.ToTable("SchemaVersions");
            entity.HasKey(version => version.Version);
            entity.Property(version => version.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<WorkflowPhaseRecord>(entity =>
        {
            entity.ToTable("WorkflowPhases");
            entity.HasKey(phase => new { phase.ProjectId, phase.Id });
            entity.Property(phase => phase.Id).HasMaxLength(32).IsRequired();
            entity.Property(phase => phase.Title).HasMaxLength(500).IsRequired();
            entity.Property(phase => phase.Deliverable).HasMaxLength(1000);
            entity.Property(phase => phase.GateStatement).HasMaxLength(4000);
            entity.Property(phase => phase.TemplatePath).HasMaxLength(500);
            entity.Property(phase => phase.RouteAffinity).HasConversion<int?>();
            entity.HasIndex(phase => new { phase.ProjectId, phase.SortOrder });
            entity.HasOne(phase => phase.Project)
                .WithMany()
                .HasForeignKey(phase => phase.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowStepRecord>(entity =>
        {
            entity.ToTable("WorkflowSteps");
            entity.HasKey(step => step.Id);
            entity.Property(step => step.PhaseId).HasMaxLength(32).IsRequired();
            entity.Property(step => step.Title).HasMaxLength(500).IsRequired();
            entity.Property(step => step.ContentJson).IsRequired();
            entity.Property(step => step.RouteAffinity).HasConversion<int?>();
            entity.HasIndex(step => new { step.ProjectId, step.PhaseId, step.Number }).IsUnique();
            entity.HasOne(step => step.Phase)
                .WithMany(phase => phase.Steps)
                .HasForeignKey(step => new { step.ProjectId, step.PhaseId })
                .HasPrincipalKey(phase => new { phase.ProjectId, phase.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StepProgressRecord>(entity =>
        {
            entity.ToTable("StepProgress");
            entity.HasKey(progress => progress.Id);
            entity.Property(progress => progress.PhaseId).HasMaxLength(32).IsRequired();
            entity.Property(progress => progress.Notes).HasMaxLength(4000);
            entity.Property(progress => progress.Status).HasConversion<int>();
            entity.HasIndex(progress => new { progress.ProjectId, progress.PhaseId, progress.StepNumber })
                .IsUnique();
        });

        modelBuilder.Entity<PhaseGateRecord>(entity =>
        {
            entity.ToTable("PhaseGates");
            entity.HasKey(gate => gate.Id);
            entity.Property(gate => gate.PhaseId).HasMaxLength(32).IsRequired();
            entity.Property(gate => gate.Evidence).HasMaxLength(4000);
            entity.Property(gate => gate.Notes).HasMaxLength(4000);
            entity.Property(gate => gate.OverrideReason).HasMaxLength(2000);
            entity.HasIndex(gate => new { gate.ProjectId, gate.PhaseId }).IsUnique();
        });

        modelBuilder.Entity<DocumentRecord>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasKey(document => document.Id);
            entity.Property(document => document.Title).HasMaxLength(500).IsRequired();
            entity.Property(document => document.Notes).HasMaxLength(8000);
            entity.Property(document => document.RelativeMarkdownPath).HasMaxLength(1000).IsRequired();
            entity.Property(document => document.TemplatePath).HasMaxLength(500);
            entity.Property(document => document.DocumentType).HasConversion<int>();
            entity.HasIndex(document => new { document.ProjectId, document.DocumentType }).IsUnique();
        });

        modelBuilder.Entity<DocumentFieldRecord>(entity =>
        {
            entity.ToTable("DocumentFields");
            entity.HasKey(field => field.Id);
            entity.Property(field => field.Key).HasMaxLength(200).IsRequired();
            entity.Property(field => field.Label).HasMaxLength(500).IsRequired();
            entity.Property(field => field.Value).HasMaxLength(8000);
            entity.HasIndex(field => new { field.DocumentId, field.Key }).IsUnique();
            entity.HasOne(field => field.Document)
                .WithMany(document => document.Fields)
                .HasForeignKey(field => field.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterRecord>(entity =>
        {
            entity.ToTable("Characters");
            entity.HasKey(character => character.Id);
            entity.Property(character => character.Name).HasMaxLength(500).IsRequired();
            entity.Property(character => character.Role).HasMaxLength(200);
            entity.Property(character => character.Goal).HasMaxLength(4000);
            entity.Property(character => character.Need).HasMaxLength(4000);
            entity.Property(character => character.Fear).HasMaxLength(4000);
            entity.Property(character => character.Wound).HasMaxLength(4000);
            entity.Property(character => character.FalseBelief).HasMaxLength(4000);
            entity.Property(character => character.Contradiction).HasMaxLength(4000);
            entity.Property(character => character.Skills).HasMaxLength(4000);
            entity.Property(character => character.Weaknesses).HasMaxLength(4000);
            entity.Property(character => character.Resources).HasMaxLength(4000);
            entity.Property(character => character.RelationshipsNotes).HasMaxLength(8000);
            entity.Property(character => character.StartingState).HasMaxLength(4000);
            entity.Property(character => character.EndingState).HasMaxLength(4000);
            entity.Property(character => character.BookArc).HasMaxLength(4000);
            entity.Property(character => character.SeriesArc).HasMaxLength(4000);
            entity.Property(character => character.SceneAppearancesNotes).HasMaxLength(8000);
            entity.HasIndex(character => new { character.ProjectId, character.Name });
        });

        modelBuilder.Entity<WorldEntryRecord>(entity =>
        {
            entity.ToTable("WorldEntries");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Name).HasMaxLength(500).IsRequired();
            entity.Property(entry => entry.Category).HasMaxLength(200);
            entity.Property(entry => entry.Depth).HasConversion<int>();
            entity.Property(entry => entry.Notes).HasMaxLength(8000);
            entity.Property(entry => entry.TravelDistance).HasMaxLength(500);
            entity.Property(entry => entry.TravelTime).HasMaxLength(500);
            entity.Property(entry => entry.CanonicalFacts).HasMaxLength(8000);
            entity.Property(entry => entry.ConflictingEntries).HasMaxLength(8000);
            entity.HasIndex(entry => new { entry.ProjectId, entry.Category, entry.Name });
        });

        modelBuilder.Entity<BeatRecord>(entity =>
        {
            entity.ToTable("Beats");
            entity.HasKey(beat => beat.Id);
            entity.Property(beat => beat.Name).HasMaxLength(500).IsRequired();
            entity.Property(beat => beat.Summary).HasMaxLength(8000);
            entity.Property(beat => beat.ChapterRef).HasMaxLength(200);
            entity.Property(beat => beat.SceneRef).HasMaxLength(200);
            entity.Property(beat => beat.Event).HasMaxLength(4000);
            entity.Property(beat => beat.Cause).HasMaxLength(4000);
            entity.Property(beat => beat.Consequence).HasMaxLength(4000);
            entity.Property(beat => beat.ArcFunction).HasMaxLength(500);
            entity.Property(beat => beat.Theme).HasMaxLength(500);
            entity.Property(beat => beat.Escalation).HasMaxLength(4000);
            entity.Property(beat => beat.Status).HasConversion<int>();
            entity.HasIndex(beat => new { beat.ProjectId, beat.Number }).IsUnique();
        });

        modelBuilder.Entity<SceneRecord>(entity =>
        {
            entity.ToTable("Scenes");
            entity.HasKey(scene => scene.Id);
            entity.Property(scene => scene.Title).HasMaxLength(500).IsRequired();
            entity.Property(scene => scene.Location).HasMaxLength(500);
            entity.Property(scene => scene.Time).HasMaxLength(500);
            entity.Property(scene => scene.Goal).HasMaxLength(4000);
            entity.Property(scene => scene.Opposition).HasMaxLength(4000);
            entity.Property(scene => scene.Stakes).HasMaxLength(4000);
            entity.Property(scene => scene.MainEvent).HasMaxLength(4000);
            entity.Property(scene => scene.Revelation).HasMaxLength(4000);
            entity.Property(scene => scene.EmotionalTurn).HasMaxLength(4000);
            entity.Property(scene => scene.Choice).HasMaxLength(4000);
            entity.Property(scene => scene.Outcome).HasMaxLength(4000);
            entity.Property(scene => scene.Consequence).HasMaxLength(4000);
            entity.Property(scene => scene.SetupObligations).HasMaxLength(8000);
            entity.Property(scene => scene.PayoffObligations).HasMaxLength(8000);
            entity.Property(scene => scene.Status).HasConversion<int>();
            entity.HasIndex(scene => new { scene.ProjectId, scene.ChapterId, scene.SequenceNumber });
            entity.HasOne<ChapterRecord>()
                .WithMany()
                .HasForeignKey(scene => scene.ChapterId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IdeaRecord>(entity =>
        {
            entity.ToTable("Ideas");
            entity.HasKey(idea => idea.Id);
            entity.Property(idea => idea.Title).HasMaxLength(500).IsRequired();
            entity.Property(idea => idea.Notes).HasMaxLength(8000);
            entity.Property(idea => idea.Decision).HasMaxLength(100);
            entity.HasIndex(idea => new { idea.ProjectId, idea.Title });
            entity.HasOne(idea => idea.Score)
                .WithOne(score => score.Idea)
                .HasForeignKey<IdeaScoreRecord>(score => score.IdeaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdeaScoreRecord>(entity =>
        {
            entity.ToTable("IdeaScores");
            entity.HasKey(score => score.Id);
            entity.Property(score => score.Decision).HasMaxLength(100);
            entity.HasIndex(score => score.IdeaId).IsUnique();
            entity.HasIndex(score => score.ProjectId);
        });

        modelBuilder.Entity<SubmissionRecord>(entity =>
        {
            entity.ToTable("Submissions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(500).IsRequired();
            entity.Property(item => item.AgencyOrPublisher).HasMaxLength(500);
            entity.Property(item => item.Website).HasMaxLength(1000);
            entity.Property(item => item.Fit).HasMaxLength(2000);
            entity.Property(item => item.Requirements).HasMaxLength(4000);
            entity.Property(item => item.MaterialSent).HasMaxLength(2000);
            entity.Property(item => item.Response).HasConversion<int>();
            entity.Property(item => item.Outcome).HasConversion<int>();
            entity.Property(item => item.RouteAffinity).HasConversion<int>();
            entity.HasIndex(item => new { item.ProjectId, item.Name });
            entity.HasIndex(item => new { item.ProjectId, item.Outcome });
            entity.HasIndex(item => new { item.ProjectId, item.RouteAffinity });
        });

        modelBuilder.Entity<LaunchItemRecord>(entity =>
        {
            entity.ToTable("LaunchItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Phase).HasMaxLength(200);
            entity.Property(item => item.Channel).HasMaxLength(200);
            entity.Property(item => item.Asset).HasMaxLength(500).IsRequired();
            entity.Property(item => item.Audience).HasMaxLength(500);
            entity.Property(item => item.Owner).HasMaxLength(200);
            entity.Property(item => item.Link).HasMaxLength(1000);
            entity.Property(item => item.Result).HasMaxLength(4000);
            entity.Property(item => item.Status).HasConversion<int>();
            entity.Property(item => item.RouteAffinity).HasConversion<int>();
            entity.HasIndex(item => new { item.ProjectId, item.Date });
            entity.HasIndex(item => new { item.ProjectId, item.Status });
        });

        modelBuilder.Entity<RightsAndContractRecord>(entity =>
        {
            entity.ToTable("RightsAndContracts");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Party).HasMaxLength(500).IsRequired();
            entity.Property(item => item.RightOrService).HasMaxLength(500).IsRequired();
            entity.Property(item => item.Territory).HasMaxLength(200);
            entity.Property(item => item.Format).HasMaxLength(200);
            entity.Property(item => item.Payment).HasPrecision(18, 2);
            entity.Property(item => item.PaymentNotes).HasMaxLength(2000);
            entity.Property(item => item.Restrictions).HasMaxLength(4000);
            entity.Property(item => item.AgreementFile).HasMaxLength(1000);
            entity.Property(item => item.Status).HasConversion<int>();
            entity.HasIndex(item => new { item.ProjectId, item.Party });
            entity.HasIndex(item => new { item.ProjectId, item.Status });
        });

        modelBuilder.Entity<PublishingFormatRecord>(entity =>
        {
            entity.ToTable("PublishingFormats");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(500).IsRequired();
            entity.Property(item => item.IsbnOrAsin).HasMaxLength(100);
            entity.Property(item => item.TrimOrFileSpec).HasMaxLength(500);
            entity.Property(item => item.Price).HasPrecision(18, 2);
            entity.Property(item => item.Distributor).HasMaxLength(500);
            entity.Property(item => item.AssetLink).HasMaxLength(1000);
            entity.Property(item => item.Notes).HasMaxLength(4000);
            entity.Property(item => item.FormatKind).HasConversion<int>();
            entity.Property(item => item.PublicationStatus).HasConversion<int>();
            entity.HasIndex(item => new { item.ProjectId, item.Name });
            entity.HasIndex(item => new { item.ProjectId, item.PublicationStatus });
        });

        modelBuilder.Entity<MetadataRecordEntity>(entity =>
        {
            entity.ToTable("MetadataRecords");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Title).HasMaxLength(500).IsRequired();
            entity.Property(item => item.Subtitle).HasMaxLength(500);
            entity.Property(item => item.Series).HasMaxLength(500);
            entity.Property(item => item.SeriesNumber).HasMaxLength(50);
            entity.Property(item => item.Author).HasMaxLength(500);
            entity.Property(item => item.Description).HasMaxLength(8000);
            entity.Property(item => item.Categories).HasMaxLength(2000);
            entity.Property(item => item.SearchTerms).HasMaxLength(2000);
            entity.Property(item => item.ReaderAge).HasMaxLength(100);
            entity.Property(item => item.Language).HasMaxLength(100);
            entity.Property(item => item.Edition).HasMaxLength(200);
            entity.Property(item => item.Publisher).HasMaxLength(500);
            entity.Property(item => item.PricingNotes).HasMaxLength(2000);
            entity.Property(item => item.TerritoryRights).HasMaxLength(2000);
            entity.Property(item => item.Isbn).HasMaxLength(100);
            entity.Property(item => item.FormatName).HasMaxLength(200);
            entity.Property(item => item.PublicationStatus).HasConversion<int>();
            entity.HasIndex(item => new { item.ProjectId, item.Title });
            entity.HasIndex(item => new { item.ProjectId, item.PublicationStatus });
        });

        modelBuilder.Entity<PerformanceRecordEntity>(entity =>
        {
            entity.ToTable("PerformanceRecords");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.PeriodLabel).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Format).HasMaxLength(200);
            entity.Property(item => item.Sales).HasPrecision(18, 2);
            entity.Property(item => item.ReadThrough).HasPrecision(18, 4);
            entity.Property(item => item.AdCost).HasPrecision(18, 2);
            entity.Property(item => item.Availability).HasMaxLength(500);
            entity.Property(item => item.ReturnsOrIssues).HasMaxLength(4000);
            entity.Property(item => item.Notes).HasMaxLength(4000);
            entity.HasIndex(item => new { item.ProjectId, item.PeriodStart });
            entity.HasIndex(item => new { item.ProjectId, item.Format });
        });

        modelBuilder.Entity<CorrectionRecord>(entity =>
        {
            entity.ToTable("Corrections");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Error).HasMaxLength(2000).IsRequired();
            entity.Property(item => item.Location).HasMaxLength(500);
            entity.Property(item => item.CorrectionText).HasMaxLength(4000).IsRequired();
            entity.Property(item => item.FormatsUpdated).HasMaxLength(500);
            entity.Property(item => item.NewEdition).HasMaxLength(200);
            entity.Property(item => item.Status).HasConversion<int>();
            entity.HasIndex(item => new { item.ProjectId, item.Status });
            entity.HasIndex(item => new { item.ProjectId, item.ReportedDate });
        });

        modelBuilder.Entity<DraftingTargetRecord>(entity =>
        {
            entity.ToTable("DraftingTargets");
            entity.HasKey(item => item.ProjectId);
            entity.HasOne(item => item.Project)
                .WithMany()
                .HasForeignKey(item => item.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DraftingSessionRecord>(entity =>
        {
            entity.ToTable("DraftingSessions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ViewpointLabel).HasMaxLength(500);
            entity.HasIndex(item => new { item.ProjectId, item.StartedUtc });
            entity.HasIndex(item => new { item.ProjectId, item.CompletionReason, item.EndedUtc });
            entity.HasOne(item => item.Project)
                .WithMany()
                .HasForeignKey(item => item.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
