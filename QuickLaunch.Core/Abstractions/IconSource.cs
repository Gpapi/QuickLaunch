namespace QuickLaunch.Core.Abstractions;

public enum IconSourceKind
{
    /// <summary>Ask the shell for the icon of a path or moniker (file, folder, or app).</summary>
    Shell,

    /// <summary>Load an image file directly, e.g. a package's logo asset.</summary>
    Image,
}

/// <summary>
/// Where a result's artwork comes from. Core describes it; the UI turns it into a bitmap,
/// so no imaging types leak into the search layer.
/// </summary>
public sealed record IconSource(IconSourceKind Kind, string Path);
