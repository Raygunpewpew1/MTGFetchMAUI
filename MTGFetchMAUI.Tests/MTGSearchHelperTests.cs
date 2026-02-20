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
}
