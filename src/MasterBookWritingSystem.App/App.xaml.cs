using System.Windows;
using MasterBookWritingSystem.App.DependencyInjection;
using MasterBookWritingSystem.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MasterBookWritingSystem.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var seedErrors = SeedPaths.ValidateRequiredSeedFiles();
        if (seedErrors.Count > 0)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, seedErrors)
                + Environment.NewLine
                + Environment.NewLine
                + "The application cannot start without its seed templates. "
                + "Reinstall or restore the seed folder next to the executable.",
                "Master Book-Writing System — Missing seed content",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(static (_, services) => services.AddApplicationServices())
            .Build();

        await _host.StartAsync().ConfigureAwait(true);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
