using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using QuickLaunch.UI.Native;
using QuickLaunch.UI.ViewModels;
using Windows.Graphics;
using Windows.System;

namespace QuickLaunch.UI;

public sealed partial class MainWindow : Window
{
    private const int LauncherWidthDips = 680;
    private const int LauncherHeightDips = 66;

    /// <summary>Fraction of the work area above the launcher. Spotlight sits high, not centred.</summary>
    private const double VerticalPlacement = 0.28;

    /// <summary>
    /// Somewhere far off any real desktop. The window is parked here before its first
    /// activation so that laying out the visual tree never paints a frame the user can see.
    /// </summary>
    private const int OffScreen = -32000;

    private readonly IntPtr _hwnd;

    public MainViewModel ViewModel { get; }

    /// <summary>Set by the app before activation when launched by the boot task.</summary>
    public bool StartHidden { get; set; }

    /// <summary>
    /// Whether losing focus dismisses the launcher. Always true in normal use; the
    /// --no-auto-hide switch turns it off so the window can be inspected while developing.
    /// </summary>
    public bool AutoHideOnDeactivate { get; set; } = true;

    public MainWindow(MainViewModel viewModel)
    {
        // x:Bind resolves against ViewModel inside InitializeComponent,
        // so the property has to be set before that call.
        ViewModel = viewModel;

        InitializeComponent();

        // WindowNative.GetWindowHandle is the bridge from WinUI 3 into Win32.
        // Every WinUI 3 Window has an underlying HWND — this is how you get it.
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        ConfigureWindow();

        Activated += OnActivated;

        // Loaded fires once the XAML tree is laid out and rendered. Only then does the
        // TextBox exist in the visual tree to receive focus.
        ((FrameworkElement)Content).Loaded += OnContentLoaded;
    }

    private void ConfigureWindow()
    {
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        AppWindow.IsShownInSwitchers = false;

        // Park off-screen. Activate() has to run for the visual tree to lay out, and this
        // keeps that first activation invisible whether or not we are starting hidden.
        AppWindow.MoveAndResize(new RectInt32(OffScreen, OffScreen, LauncherWidthDips, LauncherHeightDips));
    }

    private void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        if (StartHidden)
        {
            AppWindow.Hide();
        }
        else
        {
            ShowLauncher();
        }
    }

    /// <summary>Summons the launcher onto the monitor the mouse is on, focused and ready to type.</summary>
    public void ShowLauncher()
    {
        PositionOnActiveMonitor();

        AppWindow.Show(activateWindow: true);
        ForceForeground();

        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
    }

    /// <summary>Dismisses the launcher and resets it, so the next summon starts clean.</summary>
    public void HideLauncher()
    {
        AppWindow.Hide();
        ViewModel.Clear();
    }

    public void ToggleLauncher()
    {
        if (AppWindow.IsVisible)
        {
            HideLauncher();
        }
        else
        {
            ShowLauncher();
        }
    }

    /// <summary>
    /// Reports that the global shortcut could not be claimed. The launcher still works from
    /// the tray, so this shows the window with the reason in place of the usual prompt.
    /// </summary>
    public void ReportHotKeyFailure(string message)
    {
        ViewModel.PlaceholderText = message;
        ShowLauncher();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        // Losing focus means the user clicked elsewhere — a launcher should get out of the way.
        if (AutoHideOnDeactivate && args.WindowActivationState == WindowActivationState.Deactivated && AppWindow.IsVisible)
        {
            HideLauncher();
        }
    }

    private void PositionOnActiveMonitor()
    {
        Win32.GetCursorPos(out var cursor);

        var display = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
        var workArea = display.WorkArea;
        double scale = GetScaleForPoint(cursor);

        int widthPx = (int)(LauncherWidthDips * scale);
        int heightPx = (int)(LauncherHeightDips * scale);

        int x = workArea.X + ((workArea.Width - widthPx) / 2);
        int y = workArea.Y + (int)(workArea.Height * VerticalPlacement);

        AppWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));
    }

    /// <summary>
    /// Scale factor of the monitor under a point. The window's own DPI is not usable here:
    /// when summoning onto a different monitor it still reports the one it is leaving.
    /// </summary>
    private static double GetScaleForPoint(Win32.POINT point)
    {
        var monitor = Win32.MonitorFromPoint(point, Win32.MONITOR_DEFAULTTONEAREST);

        if (monitor != IntPtr.Zero &&
            Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
        {
            return dpiX / 96.0;
        }

        return 1.0;
    }

    /// <summary>
    /// Pulls the launcher to the front. A hot key press does not grant this process
    /// foreground rights, so Windows would flash the taskbar instead of activating us.
    /// Briefly sharing an input queue with the current foreground thread is the
    /// long-standing way around that.
    /// </summary>
    private void ForceForeground()
    {
        var foreground = Win32.GetForegroundWindow();

        if (foreground == _hwnd)
        {
            return;
        }

        uint foregroundThread = Win32.GetWindowThreadProcessId(foreground, IntPtr.Zero);
        uint currentThread = Win32.GetCurrentThreadId();

        bool attached = foregroundThread != 0
            && foregroundThread != currentThread
            && Win32.AttachThreadInput(currentThread, foregroundThread, true);

        Win32.SetForegroundWindow(_hwnd);
        Win32.SetFocus(_hwnd);

        if (attached)
        {
            Win32.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;

        // First Escape clears the query, second dismisses — so Escape never throws away
        // something the user can still see.
        if (!ViewModel.Clear())
        {
            HideLauncher();
        }
    }
}
