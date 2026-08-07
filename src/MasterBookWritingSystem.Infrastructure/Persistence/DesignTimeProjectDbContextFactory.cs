using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MasterBookWritingSystem.Infrastructure.Persistence;

public sealed class DesignTimeProjectDbContextFactory : IDesignTimeDbContextFactory<ProjectDbContext>
{
    public ProjectDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ProjectDbContext>()
            .UseSqlite("Data Source=design-time.mbws")
            .Options;

        return new ProjectDbContext(options);
    }
}
