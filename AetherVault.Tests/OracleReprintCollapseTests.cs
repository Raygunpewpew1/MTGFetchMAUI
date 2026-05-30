using AetherVault.Core;
using AetherVault.Models;
using AetherVault.Services;

namespace AetherVault.Tests;

public class OracleReprintCollapseTests
{
    [Fact]
    public void ShouldApply_DefaultCatalogSearch_IsTrue()
    {
        var options = new SearchOptions { NameFilter = "bolt" };
        Assert.True(OracleReprintCollapse.ShouldApply(options));
    }

    [Fact]
    public void ShouldApply_SetFilter_IsFalse()
    {
        var options = new SearchOptions { SetFilter = "mh3" };
        Assert.False(OracleReprintCollapse.ShouldApply(options));
    }

    [Fact]
    public void ShouldApply_ShowAllPrintings_IsFalse()
    {
        var options = new SearchOptions { ShowAllPrintings = true };
        Assert.False(OracleReprintCollapse.ShouldApply(options));
    }

    [Fact]
    public void ShouldApply_CollectionQuery_IsFalse()
    {
        var options = new SearchOptions { NameFilter = "bolt" };
        Assert.False(OracleReprintCollapse.ShouldApply(options, isCollectionQuery: true));
    }

    [Fact]
    public void SelectBestPrinting_PrefersNewestEnglishPaperRelease()
    {
        var older = new Card
        {
            Uuid = "old",
            ScryfallOracleId = "oracle-1",
            Language = "English",
            Availability = "paper",
            SetReleaseDate = "2010-01-01",
            SetCode = "m10",
            EdhRecRank = 50
        };
        var newer = new Card
        {
            Uuid = "new",
            ScryfallOracleId = "oracle-1",
            Language = "English",
            Availability = "paper",
            SetReleaseDate = "2024-01-01",
            SetCode = "clu",
            EdhRecRank = 500
        };

        var picked = OracleReprintCollapse.SelectBestPrinting([older, newer]);
        Assert.Equal("new", picked.Uuid);
    }

    [Fact]
    public void CollapseCards_ReturnsOneRowPerOracle()
    {
        var cards = new[]
        {
            new Card { Uuid = "a1", ScryfallOracleId = "o1", SetReleaseDate = "2020-01-01", Language = "English", Availability = "paper" },
            new Card { Uuid = "a2", ScryfallOracleId = "o1", SetReleaseDate = "2024-01-01", Language = "English", Availability = "paper" },
            new Card { Uuid = "b1", ScryfallOracleId = "o2", SetReleaseDate = "2019-01-01", Language = "English", Availability = "paper" },
        };

        var collapsed = OracleReprintCollapse.CollapseCards(cards);
        Assert.Equal(2, collapsed.Length);
        Assert.Contains(collapsed, c => c.Uuid == "a2");
        Assert.Contains(collapsed, c => c.Uuid == "b1");
    }
}
