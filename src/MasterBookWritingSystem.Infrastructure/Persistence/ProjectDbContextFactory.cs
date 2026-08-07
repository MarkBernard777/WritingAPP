using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Persistence;

public static class ProjectDbContextFactory
{
    public static ProjectDbContext Create(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true,
        }.ToString();

        var options = new DbContextOptionsBuilder<ProjectDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new ProjectDbContext(options);
    }
}
