using System;

namespace QuickLaunch.Core.Abstractions;

/// <summary>
/// A source that is built in the background and replaced once it is ready.
/// </summary>
/// <remarks>
/// The launcher can be summoned before its indexes exist, so a query typed early would
/// otherwise strand the user on an empty list. This lets whatever is driving the search
/// re-run the current query when an index arrives, without knowing what kind it is.
/// </remarks>
public interface ISearchIndex
{
    /// <summary>Raised on a background thread after the index is replaced.</summary>
    event EventHandler? Updated;
}
