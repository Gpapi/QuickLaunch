using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickLaunch.Core.Abstractions;

namespace QuickLaunch.Core.Search;

/// <summary>
/// Runs a query across every provider and keeps one ranked list of the best results.
/// </summary>
/// <remarks>
/// Each keystroke supersedes the one before it, so a new search cancels the previous one
/// rather than racing it — otherwise a slow provider could deliver results for a query the
/// user has already moved on from.
///
/// <see cref="ResultsChanged"/> is raised from background threads. A UI subscriber has to
/// marshal to its own thread.
/// </remarks>
public sealed class SearchOrchestrator(IEnumerable<ISearchProvider> providers, int maxResults = 20)
{
    private readonly IReadOnlyList<ISearchProvider> _providers = providers.ToArray();
    private readonly Lock _gate = new();

    private SearchRun? _inFlight;

    /// <summary>Raised with the current best results whenever they change.</summary>
    public event EventHandler<IReadOnlyList<SearchResult>>? ResultsChanged;

    /// <summary>
    /// Raised when a search fails. Searches run detached, so without this a provider
    /// throwing is indistinguishable from a query that matched nothing.
    /// </summary>
    public event EventHandler<Exception>? SearchFailed;

    /// <summary>
    /// Starts searching for <paramref name="rawQuery"/>, abandoning any search already
    /// running. Returns immediately.
    /// </summary>
    public void Search(string? rawQuery)
    {
        var query = Query.Parse(rawQuery);

        // Handled before a run exists. Creating one and then disposing it would leave a
        // disposed run as the in-flight search, and the next keystroke would cancel it —
        // throwing from inside the setter that raised this call.
        if (query.IsEmpty)
        {
            lock (_gate)
            {
                _inFlight?.Cancel();
                _inFlight = null;
            }

            ResultsChanged?.Invoke(this, []);
            return;
        }

        var run = new SearchRun();

        lock (_gate)
        {
            _inFlight?.Cancel();
            _inFlight = run;
        }

        _ = RunAsync(query, run);
    }

    /// <summary>Abandons any running search and clears the results.</summary>
    public void Clear() => Search(null);

    private async Task RunAsync(Query query, SearchRun run)
    {
        try
        {
            await Task.WhenAll(_providers.Select(provider => ConsumeAsync(provider, query, run)));
            Publish(run);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query; its results are the ones that matter now.
        }
        catch (Exception exception)
        {
            // Nothing awaits this task, so an unreported failure here would look exactly
            // like a query that simply matched nothing.
            SearchFailed?.Invoke(this, exception);
        }
        finally
        {
            // Disposed under the lock so that a concurrent Search can never cancel a run
            // that is already gone.
            lock (_gate)
            {
                if (ReferenceEquals(_inFlight, run))
                {
                    _inFlight = null;
                }

                run.Dispose();
            }
        }
    }

    private async Task ConsumeAsync(ISearchProvider provider, Query query, SearchRun run)
    {
        var token = run.Token;

        await foreach (var result in provider.SearchAsync(query, token).WithCancellation(token))
        {
            if (run.Add(result with { Score = result.Score * provider.Weight / 100 }))
            {
                Publish(run);
            }
        }
    }

    private void Publish(SearchRun run)
    {
        if (run.Token.IsCancellationRequested)
        {
            return;
        }

        var ranked = run.Rank(maxResults);

        // Checked again after ranking: a newer query may have started while we sorted,
        // and delivering these now would overwrite its results with stale ones.
        if (!run.Token.IsCancellationRequested)
        {
            ResultsChanged?.Invoke(this, ranked);
        }
    }

    /// <summary>
    /// One search's accumulating results and its cancellation.
    /// </summary>
    private sealed class SearchRun : IDisposable
    {
        /// <summary>
        /// Results are published at most this often while a search runs, so a provider
        /// yielding hundreds of matches cannot flood the UI with relayouts.
        /// </summary>
        private static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(16);

        private readonly CancellationTokenSource _cancellation = new();
        private readonly List<SearchResult> _collected = [];
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Lock _gate = new();

        // A deadline rather than "time since last publish": subtracting a sentinel like
        // TimeSpan.MinValue from the elapsed time overflows before it can be compared.
        private TimeSpan _nextPublish = TimeSpan.Zero;

        public CancellationToken Token => _cancellation.Token;

        public void Cancel() => _cancellation.Cancel();

        /// <summary>Records a result and reports whether it is time to publish again.</summary>
        public bool Add(SearchResult result)
        {
            lock (_gate)
            {
                _collected.Add(result);

                if (_clock.Elapsed < _nextPublish)
                {
                    return false;
                }

                _nextPublish = _clock.Elapsed + PublishInterval;
                return true;
            }
        }

        public IReadOnlyList<SearchResult> Rank(int maxResults)
        {
            lock (_gate)
            {
                return _collected
                    .OrderByDescending(result => result.Score)

                    // Equal scores mean equally good matches, so the shorter title is the
                    // more specific one: "Code" before "Visual Studio Code" for "code".
                    .ThenBy(result => result.Title.Length)
                    .ThenBy(result => result.Title, StringComparer.CurrentCultureIgnoreCase)
                    .Take(maxResults)
                    .ToArray();
            }
        }

        public void Dispose() => _cancellation.Dispose();
    }
}
