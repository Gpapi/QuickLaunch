using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using QuickLaunch.Core.Indexing;
using Xunit.Abstractions;

namespace QuickLaunch.Tests;

public class FileIndexTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "QuickLaunchTests", Guid.NewGuid().ToString("N"));

    private FileIndex BuildTree()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Documents", "Finance"));
        Directory.CreateDirectory(Path.Combine(_root, "Documents", "node_modules", "left-pad"));
        Directory.CreateDirectory(Path.Combine(_root, "Pictures"));
        Directory.CreateDirectory(Path.Combine(_root, ".cargo", "registry"));
        Directory.CreateDirectory(Path.Combine(_root, "Documents", "obj", "Debug"));

        File.WriteAllText(Path.Combine(_root, "Documents", "quarterly-report.docx"), "x");
        File.WriteAllText(Path.Combine(_root, "Documents", "Finance", "budget-2026.xlsx"), "x");
        File.WriteAllText(Path.Combine(_root, "Documents", "node_modules", "left-pad", "index.js"), "x");
        File.WriteAllText(Path.Combine(_root, "Pictures", "holiday.jpg"), "x");
        File.WriteAllText(Path.Combine(_root, ".cargo", "registry", "download.rs"), "x");
        File.WriteAllText(Path.Combine(_root, "Documents", "obj", "Debug", "quarterly-report.dll"), "x");

        var options = new FileIndexOptions { Roots = [_root] };
        return FileIndexBuilder.Build(options, CancellationToken.None);
    }

    private FileIndex Tree => field ??= BuildTree();

    private int IndexOf(FileIndex index, string name)
    {
        for (int i = 0; i < index.Count; i++)
        {
            if (index.GetName(i) == name)
            {
                return i;
            }
        }

        return -1;
    }

    [Fact]
    public void Indexes_files_and_folders_under_the_root()
    {
        var index = Tree;

        Assert.True(IndexOf(index, "quarterly-report.docx") >= 0);
        Assert.True(IndexOf(index, "budget-2026.xlsx") >= 0);
        Assert.True(IndexOf(index, "holiday.jpg") >= 0);
        Assert.True(IndexOf(index, "Finance") >= 0);
    }

    [Fact]
    public void Rebuilds_a_full_path_from_the_parent_chain()
    {
        var index = Tree;
        int budget = IndexOf(index, "budget-2026.xlsx");

        Assert.Equal(Path.Combine(_root, "Documents", "Finance", "budget-2026.xlsx"), index.GetPath(budget));
        Assert.Equal(Path.Combine(_root, "Documents", "Finance"), index.GetParentPath(budget));
    }

    [Fact]
    public void Distinguishes_folders_from_files()
    {
        var index = Tree;

        Assert.True(index.IsDirectory(IndexOf(index, "Finance")));
        Assert.False(index.IsDirectory(IndexOf(index, "holiday.jpg")));
    }

    [Fact]
    public void Excluded_folders_are_not_descended_into()
    {
        var index = Tree;

        // The folder itself is skipped entirely, so nothing beneath it is indexed either.
        Assert.Equal(-1, IndexOf(index, "node_modules"));
        Assert.Equal(-1, IndexOf(index, "left-pad"));
        Assert.Equal(-1, IndexOf(index, "index.js"));

        // Dot folders are tool caches that Windows does not mark hidden.
        Assert.Equal(-1, IndexOf(index, ".cargo"));
        Assert.Equal(-1, IndexOf(index, "registry"));
        Assert.Equal(-1, IndexOf(index, "download.rs"));

        // Build output holds copies of the source that would compete with it.
        Assert.Equal(-1, IndexOf(index, "obj"));
        Assert.Equal(-1, IndexOf(index, "quarterly-report.dll"));
    }

    [Fact]
    public void Options_reject_paths_the_walker_would_never_index()
    {
        // What the watchers use to decide an event is worth reacting to. It has to agree
        // with the walker, or the quiet period is reset by churn that cannot change
        // anything — which is most of the churn on a Windows machine.
        var options = new FileIndexOptions();

        Assert.False(options.CouldContain(@"C:\Windows\System32\drivers\etc\hosts"));
        Assert.False(options.CouldContain(@"C:\ProgramData\Microsoft\Windows\whatever.tmp"));
        Assert.False(options.CouldContain(@"C:\Users\someone\.cargo\registry\a.rs"));
        Assert.False(options.CouldContain(@"C:\src\app\node_modules\left-pad\index.js"));
        Assert.False(options.CouldContain(@"C:\src\app\obj\Debug\app.dll"));

        Assert.True(options.CouldContain(@"C:\GitProjects\QuickLaunch\README.md"));
        Assert.True(options.CouldContain(@"C:\Users\someone\Documents\report.docx"));
    }

    [Fact]
    public void Search_finds_a_file_by_an_abbreviation_of_its_name()
    {
        var index = Tree;
        var hits = index.Search("budget", 10, CancellationToken.None);

        Assert.NotEmpty(hits);
        Assert.Equal("budget-2026.xlsx", index.GetName(hits[0].Index));
    }

    [Fact]
    public void Search_returns_nothing_for_an_empty_index() =>
        Assert.Empty(FileIndex.Empty.Search("anything", 10, CancellationToken.None));

    [Fact]
    public void Search_honours_the_result_limit()
    {
        var index = Tree;
        var hits = index.Search("e", 2, CancellationToken.None);

        Assert.True(hits.Count <= 2);
    }

    [Fact]
    public void A_snapshot_round_trips()
    {
        var index = Tree;
        string path = Path.Combine(_root, "index.bin");

        FileIndexSnapshot.Save(index, path);
        var loaded = FileIndexSnapshot.TryLoad(path);

        Assert.NotNull(loaded);
        Assert.Equal(index.Count, loaded!.Count);

        int original = IndexOf(index, "budget-2026.xlsx");
        int restored = IndexOf(loaded, "budget-2026.xlsx");

        Assert.Equal(index.GetPath(original), loaded.GetPath(restored));
        Assert.Equal(index.IsDirectory(original), loaded.IsDirectory(restored));
    }

    [Fact]
    public void A_corrupt_snapshot_is_ignored_rather_than_trusted()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "corrupt.bin");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Null(FileIndexSnapshot.TryLoad(path));
    }

    [Theory]
    [InlineData("a length prefix that never terminates", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData("a name that is cut off mid-string", new byte[] { 0x40, 0x41, 0x42 })]
    [InlineData("nothing at all after the header", new byte[0])]
    public void A_snapshot_with_a_valid_header_and_a_broken_body_is_rejected(string _, byte[] body)
    {
        // The header can be perfectly good and the rest still garbage. Every way that can
        // go wrong has to end in "ignore the cache and rebuild", never in an exception:
        // the load runs on a discarded task, so anything thrown there disables file search
        // for the life of the process and silently repeats on every launch.
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "broken.bin");

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x58494C51u);   // magic
            writer.Write(1);             // version
            writer.Write(4);             // entry count
            writer.Write(body);
        }

        Assert.Null(FileIndexSnapshot.TryLoad(path));
    }

    [Fact]
    public void A_missing_snapshot_is_not_an_error() =>
        Assert.Null(FileIndexSnapshot.TryLoad(Path.Combine(_root, "does-not-exist.bin")));

    [Fact]
    [Trait("Category", "Machine")]
    public void The_default_configuration_indexes_work_kept_outside_the_user_profile()
    {
        // Regression: the roots used to be the user profile alone, so a projects folder at
        // the root of a drive — where plenty of people keep their work — was invisible.
        //
        // Anchored to this source file rather than to where the test binary runs from: the
        // binary lives under bin, which the index deliberately excludes.
        string sourceFolder = Path.GetDirectoryName(ThisFile())!;

        if (!Directory.Exists(sourceFolder))
        {
            output.WriteLine($"built elsewhere ({sourceFolder}); nothing to check here.");
            return;
        }

        var clock = Stopwatch.StartNew();
        var index = FileIndexBuilder.Build(new FileIndexOptions(), CancellationToken.None);
        clock.Stop();

        output.WriteLine($"{index.Count:N0} entries in {clock.Elapsed.TotalSeconds:N1}s");

        string folder = new DirectoryInfo(sourceFolder).Name;
        var hits = index.Search(folder, 50, CancellationToken.None);

        Assert.True(
            hits.Any(hit => index.GetPath(hit.Index).Equals(sourceFolder, StringComparison.OrdinalIgnoreCase)),
            $"the index did not contain the folder this test's source lives in: {sourceFolder}");
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    [Trait("Category", "Machine")]
    public void Searching_three_hundred_thousand_names_stays_within_budget()
    {
        var index = SyntheticIndex(300_000);

        // Warm up: first call pays for JIT and the thread pool spinning up, which is not
        // what a keystroke on a running launcher costs.
        index.Search("report", 20, CancellationToken.None);

        var elapsed = new List<double>();

        foreach (string query in new[] { "report", "budget", "vsc", "index", "hol" })
        {
            // Best of several. The scan is parallel, and the rest of the test suite is
            // competing for the same cores, so a single sample measures the machine's
            // scheduling as much as the code. The fastest run is the one that reflects
            // what a keystroke costs on an otherwise idle launcher.
            double best = double.MaxValue;

            for (int attempt = 0; attempt < 5; attempt++)
            {
                var clock = Stopwatch.StartNew();
                index.Search(query, 20, CancellationToken.None);
                clock.Stop();

                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }

            elapsed.Add(best);
            output.WriteLine($"  '{query}': {best:F1} ms");
        }

        // A query no name can contain: every candidate is rejected by the character mask,
        // so this is the cost of the scan itself with no matching work at all.
        double prefilterOnly = double.MaxValue;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            var clock = Stopwatch.StartNew();
            index.Search("qqqq", 20, CancellationToken.None);
            clock.Stop();
            prefilterOnly = Math.Min(prefilterOnly, clock.Elapsed.TotalMilliseconds);
        }

        double worst = elapsed.Max();
        output.WriteLine($"prefilter floor: {prefilterOnly:F1} ms  ({Environment.ProcessorCount} cores)");
        output.WriteLine($"worst: {worst:F1} ms over {index.Count:N0} entries");

        Assert.True(worst < 15.0, $"slowest query took {worst:F1} ms, budget is 15 ms");
    }

    [Fact]
    [Trait("Category", "Machine")]
    public void A_cancelled_search_gives_up_promptly()
    {
        var index = SyntheticIndex(300_000);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var clock = Stopwatch.StartNew();
        index.Search("report", 20, cancellation.Token);
        clock.Stop();

        output.WriteLine($"cancelled search returned in {clock.Elapsed.TotalMilliseconds:F1} ms");
        Assert.True(clock.Elapsed.TotalMilliseconds < 15.0);
    }

    /// <summary>
    /// An index of plausible file names, built without touching the disk so the benchmark
    /// measures scanning rather than the machine's I/O on the day.
    /// </summary>
    private static FileIndex SyntheticIndex(int count)
    {
        string[] words = ["report", "budget", "notes", "index", "holiday", "invoice", "draft", "backup", "photo", "readme"];
        string[] extensions = [".docx", ".xlsx", ".md", ".png", ".cs", ".txt"];

        var names = new string[count];
        var parents = new int[count];
        var directories = new bool[count];

        names[0] = @"C:\synthetic";
        parents[0] = -1;
        directories[0] = true;

        for (int i = 1; i < count; i++)
        {
            names[i] = $"{words[i % words.Length]}-{i:D6}{extensions[i % extensions.Length]}";
            parents[i] = 0;
            directories[i] = false;
        }

        return new FileIndex(names, parents, directories);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Leaving a temp folder behind is not worth failing a test over.
        }
    }
}
