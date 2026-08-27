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
    /// Drives the placeholder. x:Bind converts bool to Visibility implicitly,
    /// so no converter is needed in XAML.
    /// </summary>
    public bool IsQueryEmpty => QueryText.Length == 0;

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
