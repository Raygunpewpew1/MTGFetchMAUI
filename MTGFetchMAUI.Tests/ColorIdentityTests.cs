using MTGFetchMAUI.Core;
using Xunit;

namespace MTGFetchMAUI.Tests;

public class ColorIdentityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FromString_NullOrEmpty_ReturnsEmpty(string? input)
    {
        var result = ColorIdentity.FromString(input);
        Assert.True(result.IsColorless);
        Assert.Equal(0, result.Count);
    }

    [Theory]
    [InlineData("W", "W")]
    [InlineData("U", "U")]
    [InlineData("B", "B")]
    [InlineData("R", "R")]
    [InlineData("G", "G")]
    public void FromString_ValidSingleColor_ReturnsCorrectIdentity(string input, string expected)
    {
        var result = ColorIdentity.FromString(input);
        Assert.Equal(expected, result.AsString());
        Assert.True(result.IsMonoColor);
    }

    [Theory]
    [InlineData("WU", "WU")]
    [InlineData("WUBRG", "WUBRG")]
    [InlineData("RG", "RG")]
    public void FromString_ValidCombination_ReturnsCorrectIdentity(string input, string expected)
    {
        var result = ColorIdentity.FromString(input);
        Assert.Equal(expected, result.AsString());
        Assert.True(result.IsMultiColor);
    }

    [Theory]
    [InlineData("wU", "WU")]
    [InlineData("uB", "UB")]
    [InlineData("rg", "RG")]
    public void FromString_MixedCase_ReturnsCorrectIdentity(string input, string expected)
    {
        var result = ColorIdentity.FromString(input);
        Assert.Equal(expected, result.AsString());
    }

    [Fact]
    public void FromString_InvalidCharacters_IgnoresInvalid()
    {
        var result = ColorIdentity.FromString("W1X");
        Assert.Equal("W", result.AsString());
        Assert.True(result.IsMonoColor);
    }

    [Fact]
    public void FromString_OrderIndependence()
    {
        var result1 = ColorIdentity.FromString("WU");
        var result2 = ColorIdentity.FromString("UW");

        Assert.Equal(result1, result2);
        // Ensure both produce the same canonical string representation (WUBRG order)
        Assert.Equal("WU", result1.AsString());
        Assert.Equal("WU", result2.AsString());
    }
}
