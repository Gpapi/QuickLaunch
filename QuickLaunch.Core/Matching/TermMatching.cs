using System.Collections.Generic;
using QuickLaunch.Core.Abstractions;

namespace QuickLaunch.Core.Matching;

/// <summary>
/// Scores a query against a candidate that can be found under several names.
/// </summary>
/// <remarks>
/// Applications are searched by title and by executable name; settings pages by title and
/// by the words people actually type for them. Both want the same rule — take the best
/// term, but let the displayed title win a tie — so it lives here rather than twice.
/// </remarks>
public static class TermMatching
{
    /// <summary>
    /// Charged against a match on anything but the first term, so that when a query matches
    /// one candidate's title and another's alias, the title wins.
    /// </summary>
    public const int SecondaryTermPenalty = 12;

    /// <param name="terms">Names to match against, the displayed title first.</param>
    /// <param name="masks">Character masks for <paramref name="terms"/>, same order.</param>
    /// <param name="highlights">
    /// Positions within the *first* term only. A match on an alias has no positions in the
    /// title to point at, so the row is left unhighlighted rather than emphasising the
    /// wrong characters.
    /// </param>
    public static bool TryScore(
        FuzzyMatcher matcher,
        string queryText,
        ulong queryMask,
        IReadOnlyList<string> terms,
        IReadOnlyList<ulong> masks,
        out int score,
        out IReadOnlyList<MatchSpan> highlights)
    {
        score = FuzzyMatcher.NoMatch;
        highlights = [];

        bool matched = false;

        for (int i = 0; i < terms.Count; i++)
        {
            if (!matcher.TryMatch(queryText, queryMask, terms[i], masks[i], out int termScore, out var termHighlights))
            {
                continue;
            }

            if (i > 0)
            {
                termScore -= SecondaryTermPenalty;
            }

            if (!matched || termScore > score)
            {
                score = termScore;
                highlights = i == 0 ? termHighlights : [];
            }

            matched = true;
        }

        return matched;
    }
}
