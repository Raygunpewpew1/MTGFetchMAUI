using AetherVault.Core;

namespace AetherVault.Tests;

public class SearchOptionsTests
{
    [Fact]
    public void ActiveFilterCount_Default_IsZero()
    {
        var options = new SearchOptions();
        Assert.Equal(0, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithNameFilter_IsOne()
    {
        var options = new SearchOptions { NameFilter = "Lotus" };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithColorIdentityFilter_IsOne()
    {
        var options = new SearchOptions { ColorIdentityFilter = "W" };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithColorFilter_IsOne()
    {
        var options = new SearchOptions { ColorFilter = "W" };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithTextFilter_IsOne()
    {
        var options = new SearchOptions { TextFilter = "flying" };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithTypeFilter_Any_IsZero()
    {
        var options = new SearchOptions { TypeFilter = "Any" };
        Assert.Equal(0, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithTypeFilter_NotAny_IsOne()
    {
        var options = new SearchOptions { TypeFilter = "Creature" };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithSubtypeFilter_IsOne()
    {
        var options = new SearchOptions { SubtypeFilter = "Dragon" };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithSupertypeFilter_IsOne()
    {
        var options = new SearchOptions { SupertypeFilter = "Legendary" };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithRarityFilter_IsOne()
    {
        var options = new SearchOptions { RarityFilter = new List<CardRarity> { CardRarity.Rare } };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithSetFilter_IsOne()
    {
        var options = new SearchOptions { SetFilter = "KHM" };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithUseCMCRange_IsOne()
    {
        var options = new SearchOptions { UseCMCRange = true };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithUseCMCExact_IsOne()
    {
        var options = new SearchOptions { UseCMCExact = true };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithPowerFilter_IsOne()
    {
        var options = new SearchOptions { PowerFilter = "2" };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithToughnessFilter_IsOne()
    {
        var options = new SearchOptions { ToughnessFilter = "2" };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithUseLegalFormat_IsOne()
    {
        var options = new SearchOptions { UseLegalFormat = true };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithArtistFilter_IsOne()
    {
        var options = new SearchOptions { ArtistFilter = "John Avon" };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WithIncludeTokens_IsOne()
    {
        var options = new SearchOptions { IncludeTokens = true };
        Assert.Equal(1, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_MultipleFilters_SumsCorrectly()
    {
        var options = new SearchOptions
        {
            ColorFilter = "W",
            TypeFilter = "Creature",
            IncludeTokens = true,
            RarityFilter = new List<CardRarity> { CardRarity.Rare, CardRarity.Mythic }
        };
        // 4 filters are active: Color, Type, IncludeTokens, and Rarity
        Assert.Equal(4, options.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_EmptyOrNullStrings_AreZero()
    {
        var zeroOptions = new SearchOptions
        {
            ColorFilter = "",
            TextFilter = null!,
            SubtypeFilter = "",
            SupertypeFilter = null!,
            SetFilter = "",
            PowerFilter = null!,
            ToughnessFilter = "",
            ArtistFilter = null!
        };
        Assert.Equal(0, zeroOptions.ActiveFilterCount);
    }

    [Fact]
    public void ActiveFilterCount_WhitespaceStrings_AreZero()
    {
        var options = new SearchOptions { TextFilter = "  " };
        Assert.Equal(0, options.ActiveFilterCount);
    }

    [Fact]
    public void HasActiveFilters_ReturnsTrue_WhenFiltersActive()
    {
        var options = new SearchOptions { NameFilter = "Bolt" };
        Assert.True(options.HasActiveFilters);

        var options2 = new SearchOptions { UseLegalFormat = true };
        Assert.True(options2.HasActiveFilters);
    }

    [Fact]
    public void HasActiveFilters_ReturnsFalse_WhenNoFiltersActive()
    {
        var options = new SearchOptions();
        Assert.False(options.HasActiveFilters);
    }
}
