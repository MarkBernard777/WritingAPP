using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Architecture;

public class LayerDependencyTests
{
    [Fact]
    public void Core_DoesNotReference_InfrastructureOrApp()
    {
        var referencedNames = typeof(Project).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("MasterBookWritingSystem.Infrastructure", referencedNames);
        Assert.DoesNotContain("MasterBookWritingSystem.App", referencedNames);
    }

    [Fact]
    public void Infrastructure_DoesNotReference_App()
    {
        var referencedNames = typeof(InfrastructureServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("MasterBookWritingSystem.App", referencedNames);
        Assert.Contains("MasterBookWritingSystem.Core", referencedNames);
    }

    [Fact]
    public void Infrastructure_Registers_ApplicationPaths()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure();

        using var provider = services.BuildServiceProvider();
        var paths = provider.GetRequiredService<IApplicationPaths>();

        Assert.Contains("MasterBookWritingSystem", paths.LocalAppDataDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectRepository_Contract_LivesInCore()
    {
        Assert.True(typeof(IProjectRepository).IsInterface);
        Assert.Equal("MasterBookWritingSystem.Core", typeof(IProjectRepository).Assembly.GetName().Name);
    }
}
