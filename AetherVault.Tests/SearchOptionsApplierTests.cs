using AetherVault.Core;
using AetherVault.Data;

namespace AetherVault.Tests;

/// <summary>
/// Ensures each <see cref="SearchOptions"/> field maps to the expected SQL fragment via <see cref="SearchOptionsApplier"/>.
/// Does not execute SQLite — only inspects generated SQL strings.
/// </summary>
public class SearchOptionsApplierTests
{
    private static string BuildSql(Action<SearchOptions>? configure = null)
    {
        var options = new SearchOptions();
        configure?.Invoke(options);
        var helper = new MtgSearchHelper();
        helper.SearchCards();
        SearchOptionsApplier.Apply(helper, options);
        return helper.Build().sql;
    }

    [Fact]
    public void Apply_NameFilter_ContainsNameLike() =>
        Assert.Contains("c.name LIKE", BuildSql(o => o.NameFilter = "Test"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_TextFilter_ContainsTextLike() =>
        Assert.Contains("c.text LIKE", BuildSql(o => o.TextFilter = "flying"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_KeywordsFilter_ContainsJsonKeywords() =>
        Assert.Contains("json_valid(c.keywords)", BuildSql(o => o.KeywordsFilter = "Flying"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_GameChangerOnly_ContainsIsGameChanger() =>
        Assert.Contains("isGameChanger", BuildSql(o => o.GameChangerOnly = true), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_TypeFilter_ContainsTypeLike() =>
        Assert.Contains("c.type LIKE", BuildSql(o => o.TypeFilter = "Creature"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_SubtypeFilter_ContainsSubtypesLike() =>
        Assert.Contains("c.subtypes LIKE", BuildSql(o => o.SubtypeFilter = "Dragon"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_SupertypeFilter_ContainsSupertypesLike() =>
        Assert.Contains("c.supertypes LIKE", BuildSql(o => o.SupertypeFilter = "Legendary"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_ColorFilter_ContainsColorsLike() =>
        Assert.Contains("c.colors LIKE", BuildSql(o => o.ColorFilter = "W"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_ColorIdentityFilter_ContainsColorIdentityLike() =>
        Assert.Contains("c.colorIdentity LIKE", BuildSql(o => o.ColorIdentityFilter = "U"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_RarityFilter_ContainsRarityIn() =>
        Assert.Contains("c.rarity IN", BuildSql(o => o.RarityFilter.Add(CardRarity.Rare)), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_SetFilter_ContainsSetCodeIn() =>
        Assert.Contains("c.setCode IN", BuildSql(o => o.SetFilter = "KHM"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_LayoutFilter_ContainsLayoutIn() =>
        Assert.Contains("c.layout IN", BuildSql(o => o.LayoutFilter.Add(CardLayout.Transform)), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_CmcRange_ContainsManaValueBetween() =>
        Assert.Contains("c.manaValue BETWEEN", BuildSql(o =>
        {
            o.UseCmcRange = true;
            o.CmcMin = 2;
            o.CmcMax = 5;
        }), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_CmcExact_ContainsManaValueEquals() =>
        Assert.Contains("c.manaValue =", BuildSql(o =>
        {
            o.UseCmcExact = true;
            o.CmcExact = 3;
        }), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_PowerFilter_ContainsPowerEquals() =>
        Assert.Contains("c.power =", BuildSql(o => o.PowerFilter = "*"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_ToughnessFilter_ContainsToughnessEquals() =>
        Assert.Contains("c.toughness =", BuildSql(o => o.ToughnessFilter = "4"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_LegalFormat_ContainsFormatColumn() =>
        Assert.Contains("cl.standard", BuildSql(o =>
        {
            o.UseLegalFormat = true;
            o.LegalFormat = DeckFormat.Standard;
        }), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_ArtistFilter_ContainsArtistLike() =>
        Assert.Contains("c.artist LIKE", BuildSql(o => o.ArtistFilter = "Avon"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_DefaultPrimarySide_IncludesSidePredicate() =>
        Assert.Contains("c.side = 'a'", BuildSql(_ => { }), StringComparison.Ordinal);

    [Fact]
    public void Apply_PrimarySideOnlyFalse_OmitsSideEqualsA() =>
        Assert.DoesNotContain("c.side = 'a'", BuildSql(o => o.PrimarySideOnly = false));

    [Fact]
    public void Apply_NoVariations_ContainsVariationsGuard() =>
        Assert.Contains("c.variations", BuildSql(o => o.NoVariations = true), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_CommanderOnly_ContainsCommanderHeuristic() =>
        Assert.Contains("Legendary", BuildSql(o => o.CommanderOnly = true), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_AvailabilityFilter_ContainsJsonAvailability() =>
        Assert.Contains("json_valid(c.availability)", BuildSql(o => o.AvailabilityFilter.Add("arena")), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_FinishesFilter_ContainsJsonFinishes() =>
        Assert.Contains("json_valid(c.finishes)", BuildSql(o => o.FinishesFilter.Add("foil")), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_TypeAny_DoesNotAddTypePredicate() =>
        Assert.DoesNotContain("c.type LIKE", BuildSql(o => o.TypeFilter = "Any"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_DefaultCatalogSearch_CollapsesReprints() =>
        Assert.Contains("av_oracle_rn", BuildSql(), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_SetFilter_SkipsReprintCollapse() =>
        Assert.DoesNotContain("av_oracle_rn", BuildSql(o => o.SetFilter = "mhm"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Apply_ShowAllPrintings_SkipsReprintCollapse() =>
        Assert.DoesNotContain("av_oracle_rn", BuildSql(o => o.ShowAllPrintings = true), StringComparison.OrdinalIgnoreCase);
}
