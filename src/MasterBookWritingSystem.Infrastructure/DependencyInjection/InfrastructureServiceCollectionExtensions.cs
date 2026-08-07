using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Infrastructure.Paths;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers infrastructure services. SQLite, filesystem and export
    /// implementations are added in later milestones.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IApplicationPaths, LocalApplicationPaths>();
        return services;
    }
}
