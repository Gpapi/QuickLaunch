using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using QuickLaunch.UI.ViewModels;
using System;

namespace QuickLaunch.UI;

/// <summary>
/// Application entry point and composition root.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>
    /// The application service provider. Views resolve their view models from here;
    /// everything else is constructor-injected.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    /// <summary>
    /// Builds the DI container. Search providers and indexing services are registered
    /// here as they land; the container is built once, at startup, before any window exists.
    /// </summary>
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }
}
