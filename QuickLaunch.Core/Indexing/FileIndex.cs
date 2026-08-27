using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QuickLaunch.Core.Matching;

namespace QuickLaunch.Core.Indexing;

/// <summary>A scored entry from the index.</summary>
public readonly record struct FileHit(int Index, int Score);

/// <summary>
/// An immutable snapshot of the indexed file system, laid out for fast scanning.
/// </summary>
/// <remarks>
/// Stored as parallel arrays rather than a list of objects. Three hundred thousand
/// entries as objects would be three hundred thousand pointer chases per keystroke;
/// as arrays the scan walks contiguous memory. Full paths are never materialised —
/// each entry keeps only its own name and the index of its parent, so a path is
/// reconstructed for the twenty rows actually shown rather than for all of them.
/// </remarks>
public sealed class FileIndex
{
    private readonly string[] _names;
    private readonly int[] _parents;
    private readonly ulong[] _masks;
    private readonly bool[] _directories;

    internal FileIndex(string[] names, int[] parents, bool[] directories)
    {
        _names = names;
        _parents = parents;
        _directories = directories;

        _masks = new ulong[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            _masks[i] = FuzzyMatcher.ComputeMask(names[i]);
        }
    }

    public static FileIndex Empty { get; } = new([], [], []);

    public int Count => _names.Length;

    public string GetName(int index) => _names[index];

    public bool IsDirectory(int index) => _directories[index];

    /// <summary>Index of the entry's parent, or -1 for a root. Used when persisting.</summary>
    public int GetParent(int index) => _parents[index];

    /// <summary>
    /// Rebuilds an entry's full path by walking up to its root.
    /// </summary>
    public string GetPath(int index)
    {
        var segments = new List<string>(8);

        for (int current = index; current >= 0; current = _parents[current])
        {
            segments.Add(_names[current]);
        }

        // A root entry stores its whole path as its name, so it needs no separator added.
        var path = new StringBuilder(segments[^1]);

        for (int i = segments.Count - 2; i >= 0; i--)
        {
            if (path.Length > 0 && path[^1] != System.IO.Path.DirectorySeparatorChar)
            {
                path.Append(System.IO.Path.DirectorySeparatorChar);
            }

            path.Append(segments[i]);
        }

        return path.ToString();
    }

    /// <summary>Path of the folder an entry lives in, for display under its name.</summary>
    public string GetParentPath(int index)
    {
        int parent = _parents[index];
        return parent < 0 ? string.Empty : GetPath(parent);
    }

    /// <summary>
    /// Finds the best <paramref name="limit"/> entries matching <paramref name="queryText"/>.
    /// </summary>
    /// <remarks>
    /// The scan is split across cores, each partition keeping only its own best few. That
    /// bound is what keeps a broad query survivable: "e" matches most of the index, and
    /// collecting every hit before sorting would allocate hundreds of thousands of
    /// entries for a list of twenty.
    /// </remarks>
    public IReadOnlyList<FileHit> Search(string queryText, int limit, CancellationToken cancellationToken)
    {
        if (_names.Length == 0 || string.IsNullOrEmpty(queryText))
        {
            return [];
        }

        ulong queryMask = FuzzyMatcher.ComputeMask(queryText);
        int partitionCount = Math.Clamp(Environment.ProcessorCount - 1, 1, 16);
        int perPartition = (_names.Length + partitionCount - 1) / partitionCount;

        var results = new List<FileHit>[partitionCount];

        Parallel.For(0, partitionCount, partition =>
        {
            int start = partition * perPartition;
            int end = Math.Min(start + perPartition, _names.Length);

            results[partition] = ScanRange(queryText, queryMask, start, end, limit, cancellationToken);
        });

        var merged = new List<FileHit>(partitionCount * limit);

        foreach (var partition in results)
        {
            merged.AddRange(partition);
        }

        merged.Sort(static (left, right) => right.Score.CompareTo(left.Score));

        if (merged.Count > limit)
        {
            merged.RemoveRange(limit, merged.Count - limit);
        }

        return merged;
    }

    private List<FileHit> ScanRange(
        string queryText,
        ulong queryMask,
        int start,
        int end,
        int limit,
        CancellationToken cancellationToken)
    {
        // One matcher per partition: instances carry scratch buffers and are not thread-safe.
        var matcher = new FuzzyMatcher();
        var best = new List<FileHit>(limit);
        int weakest = int.MinValue;

        for (int i = start; i < end; i++)
        {
            // Checked periodically rather than per entry: the token is shared across
            // threads and reading it is far more expensive than one comparison.
            if ((i & 0xFFF) == 0 && cancellationToken.IsCancellationRequested)
            {
                return best;
            }

            if (!matcher.TryScore(queryText, queryMask, _names[i], _masks[i], out int score))
            {
                continue;
            }

            if (best.Count < limit)
            {
                best.Add(new FileHit(i, score));

                if (best.Count == limit)
                {
                    weakest = Weakest(best);
                }

                continue;
            }

            if (score <= weakest)
            {
                continue;
            }

            Replace(best, score, i);
            weakest = Weakest(best);
        }

        return best;
    }

    private static int Weakest(List<FileHit> hits)
    {
        int weakest = int.MaxValue;

        foreach (var hit in hits)
        {
            if (hit.Score < weakest)
            {
                weakest = hit.Score;
            }
        }

        return weakest;
    }

    private static void Replace(List<FileHit> hits, int score, int index)
    {
        int weakestAt = 0;

        for (int i = 1; i < hits.Count; i++)
        {
            if (hits[i].Score < hits[weakestAt].Score)
            {
                weakestAt = i;
            }
        }

        hits[weakestAt] = new FileHit(index, score);
    }
}
