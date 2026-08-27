using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickLaunch.Core.Abstractions;
using QuickLaunch.Core.Indexing;
using QuickLaunch.Core.Providers;

namespace QuickLaunch.Tests;

public class SettingsProviderTests
{
    private static async Task<List<SearchResult>> Search(string query)
    {
        var found = new List<SearchResult>();

        await foreach (var result in new SettingsSearchProvider().SearchAsync(Query.Parse(query), CancellationToken.None))
        {
            found.Add(result);
        }

        return [.. found.OrderByDescending(r => r.Score)];
    }

    [Theory]
    [InlineData("bluetooth", "Bluetooth & devices")]
    [InlineData("display", "Display")]
    [InlineData("windows update", "Windows Update")]
    [InlineData("startup apps", "Startup apps")]
    public async Task Finds_a_page_by_its_name(string query, string expected)
    {
        var results = await Search(query);

        Assert.NotEmpty(results);
        Assert.Equal(expected, results[0].Title);
    }

    [Theory]
    [InlineData("resolution", "Display")]
    [InlineData("wallpaper", "Background")]
    [InlineData("uninstall", "Installed apps")]
    [InlineData("timezone", "Date & time")]
    [InlineData("dark mode", "Colours")]
    public async Task Finds_a_page_by_a_word_people_actually_type(string query, string expected)
    {
        var results = await Search(query);

        Assert.NotEmpty(results);
        Assert.Contains(results.Take(3), r => r.Title == expected);
    }

    [Fact]
    public async Task An_empty_query_produces_nothing() =>
        Assert.Empty(await Search("   "));

    [Fact]
    public async Task Every_result_can_be_launched()
    {
        var results = await Search("s");

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.NotNull(result.Launch));
        Assert.All(results, result => Assert.Equal(ResultKind.Setting, result.Kind));
    }

    [Fact]
    public void Every_catalog_entry_is_complete_and_unique()
    {
        var entries = SettingsCatalog.Entries;

        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.False(string.IsNullOrWhiteSpace(entry.Target));
            Assert.False(string.IsNullOrWhiteSpace(entry.Category));
            Assert.Equal(entry.SearchTerms.Count, entry.SearchTermMasks.Count);
            Assert.Equal(entry.Name, entry.SearchTerms[0]);
        }

        // A duplicate target would show the same page twice under different names.
        Assert.DoesNotContain(entries.GroupBy(e => e.Target, StringComparer.OrdinalIgnoreCase), group => group.Count() > 1);
    }

    [Fact]
    public void Every_target_is_a_recognised_kind_of_thing_to_open()
    {
        foreach (var entry in SettingsCatalog.Entries)
        {
            bool recognised = entry.Target.StartsWith("ms-settings:", StringComparison.Ordinal)
                || entry.Target.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase)
                || entry.Target.Equals("control.exe", StringComparison.OrdinalIgnoreCase);

            Assert.True(recognised, $"'{entry.Name}' opens something unexpected: {entry.Target}");

            // Switches must live in Arguments. Folded into the target, the shell would look
            // for a program whose file name contains them.
            Assert.DoesNotContain(' ', entry.Target);
        }
    }
}

public class WebSearchProviderTests
{
    private static async Task<List<SearchResult>> Search(string query)
    {
        var found = new List<SearchResult>();

        await foreach (var result in new WebSearchProvider().SearchAsync(Query.Parse(query), CancellationToken.None))
        {
            found.Add(result);
        }

        return found;
    }

    [Theory]
    [InlineData("github.com")]
    [InlineData("www.github.com")]
    [InlineData("https://github.com/cli/cli")]
    [InlineData("http://example.org")]
    [InlineData("docs.microsoft.com/en-us/windows")]
    [InlineData("example.co.uk")]
    [InlineData("localhost.dev:5173")]
    public void Recognises_an_address(string text)
    {
        Assert.True(WebSearchProvider.TryReadAddress(text, out string? url), $"'{text}' should be an address");
        Assert.NotNull(url);
        Assert.StartsWith("http", url);
    }

    [Theory]
    [InlineData("budget-2026.xlsx")]      // a file name, not a site
    [InlineData("readme.md")]
    [InlineData("notes.txt")]
    [InlineData("how do i rename a file")]
    [InlineData("visual studio code")]
    [InlineData("chrome")]
    [InlineData(".")]
    [InlineData("trailing.")]
    public void Does_not_mistake_ordinary_text_for_an_address(string text) =>
        Assert.False(WebSearchProvider.TryReadAddress(text, out _), $"'{text}' should not be an address");

    [Fact]
    public void Keeps_the_path_when_the_path_itself_contains_a_dot()
    {
        // The suffix has to come from the host, not from the last dot in the whole string.
        Assert.True(WebSearchProvider.TryReadAddress("github.com/a/b.txt", out string? url));
        Assert.Equal("https://github.com/a/b.txt", url);
    }

    [Fact]
    public async Task Always_offers_a_web_search()
    {
        var results = await Search("something nobody has installed");

        var search = Assert.Single(results);
        Assert.Equal(ResultKind.WebSearch, search.Kind);
        Assert.NotNull(search.Launch);
        Assert.Contains("something%20nobody", search.Launch!.Target);
    }

    [Fact]
    public async Task Offers_to_open_an_address_above_searching_for_it()
    {
        var results = await Search("github.com");

        Assert.Equal(2, results.Count);
        Assert.Equal(ResultKind.Url, results[0].Kind);
        Assert.Equal(ResultKind.WebSearch, results[1].Kind);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task An_empty_query_produces_nothing() =>
        Assert.Empty(await Search("  "));

    [Fact]
    public async Task The_search_engine_is_configurable()
    {
        var options = new WebSearchOptions("https://duckduckgo.com/?q={0}") { EngineName = "DuckDuckGo" };
        var provider = new WebSearchProvider(options);

        var found = new List<SearchResult>();

        await foreach (var result in provider.SearchAsync(Query.Parse("windows"), CancellationToken.None))
        {
            found.Add(result);
        }

        var search = Assert.Single(found);
        Assert.Equal("DuckDuckGo", search.Subtitle);
        Assert.StartsWith("https://duckduckgo.com/?q=windows", search.Launch!.Target);
    }
}
