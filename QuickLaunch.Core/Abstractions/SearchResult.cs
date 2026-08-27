using System.Collections.Generic;

namespace QuickLaunch.Core.Abstractions;

/// <summary>
/// What a result represents. Drives the fallback glyph, the grouping hint shown on the
/// row, and the provider weighting used when ranking.
/// </summary>
public enum ResultKind
{
    Application,
    File,
    Folder,
    Setting,
    Url,
    WebSearch,
}

/// <summary>
/// A run of characters in a title that matched the query, so the view can emphasise them.
/// </summary>
public readonly record struct MatchSpan(int Start, int Length);

/// <summary>
/// One row of the launcher: everything the UI needs to draw and rank a candidate.
/// </summary>
/// <remarks>
/// Launching and icon resolution are deliberately absent for now — they arrive with the
/// providers in M2 rather than as placeholders that assert capability the app lacks.
/// </remarks>
public sealed record SearchResult
{
    /// <summary>
    /// Stable identity for this result across searches, used as the frecency key.
    /// Conventionally "kind:canonical-target", e.g. "app:C:\...\chrome.exe".
    /// </summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Supporting line: a path, a category, or the action that will be taken.</summary>
    public string Subtitle { get; init; } = string.Empty;

    public ResultKind Kind { get; init; }

    /// <summary>Match quality from the fuzzy matcher, before provider weighting.</summary>
    public int Score { get; init; }

    /// <summary>Regions of <see cref="Title"/> the query matched.</summary>
    public IReadOnlyList<MatchSpan> TitleHighlights { get; init; } = [];
}
