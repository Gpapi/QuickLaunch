using System;
using System.Numerics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using QuickLaunch.UI.Native;
using QuickLaunch.UI.ViewModels;
using Windows.Graphics;
using Windows.System;

namespace QuickLaunch.UI;

public sealed partial class MainWindow : Window
{
    /// <summary>Fraction of the work area above the launcher. Spotlight sits high, not centred.</summary>
    private const double VerticalPlacement = 0.28;

    /// <summary>
    /// Somewhere far off any real desktop. The window is parked here before its first
    /// activation so that laying out the visual tree never paints a frame the user can see.
    /// </summary>
    private const int OffScreen = -32000;

    private static readonly TimeSpan EntranceSlideDuration = TimeSpan.FromMilliseconds(170);
    private static readonly TimeSpan EntranceFadeDuration = TimeSpan.FromMilliseconds(120);

    private readonly IntPtr _hwnd;

    /// <summary>Scale of the monitor the launcher was last placed on.</summary>
    private double _scale = 1.0;

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
        ApplyWindowChrome();

        Activated += OnActivated;
        ViewModel.ResultsChanged += OnResultsChanged;

        // Translation is animatable without fighting the layout system, which owns Offset.
        ElementCompositionPreview.SetIsTranslationEnabled(RootPanel, true);

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
        AppWindow.MoveAndResize(new RectInt32(OffScreen, OffScreen, 1, 1));
    }

    /// <summary>
    /// Asks DWM for the rounded frame and a border colour that matches the panel's own.
    /// Left to the system default these do not necessarily agree with the radius and
    /// stroke the panel draws just inside them.
    /// </summary>
    private void ApplyWindowChrome()
    {
        uint cornerPreference = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(uint));

        string key = RootPanel.ActualTheme == ElementTheme.Light
            ? "WindowBorderColorLight"
            : "WindowBorderColorDark";

        if (Application.Current.Resources[key] is Windows.UI.Color color)
        {
            // DWM takes 0x00BBGGRR — the opposite channel order to a XAML colour.
            uint borderColor = (uint)(color.R | (color.G << 8) | (color.B << 16));
            Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
        }
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

        PlayEntranceAnimation();
    }

    /// <summary>Dismisses the launcher and resets it, so the next summon starts clean.</summary>
    public void HideLauncher()
    {
        // Deliberately not animated. An exit animation would sit between the keystroke and
        // the launcher getting out of the way, which reads as lag however pretty it looks.
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
        if (AutoHideOnDeactivate
            && args.WindowActivationState == WindowActivationState.Deactivated
            && AppWindow.IsVisible)
        {
            HideLauncher();
        }
    }

    private void OnResultsChanged(object? sender, EventArgs e)
    {
        ResizeToContent();
        ResultsScroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    // ---- Geometry -------------------------------------------------------

    /// <summary>Looks a numeric design token up from the merged theme dictionary.</summary>
    private static double Token(string key) => (double)Application.Current.Resources[key];

    /// <summary>
    /// Height the window needs to show the current results, in DIPs. Derived from the same
    /// tokens the XAML lays out with, so the window can never disagree with its content.
    /// </summary>
    private double ContentHeight()
    {
        double height = Token("SearchRowHeight");

        if (!ViewModel.ShowsResultsArea)
        {
            return height;
        }

        var padding = (Thickness)Application.Current.Resources["ResultsPadding"];

        // The empty state occupies exactly one row, so both cases size the same way.
        int visibleRows = ViewModel.HasResults
            ? Math.Min(ViewModel.ResultCount, MainViewModel.MaxVisibleResults)
            : 1;

        // +1 for the hairline separator above the list.
        return height + 1 + padding.Top + padding.Bottom + (visibleRows * Token("ResultRowHeight"));
    }

    private void PositionOnActiveMonitor()
    {
        Win32.GetCursorPos(out var cursor);

        var display = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
        var workArea = display.WorkArea;
        _scale = GetScaleForPoint(cursor);

        int widthPx = (int)(Token("LauncherWidth") * _scale);
        int heightPx = (int)(ContentHeight() * _scale);

        int x = workArea.X + ((workArea.Width - widthPx) / 2);
        int y = workArea.Y + (int)(workArea.Height * VerticalPlacement);

        AppWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));
    }

    /// <summary>
    /// Grows or shrinks the window to fit the results, keeping the top edge anchored so the
    /// query line stays exactly where the user is already looking.
    /// </summary>
    private void ResizeToContent()
    {
        int heightPx = (int)(ContentHeight() * _scale);

        if (heightPx != AppWindow.Size.Height)
        {
            AppWindow.Resize(new SizeInt32(AppWindow.Size.Width, heightPx));
        }
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

    // ---- Motion ---------------------------------------------------------

    /// <summary>
    /// Short rise and fade as the launcher appears. Expo-out settles almost immediately,
    /// so the panel reads as arriving rather than as travelling.
    /// </summary>
    private void PlayEntranceAnimation()
    {
        var visual = ElementCompositionPreview.GetElementVisual(RootPanel);
        var compositor = visual.Compositor;

        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1.0f), new Vector2(0.3f, 1.0f));

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0.0f, new Vector3(0.0f, (float)Token("EntranceOffsetY"), 0.0f));
        slide.InsertKeyFrame(1.0f, Vector3.Zero, easing);
        slide.Duration = EntranceSlideDuration;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0.0f, 0.0f);
        fade.InsertKeyFrame(1.0f, 1.0f, easing);
        fade.Duration = EntranceFadeDuration;

        visual.StartAnimation("Translation", slide);
        visual.StartAnimation("Opacity", fade);
    }

    // ---- Keyboard -------------------------------------------------------

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;

                // First Escape clears the query, second dismisses — so Escape never throws
                // away something the user can still see.
                if (!ViewModel.Clear())
                {
                    HideLauncher();
                }

                break;

            // Arrow keys are handled here rather than left to the TextBox, which would
            // otherwise move the caret instead of the highlight.
            case VirtualKey.Down:
                e.Handled = true;
                ViewModel.MoveSelection(1);
                ScrollSelectionIntoView();
                break;

            case VirtualKey.Up:
                e.Handled = true;
                ViewModel.MoveSelection(-1);
                ScrollSelectionIntoView();
                break;
        }
    }

    private void ScrollSelectionIntoView()
    {
        if (ViewModel.SelectedIndex < 0)
        {
            return;
        }

        double rowHeight = Token("ResultRowHeight");
        double rowTop = ViewModel.SelectedIndex * rowHeight;
        double rowBottom = rowTop + rowHeight;

        double viewportTop = ResultsScroller.VerticalOffset;
        double viewportBottom = viewportTop + ResultsScroller.ViewportHeight;

        if (rowTop < viewportTop)
        {
            ResultsScroller.ChangeView(null, rowTop, null, disableAnimation: true);
        }
        else if (rowBottom > viewportBottom)
        {
            ResultsScroller.ChangeView(null, rowBottom - ResultsScroller.ViewportHeight, null, disableAnimation: true);
        }
    }
}
