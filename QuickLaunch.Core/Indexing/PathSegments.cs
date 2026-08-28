using System;

namespace QuickLaunch.Core.Indexing;

internal static class PathSegments
{
    /// <summary>
    /// Walks a path's segments without allocating. Used on every file system event, which
    /// on a busy machine arrive in bursts of hundreds.
    /// </summary>
    public static SegmentEnumerator EnumerateDirectorySeparatedSegments(this ReadOnlySpan<char> path) => new(path);

    public ref struct SegmentEnumerator(ReadOnlySpan<char> path)
    {
        private ReadOnlySpan<char> _remaining = path;

        public ReadOnlySpan<char> Current { get; private set; } = default;

        public readonly SegmentEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            while (_remaining.Length > 0)
            {
                int separator = _remaining.IndexOfAny('\\', '/');

                if (separator < 0)
                {
                    Current = _remaining;
                    _remaining = default;
                    return true;
                }

                Current = _remaining[..separator];
                _remaining = _remaining[(separator + 1)..];

                if (Current.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
