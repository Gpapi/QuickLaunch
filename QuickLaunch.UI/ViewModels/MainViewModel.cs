using CommunityToolkit.Mvvm.ComponentModel;

namespace QuickLaunch.UI.ViewModels;

/// <summary>
/// State behind the launcher bar. Deliberately holds no Win32 or window concerns —
/// those stay in MainWindow, which owns the HWND.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// The raw text in the search box. Two-way bound with UpdateSourceTrigger=PropertyChanged,
    /// so this updates on every keystroke, not on focus loss.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueryEmpty))]
    public partial string QueryText { get; set; } = string.Empty;

    /// <summary>
    /// Drives the placeholder's visibility. x:Bind converts bool to Visibility implicitly,
    /// so no converter is needed in XAML.
    /// </summary>
    public bool IsQueryEmpty => QueryText.Length == 0;

    /// <summary>
    /// The prompt shown while the query is empty. Also doubles as the channel for
    /// startup problems the user needs to know about, such as a rejected hot key.
    /// </summary>
    [ObservableProperty]
    public partial string PlaceholderText { get; set; } = "Search apps, files...";

    /// <summary>
    /// Label of the shortcut that actually got registered, e.g. "Ctrl+Alt+Space".
    /// Shown in the tray tooltip so the user can always find out how to summon the launcher.
    /// </summary>
    [ObservableProperty]
    public partial string HotKey { get; set; } = "Alt+Space";

    /// <summary>Clears the query. Returns true if there was anything to clear.</summary>
    public bool Clear()
    {
        if (IsQueryEmpty)
        {
            return false;
        }

        QueryText = string.Empty;
        return true;
    }
}
