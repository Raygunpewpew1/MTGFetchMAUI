using AetherVault.Core;

namespace AetherVault.Tests;

public class SearchFiltersSummaryBuilderTests
{
    [Fact]
    public void Build_EmptyOptions_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SearchFiltersSummaryBuilder.Build(new SearchOptions()));
    }

    [Fact]
    public void Build_IncludesColorCmcAndFormat()
    {
        var options = new SearchOptions
        {
            ColorFilter = "W, U",
            UseCmcExact = true,
            CmcExact = 3,
            UseLegalFormat = true,
            LegalFormat = DeckFormat.Commander
        };

        var summary = SearchFiltersSummaryBuilder.Build(options);

        Assert.Contains("Colors: White, Blue", summary);
        Assert.Contains("CMC: 3", summary);
        Assert.Contains("Format: Commander", summary);
    }

    [Fact]
    public void Build_TruncatesLongSummaries()
    {
        var options = new SearchOptions
        {
            TextFilter = new string('x', 200),
            TypeFilter = "Creature",
            SubtypeFilter = "Elf",
            KeywordsFilter = "Flying, Trample, Vigilance",
            ArtistFilter = "Some Very Long Artist Name Here"
        };

        var summary = SearchFiltersSummaryBuilder.Build(options);
        Assert.True(summary.Length <= 121); // 120 + ellipsis
        Assert.EndsWith("…", summary);
    }
}
