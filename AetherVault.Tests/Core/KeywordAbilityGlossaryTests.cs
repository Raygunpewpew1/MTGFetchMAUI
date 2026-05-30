using AetherVault.Core;

namespace AetherVault.Tests.Core;

public class KeywordAbilityGlossaryTests
{
    [Theory]
    [InlineData("Flying")]
    [InlineData("flying")]
    [InlineData("FIRST STRIKE")]
    public void TryGetSummary_KnownKeywords_ReturnsNonEmpty(string keyword)
    {
        var s = KeywordAbilityGlossary.TryGetSummary(keyword);
        Assert.False(string.IsNullOrWhiteSpace(s));
    }

    [Fact]
    public void TryGetSummary_HexproofFrom_PrefersLongerPrefix()
    {
        var s = KeywordAbilityGlossary.TryGetSummary("Hexproof from artifacts");
        Assert.NotNull(s);
        Assert.Contains("quality", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGetSummary_Unknown_ReturnsNull()
    {
        Assert.Null(KeywordAbilityGlossary.TryGetSummary("TotallyFakeKeywordXYZ"));
    }

    [Theory]
    [InlineData("Morph")]
    [InlineData("Goad")]
    [InlineData("Landfall")]
    public void TryGetSummary_MtgjsonKeywords_ReturnsNonEmpty(string keyword)
    {
        var s = KeywordAbilityGlossary.TryGetSummary(keyword);
        Assert.False(string.IsNullOrWhiteSpace(s));
    }
}
