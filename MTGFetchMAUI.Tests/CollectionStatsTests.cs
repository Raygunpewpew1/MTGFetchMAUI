using Xunit;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Data;
using MTGFetchMAUI.Core;

namespace MTGFetchMAUI.Tests;

public class CollectionStatsTests
{
    [Fact]
    public void CalculateStats_ShouldComputeCorrectly()
    {
        // Arrange
        var items = new[]
        {
            // 4x 2-CMC Creature
            new CollectionItem
            {
                Quantity = 4,
                Card = new Card { CMC = 2, CardType = "Creature", Rarity = CardRarity.Common }
            },
            // 2x 3-CMC Instant (Spell)
            new CollectionItem
            {
                Quantity = 2,
                Card = new Card { CMC = 3, CardType = "Instant", Rarity = CardRarity.Uncommon }
            },
            // 4x Land (CMC 0)
            new CollectionItem
            {
                Quantity = 4,
                Card = new Card { CMC = 0, CardType = "Land", Rarity = CardRarity.Rare }
            }
        };

        // Act
        // We will need to make CalculateStats accessible. I plan to make it a public static method in CollectionRepository.
        // For now, I'll assume it will be CollectionRepository.CalculateStats(items);
        var stats = CollectionRepository.CalculateStats(items);

        // Assert
        Assert.Equal(10, stats.TotalCards); // 4 + 2 + 4
        Assert.Equal(3, stats.UniqueCards);

        Assert.Equal(4, stats.CreatureCount);
        Assert.Equal(2, stats.SpellCount);
        Assert.Equal(4, stats.LandCount);

        Assert.Equal(4, stats.CommonCount);
        Assert.Equal(2, stats.UncommonCount);
        Assert.Equal(4, stats.RareCount);

        // Avg CMC (Non-Lands): (4*2 + 2*3) / (4 + 2) = (8 + 6) / 6 = 14/6 = 2.333...
        Assert.Equal(2.33, stats.AvgCMC, 2);
    }

    [Fact]
    public void CalculateStats_NoCards_ShouldReturnZeroAvgCMC()
    {
        var items = Array.Empty<CollectionItem>();
        var stats = CollectionRepository.CalculateStats(items);
        Assert.Equal(0, stats.AvgCMC);
    }

    [Fact]
    public void CalculateStats_OnlyLands_ShouldReturnZeroAvgCMC()
    {
        var items = new[]
        {
            new CollectionItem { Quantity = 4, Card = new Card { CMC = 0, CardType = "Land" } }
        };
        var stats = CollectionRepository.CalculateStats(items);
        Assert.Equal(0, stats.AvgCMC);
    }
}
