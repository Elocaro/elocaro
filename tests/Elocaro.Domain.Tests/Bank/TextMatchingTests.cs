using Elocaro.Domain.Bank.Matching;

namespace Elocaro.Domain.Tests.Bank;

public class TextMatchingTests
{
    private static readonly DateOnly Booking = new(2026, 4, 3);

    [Theory]
    [InlineData("Miete 03/26", "2026-03")]
    [InlineData("Miete 3/2026 Whg 4", "2026-03")]
    [InlineData("Miete 03.2026", "2026-03")]
    [InlineData("MIETE MÄRZ 2026", "2026-03")]
    [InlineData("Miete Maerz", "2026-03")]
    [InlineData("Miete 2026-03", "2026-03")]
    [InlineData("Jan-Mär 2026", "2026-01,2026-02,2026-03")]
    [InlineData("Miete Januar bis März 2026", "2026-01,2026-02,2026-03")]
    [InlineData("Miete Februar und April 2026", "2026-02,2026-04")]
    [InlineData("Miete 01-03/2026", "2026-01,2026-02,2026-03")]
    [InlineData("Miete Dezember", "2025-12")]
    [InlineData("Miete vom 01.03.2026", "")]
    [InlineData("RE 12345 vom 03.04.2026", "")]
    [InlineData("Dauerauftrag", "")]
    public void Months_are_recognized_in_common_notations(string purpose, string expected)
    {
        var months = TextMatching.Months(purpose, Booking);

        Assert.Equal(expected, string.Join(',', months.Select(m => m.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture))));
    }

    [Theory]
    [InlineData("Wohnung 3", "Miete Whg. 3 04/26", true)]
    [InlineData("Wohnung 3", "Miete WE 3", true)]
    [InlineData("Wohnung 3", "Miete Wohnung 31", false)]
    [InlineData("Wohnung 1", "Miete Wohnung 12", false)]
    public void Unit_names_are_found_with_abbreviations_and_whole_numbers(string unit, string purpose, bool expected) =>
        Assert.Equal(expected, TextMatching.ContainsUnit(purpose, unit));

    [Fact]
    public void Normalize_removes_umlauts_diacritics_and_punctuation() =>
        Assert.Equal("mueller schmidt ssimon cafe", TextMatching.Normalize("  MÜLLER-Schmidt, Šsimon & Café "));

    [Fact]
    public void Fuzzy_tokens_allow_one_typo_only_for_longer_words()
    {
        Assert.True(TextMatching.TokenMatches("mustermann", "mustermamn"));
        Assert.False(TextMatching.TokenMatches("maier", "mayer"));
        Assert.False(TextMatching.TokenMatches("mustermann", "mustermeier"));
    }
}
