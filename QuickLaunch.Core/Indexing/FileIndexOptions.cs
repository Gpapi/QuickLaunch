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
    /// <remarks>
    /// Every fixed drive, from its root. Indexing only the user profile seems tidy until
    /// you keep your work somewhere else — a projects folder at the root of C: is exactly
    /// the thing people search for, and it would not have been there.
    /// </remarks>
    public IReadOnlyList<string> Roots { get; init; } = FixedDriveRoots();

    /// <summary>
    /// Full paths never descended into, matched exactly rather than by name.
    /// </summary>
    /// <remarks>
    /// These have to be paths, not names: excluding every folder called "Windows" would
    /// also exclude one inside a project. What is here is either already covered by the
    /// application catalogue or is machinery no one searches for by file name.
    /// </remarks>
    public IReadOnlySet<string> ExcludedPaths { get; init; } = DefaultExcludedPaths();

    private static List<string> FixedDriveRoots()
    {
        var roots = new List<string>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    roots.Add(drive.RootDirectory.FullName);
                }
            }
            catch (IOException)
            {
                // A drive that disappears mid-enumeration is simply not indexed.
            }
        }

        // Better a profile-only index than none at all.
        if (roots.Count == 0)
        {
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        return roots;
    }

    private static HashSet<string> DefaultExcludedPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.Windows,
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.CommonApplicationData,
                 })
        {
            string path = Environment.GetFolderPath(folder);

            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path.TrimEnd(Path.DirectorySeparatorChar));
            }
        }

        string systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";

        paths.Add(Path.Combine(systemDrive, "PerfLogs"));
        paths.Add(Path.Combine(systemDrive, "Recovery"));

        return paths;
    }

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
