using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using QuickLaunch.Core.Abstractions;

namespace QuickLaunch.Core.Providers;

/// <summary>
/// Where the query goes when nothing on the machine matches it.
/// </summary>
/// <param name="SearchUrlFormat">
/// Search URL with <c>{0}</c> where the escaped query belongs.
/// </param>
public sealed record WebSearchOptions(string SearchUrlFormat = "https://www.google.com/search?q={0}")
{
    /// <summary>Name shown under the result, so the user knows where they are being sent.</summary>
    public string EngineName { get; init; } = "Google";
}

/// <summary>
/// Turns a query into a web search, and recognises when it is already an address.
/// </summary>
public sealed class WebSearchProvider(WebSearchOptions? options = null) : ISearchProvider
{
    private readonly WebSearchOptions _options = options ?? new WebSearchOptions();

    /// <summary>
    /// Suffixes accepted as a domain when the query has no scheme.
    /// </summary>
    /// <remarks>
    /// A dot alone is not enough to call something an address: "budget-2026.xlsx" and
    /// "readme.md" are file names, and offering to open them as websites would be wrong far
    /// more often than right. Requiring a recognisable suffix keeps the offer honest, and
    /// anything typed with an explicit http:// or https:// is taken at its word regardless.
    /// </remarks>
    private static readonly HashSet<string> CommonSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "org", "net", "edu", "gov", "mil", "int", "io", "ai", "dev", "app", "co",
        "me", "tv", "cc", "info", "biz", "xyz", "online", "site", "sh", "gg", "so",
        "uk", "de", "fr", "es", "it", "nl", "pl", "ru", "ua", "ge", "tr", "us", "ca",
        "au", "nz", "jp", "cn", "in", "br", "se", "no", "fi", "dk", "ch", "at", "be",
    };

    /// <summary>Characters that end a host name and begin a path, query, fragment or port.</summary>
    private static readonly SearchValues<char> HostTerminators = SearchValues.Create("/?#:");

    /// <summary>
    /// Fixed, and far below any real match. This is a fallback, not a competitor: it should
    /// sit at the bottom when anything on the machine matched, and still be there when
    /// nothing did.
    /// </summary>
    private const int FallbackScore = 1;

    public string Name => "Web";

    public int Weight => 100;

    public async IAsyncEnumerable<SearchResult> SearchAsync(
        Query query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (query.IsEmpty)
        {
            yield break;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (TryReadAddress(query.Text, out string? url))
        {
            yield return new SearchResult
            {
                Id = $"url:{url}",
                Title = query.Text,
                Subtitle = "Open in your browser",
                Kind = ResultKind.Url,

                // Ranked above the search fallback: someone who typed an address meant it.
                Score = FallbackScore + 1,
                Launch = new LaunchTarget(url!),
            };
        }

        yield return new SearchResult
        {
            Id = "web:search",
            Title = $"Search the web for “{query.Text}”",
            Subtitle = _options.EngineName,
            Kind = ResultKind.WebSearch,
            Score = FallbackScore,
            Launch = new LaunchTarget(string.Format(_options.SearchUrlFormat, Uri.EscapeDataString(query.Text))),
        };

        await Task.CompletedTask;
    }

    /// <summary>Recognises a query that is already a web address.</summary>
    internal static bool TryReadAddress(string text, out string? url)
    {
        url = null;

        if (text.AsSpan().ContainsAny(' ', '\t'))
        {
            return false;
        }

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = text;
            return Uri.IsWellFormedUriString(url, UriKind.Absolute);
        }

        // The host has to be isolated before looking for its suffix. Taking the last dot in
        // the whole string would read "github.com/a/b.txt" as ending in ".txt" and reject a
        // perfectly good address.
        var host = text.AsSpan();
        int hostEnd = host.IndexOfAny(HostTerminators);

        if (hostEnd >= 0)
        {
            host = host[..hostEnd];
        }

        int lastDot = host.LastIndexOf('.');

        if (lastDot <= 0 || lastDot == host.Length - 1)
        {
            return false;
        }

        if (!CommonSuffixes.Contains(host[(lastDot + 1)..].ToString()))
        {
            return false;
        }

        url = "https://" + text;
        return Uri.IsWellFormedUriString(url, UriKind.Absolute);
    }
}
