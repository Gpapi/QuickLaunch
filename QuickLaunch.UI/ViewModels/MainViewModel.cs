using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickLaunch.Core.Abstractions;

namespace QuickLaunch.UI.ViewModels;

/// <summary>
/// State behind the launcher bar. Deliberately holds no Win32 or window concerns —
/// those stay in MainWindow, which owns the HWND.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>How many rows the window will grow to show before the list starts scrolling.</summary>
    public const int MaxVisibleResults = 8;

    /// <summary>
    /// The raw text in the search box. Two-way bound with UpdateSourceTrigger=PropertyChanged,
    /// so this updates on every keystroke, not on focus loss.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueryEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowsEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowsResultsArea))]
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
    public partial string PlaceholderText { get; set; } = "Search apps, files and settings";

    /// <summary>
    /// Label of the shortcut that actually got registered, e.g. "Ctrl+Alt+Space".
    /// Shown in the tray tooltip so the user can always find out how to summon the launcher.
    /// </summary>
    [ObservableProperty]
    public partial string HotKey { get; set; } = "Alt+Space";

    public ObservableCollection<ResultItemViewModel> Results { get; } = [];

    /// <summary>
    /// Raised on every keystroke. Whatever is producing results subscribes to this;
    /// the view model itself stays unaware of where results come from.
    /// </summary>
    public event EventHandler<string>? QueryChanged;

    partial void OnQueryTextChanged(string value) => QueryChanged?.Invoke(this, value);

    /// <summary>Raised when the result set changes, so the window can resize to fit.</summary>
    public event EventHandler? ResultsChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    [NotifyPropertyChangedFor(nameof(ShowsEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowsResultsArea))]
    public partial int ResultCount { get; private set; }

    public bool HasResults => ResultCount > 0;

    /// <summary>True when the user has typed something but nothing matched.</summary>
    public bool ShowsEmptyState => !IsQueryEmpty && ResultCount == 0;

    /// <summary>Whether the panel below the query line is shown at all.</summary>
    public bool ShowsResultsArea => HasResults || ShowsEmptyState;

    /// <summary>Index of the highlighted row, or -1 when there is nothing to highlight.</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; private set; } = -1;

    public ResultItemViewModel? SelectedResult =>
        SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    /// <summary>Replaces the visible results and selects the best one.</summary>
    public void SetResults(IEnumerable<SearchResult> results)
    {
        Results.Clear();

        foreach (var result in results)
        {
            Results.Add(new ResultItemViewModel(result));
        }

        ResultCount = Results.Count;
        Select(Results.Count > 0 ? 0 : -1);

        ResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves the highlight by <paramref name="delta"/> rows, wrapping at both ends so
    /// holding Down cycles rather than sticking at the bottom.
    /// </summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        int next = (SelectedIndex + delta) % Results.Count;

        if (next < 0)
        {
            next += Results.Count;
        }

        Select(next);
    }

    public void Select(int index)
    {
        if (SelectedResult is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedIndex = index;

        if (SelectedResult is { } current)
        {
            current.IsSelected = true;
        }
    }

    /// <summary>Clears the query and its results. Returns true if there was anything to clear.</summary>
    public bool Clear()
    {
        if (IsQueryEmpty && Results.Count == 0)
        {
            return false;
        }

        QueryText = string.Empty;
        SetResults([]);
        return true;
    }
}
