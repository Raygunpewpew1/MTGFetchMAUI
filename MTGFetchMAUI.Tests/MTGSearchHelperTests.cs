using MTGFetchMAUI.Core;
using MTGFetchMAUI.Data;

namespace MTGFetchMAUI.Tests;

public class MTGSearchHelperTests
{
    [Fact]
    public void OrderBy_Ascending_AppendsCorrectClause()
    {
        // Arrange
        var helper = new MTGSearchHelper();

        // Act
        helper.SearchCards()
              .OrderBy("c.name");

        var result = helper.Build();

        // Assert
        Assert.Contains("ORDER BY c.name", result.sql);
        Assert.DoesNotContain("DESC", result.sql);
    }

    [Fact]
    public void OrderBy_Descending_AppendsCorrectClause()
    {
        // Arrange
        var helper = new MTGSearchHelper();

        // Act
        helper.SearchCards()
              .OrderBy("c.name", desc: true);

        var result = helper.Build();

        // Assert
        Assert.Contains("ORDER BY c.name DESC", result.sql);
    }

    [Fact]
    public void OrderBy_OverwritesPreviousCall()
    {
        // Arrange
        var helper = new MTGSearchHelper();

        // Act
        helper.SearchCards()
              .OrderBy("c.name", desc: true)
              .OrderBy("c.power", desc: false);

        var result = helper.Build();

        // Assert
        Assert.Contains("ORDER BY c.power", result.sql);
        Assert.DoesNotContain("ORDER BY c.name", result.sql);
        Assert.DoesNotContain("DESC", result.sql);
    }

    [Fact]
    public void OrderBy_EmptyField_StillAddsClause()
    {
        // Edge case: empty string field
        // Arrange
        var helper = new MTGSearchHelper();

        // Act
        helper.SearchCards()
              .OrderBy("");

        var result = helper.Build();

        // Assert
        // Current implementation allows empty field, resulting in "ORDER BY "
        Assert.Contains("ORDER BY ", result.sql);
    }

    [Fact]
    public void OrderBy_WithOtherClauses_PositionsCorrectly()
    {
        // Arrange
        var helper = new MTGSearchHelper();

        // Act
        helper.SearchCards()
              .WhereNameContains("Test")
              .OrderBy("c.name")
              .Limit(10);

        var result = helper.Build();
        var sql = result.sql;

        // Assert
        // Expected order: WHERE ... ORDER BY ... LIMIT ...
        var whereIndex = sql.IndexOf("WHERE");
        var orderByIndex = sql.IndexOf("ORDER BY");
        var limitIndex = sql.IndexOf("LIMIT");

        Assert.True(whereIndex < orderByIndex, "WHERE should be before ORDER BY");
        Assert.True(orderByIndex < limitIndex, "ORDER BY should be before LIMIT");
    }

    [Fact]
    public void WhereColors_MultipleColors_UsesOR()
    {
        // Arrange
        var helper = new MTGSearchHelper();

        // Act
        helper.SearchCards()
              .WhereColors("W, U");

        var result = helper.Build();

        // Assert
        // We expect OR logic between colors: (c.colors LIKE @p1 OR c.colors LIKE @p2)
        // Note: CondSidePrimary also contains "OR", so we must be specific.
        Assert.Contains(" OR c.colors LIKE", result.sql);
    }

    [Fact]
    public void WhereLegalIn_UsesLike()
    {
        // Arrange
        var helper = new MTGSearchHelper();

        // Act
        helper.SearchCards()
              .WhereLegalIn(DeckFormat.Standard);

        var result = helper.Build();

        // Assert
        Assert.Contains("cl.standard LIKE", result.sql);
    }

    [Fact]
    public void IncludeAllFaces_SkipsPrimarySideFilter()
    {
        // Arrange
        var helper = new MTGSearchHelper();

        // Act
        helper.SearchCards()
              .IncludeAllFaces();

        var result = helper.Build();

        // Assert
        Assert.DoesNotContain("c.side", result.sql);
    }

    // ── No-op guard tests ─────────────────────────────────────────────

    [Fact]
    public void WhereColors_EmptyString_AddsNoCondition()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereColors("");
        var result = helper.Build();
        Assert.DoesNotContain("c.colors", result.sql);
    }

    [Fact]
    public void WhereColors_WhitespaceOnly_AddsNoCondition()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereColors("   ");
        var result = helper.Build();
        Assert.DoesNotContain("c.colors", result.sql);
    }

    [Fact]
    public void WhereColors_SingleColor_SingleCondition()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereColors("W");
        var result = helper.Build();
        Assert.Contains("c.colors LIKE", result.sql);
        // No OR because there's only one color
        Assert.DoesNotContain(" OR c.colors LIKE", result.sql);
    }

    [Fact]
    public void WhereType_EmptyArray_AddsNoCondition()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereType(Array.Empty<string>());
        var result = helper.Build();
        Assert.DoesNotContain("c.type LIKE", result.sql);
    }

    [Fact]
    public void WhereSubtype_EmptyString_AddsNoCondition()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereSubtype("");
        var result = helper.Build();
        Assert.DoesNotContain("c.subtypes", result.sql);
    }

    [Fact]
    public void WhereSubtype_ArrayOfAllBlanks_AddsNoCondition()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereSubtype(new[] { "  ", "\t", "" });
        var result = helper.Build();
        Assert.DoesNotContain("c.subtypes", result.sql);
    }

    [Fact]
    public void WhereRarity_EmptyArray_AddsNoCondition()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereRarity(Array.Empty<CardRarity>());
        var result = helper.Build();
        Assert.DoesNotContain("rarity", result.sql);
    }

    [Fact]
    public void WhereLegalInAny_EmptyArray_AddsNoCondition()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereLegalInAny(Array.Empty<DeckFormat>());
        var result = helper.Build();
        // No legality column references added
        Assert.DoesNotContain("cl.standard", result.sql);
        Assert.DoesNotContain("cl.modern", result.sql);
    }

    // ── Type and attribute filter tests ──────────────────────────────

    [Fact]
    public void WhereType_Array_MultipleTypes_UsesOR()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereType(new[] { "Creature", "Instant" });
        var result = helper.Build();
        Assert.Contains("OR", result.sql);
        Assert.Contains("c.type LIKE", result.sql);
    }

    [Fact]
    public void WhereRarity_Single_UsesINClause()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereRarity(CardRarity.Rare);
        var result = helper.Build();
        Assert.Contains("c.rarity IN (", result.sql);
    }

    [Fact]
    public void WhereRarity_Multiple_AllInSingleINClause()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereRarity(new[] { CardRarity.Rare, CardRarity.Mythic });
        var result = helper.Build();
        // Should have one IN clause with both values
        Assert.Contains("c.rarity IN (", result.sql);
        Assert.Equal(1, CountOccurrences(result.sql, "c.rarity IN ("));
        Assert.Equal(2, result.parameters.Count(p => p.name.StartsWith("pRarity")));
    }

    [Fact]
    public void WherePower_AddsCondition()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WherePower("2");
        var result = helper.Build();
        Assert.Contains("c.power", result.sql);
        Assert.Single(result.parameters, p => p.value.Equals("2"));
    }

    [Fact]
    public void WhereUUID_AddsCondition()
    {
        var uuid = "abc-123";
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereUUID(uuid);
        var result = helper.Build();
        Assert.Contains("c.uuid", result.sql);
        Assert.Single(result.parameters, p => p.value.Equals(uuid));
    }

    // ── Numeric filter tests ──────────────────────────────────────────

    [Fact]
    public void WhereCMC_ExactValue_AddsManaValueCondition()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereCMC(3);
        var result = helper.Build();
        Assert.Contains("manaValue", result.sql);
        Assert.Single(result.parameters, p => p.value.Equals(3.0));
    }

    [Fact]
    public void WhereCMCBetween_AddsAndClause()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereCMCBetween(2, 5);
        var result = helper.Build();
        Assert.Contains("AND @", result.sql);
        Assert.Contains("manaValue", result.sql);
        Assert.Single(result.parameters, p => p.value.Equals(2.0));
        Assert.Single(result.parameters, p => p.value.Equals(5.0));
    }

    [Fact]
    public void WhereManaValue_WithOperator_AddsCustomOperator()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereManaValue(3, "<");
        var result = helper.Build();
        Assert.Contains("c.manaValue <", result.sql);
    }

    // ── Legality filter tests ─────────────────────────────────────────

    [Fact]
    public void WhereLegalIn_BannedStatus_UsesBannedParam()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereBannedIn(DeckFormat.Standard);
        var result = helper.Build();
        Assert.Contains("cl.standard", result.sql);
        Assert.Single(result.parameters, p => p.value.Equals("banned"));
    }

    [Fact]
    public void WhereLegalInAny_MultipleFormats_UsesOR()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereLegalInAny(new[] { DeckFormat.Standard, DeckFormat.Modern });
        var result = helper.Build();
        Assert.Contains("cl.standard", result.sql);
        Assert.Contains("cl.modern", result.sql);
        Assert.Contains(" OR ", result.sql);
    }

    // ── Query builder / modifier tests ────────────────────────────────

    [Fact]
    public void BuildCount_WrapsInCountQuery()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().WhereNameContains("Dragon");
        var result = helper.BuildCount();
        Assert.Contains("SELECT COUNT(*)", result.sql);
    }

    [Fact]
    public void SearchMyCollection_UsesCollectionBase()
    {
        var cardHelper = new MTGSearchHelper();
        cardHelper.SearchCards();
        var cardSql = cardHelper.Build().sql;

        var collHelper = new MTGSearchHelper();
        collHelper.SearchMyCollection();
        var collSql = collHelper.Build().sql;

        Assert.NotEqual(cardSql, collSql);
    }

    [Fact]
    public void Limit_AppendsLimitClause()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().Limit(10);
        var result = helper.Build();
        Assert.Contains("LIMIT 10", result.sql);
    }

    [Fact]
    public void Offset_AppendsOffsetClause()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards().Offset(25);
        var result = helper.Build();
        Assert.Contains("OFFSET 25", result.sql);
    }

    // ── WHERE composition tests ───────────────────────────────────────

    [Fact]
    public void MultipleWhere_JoinedWithAND()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards()
              .WhereNameContains("Dragon")
              .WhereTextContains("flying");
        var result = helper.Build();
        Assert.Contains("AND", result.sql);
        Assert.Contains("c.name LIKE", result.sql);
        Assert.Contains("c.text LIKE", result.sql);
    }

    [Fact]
    public void Build_Default_AddsSidePrimaryFilter()
    {
        var helper = new MTGSearchHelper();
        helper.SearchCards();
        var result = helper.Build();
        Assert.Contains("c.side", result.sql);
    }

    // ── Helper ────────────────────────────────────────────────────────

    private static int CountOccurrences(string source, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }
}
