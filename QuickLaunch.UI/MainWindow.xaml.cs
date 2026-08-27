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
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        // x:Bind resolves against ViewModel inside InitializeComponent,
        // so the property has to be set before that call.
        ViewModel = viewModel;

        InitializeComponent();
        ConfigureWindow();

        // Grid.Loaded fires once the XAML tree is laid out and rendered.
        // This is the right place to focus the TextBox — earlier than this
        // and the control doesn't exist yet in the visual tree.
        ((FrameworkElement)Content).Loaded += OnContentLoaded;
    }

    private void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        CenterOnScreen();
        SearchBox.Focus(FocusState.Programmatic);
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
    }

    private void CenterOnScreen()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var workArea = display.WorkArea;

        // WindowNative.GetWindowHandle is the bridge from WinUI 3 into Win32.
        // Every WinUI 3 Window has an underlying HWND — this is how you get it.
        // You'll see this same call again when we register the global hotkey.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // GetDpiForWindow returns the actual DPI for the monitor this window is on.
        // Dividing by 96 (the baseline DPI) gives us the scale factor.
        var scale = User32.GetDpiForWindow(hwnd) / 96.0;

        int widthPx  = (int)(680 * scale);
        int heightPx = (int)(66 * scale);

        int x = workArea.X + (workArea.Width  - widthPx) / 2;
        int y = workArea.Y + (int)(workArea.Height * 0.28);

        AppWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.Clear();
            e.Handled = true;
            // Later: hide the window when the query is already empty
        }
    }
}
