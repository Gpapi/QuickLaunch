using System;
using System.Linq;
using QuickLaunch.Core.Indexing;
using Xunit.Abstractions;

namespace QuickLaunch.Tests;

/// <summary>
/// Exercises the real shell on the machine running the tests. These assert shape and
/// plausibility rather than specific applications, which differ per machine.
/// </summary>
public class AppCatalogTests(ITestOutputHelper output)
{
    private static AppCatalog Loaded()
    {
        var catalog = new AppCatalog();
        catalog.Refresh();
        return catalog;
    }

    [Fact]
    public void Finds_the_applications_installed_on_this_machine()
    {
        var catalog = Loaded();

        output.WriteLine($"{catalog.Entries.Count} applications");

        foreach (var entry in catalog.Entries.Take(25))
        {
            output.WriteLine($"  {entry.Name}");
            output.WriteLine($"      launchId : {entry.LaunchId}");
            output.WriteLine($"      filePath : {entry.FilePath ?? "(none)"}");
            output.WriteLine($"      terms    : {string.Join(", ", entry.SearchTerms)}");
        }

        // Any Windows install has far more than a handful.
        Assert.True(catalog.Entries.Count > 10, $"only found {catalog.Entries.Count} applications");
    }

    [Fact]
    public void Every_entry_can_be_named_launched_and_matched()
    {
        var catalog = Loaded();

        foreach (var entry in catalog.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.False(string.IsNullOrWhiteSpace(entry.LaunchId));
            Assert.StartsWith(@"shell:AppsFolder\", entry.ShellPath, StringComparison.Ordinal);
            Assert.NotEmpty(entry.SearchTerms);
            Assert.Equal(entry.SearchTerms.Count, entry.SearchTermMasks.Count);
        }
    }

    [Fact]
    public void Includes_both_packaged_and_unpackaged_applications()
    {
        var catalog = Loaded();

        int packaged = catalog.Entries.Count(e => e.LaunchId.Contains('!'));
        int unpackaged = catalog.Entries.Count - packaged;

        output.WriteLine($"packaged: {packaged}, unpackaged: {unpackaged}");

        // Windows ships packaged apps (Settings, Store) and every machine has some
        // unpackaged ones, so finding none of either means a whole source was missed.
        Assert.True(packaged > 0, "no packaged applications found");
        Assert.True(unpackaged > 0, "no unpackaged applications found");
    }

    [Fact]
    public void Search_terms_never_include_an_opaque_identifier()
    {
        var catalog = Loaded();

        foreach (var entry in catalog.Entries)
        {
            // The display name is always a term; any extra term must be a real executable
            // name, never the GUID or hex id some apps use as their launch identity.
            foreach (string term in entry.SearchTerms.Skip(1))
            {
                Assert.NotNull(entry.FilePath);
                Assert.Equal(System.IO.Path.GetFileNameWithoutExtension(entry.FilePath!), term);
            }
        }
    }

    [Fact]
    public void Names_are_not_duplicated_by_the_same_app_appearing_twice()
    {
        var catalog = Loaded();

        var duplicates = catalog.Entries
            .GroupBy(e => e.LaunchId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.Empty(duplicates);
    }
}
