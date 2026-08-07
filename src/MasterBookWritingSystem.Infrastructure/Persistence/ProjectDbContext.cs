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
    }
}
