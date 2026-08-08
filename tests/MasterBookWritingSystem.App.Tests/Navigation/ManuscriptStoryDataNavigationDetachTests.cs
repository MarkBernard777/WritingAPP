using System.IO;
using MasterBookWritingSystem.App.DependencyInjection;
using MasterBookWritingSystem.App.Navigation;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.App.ViewModels;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.App.Tests.Navigation;

public sealed class ManuscriptStoryDataNavigationDetachTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly INavigationService _navigation;
    private readonly IProjectService _projects;

    public ManuscriptStoryDataNavigationDetachTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-nav-detach", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddApplicationServices();
        ReplaceSingleton<IProjectDialogService, NoopDialogService>(services);
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _navigation = _provider.GetRequiredService<INavigationService>();
        _projects = _provider.GetRequiredService<IProjectService>();
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
    public async Task Navigate_ManuscriptToStoryData_DoesNotThrow()
    {
        await CreateProjectAsync("Nav");
        _navigation.NavigateTo(AppSection.Manuscript);
        Assert.IsType<ManuscriptViewModel>(_navigation.CurrentViewModel);
        _navigation.NavigateTo(AppSection.StoryData);
        Assert.IsType<StoryDataViewModel>(_navigation.CurrentViewModel);
    }

    [Fact]
    public async Task Navigate_RepeatedlyBetweenManuscriptAndStoryData_DoesNotThrow()
    {
        await CreateProjectAsync("Loop");
        for (var i = 0; i < 5; i++)
        {
            _navigation.NavigateTo(AppSection.Manuscript);
            _navigation.NavigateTo(AppSection.StoryData);
        }

        Assert.IsType<StoryDataViewModel>(_navigation.CurrentViewModel);
    }

    [Fact]
    public async Task ManuscriptDetach_CalledTwice_DoesNotThrow()
    {
        await CreateProjectAsync("DetachTwice");
        _navigation.NavigateTo(AppSection.Manuscript);
        var manuscript = Assert.IsType<ManuscriptViewModel>(_navigation.CurrentViewModel);
        manuscript.Detach();
        manuscript.Detach();
    }

    [Fact]
    public async Task NavigateAway_WhilePreviewOrRefreshPending_DoesNotThrow()
    {
        await CreateProjectAsync("Pending");
        _navigation.NavigateTo(AppSection.Manuscript);
        var manuscript = Assert.IsType<ManuscriptViewModel>(_navigation.CurrentViewModel);
        await manuscript.RefreshCommand.ExecuteAsync(null);
        manuscript.MarkdownText += " pending preview words\n";
        _navigation.NavigateTo(AppSection.StoryData);
        manuscript.Detach();
        Assert.IsType<StoryDataViewModel>(_navigation.CurrentViewModel);
    }

    [Fact]
    public void StoryDataAndDocumentsDetach_AreIdempotent()
    {
        _navigation.NavigateTo(AppSection.StoryData);
        var story = Assert.IsType<StoryDataViewModel>(_navigation.CurrentViewModel);
        story.Detach();
        story.Detach();

        _navigation.NavigateTo(AppSection.Documents);
        var documents = Assert.IsType<DocumentsViewModel>(_navigation.CurrentViewModel);
        documents.Detach();
        documents.Detach();
    }

    private async Task CreateProjectAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = title,
            Author = "Tester",
            Genre = "Fantasy",
            NorthStar = "Detach",
        });
    }

    private static void ReplaceSingleton<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var existing = services.Where(descriptor => descriptor.ServiceType == typeof(TService)).ToList();
        foreach (var descriptor in existing)
        {
            services.Remove(descriptor);
        }

        services.AddSingleton<TService, TImplementation>();
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

    private sealed class NoopDialogService : IProjectDialogService
    {
        public string? PickFolder(string description) => null;

        public CreateProjectDialogResult? PromptCreateProject() => null;

        public void ShowMessage(string message, string caption)
        {
        }

        public bool Confirm(string message, string caption) => true;

        public string? PromptText(string title, string prompt, string? initialValue = null) => initialValue;

        public DialogChoice? PromptChoice(string title, string prompt, IReadOnlyList<DialogChoice> choices)
            => choices.FirstOrDefault();

        public string? PickOpenFile(string title, string filter) => null;

        public string? PickSaveFile(string title, string filter, string defaultFileName) => null;
    }
}
