using QuickLaunch.Core.Indexing;
using QuickLaunch.Core.Matching;
using QuickLaunch.Core.Providers;
using Xunit.Abstractions;

namespace QuickLaunch.Tests;

/// <summary>
/// Ranking across providers, where match quality and provider bias meet.
/// </summary>
/// <remarks>
/// The matcher alone cannot be trusted to settle these: a provider's weight multiplies its
/// scores, so a large enough bias silently overrides how well anything actually matched.
/// These pin the cases where the two pull against each other.
/// </remarks>
public class RankingTests(ITestOutputHelper output)
{
    private static readonly int AppWeight = new AppSearchProvider(new AppCatalog()).Weight;
    private static readonly int SettingWeight = new SettingsSearchProvider().Weight;
    private static readonly int FileWeight = new FileSearchProvider(new FileIndexService()).Weight;

    /// <summary>Score as the orchestrator would rank it: match quality times provider weight.</summary>
    private int Ranked(string query, string candidate, int weight)
    {
        var matcher = new FuzzyMatcher();

        Assert.True(matcher.TryMatch(query, candidate, out int score, out _), $"'{query}' should match '{candidate}'");

        int ranked = score * weight / 100;
        output.WriteLine($"  {candidate,-24} raw={score,4}  ranked={ranked,4}");
        return ranked;
    }

    [Fact]
    public void A_name_starting_with_the_query_beats_the_same_letters_scattered_elsewhere()
    {
        // Reported: "pti" put "Projecting to this PC" — p, t and i spread across three
        // words — above folders literally named ptinew and PtiMigration. The matcher had
        // it right at 80 against 65; the provider weights were reversing it.
        int folder = Ranked("pti", "ptinew", FileWeight);
        int setting = Ranked("pti", "Projecting to this PC", SettingWeight);

        Assert.True(folder > setting, $"the folder scored {folder}, the settings page {setting}");
    }

    [Fact]
    public void Every_prefix_match_beats_that_scattered_match()
    {
        int setting = Ranked("pti", "Projecting to this PC", SettingWeight);

        foreach (string folder in new[] { "ptinew", "PtiMigration", "PtiCenterService" })
        {
            Assert.True(Ranked("pti", folder, FileWeight) > setting, $"'{folder}' ranked below the settings page");
        }
    }

    [Fact]
    public void An_application_still_wins_when_a_file_merely_starts_with_the_query()
    {
        // The other direction, and the reason applications keep a weight at all: "Code
        // Snippets" starts with the query and so scores higher on match quality alone,
        // but someone typing "code" into a launcher means the editor.
        int application = Ranked("code", "Visual Studio Code", AppWeight);
        int folder = Ranked("code", "Code Snippets", FileWeight);

        Assert.True(application > folder, $"the app scored {application}, the folder {folder}");
    }

    [Fact]
    public void Provider_bias_stays_smaller_than_the_gap_between_a_good_and_a_poor_match()
    {
        // A weight is a tie-breaker, not a veto. Keeping the spread inside 25% means no
        // provider can lift a poor match above a clearly better one from another.
        int[] weights = [AppWeight, SettingWeight, FileWeight];

        int highest = weights.Max();
        int lowest = weights.Min();

        output.WriteLine($"weights: app={AppWeight}, setting={SettingWeight}, file={FileWeight}");
        Assert.True(highest * 100 / lowest <= 125, $"weights span {highest * 100 / lowest}% of each other");
    }
}
