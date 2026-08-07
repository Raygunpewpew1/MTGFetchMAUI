using AetherVault.Core;

namespace AetherVault.Tests;

public class ManaCostSymbolsTests
{
    [Fact]
    public void Enumerate_YieldsInnerTokens()
    {
        var symbols = ManaCostSymbols.Enumerate("{2}{W}{G/U}").ToArray();
        Assert.Equal(["2", "W", "G/U"], symbols);
    }

    [Fact]
    public void Take_RespectsMaxAndFallback()
    {
        Assert.Equal(["2", "W"], ManaCostSymbols.Take("{2}{W}{U}{B}", 2));
        Assert.Equal(["C"], ManaCostSymbols.Take("", 6, fallbackWhenEmpty: "C"));
        Assert.Empty(ManaCostSymbols.Take(null, 6));
    }
}
