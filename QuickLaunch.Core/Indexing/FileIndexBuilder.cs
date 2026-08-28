using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace QuickLaunch.Core.Indexing;

/// <summary>
/// Walks the configured roots and produces a <see cref="FileIndex"/>.
/// </summary>
public static class FileIndexBuilder
{
    /// <summary>
    /// Enumerates every root. Blocking and I/O bound; run it off the UI thread.
    /// </summary>
    public static FileIndex Build(FileIndexOptions options, CancellationToken cancellationToken)
    {
        var names = new List<string>(1 << 16);
        var parents = new List<int>(1 << 16);
        var directories = new List<bool>(1 << 16);

        foreach (string root in options.Roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            // A root keeps its whole path as its name, so path reconstruction has a base
            // to stop at and no entry ever stores a path prefix twice.
            names.Add(root);
            parents.Add(-1);
            directories.Add(true);

            Walk(root, names.Count - 1, options, names, parents, directories, cancellationToken);
        }

        return new FileIndex([.. names], [.. parents], [.. directories]);
    }

    private static void Walk(
        string rootPath,
        int rootIndex,
        FileIndexOptions options,
        List<string> names,
        List<int> parents,
        List<bool> directories,
        CancellationToken cancellationToken)
    {
        // Skipping hidden and system entries keeps AppData, .git and $Recycle.Bin out
        // without naming them.
        //
        // Reparse points are deliberately NOT skipped here. With OneDrive Files On-Demand —
        // on by default, and Windows 11 turns on Known Folder Backup out of the box — every
        // file under a synced Desktop, Documents or Pictures is a cloud placeholder carrying
        // the reparse attribute. Skipping the attribute wholesale would drop the contents of
        // exactly the folders people search most. Reparse *directories* are still skipped
        // below, since that is where junction and symlink loops live.
        var enumeration = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        var pending = new Stack<(string Path, int Index, int Depth)>();
        pending.Push((rootPath, rootIndex, 0));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (path, index, depth) = pending.Pop();

            IEnumerable<FileSystemInfo> entries;

            try
            {
                entries = new DirectoryInfo(path).EnumerateFileSystemInfos("*", enumeration);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A folder that vanished or refuses access is not a reason to abandon the
                // rest of the tree.
                continue;
            }

            foreach (var entry in Safely(entries))
            {
                if (names.Count >= options.MaxEntries)
                {
                    return;
                }

                var attributes = entry.Attributes;
                bool isDirectory = (attributes & FileAttributes.Directory) != 0;

                // A junction or directory symlink points at a tree that is either indexed
                // elsewhere or loops back here.
                if (isDirectory && (attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (isDirectory && IsExcludedFolder(entry, options))
                {
                    continue;
                }

                names.Add(entry.Name);
                parents.Add(index);
                directories.Add(isDirectory);

                if (isDirectory && depth + 1 < options.MaxDepth)
                {
                    pending.Push((entry.FullName, names.Count - 1, depth + 1));
                }
            }
        }
    }

    private static bool IsExcludedFolder(FileSystemInfo entry, FileIndexOptions options) =>
        options.ExcludedFolderNames.Contains(entry.Name)
        || (options.ExcludeDotFolders && entry.Name.StartsWith('.'))
        || options.ExcludedPaths.Contains(entry.FullName.TrimEnd(Path.DirectorySeparatorChar));

    /// <summary>
    /// Enumerating can throw part-way through, after yielding some entries. This keeps
    /// what was produced before the failure instead of losing the whole directory.
    /// </summary>
    private static IEnumerable<FileSystemInfo> Safely(IEnumerable<FileSystemInfo> entries)
    {
        using var enumerator = entries.GetEnumerator();

        while (true)
        {
            FileSystemInfo current;

            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                current = enumerator.Current;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                yield break;
            }

            yield return current;
        }
    }
}
