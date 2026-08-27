using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using QuickLaunch.Core.Abstractions;

namespace QuickLaunch.UI.ViewModels;

/// <summary>
/// One row in the results list: a <see cref="SearchResult"/> plus the presentation-only
/// state the view needs.
/// </summary>
public sealed partial class ResultItemViewModel(SearchResult result) : ObservableObject
{
    public SearchResult Result { get; } = result;

    public string Title => Result.Title;

    public string Subtitle => Result.Subtitle;

    /// <summary>True when the subtitle should take up space at all.</summary>
    public bool HasSubtitle => !string.IsNullOrEmpty(Result.Subtitle);

    /// <summary>
    /// The result's real artwork, filled in shortly after the row appears. Null until
    /// then, and for results the shell has no icon for.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    public partial ImageSource? Icon { get; set; }

    public bool HasIcon => Icon is not null;

    /// <summary>
    /// Stand-in shown until the icon arrives, and permanently for results that have none.
    /// Drawn at the same size and position, so nothing shifts when the icon replaces it.
    /// </summary>
    public string Glyph => Result.Kind switch
    {
        ResultKind.Application => "\uE71D",   // AllApps
        ResultKind.File => "\uE8A5",   // Document
        ResultKind.Folder => "\uE8B7",   // Folder
        ResultKind.Setting => "\uE713",   // Settings
        ResultKind.Url => "\uE71B",   // Link
        ResultKind.WebSearch => "\uE721",   // Search
        _ => "\uE8A5",   // Document
    };

    /// <summary>Short right-aligned label telling the user what kind of thing this is.</summary>
    public string KindHint => Result.Kind switch
    {
        ResultKind.Application => "App",
        ResultKind.File => "File",
        ResultKind.Folder => "Folder",
        ResultKind.Setting => "Setting",
        ResultKind.Url => "Link",
        ResultKind.WebSearch => "Web",
        _ => string.Empty,
    };

    /// <summary>Drives the row's selection treatment. Set by the list, never by the row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsHover))]
    public partial bool IsSelected { get; set; }

    /// <summary>Whether the pointer is currently over this row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsHover))]
    public partial bool IsPointerOver { get; set; }

    /// <summary>
    /// Hover is only drawn on rows that are not already selected — stacking the two
    /// fills would make the hovered row read as the selected one.
    /// </summary>
    public bool ShowsHover => IsPointerOver && !IsSelected;
}
