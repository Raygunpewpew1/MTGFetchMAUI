using MTGFetchMAUI.Core;
using MTGFetchMAUI.Data;
using MTGFetchMAUI.Models;

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

    [Fact]
    public void CalculateStats_MythicRarity_CountedInMythicCount()
    {
        var items = new[]
        {
            new CollectionItem { Quantity = 2, Card = new Card { CMC = 5, CardType = "Creature", Rarity = CardRarity.Mythic } }
        };
        var stats = CollectionRepository.CalculateStats(items);
        Assert.Equal(2, stats.MythicCount);
        Assert.Equal(0, stats.RareCount);
        Assert.Equal(0, stats.CommonCount);
        Assert.Equal(0, stats.UncommonCount);
    }

    [Fact]
    public void CalculateStats_SpecialRarity_NotCountedInAnyRarityBucket()
    {
        // Special and Bonus rarities fall through the switch — none of the four buckets increase
        var items = new[]
        {
            new CollectionItem { Quantity = 1, Card = new Card { CMC = 3, CardType = "Creature", Rarity = CardRarity.Special } }
        };
        var stats = CollectionRepository.CalculateStats(items);
        Assert.Equal(0, stats.CommonCount);
        Assert.Equal(0, stats.UncommonCount);
        Assert.Equal(0, stats.RareCount);
        Assert.Equal(0, stats.MythicCount);
        Assert.Equal(1, stats.TotalCards); // still counts toward total
    }

    [Fact]
    public void CalculateStats_FoilCard_CountedInFoilCount()
    {
        var items = new[]
        {
            new CollectionItem
            {
                Quantity = 3,
                IsFoil = true,
                Card = new Card { CMC = 2, CardType = "Creature", Rarity = CardRarity.Rare }
            }
        };
        var stats = CollectionRepository.CalculateStats(items);
        Assert.Equal(3, stats.FoilCount);
    }

    [Fact]
    public void CalculateStats_EtchedCard_CountedInFoilCount()
    {
        var items = new[]
        {
            new CollectionItem
            {
                Quantity = 1,
                IsEtched = true,
                Card = new Card { CMC = 2, CardType = "Instant", Rarity = CardRarity.Uncommon }
            }
        };
        var stats = CollectionRepository.CalculateStats(items);
        Assert.Equal(1, stats.FoilCount);
    }

    [Fact]
    public void CalculateStats_ZeroCMCNonLand_IncludedInAvgCalc()
    {
        // A 0-CMC creature (e.g. Memnite) is not a land, so it IS counted in AvgCMC
        // and brings the average down compared to not including it
        var items = new[]
        {
            // 4x 4-CMC creature
            new CollectionItem { Quantity = 4, Card = new Card { CMC = 4, CardType = "Creature", Rarity = CardRarity.Common } },
            // 4x 0-CMC creature
            new CollectionItem { Quantity = 4, Card = new Card { CMC = 0, CardType = "Creature", Rarity = CardRarity.Common } }
        };
        var stats = CollectionRepository.CalculateStats(items);
        // AvgCMC = (4*4 + 4*0) / 8 = 16/8 = 2.0
        Assert.Equal(2.0, stats.AvgCMC, 2);
    }

    [Fact]
    public void CalculateStats_ArtifactCreature_CountedAsCreature()
    {
        // IsCreature is checked first in the if/else chain
        var items = new[]
        {
            new CollectionItem { Quantity = 2, Card = new Card { CMC = 3, CardType = "Artifact Creature", Rarity = CardRarity.Common } }
        };
        var stats = CollectionRepository.CalculateStats(items);
        Assert.Equal(2, stats.CreatureCount);
        Assert.Equal(0, stats.SpellCount);
        Assert.Equal(0, stats.LandCount);
    }

    [Fact]
    public void CalculateStats_Planeswalker_CountedAsSpell()
    {
        // Planeswalker is neither Creature nor Land — falls into SpellCount
        var items = new[]
        {
            new CollectionItem { Quantity = 1, Card = new Card { CMC = 4, CardType = "Legendary Planeswalker", Rarity = CardRarity.Mythic } }
        };
        var stats = CollectionRepository.CalculateStats(items);
        Assert.Equal(1, stats.SpellCount);
        Assert.Equal(0, stats.CreatureCount);
        Assert.Equal(0, stats.LandCount);
    }
}
