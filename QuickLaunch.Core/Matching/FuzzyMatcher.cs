using System;
using System.Collections.Generic;
using QuickLaunch.Core.Abstractions;

namespace QuickLaunch.Core.Matching;

/// <summary>
/// Ranks a candidate string against a query, the way fzf and Sublime's command palette do.
/// </summary>
/// <remarks>
/// Not edit distance. Launcher queries are abbreviations rather than misspellings: the
/// user types "vsc" for "Visual Studio Code", so what matters is where the matched
/// characters landed — at word starts, in runs, near the front — not how many characters
/// separate them. Levenshtein would rank "Visual Studio Code" far below any short string
/// that happens to be three edits from "vsc".
///
/// Instances hold reusable scratch buffers and are therefore NOT thread-safe. Use one per
/// thread; the parallel file index gives each partition its own.
/// </remarks>
public sealed class FuzzyMatcher
{
    // Awarded for every matched character, so more of the query matching always wins.
    private const int ScoreMatch = 16;

    // The first character of a word is what people abbreviate with, so it counts most.
    private const int BonusBoundary = 8;

    // "sBar" in "sideBar" — a capital after a lowercase starts a word without a separator.
    private const int BonusCamel = 7;

    // Runs read as a real prefix rather than scattered letters.
    private const int BonusConsecutive = 8;

    // The first character the user typed is the one they were most deliberate about,
    // so wherever it lands, that landing spot counts double.
    private const int FirstCharBonusMultiplier = 2;

    // How far into the candidate the match begins is a real relevance signal: "chr" means
    // "Chrome" far more often than "Google Chrome". Charged per character before the first
    // match and capped, so a late match is demoted without being buried.
    private const int PenaltyLeadingChar = 1;
    private const int MaxLeadingPenalty = 16;

    private const int PenaltyGapStart = 3;
    private const int PenaltyGapExtend = 1;

    /// <summary>Score meaning "these characters are not in this candidate, in order".</summary>
    public const int NoMatch = int.MinValue;

    /// <summary>
    /// Longer candidates are matched no further than this. Titles and file names are far
    /// shorter; the cap only bounds the work done on pathological input.
    /// </summary>
    private const int MaxCandidateLength = 128;

    /// <summary>Queries longer than this cannot usefully be abbreviations.</summary>
    private const int MaxQueryLength = 32;

    private int[] _previous = new int[MaxCandidateLength];
    private int[] _current = new int[MaxCandidateLength];
    private int[] _bonus = new int[MaxCandidateLength];
    private short[] _parents = new short[MaxQueryLength * MaxCandidateLength];

    // Case folding is done once per string rather than once per cell. The inner loop
    // visits every (query, candidate) pair, so folding there repeated the work for each
    // character of the query — the single biggest cost in scanning a large index.
    private readonly char[] _foldedCandidate = new char[MaxCandidateLength];
    private readonly char[] _foldedQuery = new char[MaxQueryLength];

    // The same query is matched against every candidate in a partition, so its folded
    // form is kept between calls. Comparing a handful of characters to decide whether it
    // changed is far cheaper than folding them again three hundred thousand times.
    private readonly char[] _lastQuery = new char[MaxQueryLength];
    private int _lastQueryLength = -1;

    /// <summary>
    /// A 64-bit summary of which characters a string contains, letting an obvious
    /// non-match be rejected with one AND rather than a full dynamic-programming pass.
    /// Precompute it per candidate and keep it beside the candidate.
    /// </summary>
    public static ulong ComputeMask(ReadOnlySpan<char> text)
    {
        ulong mask = 0;

        foreach (char c in text)
        {
            mask |= 1UL << BitOf(c);
        }

        return mask;
    }

    /// <summary>Folds the query to lower case, reusing the previous result when it is unchanged.</summary>
    private void FoldQuery(ReadOnlySpan<char> query)
    {
        if (_lastQueryLength == query.Length && query.SequenceEqual(_lastQuery.AsSpan(0, _lastQueryLength)))
        {
            return;
        }

        for (int i = 0; i < query.Length; i++)
        {
            _lastQuery[i] = query[i];
            _foldedQuery[i] = char.ToLowerInvariant(query[i]);
        }

        _lastQueryLength = query.Length;
    }

    private static int BitOf(char c)
    {
        c = char.ToLowerInvariant(c);

        if (c is >= 'a' and <= 'z')
        {
            return c - 'a';
        }

        if (c is >= '0' and <= '9')
        {
            return 26 + (c - '0');
        }

        // Everything else shares the remaining bits. Collisions only cost a wasted
        // full comparison, never a missed match.
        return 36 + (c % 28);
    }

    /// <summary>
    /// Scores <paramref name="candidate"/> against <paramref name="query"/> and reports
    /// which characters matched.
    /// </summary>
    /// <param name="queryMask">Query mask from <see cref="ComputeMask"/>.</param>
    /// <param name="candidateMask">Candidate mask from <see cref="ComputeMask"/>.</param>
    /// <returns>False when the query's characters do not appear in order.</returns>
    public bool TryMatch(
        ReadOnlySpan<char> query,
        ulong queryMask,
        ReadOnlySpan<char> candidate,
        ulong candidateMask,
        out int score,
        out IReadOnlyList<MatchSpan> highlights) =>
        Match(query, queryMask, candidate, candidateMask, trace: true, out score, out highlights);

    /// <summary>
    /// Scores without working out which characters matched.
    /// </summary>
    /// <remarks>
    /// The file index scores hundreds of thousands of candidates for every keystroke but
    /// only ever shows twenty. Skipping the traceback avoids allocating a highlight list
    /// for each of the many thousands that match and are then discarded.
    /// </remarks>
    public bool TryScore(
        ReadOnlySpan<char> query,
        ulong queryMask,
        ReadOnlySpan<char> candidate,
        ulong candidateMask,
        out int score) =>
        Match(query, queryMask, candidate, candidateMask, trace: false, out score, out _);

    private bool Match(
        ReadOnlySpan<char> query,
        ulong queryMask,
        ReadOnlySpan<char> candidate,
        ulong candidateMask,
        bool trace,
        out int score,
        out IReadOnlyList<MatchSpan> highlights)
    {
        score = NoMatch;
        highlights = [];

        if (query.IsEmpty || candidate.IsEmpty)
        {
            return false;
        }

        // Cheapest rejection first: a character the candidate does not contain at all.
        if ((queryMask & ~candidateMask) != 0)
        {
            return false;
        }

        if (query.Length > MaxQueryLength)
        {
            query = query[..MaxQueryLength];
        }

        if (candidate.Length > MaxCandidateLength)
        {
            candidate = candidate[..MaxCandidateLength];
        }

        if (query.Length > candidate.Length)
        {
            return false;
        }

        ComputeBonuses(candidate);

        int n = query.Length;
        int m = candidate.Length;

        FoldQuery(query);

        int bestScore = NoMatch;
        int bestEnd = -1;

        for (int i = 0; i < n; i++)
        {
            char queryChar = _foldedQuery[i];

            // Best score for query[0..i-1] ending at some position at least two back,
            // carried forward so the gap penalty stays affine instead of quadratic.
            // Its source position rides along, so the traceback needs no second scan.
            int carried = NoMatch;
            short carriedParent = -1;

            for (int j = 0; j < m; j++)
            {
                if (i > 0 && j >= 2)
                {
                    int extended = _previous[j - 2] == NoMatch ? NoMatch : _previous[j - 2] - PenaltyGapStart;
                    int continued = carried == NoMatch ? NoMatch : carried - PenaltyGapExtend;

                    if (extended >= continued)
                    {
                        carried = extended;
                        carriedParent = (short)(j - 2);
                    }
                    else
                    {
                        carried = continued;
                    }
                }

                // A candidate position cannot hold the i-th query character if there is
                // not room for the ones before it.
                if (j < i || _foldedCandidate[j] != queryChar)
                {
                    _current[j] = NoMatch;
                    continue;
                }

                int predecessor;
                short parent;

                if (i == 0)
                {
                    predecessor = -Math.Min(j, MaxLeadingPenalty) * PenaltyLeadingChar;
                    parent = -1;
                }
                else
                {
                    int consecutive = j >= 1 && _previous[j - 1] != NoMatch
                        ? _previous[j - 1] + BonusConsecutive
                        : NoMatch;

                    if (consecutive >= carried && consecutive != NoMatch)
                    {
                        predecessor = consecutive;
                        parent = (short)(j - 1);
                    }
                    else if (carried != NoMatch)
                    {
                        predecessor = carried;
                        parent = carriedParent;
                    }
                    else
                    {
                        _current[j] = NoMatch;
                        continue;
                    }
                }

                int bonus = i == 0 ? _bonus[j] * FirstCharBonusMultiplier : _bonus[j];
                _current[j] = predecessor + ScoreMatch + bonus;

                if (trace)
                {
                    _parents[(i * MaxCandidateLength) + j] = parent;
                }

                if (i == n - 1 && _current[j] > bestScore)
                {
                    bestScore = _current[j];
                    bestEnd = j;
                }
            }

            (_previous, _current) = (_current, _previous);
        }

        if (bestEnd < 0)
        {
            return false;
        }

        score = bestScore;
        highlights = trace ? Trace(n, bestEnd) : [];
        return true;
    }

    /// <summary>
    /// Convenience overload that computes both masks. Use the masked overload wherever the
    /// candidate's mask can be stored alongside it.
    /// </summary>
    public bool TryMatch(string query, string candidate, out int score, out IReadOnlyList<MatchSpan> highlights) =>
        TryMatch(query, ComputeMask(query), candidate, ComputeMask(candidate), out score, out highlights);

    private void ComputeBonuses(ReadOnlySpan<char> candidate)
    {
        for (int j = 0; j < candidate.Length; j++)
        {
            char c = candidate[j];
            _foldedCandidate[j] = char.ToLowerInvariant(c);

            if (j == 0)
            {
                _bonus[j] = BonusBoundary;
                continue;
            }

            char previous = candidate[j - 1];

            _bonus[j] = IsSeparator(previous) ? BonusBoundary
                : char.IsLower(previous) && char.IsUpper(c) ? BonusCamel
                : 0;
        }
    }

    private static bool IsSeparator(char c) =>
        c is ' ' or '_' or '-' or '.' or '/' or '\\' or ':' or '(' or '[';

    private IReadOnlyList<MatchSpan> Trace(int queryLength, int end)
    {
        Span<int> positions = stackalloc int[queryLength];
        int position = end;

        for (int i = queryLength - 1; i >= 0; i--)
        {
            positions[i] = position;
            position = _parents[(i * MaxCandidateLength) + position];

            if (position < 0 && i > 0)
            {
                // Cannot happen for a scored match, but never walk off the buffer.
                return [];
            }
        }

        var spans = new List<MatchSpan>();
        int spanStart = positions[0];
        int spanLength = 1;

        for (int i = 1; i < queryLength; i++)
        {
            if (positions[i] == positions[i - 1] + 1)
            {
                spanLength++;
                continue;
            }

            spans.Add(new MatchSpan(spanStart, spanLength));
            spanStart = positions[i];
            spanLength = 1;
        }

        spans.Add(new MatchSpan(spanStart, spanLength));
        return spans;
    }
}
