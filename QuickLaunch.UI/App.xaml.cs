using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using QuickLaunch.Core.Abstractions;
using QuickLaunch.Core.Indexing;
using QuickLaunch.Core.Providers;
using QuickLaunch.Core.Search;
using QuickLaunch.UI.Services;
using QuickLaunch.UI.ViewModels;

namespace QuickLaunch.UI;

/// <summary>
/// Composition root and application lifetime owner.
/// </summary>
public partial class App : Application
{
    private readonly SingleInstanceGate _gate;
    private readonly bool _startHidden;
    private readonly bool _autoHide;

    private MainWindow? _window;
    private ServiceProvider? _services;
    private SearchCoordinator? _search;

    /// <summary>
    /// The application service provider. Views resolve their view models from here;
    /// everything else is constructor-injected.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    internal App(SingleInstanceGate gate, bool startHidden, bool autoHide)
    {
        _gate = gate;
        _startHidden = startHidden;
        _autoHide = autoHide;

        UnhandledException += (_, e) => CrashLog.Write(e.Exception);

        InitializeComponent();

        _services = ConfigureServices();
        Services = _services;
    }

    /// <summary>
    /// Builds the DI container. Search providers and indexing services are registered
    /// here as they land; the container is built once, at startup, before any window exists.
    /// </summary>
    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<MessageWindow>();
        services.AddSingleton<HotKeyService>();
        services.AddSingleton<TrayIconService>();

        services.AddSingleton<AppCatalog>();
        services.AddSingleton(new FileIndexOptions());
        services.AddSingleton<FileIndexService>();

        services.AddSingleton<ISearchProvider, AppSearchProvider>();
        services.AddSingleton<ISearchProvider, SettingsSearchProvider>();
        services.AddSingleton<ISearchProvider, FileSearchProvider>();
        services.AddSingleton(new WebSearchOptions());
        services.AddSingleton<ISearchProvider, WebSearchProvider>();
        services.AddSingleton<SearchOrchestrator>();
        services.AddSingleton<IconService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = Services.GetRequiredService<MainWindow>();

        // The XAML tree only lays out once the window has been activated, so activate
        // unconditionally and let the window hide itself again if we start in the background.
        _window.StartHidden = _startHidden;
        _window.AutoHideOnDeactivate = _autoHide;
        _window.Activate();

        StartSearch();
        RegisterHotKeys();
        SetUpTrayIcon();

        // A later launch (Start menu, shortcut, boot task racing us) summons this instance.
        _gate.ListenForActivation(() => _window.DispatcherQueue.TryEnqueue(() => _window.ShowLauncher()));
    }

    private void StartSearch()
    {
        var catalog = Services.GetRequiredService<AppCatalog>();
        var files = Services.GetRequiredService<FileIndexService>();

        _search = new SearchCoordinator(
            _window!.ViewModel,
            Services.GetRequiredService<SearchOrchestrator>(),
            [catalog, files],
            Services.GetRequiredService<IconService>(),
            _window.DispatcherQueue);

        _search.Start();

        // Both sources touch the disk and the shell, so neither is built on the UI thread.
        // Each announces itself when it is ready and the current query is re-run.
        _ = Task.Run(catalog.Refresh);
        _ = Task.Run(files.Start);
    }

    private void RegisterHotKeys()
    {
        var hotKeys = Services.GetRequiredService<HotKeyService>();
        var preferred = HotKeyBinding.Defaults[0];
        string? firstError = null;

        foreach (var binding in HotKeyBinding.Defaults)
        {
            if (hotKeys.TryRegister(binding.Modifiers, binding.VirtualKey, () => _window!.ToggleLauncher(), out string? error))
            {
                _window!.ViewModel.HotKey = binding.ToString();

                if (binding != preferred)
                {
                    // Say so rather than silently binding something the user did not expect.
                    _window.ViewModel.PlaceholderText =
                        $"{preferred} was taken — press {binding} to summon QuickLaunch";
                }

                return;
            }

            firstError ??= error;
        }

        // Every candidate was refused. The tray icon still opens the launcher, so report
        // the problem instead of failing hard.
        _window!.ReportHotKeyFailure($"No shortcut could be registered. {firstError}");
    }

    private void SetUpTrayIcon()
    {
        var tray = Services.GetRequiredService<TrayIconService>();

        tray.ShowRequested += (_, _) => _window!.DispatcherQueue.TryEnqueue(() => _window!.ShowLauncher());
        tray.QuitRequested += (_, _) => _window!.DispatcherQueue.TryEnqueue(Shutdown);

        tray.Show($"QuickLaunch  —  {_window!.ViewModel.HotKey}");
    }

    /// <summary>
    /// Tears down the Win32 resources before exiting. Skipping this leaves a dead tray
    /// icon behind until the user hovers over it.
    /// </summary>
    private void Shutdown()
    {
        _services?.Dispose();
        _services = null;

        Exit();
    }
}
