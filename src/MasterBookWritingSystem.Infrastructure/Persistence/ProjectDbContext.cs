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

    public DbSet<SchemaVersionRecord> SchemaVersions => Set<SchemaVersionRecord>();

    public DbSet<WorkflowPhaseRecord> WorkflowPhases => Set<WorkflowPhaseRecord>();

    public DbSet<WorkflowStepRecord> WorkflowSteps => Set<WorkflowStepRecord>();

    public DbSet<StepProgressRecord> StepProgress => Set<StepProgressRecord>();

    public DbSet<PhaseGateRecord> PhaseGates => Set<PhaseGateRecord>();

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

        modelBuilder.Entity<ChapterRecord>(entity =>
        {
            entity.ToTable("Chapters");
            entity.HasKey(chapter => chapter.Id);
            entity.Property(chapter => chapter.Title).HasMaxLength(500).IsRequired();
            entity.Property(chapter => chapter.RelativeMarkdownPath).HasMaxLength(1000).IsRequired();
            entity.Property(chapter => chapter.ContentHash).HasMaxLength(128);
            entity.HasIndex(chapter => new { chapter.ProjectId, chapter.SequenceNumber }).IsUnique();
            entity.HasOne(chapter => chapter.Project)
                .WithMany(project => project.Chapters)
                .HasForeignKey(chapter => chapter.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
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
    }
}
