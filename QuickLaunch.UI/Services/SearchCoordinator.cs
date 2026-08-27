using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using QuickLaunch.Core.Abstractions;
using QuickLaunch.Core.Indexing;
using QuickLaunch.Core.Search;
using QuickLaunch.UI.ViewModels;

namespace QuickLaunch.UI.Services;

/// <summary>
/// Connects the search box to the search engine, and the engine's answers back to the list.
/// </summary>
/// <remarks>
/// This is where threads are crossed. The orchestrator runs providers on the thread pool
/// and raises its results from whichever one finished, so everything it hands back has to
/// be marshalled onto the UI thread before it touches a view model. Keeping that in one
/// place is what lets both the view models and Core stay free of dispatcher plumbing.
/// </remarks>
internal sealed class SearchCoordinator(
    MainViewModel viewModel,
    SearchOrchestrator orchestrator,
    AppCatalog catalog,
    IconService icons,
    DispatcherQueue dispatcher)
{
    public void Start()
    {
        // Raised from inside the QueryText setter, before its change notifications go out,
        // so an exception escaping here would silently stop the whole view model updating.
        viewModel.QueryChanged += (_, query) =>
        {
            try
            {
                orchestrator.Search(query);
            }
            catch (Exception exception)
            {
                CrashLog.Write(exception);
            }
        };
        orchestrator.ResultsChanged += (_, results) => dispatcher.TryEnqueue(() => Show(results));

        // A search runs detached, so a fault in a provider would otherwise be
        // indistinguishable from a query that matched nothing.
        orchestrator.SearchFailed += (_, exception) => CrashLog.Write(exception);

        // The catalog is still loading when the launcher first appears. Re-running the
        // current query once it lands means an early search fills in rather than
        // stranding the user on an empty list.
        catalog.Updated += (_, _) => dispatcher.TryEnqueue(() => orchestrator.Search(viewModel.QueryText));
    }

    private void Show(IReadOnlyList<SearchResult> results)
    {
        viewModel.SetResults(results);
        _ = LoadIconsAsync([.. viewModel.Results]);
    }

    /// <summary>
    /// Fills in artwork after the rows are already on screen. Waiting for icons before
    /// showing anything would put the shell's latency between the keystroke and the list.
    /// </summary>
    private async Task LoadIconsAsync(IReadOnlyList<ResultItemViewModel> items)
    {
        foreach (var item in items)
        {
            if (item.Result.Icon is not { } source)
            {
                continue;
            }

            item.Icon = await icons.GetAsync(source);
        }
    }
}
