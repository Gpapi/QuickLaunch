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

    /// <summary>
    /// Whether a path could ever appear in the index.
    /// </summary>
    /// <remarks>
    /// Used to decide whether a file system event is worth reacting to. Without this the
    /// watchers reset their countdown for churn under Windows, ProgramData and AppData —
    /// none of which the walker would ever index, so the work could not change the result.
    /// </remarks>
    public bool CouldContain(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (string excluded in ExcludedPaths)
        {
            if (path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)
                && (path.Length == excluded.Length || path[excluded.Length] == Path.DirectorySeparatorChar))
            {
                return false;
            }
        }

        // Every directory on the way down, and the entry itself; the leaf is checked too
        // because a file inside an excluded folder is what most events are.
        foreach (var segment in path.AsSpan().EnumerateDirectorySeparatedSegments())
        {
            if (segment.Length == 0)
            {
                continue;
            }

            if (ExcludeDotFolders && segment[0] == '.')
            {
                return false;
            }

            if (ExcludedFolderNames.Contains(segment.ToString()))
            {
                return false;
            }
        }

        return true;
    }

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
    ///
    /// Build output is here for the same reason: on a machine with source on it, bin and
    /// obj hold copies of everything that was compiled, and those copies compete with the
    /// source files for the top rows. The rule matches by name at any depth, so a folder
    /// genuinely called "bin" is excluded too — the trade is deliberate, and this list is
    /// the place to change it.
    /// </remarks>
    public IReadOnlySet<string> ExcludedFolderNames { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules",
            "__pycache__",
            ".gradle",
            ".nuget",
            "bin",
            "obj",
            ".vs",
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
