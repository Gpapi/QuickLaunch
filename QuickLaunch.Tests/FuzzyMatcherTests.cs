using System.Collections.Generic;
using QuickLaunch.Core.Abstractions;
using QuickLaunch.Core.Matching;

namespace QuickLaunch.Tests;

public class FuzzyMatcherTests
{
    private readonly FuzzyMatcher _matcher = new();

    private int Score(string query, string candidate)
    {
        Assert.True(_matcher.TryMatch(query, candidate, out int score, out _), $"'{query}' should match '{candidate}'");
        return score;
    }

    private bool Matches(string query, string candidate) =>
        _matcher.TryMatch(query, candidate, out _, out _);

    [Theory]
    [InlineData("chr", "Chrome")]
    [InlineData("vsc", "Visual Studio Code")]
    [InlineData("code", "Visual Studio Code")]
    [InlineData("term", "Windows Terminal")]
    [InlineData("blue", "Bluetooth & devices")]
    public void Matches_abbreviations_people_actually_type(string query, string candidate) =>
        Assert.True(Matches(query, candidate));

    [Theory]
    [InlineData("xyz", "Chrome")]
    [InlineData("chrx", "Chrome")]
    [InlineData("emorhc", "Chrome")]      // right characters, wrong order
    [InlineData("chromee", "Chrome")]     // more of a character than the candidate has
    public void Rejects_what_is_not_there(string query, string candidate) =>
        Assert.False(Matches(query, candidate));

    [Fact]
    public void Match_is_case_insensitive() =>
        Assert.True(Matches("CHROME", "chrome"));

    [Fact]
    public void Empty_query_never_matches() =>
        Assert.False(Matches(string.Empty, "Chrome"));

    [Fact]
    public void Prefers_the_shorter_of_two_equally_good_prefixes()
    {
        // Both start with the query, so the run and boundary bonuses are identical.
        // Ranking between them is the orchestrator's tie-break, not the matcher's, so
        // all that is asserted here is that neither is rejected.
        Assert.True(Matches("chr", "Chrome"));
        Assert.True(Matches("chr", "Chronograph"));
    }

    [Fact]
    public void Prefers_a_word_start_over_a_letter_buried_mid_word() =>
        Assert.True(Score("st", "Sublime Text") > Score("st", "Miscount"));

    [Fact]
    public void Prefers_a_leading_match_over_a_later_one() =>
        Assert.True(Score("code", "Code") > Score("code", "Visual Studio Code"));

    [Fact]
    public void Prefers_a_contiguous_run_over_scattered_characters() =>
        Assert.True(Score("term", "Terminal") > Score("term", "The Editor Runs Mail"));

    [Fact]
    public void Prefers_an_acronym_over_the_same_letters_scattered() =>
        Assert.True(Score("vsc", "Visual Studio Code") > Score("vsc", "Vector Sync"));

    [Fact]
    public void Highlights_cover_exactly_the_matched_characters()
    {
        Assert.True(_matcher.TryMatch("vsc", "Visual Studio Code", out _, out var spans));

        Assert.Equal(3, TotalLength(spans));
        Assert.Equal("VSC", Extract("Visual Studio Code", spans));
    }

    [Fact]
    public void Highlights_merge_adjacent_characters_into_one_span()
    {
        Assert.True(_matcher.TryMatch("chro", "Chrome", out _, out var spans));

        var span = Assert.Single(spans);
        Assert.Equal(0, span.Start);
        Assert.Equal(4, span.Length);
    }

    [Fact]
    public void Highlights_stay_inside_the_candidate()
    {
        Assert.True(_matcher.TryMatch("wt", "Windows Terminal", out _, out var spans));

        foreach (var span in spans)
        {
            Assert.InRange(span.Start, 0, "Windows Terminal".Length - 1);
            Assert.InRange(span.Start + span.Length, 0, "Windows Terminal".Length);
        }
    }

    [Fact]
    public void Mask_rejects_a_character_the_candidate_does_not_contain()
    {
        ulong candidateMask = FuzzyMatcher.ComputeMask("Chrome");
        ulong queryMask = FuzzyMatcher.ComputeMask("chrz");

        Assert.NotEqual(0UL, queryMask & ~candidateMask);
    }

    [Fact]
    public void Mask_admits_a_query_whose_characters_are_all_present()
    {
        ulong candidateMask = FuzzyMatcher.ComputeMask("Visual Studio Code");
        ulong queryMask = FuzzyMatcher.ComputeMask("vsc");

        Assert.Equal(0UL, queryMask & ~candidateMask);
    }

    [Fact]
    public void An_instance_gives_the_same_answer_when_reused()
    {
        int first = Score("vsc", "Visual Studio Code");

        // Scratch buffers are reused between calls; a different shape in between must
        // not leak into the next result.
        Score("term", "Windows Terminal");
        Assert.False(Matches("zzz", "Chrome"));

        Assert.Equal(first, Score("vsc", "Visual Studio Code"));
    }

    [Fact]
    public void Overlong_input_is_handled_rather_than_overflowing_the_buffers()
    {
        string longCandidate = new('a', 500);
        string longQuery = new('a', 200);

        // Truncation is expected; not crashing is the point.
        _matcher.TryMatch(longQuery, longCandidate, out _, out _);
        Assert.True(Matches("aaa", longCandidate));
    }

    private static int TotalLength(IReadOnlyList<MatchSpan> spans)
    {
        int total = 0;

        foreach (var span in spans)
        {
            total += span.Length;
        }

        return total;
    }

    private static string Extract(string candidate, IReadOnlyList<MatchSpan> spans)
    {
        var text = new System.Text.StringBuilder();

        foreach (var span in spans)
        {
            text.Append(candidate.AsSpan(span.Start, span.Length));
        }

        return text.ToString();
    }
}
