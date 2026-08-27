using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using QuickLaunch.Core.Abstractions;
using QuickLaunch.Core.Indexing;
using QuickLaunch.Core.Matching;

namespace QuickLaunch.Core.Providers;

/// <summary>
/// Matches the query against Windows Settings pages.
/// </summary>
public sealed class SettingsSearchProvider : ISearchProvider
{
    /// <summary>
    /// The Settings app, used as the icon for every page it owns. Its identity is fixed
    /// across Windows installations; if it ever is not there, the row falls back to the
    /// gear glyph rather than showing nothing.
    /// </summary>
    private static readonly IconSource SettingsIcon = new(
        IconSourceKind.Shell,
        @"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel");

    public string Name => "Settings";

    /// <summary>
    /// Just below applications. Someone typing "bluetooth" almost always wants the settings
    /// page, but a query that names an installed program should still surface the program.
    /// </summary>
    public int Weight => 110;

    public async IAsyncEnumerable<SearchResult> SearchAsync(
        Query query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (query.IsEmpty)
        {
            yield break;
        }

        var matcher = new FuzzyMatcher();
        ulong queryMask = FuzzyMatcher.ComputeMask(query.Text);

        foreach (var entry in SettingsCatalog.Entries)
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

    private static SearchResult Create(SettingEntry entry, int score, IReadOnlyList<MatchSpan> highlights) => new()
    {
        Id = $"setting:{entry.Target}",
        Title = entry.Name,
        Subtitle = entry.Category,
        Kind = ResultKind.Setting,
        Score = score,
        TitleHighlights = highlights,
        Launch = new LaunchTarget(entry.Target, entry.Arguments),
        Icon = SettingsIcon,
    };
}
