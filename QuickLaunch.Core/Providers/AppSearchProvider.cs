using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using QuickLaunch.Core.Abstractions;
using QuickLaunch.Core.Indexing;
using QuickLaunch.Core.Matching;

namespace QuickLaunch.Core.Providers;

/// <summary>
/// Matches the query against installed applications.
/// </summary>
public sealed class AppSearchProvider(AppCatalog catalog) : ISearchProvider
{
    public string Name => "Applications";

    /// <summary>
    /// Applications outrank everything else: someone typing into a launcher is far more
    /// often trying to start a program than to open a document that shares its name.
    /// </summary>
    public int Weight => 120;

    public async IAsyncEnumerable<SearchResult> SearchAsync(
        Query query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (query.IsEmpty)
        {
            yield break;
        }

        // One matcher per call: instances carry scratch buffers and are not thread-safe.
        var matcher = new FuzzyMatcher();
        ulong queryMask = FuzzyMatcher.ComputeMask(query.Text);

        foreach (var entry in catalog.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TermMatching.TryScore(
                    matcher,
                    query.Text,
                    queryMask,
                    entry.SearchTerms,
                    entry.SearchTermMasks,
                    out int score,
                    out var highlights))
            {
                yield return Create(entry, score, highlights);
            }
        }

        await Task.CompletedTask;
    }

    private static SearchResult Create(AppEntry entry, int score, IReadOnlyList<MatchSpan> highlights) => new()
    {
        Id = $"app:{entry.LaunchId}",
        Title = entry.Name,
        Subtitle = Describe(entry),
        Kind = ResultKind.Application,
        Score = score,
        TitleHighlights = highlights,
        Launch = new LaunchTarget(entry.ShellPath),
        Icon = new IconSource(IconSourceKind.Shell, entry.ShellPath),
    };

    /// <summary>
    /// Where the app lives, or nothing.
    /// </summary>
    /// <remarks>
    /// Only a real rooted path is worth a line. Packaged apps have none, and some system
    /// entries identify themselves relative to a known folder — showing that would put a
    /// bare GUID under the title. The row already says "App" on the right, so a subtitle
    /// of "Application" would be noise either way.
    /// </remarks>
    private static string Describe(AppEntry entry)
    {
        if (entry.FilePath is null || !Path.IsPathFullyQualified(entry.FilePath))
        {
            return string.Empty;
        }

        return Path.GetDirectoryName(entry.FilePath) ?? string.Empty;
    }
}
