using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using QuickLaunch.Core.Abstractions;
using QuickLaunch.Core.Indexing;
using QuickLaunch.Core.Matching;

namespace QuickLaunch.Core.Providers;

/// <summary>
/// Matches the query against indexed files and folders.
/// </summary>
public sealed class FileSearchProvider(FileIndexService index) : ISearchProvider
{
    /// <summary>
    /// How long a query must survive before the index is scanned.
    /// </summary>
    /// <remarks>
    /// Applications come from a few hundred in-memory entries and can answer on every
    /// keystroke. Scanning the file index is orders of magnitude more work, and most
    /// keystrokes are followed by another one within this window — so waiting means a
    /// burst of typing scans once instead of once per character. Nothing else is delayed:
    /// the other providers have already painted by the time this fires.
    /// </remarks>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// Below this the query matches so much of the disk that the results are noise.
    /// </summary>
    private const int MinimumQueryLength = 2;

    /// <summary>How many index hits to consider before ranking against other providers.</summary>
    private const int Candidates = 30;

    public string Name => "Files";

    /// <summary>
    /// Files carry no bias of their own. They rank below applications because a launcher
    /// query more often means "start this program" than "open a document of that name",
    /// and the application weighting alone is enough to express that.
    /// </summary>
    /// <remarks>
    /// This was 85, which was low enough to override match quality rather than break ties
    /// with it: the query "pti" scored "ptinew" at 80 and "Projecting to this PC" — three
    /// scattered letters across three words — at 65, and the weighting still put the
    /// settings page first. A provider's bias has to be smaller than the difference
    /// between a good match and a poor one.
    /// </remarks>
    public int Weight => 100;

    public async IAsyncEnumerable<SearchResult> SearchAsync(
        Query query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (query.Text.Length < MinimumQueryLength)
        {
            yield break;
        }

        // Cancelled by the next keystroke, in which case the scan never starts.
        await Task.Delay(Debounce, cancellationToken);

        var snapshot = index.Index;
        var hits = await Task.Run(
            () => snapshot.Search(query.Text, Candidates, cancellationToken),
            cancellationToken);

        // Highlights are worth computing now: only these few survived the scan.
        var matcher = new FuzzyMatcher();

        foreach (var hit in hits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Create(snapshot, hit, matcher, query.Text);
        }
    }

    private static SearchResult Create(FileIndex index, FileHit hit, FuzzyMatcher matcher, string queryText)
    {
        string name = index.GetName(hit.Index);
        string path = index.GetPath(hit.Index);
        bool isDirectory = index.IsDirectory(hit.Index);

        matcher.TryMatch(queryText, name, out _, out var highlights);

        return new SearchResult
        {
            Id = $"file:{path}",
            Title = name,
            Subtitle = index.GetParentPath(hit.Index),
            Kind = isDirectory ? ResultKind.Folder : ResultKind.File,
            Score = hit.Score,
            TitleHighlights = highlights,
            Launch = new LaunchTarget(path),
            Icon = new IconSource(IconSourceKind.Shell, path),
        };
    }
}
