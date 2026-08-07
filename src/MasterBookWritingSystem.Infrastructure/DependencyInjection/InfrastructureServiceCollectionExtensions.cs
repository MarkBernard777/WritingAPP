using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Infrastructure.Paths;
using MasterBookWritingSystem.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IApplicationPaths, LocalApplicationPaths>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IChapterFileStore, ChapterFileStore>();
        return services;
    }
}
