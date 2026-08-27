using System.Collections.Generic;
using System.Threading;

namespace QuickLaunch.Core.Abstractions;

/// <summary>
/// A source of results.
/// </summary>
public interface ISearchProvider
{
    /// <summary>Identifies the provider in diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// How much this provider's scores count relative to others, as a percentage.
    /// 100 leaves a score untouched. Keeping every provider's calibration in one place
    /// is what stops each of them quietly inflating its own results.
    /// </summary>
    int Weight { get; }

    /// <summary>
    /// Produces results, yielding them as they are found.
    /// </summary>
    /// <remarks>
    /// Streaming rather than returning a list is what lets the fast in-memory sources
    /// paint immediately while a slower one is still working, instead of every result
    /// waiting on the slowest provider. Implementations must honour the token promptly:
    /// a new keystroke cancels the previous search.
    /// </remarks>
    IAsyncEnumerable<SearchResult> SearchAsync(Query query, CancellationToken cancellationToken);
}
