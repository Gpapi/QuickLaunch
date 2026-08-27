using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using QuickLaunch.Core.Matching;

namespace QuickLaunch.Core.Indexing;

/// <summary>
/// One launchable application, prepared for matching.
/// </summary>
/// <param name="Name">Display name, as the Start menu shows it.</param>
/// <param name="LaunchId">Identity to launch through the Applications folder.</param>
/// <param name="FilePath">Executable path, for apps that have one. Packaged apps do not.</param>
public sealed record AppEntry(string Name, string LaunchId, string? FilePath)
{
    /// <summary>
    /// Strings a query may match against, best first. The executable's own name is
    /// included because people search for what they type in a terminal: "code" should
    /// find Visual Studio Code even though its title never contains that word alone.
    /// </summary>
    public required IReadOnlyList<string> SearchTerms { get; init; }

    /// <summary>Character masks for <see cref="SearchTerms"/>, for cheap rejection.</summary>
    public required IReadOnlyList<ulong> SearchTermMasks { get; init; }

    /// <summary>The moniker that both launches this app and yields its icon.</summary>
    public string ShellPath => $@"shell:AppsFolder\{LaunchId}";
}

/// <summary>
/// The set of applications the launcher can start.
/// </summary>
/// <remarks>
/// Enumerates the shell's Applications folder rather than stitching together Start menu
/// shortcuts, the package manager and the App Paths registry key. That folder is already
/// the union of all three, is what Windows itself shows, and gives packaged and
/// unpackaged apps a single launch identity and a single icon path — which is also why
/// there is no .lnk parsing here.
/// </remarks>
public sealed class AppCatalog
{
    private volatile IReadOnlyList<AppEntry> _entries = [];

    /// <summary>The catalog as it currently stands. Safe to read while a refresh runs.</summary>
    public IReadOnlyList<AppEntry> Entries => _entries;

    /// <summary>Raised after <see cref="Refresh"/> replaces the catalog.</summary>
    public event EventHandler? Updated;

    /// <summary>Re-enumerates installed applications. Blocking; call it off the UI thread.</summary>
    public void Refresh()
    {
        var entries = Enumerate();

        if (entries.Count == 0)
        {
            // Never replace a good catalog with an empty one: a transient shell failure
            // would otherwise leave the launcher unable to find anything.
            return;
        }

        _entries = entries;
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private static List<AppEntry> Enumerate()
    {
        var entries = new List<AppEntry>(512);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Interop.Shell.IShellItem? appsFolder = null;
        IntPtr enumeratorPointer = IntPtr.Zero;

        try
        {
            var folderId = Interop.Shell.FolderIdAppsFolder;
            var shellItemId = Interop.Shell.IidShellItem;

            if (Interop.Shell.SHGetKnownFolderItem(ref folderId, 0, IntPtr.Zero, ref shellItemId, out appsFolder) != 0
                || appsFolder is null)
            {
                return entries;
            }

            var handlerId = Interop.Shell.BhidEnumItems;
            var enumeratorId = Interop.Shell.IidEnumShellItems;

            if (appsFolder.BindToHandler(IntPtr.Zero, ref handlerId, ref enumeratorId, out enumeratorPointer) != 0
                || enumeratorPointer == IntPtr.Zero)
            {
                return entries;
            }

            var enumerator = (Interop.Shell.IEnumShellItems)Marshal.GetObjectForIUnknown(enumeratorPointer);

            while (enumerator.Next(1, out var item, out uint fetched) == 0 && fetched == 1 && item is not null)
            {
                try
                {
                    if (Create(item) is { } entry && seen.Add(entry.LaunchId))
                    {
                        entries.Add(entry);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        catch (COMException)
        {
            // A broken shell extension can fault mid-enumeration. Whatever was collected
            // before that point is still worth keeping.
        }
        finally
        {
            if (enumeratorPointer != IntPtr.Zero)
            {
                Marshal.Release(enumeratorPointer);
            }

            if (appsFolder is not null)
            {
                Marshal.ReleaseComObject(appsFolder);
            }
        }

        return entries;
    }

    private static AppEntry? Create(Interop.Shell.IShellItem item)
    {
        string? name = Interop.Shell.GetDisplayName(item, Interop.Shell.Sigdn.NormalDisplay);
        string? launchId = Interop.Shell.GetDisplayName(item, Interop.Shell.Sigdn.ParentRelativeParsing);

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(launchId))
        {
            return null;
        }

        // Applications folder items are virtual, so FILESYSPATH never resolves for them —
        // asking is a wasted COM call per app. Unpackaged entries instead identify
        // themselves with their executable's full path, which is the path worth showing.
        string? filePath = LooksLikeExecutablePath(launchId) ? launchId : null;

        var terms = BuildSearchTerms(name, filePath);
        var masks = new ulong[terms.Count];

        for (int i = 0; i < terms.Count; i++)
        {
            masks[i] = FuzzyMatcher.ComputeMask(terms[i]);
        }

        return new AppEntry(name, launchId, filePath)
        {
            SearchTerms = terms,
            SearchTermMasks = masks,
        };
    }

    private static List<string> BuildSearchTerms(string name, string? filePath)
    {
        var terms = new List<string>(2) { name };

        if (filePath is null)
        {
            return terms;
        }

        // People search for what they would type in a terminal, so "code" should find
        // Visual Studio Code even though the title never contains that word on its own.
        string executable = Path.GetFileNameWithoutExtension(filePath);

        if (!string.IsNullOrEmpty(executable)
            && !name.Contains(executable, StringComparison.OrdinalIgnoreCase))
        {
            terms.Add(executable);
        }

        return terms;
    }

    /// <summary>
    /// Whether a launch identity is an executable path rather than an opaque identifier.
    /// </summary>
    /// <remarks>
    /// Only some entries name a real file. Others use an AppUserModelId, a registered
    /// short name, or a bare GUID. Those must not become search terms: a catalog full of
    /// hex strings makes a query like "888" match applications with nothing to do with it.
    /// </remarks>
    private static bool LooksLikeExecutablePath(string launchId) =>
        launchId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && launchId.Contains(Path.DirectorySeparatorChar);
}
