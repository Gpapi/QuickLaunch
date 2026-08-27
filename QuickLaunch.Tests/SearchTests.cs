using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickLaunch.Core.Abstractions;
using QuickLaunch.Core.Indexing;
using QuickLaunch.Core.Providers;
using QuickLaunch.Core.Search;
using QuickLaunch.Core.Services;
using Xunit.Abstractions;

namespace QuickLaunch.Tests;

/// <summary>
/// End-to-end through the search stack: real catalog, real provider, real orchestrator.
/// </summary>
public class SearchTests(ITestOutputHelper output)
{
    /// <summary>Runs a query and waits for the orchestrator to settle.</summary>
    private static IReadOnlyList<SearchResult> Search(string query, out AppCatalog catalog)
    {
        catalog = new AppCatalog();
        catalog.Refresh();

        var orchestrator = new SearchOrchestrator([new AppSearchProvider(catalog)]);

        IReadOnlyList<SearchResult> latest = [];
        using var settled = new ManualResetEventSlim();

        orchestrator.ResultsChanged += (_, results) =>
        {
            latest = results;
            settled.Set();
        };

        orchestrator.Search(query);
        settled.Wait(TimeSpan.FromSeconds(5));

        // Providers may publish more than once; give the run a moment to finish.
        Thread.Sleep(200);
        return latest;
    }

    [Fact]
    public async Task The_provider_alone_matches_installed_apps()
    {
        var catalog = new AppCatalog();
        catalog.Refresh();

        var provider = new AppSearchProvider(catalog);
        var found = new List<SearchResult>();

        await foreach (var result in provider.SearchAsync(Query.Parse("code"), CancellationToken.None))
        {
            found.Add(result);
        }

        foreach (var result in found.OrderByDescending(r => r.Score).Take(10))
        {
            output.WriteLine($"  {result.Score,6}  {result.Title}");
        }

        Assert.NotEmpty(found);
    }

    [Fact]
    public void A_query_produces_ranked_results()
    {
        var results = Search("code", out var catalog);

        output.WriteLine($"catalog: {catalog.Entries.Count} apps");

        foreach (var result in results.Take(10))
        {
            output.WriteLine($"  {result.Score,6}  {result.Title}   [{result.Subtitle}]");
        }

        Assert.NotEmpty(results);
    }

    [Fact]
    public void Results_are_ordered_best_first()
    {
        var results = Search("s", out _);

        Assert.NotEmpty(results);

        for (int i = 1; i < results.Count; i++)
        {
            Assert.True(
                results[i - 1].Score >= results[i].Score,
                $"'{results[i - 1].Title}' ({results[i - 1].Score}) came before '{results[i].Title}' ({results[i].Score})");
        }
    }

    [Fact]
    public void Every_result_can_be_launched_and_has_artwork()
    {
        var results = Search("e", out _);

        Assert.NotEmpty(results);

        foreach (var result in results)
        {
            Assert.NotNull(result.Launch);
            Assert.StartsWith(@"shell:AppsFolder\", result.Launch!.Target, StringComparison.Ordinal);
            Assert.NotNull(result.Icon);
            Assert.Equal(IconSourceKind.Shell, result.Icon!.Kind);
        }
    }

    [Fact]
    public void An_empty_query_produces_nothing()
    {
        var results = Search("   ", out _);

        Assert.Empty(results);
    }

    [Fact]
    public void A_real_query_still_works_after_an_empty_one()
    {
        // Regression: clearing the box used to leave a disposed run as the in-flight
        // search, so the next keystroke threw while cancelling it.
        var catalog = new AppCatalog();
        catalog.Refresh();

        var orchestrator = new SearchOrchestrator([new AppSearchProvider(catalog)]);

        IReadOnlyList<SearchResult> latest = [];
        using var settled = new ManualResetEventSlim();

        orchestrator.ResultsChanged += (_, results) =>
        {
            latest = results;

            if (results.Count > 0)
            {
                settled.Set();
            }
        };

        orchestrator.Search(string.Empty);
        orchestrator.Search("code");

        Assert.True(settled.Wait(TimeSpan.FromSeconds(5)), "no results after an empty query");
        Assert.NotEmpty(latest);
    }

    [Fact]
    public void Subtitles_are_a_real_path_or_nothing()
    {
        var results = Search("e", out _);

        Assert.NotEmpty(results);

        foreach (var result in results)
        {
            if (result.Subtitle.Length > 0)
            {
                Assert.True(
                    System.IO.Path.IsPathFullyQualified(result.Subtitle),
                    $"'{result.Title}' has a subtitle that is not a real path: {result.Subtitle}");
            }
        }
    }

    [Fact]
    public void The_shell_accepts_the_launch_target_of_a_real_app()
    {
        var catalog = new AppCatalog();
        catalog.Refresh();

        // Calculator is the safest thing to actually start: packaged, instant, harmless,
        // and it exercises the shell:AppsFolder moniker that every result launches through.
        var calculator = catalog.Entries.FirstOrDefault(e =>
            e.Name.Equals("Calculator", StringComparison.OrdinalIgnoreCase));

        if (calculator is null)
        {
            output.WriteLine("Calculator is not installed; skipping the live launch check.");
            return;
        }

        Assert.True(
            ResultLauncher.TryLaunch(new LaunchTarget(calculator.ShellPath), out string? error),
            $"the shell refused {calculator.ShellPath}: {error}");

        Assert.Null(error);

        // Started for the test, so close it again rather than leaving it on screen.
        Thread.Sleep(2000);

        foreach (var process in System.Diagnostics.Process.GetProcessesByName("CalculatorApp")
                     .Concat(System.Diagnostics.Process.GetProcessesByName("Calculator")))
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // Best effort only; failing to tidy up is not a test failure.
            }
        }
    }

    [Fact]
    public void The_shell_reports_a_target_that_cannot_be_launched()
    {
        Assert.False(ResultLauncher.TryLaunch(new LaunchTarget(@"C:
o\such\program.exe"), out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Nonsense_matches_nothing()
    {
        var results = Search("qqzzxxjj", out _);

        Assert.Empty(results);
    }
}
