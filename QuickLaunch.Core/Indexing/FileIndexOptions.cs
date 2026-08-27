using System;
using System.Collections.Generic;
using System.IO;

namespace QuickLaunch.Core.Indexing;

/// <summary>
/// What the file index covers.
/// </summary>
public sealed record FileIndexOptions
{
    /// <summary>Folders to index, each walked recursively.</summary>
    public IReadOnlyList<string> Roots { get; init; } =
        [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)];

    /// <summary>
    /// Folder names never descended into.
    /// </summary>
    /// <remarks>
    /// Hidden and system folders are already skipped by attribute, which covers AppData,
    /// .git and $Recycle.Bin. These are the ones that are plainly visible yet still hold
    /// tens of thousands of files nobody searches for by name.
    /// </remarks>
    public IReadOnlySet<string> ExcludedFolderNames { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules",
            "__pycache__",
            ".gradle",
            ".nuget",
        };

    /// <summary>
    /// Whether to skip folders whose name begins with a dot.
    /// </summary>
    /// <remarks>
    /// Windows marks its own machinery hidden, but cross-platform tools do not: .vscode,
    /// .cargo, .rustup and .npm sit in the profile in plain sight and hold tens of
    /// thousands of files. They are caches and package stores, not things anyone searches
    /// for by name, and leaving them in buries real results under them.
    /// </remarks>
    public bool ExcludeDotFolders { get; init; } = true;

    /// <summary>
    /// How deep to descend. Deep trees are almost always generated rather than authored,
    /// and the limit also bounds the damage from a directory structure that loops.
    /// </summary>
    public int MaxDepth { get; init; } = 16;

    /// <summary>
    /// Ceiling on indexed entries, so a pathological tree cannot exhaust memory.
    /// </summary>
    public int MaxEntries { get; init; } = 1_000_000;

    /// <summary>Where the index snapshot is cached between runs.</summary>
    public string SnapshotPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickLaunch",
        "index.bin");
}
